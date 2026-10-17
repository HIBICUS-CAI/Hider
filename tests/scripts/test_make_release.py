"""scripts/make_release.py: versions, archives, checksums, the installer templates and the command line."""

from __future__ import annotations

import hashlib
import io
import os
import re
import stat
import subprocess
import tarfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

import make_release
from common import TARGETS

from .support import REPO_DIR, Output, arguments, captured, enter, run_as_main, temp_dir

EMPTY_SHA256 = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
ABC_SHA256 = 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'


def make_inputs(root: Path, targets: list[str] | None = None) -> Path:
    """Fake publish outputs as CI's artifacts: <root>/Hider-<rid>/ with the executable, Assets and a library."""
    for target in targets if targets is not None else TARGETS:
        folder = root / f'Hider-{target}'
        (folder / 'Assets').mkdir(parents=True)
        exe = folder / ('Hider.exe' if target.startswith('win') else 'Hider')
        exe.write_text(f'#!/bin/sh\necho "Hider {target}"\n', encoding='utf-8')
        # Artifacts downloaded from Actions lose the executable bit.
        exe.chmod(0o644)
        (folder / 'Assets' / 'hider.svg').write_text('<svg/>', encoding='utf-8')
        (folder / 'libSkiaSharp.so').write_bytes(bytes(range(256)) * 64)
    return root


def file_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def all_values(version: str = '1.2.3') -> dict[str, str]:
    values = {
        'HIDER_VERSION': version,
        'HIDER_BASE_URL': f'https://github.com/o/r/releases/download/v{version}',
    }
    for target in TARGETS:
        values[f'HIDER_SHA256_{target.upper().replace("-", "_")}'] = target.replace('-', '') * 4
    return values


class ConstantsTest(unittest.TestCase):
    def test_paths(self) -> None:
        self.assertEqual(make_release.PROPS_FILE, REPO_DIR / 'Directory.Build.props')
        self.assertEqual(make_release.TEMPLATE_DIR, REPO_DIR / 'scripts' / 'release')
        for name in ['install.bat.in', 'install.sh.in', 'release-notes.md.in']:
            self.assertTrue((make_release.TEMPLATE_DIR / name).is_file(), name)

    def test_bumps(self) -> None:
        # minor first: the default of the release workflow's input.
        self.assertEqual(make_release.BUMPS, ['minor', 'patch', 'major'])

    def test_placeholder_pattern(self) -> None:
        pattern = make_release.PLACEHOLDER
        self.assertEqual(
            pattern.findall('@HIDER_VERSION@ @HIDER_SHA256_LINUX_X64@ @HIDER_A1@'),
            [
                '@HIDER_VERSION@',
                '@HIDER_SHA256_LINUX_X64@',
                '@HIDER_A1@',
            ],
        )
        for text in ['@HIDER_@', '@hider_version@', '@HIDER_version@', 'HIDER_VERSION', '@OTHER_X@', '@HIDER-X@']:
            with self.subTest(text=text):
                self.assertEqual(pattern.findall(text), [])


class TagNameTest(unittest.TestCase):
    def test_prefixes_v(self) -> None:
        self.assertEqual(make_release.tag_name('2.1.0'), 'v2.1.0')
        self.assertEqual(make_release.tag_name('0.0.0'), 'v0.0.0')


class ArchiveNameTest(unittest.TestCase):
    def test_zip_for_windows_tar_gz_for_linux(self) -> None:
        self.assertEqual(make_release.archive_name('2.1.0', 'win-x64'), 'Hider-2.1.0-win-x64.zip')
        self.assertEqual(make_release.archive_name('2.1.0', 'linux-x64'), 'Hider-2.1.0-linux-x64.tar.gz')
        self.assertEqual(make_release.archive_name('2.1.0', 'linux-arm64'), 'Hider-2.1.0-linux-arm64.tar.gz')

    def test_any_windows_target_is_a_zip(self) -> None:
        self.assertEqual(make_release.archive_name('1.0.0', 'win-arm64'), 'Hider-1.0.0-win-arm64.zip')
        self.assertEqual(make_release.archive_name('1.0.0', 'osx-x64'), 'Hider-1.0.0-osx-x64.tar.gz')


class ParseVersionTest(unittest.TestCase):
    def test_valid(self) -> None:
        self.assertEqual(make_release.parse_version('2.1.0'), (2, 1, 0))
        self.assertEqual(make_release.parse_version('0.0.0'), (0, 0, 0))
        self.assertEqual(make_release.parse_version('10.200.3000'), (10, 200, 3000))
        # Leading zeros are accepted and read as numbers.
        self.assertEqual(make_release.parse_version('01.002.0003'), (1, 2, 3))

    def test_invalid_exits(self) -> None:
        for version in [
            '',
            '2',
            '2.1',
            '2.1.0.0',
            'v2.1.0',
            '2.1.0-rc1',
            '2.1.0 ',
            ' 2.1.0',
            'a.b.c',
            '2..0',
            '-1.0.0',
        ]:
            with self.subTest(version=version), self.assertRaises(SystemExit) as raised:
                make_release.parse_version(version)
            self.assertEqual(raised.exception.code, f'invalid version "{version}" (expected MAJOR.MINOR.PATCH)')

    def test_non_ascii_digits_exit(self) -> None:
        # \d alone would take other scripts' digits (and int() would read them), but the version goes into file names
        # and URLs: ASCII only.
        for version in ['\u0661.\u0662.\u0663', '\uff11.\uff12.\uff13', '1.2.\u0663', '\U0001d7d9.0.0']:
            with self.subTest(version=version), self.assertRaises(SystemExit) as raised:
                make_release.parse_version(version)
            self.assertEqual(raised.exception.code, f'invalid version "{version}" (expected MAJOR.MINOR.PATCH)')


class PropsVersionTest(unittest.TestCase):
    def setUp(self) -> None:
        self.props = temp_dir(self) / 'Directory.Build.props'
        enter(self, mock.patch.object(make_release, 'PROPS_FILE', self.props))

    def write(self, body: str) -> None:
        self.props.write_text(f'<Project>\n  <PropertyGroup>\n{body}  </PropertyGroup>\n</Project>\n', encoding='utf-8')

    def exit_message(self) -> object:
        with self.assertRaises(SystemExit) as raised:
            make_release.props_version()
        return raised.exception.code

    def test_reads_the_version(self) -> None:
        self.write('    <Version>3.4.5</Version>\n    <Authors>x</Authors>\n')
        self.assertEqual(make_release.props_version(), '3.4.5')

    def test_no_version_exits(self) -> None:
        self.write('    <AssemblyVersion>1.0.0</AssemblyVersion>\n')
        self.assertEqual(self.exit_message(), f'{self.props} must have exactly one <Version> element')

    def test_two_versions_exit(self) -> None:
        self.write('    <Version>1.0.0</Version>\n    <Version>1.0.0</Version>\n')
        self.assertEqual(self.exit_message(), f'{self.props} must have exactly one <Version> element')

    def test_other_elements_ending_in_version_do_not_count(self) -> None:
        self.write('    <FileVersion>9.9.9</FileVersion>\n    <Version>1.0.0</Version>\n')
        # <FileVersion> contains "Version>" but not "<Version>".
        self.assertEqual(make_release.props_version(), '1.0.0')

    def test_invalid_version_exits(self) -> None:
        self.write('    <Version>2.0</Version>\n')
        self.assertEqual(self.exit_message(), 'invalid version "2.0" (expected MAJOR.MINOR.PATCH)')

    def test_empty_version_exits(self) -> None:
        self.write('    <Version></Version>\n')
        self.assertEqual(self.exit_message(), 'invalid version "" (expected MAJOR.MINOR.PATCH)')

    def test_missing_file_raises(self) -> None:
        with self.assertRaises(FileNotFoundError):
            make_release.props_version()


class RepositoryPropsTest(unittest.TestCase):
    def test_repository_version(self) -> None:
        version = make_release.props_version()
        text = (REPO_DIR / 'Directory.Build.props').read_text(encoding='utf-8')
        self.assertIn(f'<Version>{version}</Version>', text)
        self.assertRegex(version, r'^\d+\.\d+\.\d+$')


class BumpTest(unittest.TestCase):
    def test_parts(self) -> None:
        self.assertEqual(make_release.bump('minor', '2.1.0'), '2.2.0')
        self.assertEqual(make_release.bump('patch', '2.1.0'), '2.1.1')
        self.assertEqual(make_release.bump('major', '2.1.0'), '3.0.0')

    def test_resets_the_lower_parts(self) -> None:
        self.assertEqual(make_release.bump('minor', '2.1.7'), '2.2.0')
        self.assertEqual(make_release.bump('major', '2.9.9'), '3.0.0')

    def test_counts_past_nine(self) -> None:
        self.assertEqual(make_release.bump('minor', '2.9.5'), '2.10.0')
        self.assertEqual(make_release.bump('patch', '2.1.9'), '2.1.10')
        self.assertEqual(make_release.bump('major', '9.0.0'), '10.0.0')

    def test_drops_leading_zeros(self) -> None:
        self.assertEqual(make_release.bump('patch', '01.02.03'), '1.2.4')

    def test_from_zero(self) -> None:
        self.assertEqual(make_release.bump('patch', '0.0.0'), '0.0.1')

    def test_other_part_is_a_patch(self) -> None:
        # argparse limits the part to BUMPS; anything else falls through to the patch bump.
        self.assertEqual(make_release.bump('build', '1.2.3'), '1.2.4')

    def test_invalid_version_exits(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            make_release.bump('minor', 'v2.1.0')
        self.assertEqual(raised.exception.code, 'invalid version "v2.1.0" (expected MAJOR.MINOR.PATCH)')


HEAD = 'a' * 40
OTHER = 'b' * 40
TAG_OBJECT = 'c' * 40


def ls_remote(*refs: tuple[str, str]) -> str:
    """`git ls-remote --tags` output: '<object name>\\t<ref>' lines."""
    return ''.join(f'{sha}\t{ref}\n' for sha, ref in refs)


class ChooseVersionTest(unittest.TestCase):
    """The release workflow's version (release.yml, job prepare): next-version on its `git ls-remote --tags` listing."""

    last_props: mock.Mock

    def choose(self, listing: str, part: str = 'minor', head: str = HEAD, props: str = '2.0.0') -> tuple[str, str]:
        """Return the version and what was written to stderr."""
        read_props = mock.Mock(return_value=props)
        with captured() as output:
            version = make_release.choose_version(listing, head, part, read_props)
        self.assertEqual(output.out, '')
        self.last_props = read_props
        return version, output.err

    def exit_message(self, listing: str, head: str = HEAD) -> object:
        with self.assertRaises(SystemExit) as raised, captured():
            make_release.choose_version(listing, head, 'minor', mock.Mock(side_effect=AssertionError('props read')))
        return raised.exception.code

    def test_no_tags_takes_the_props_version(self) -> None:
        for listing in ['', '\n', '  \n\n']:
            with self.subTest(listing=listing):
                self.assertEqual(
                    self.choose(listing, props='3.4.5'), ('3.4.5', 'No release yet: 3.4.5 (Directory.Build.props)\n')
                )
                self.last_props.assert_called_once_with()

    def test_default_reads_directory_build_props(self) -> None:
        with captured():
            self.assertEqual(make_release.choose_version('', HEAD, 'minor'), make_release.props_version())

    def test_props_are_not_read_once_there_is_a_tag(self) -> None:
        version, err = self.choose(ls_remote((OTHER, 'refs/tags/v2.0.0')))
        self.assertEqual((version, err), ('2.1.0', 'v2.0.0 -> v2.1.0 (minor)\n'))
        self.last_props.assert_not_called()

    def test_parts(self) -> None:
        listing = ls_remote((OTHER, 'refs/tags/v2.3.4'))
        self.assertEqual(self.choose(listing, 'minor')[0], '2.4.0')
        self.assertEqual(self.choose(listing, 'patch'), ('2.3.5', 'v2.3.4 -> v2.3.5 (patch)\n'))
        self.assertEqual(self.choose(listing, 'major')[0], '3.0.0')

    def test_lightweight_and_annotated_tags(self) -> None:
        # An annotated tag is listed twice: the tag object, then peeled (^{}) to the commit.
        listing = ls_remote(
            (OTHER, 'refs/tags/v2.0.0'),
            (TAG_OBJECT, 'refs/tags/v2.1.0'),
            ('d' * 40, 'refs/tags/v2.1.0^{}'),
        )
        self.assertEqual(self.choose(listing), ('2.2.0', 'v2.1.0 -> v2.2.0 (minor)\n'))

    def test_only_a_peeled_line_counts_too(self) -> None:
        self.assertEqual(self.choose(ls_remote((OTHER, 'refs/tags/v4.0.0^{}')))[0], '4.1.0')

    def test_numeric_order(self) -> None:
        # Not text order: v2.10.0 is above v2.9.0, v10.0.0 above v9.9.9, and the listing order does not matter.
        listing = ls_remote(
            (OTHER, 'refs/tags/v2.10.0'),
            (OTHER, 'refs/tags/v2.9.0'),
            (OTHER, 'refs/tags/v2.9.10'),
        )
        self.assertEqual(self.choose(listing), ('2.11.0', 'v2.10.0 -> v2.11.0 (minor)\n'))
        listing = ls_remote((OTHER, 'refs/tags/v9.9.9'), (OTHER, 'refs/tags/v10.0.0'), (OTHER, 'refs/tags/v1.99.0'))
        self.assertEqual(self.choose(listing, 'patch')[0], '10.0.1')

    def test_leading_zeros_compare_as_numbers(self) -> None:
        listing = ls_remote((OTHER, 'refs/tags/v2.010.0'), (OTHER, 'refs/tags/v2.9.0'))
        self.assertEqual(self.choose(listing), ('2.11.0', 'v2.10.0 -> v2.11.0 (minor)\n'))

    def test_other_tags_are_ignored(self) -> None:
        others = [
            'refs/tags/v2.0',
            'refs/tags/v2.0.0-rc1',
            'refs/tags/v2.0.0.1',
            'refs/tags/release-1',
            'refs/tags/V9.0.0',
            'refs/tags/vv9.0.0',
            'refs/tags/x/v9.0.0',
            'refs/tags/v٩.0.0',
            'refs/heads/v9.0.0',
            'refs/tags/v9.0.0^{tree}',
        ]
        listing = ls_remote((OTHER, 'refs/tags/v1.2.3'), *((OTHER, ref) for ref in others))
        self.assertEqual(self.choose(listing)[0], '1.3.0')
        # Only such tags: still the first release.
        self.assertEqual(self.choose(ls_remote(*((OTHER, ref) for ref in others)), props='2.0.0')[0], '2.0.0')

    def test_head_released_already(self) -> None:
        for ref, expected in [
            ('refs/tags/v2.0.0', 'v2.0.0'),
            # Either line of an annotated tag.
            ('refs/tags/v2.0.0^{}', 'v2.0.0'),
            # Any tag v* (the listing is limited to them), not only versions.
            ('refs/tags/v2.0.0-rc1', 'v2.0.0-rc1'),
        ]:
            with self.subTest(ref=ref):
                listing = ls_remote((OTHER, 'refs/tags/v1.0.0'), (HEAD, ref))
                self.assertEqual(self.exit_message(listing), f'main ({HEAD}) is released already as {expected}')

    def test_first_tag_of_head_is_named(self) -> None:
        listing = ls_remote((HEAD, 'refs/tags/v2.1.0'), (HEAD, 'refs/tags/v2.2.0'))
        self.assertEqual(self.exit_message(listing), f'main ({HEAD}) is released already as v2.1.0')

    def test_head_on_a_branch_ref_is_not_released(self) -> None:
        listing = ls_remote((HEAD, 'refs/heads/main'), (OTHER, 'refs/tags/v2.0.0'))
        self.assertEqual(self.choose(listing)[0], '2.1.0')

    def test_object_names_must_match_exactly(self) -> None:
        # A tag on another commit whose name starts like HEAD's does not count as released.
        listing = ls_remote(('a' * 39 + 'b', 'refs/tags/v2.0.0'))
        self.assertEqual(self.choose(listing)[0], '2.1.0')

    def test_sha256_repositories(self) -> None:
        head = 'e' * 64
        self.assertEqual(self.choose(ls_remote(('f' * 64, 'refs/tags/v2.0.0')), head=head)[0], '2.1.0')
        listing = ls_remote(('f' * 64, 'refs/tags/v2.0.0'), (head, 'refs/tags/v2.1.0'))
        self.assertEqual(self.exit_message(listing, head), f'main ({head}) is released already as v2.1.0')

    def test_crlf_listing(self) -> None:
        self.assertEqual(self.choose(f'{OTHER}\trefs/tags/v2.0.0\r\n')[0], '2.1.0')

    def test_unexpected_lines_exit(self) -> None:
        for line in [
            'warning: redirecting to https://example.com',
            f'{OTHER} refs/tags/v2.0.0',
            f'{OTHER}\t',
            f'{OTHER[:-1]}\trefs/tags/v2.0.0',
            f'{OTHER.upper()}\trefs/tags/v2.0.0',
            f'{OTHER}\trefs/tags/v2.0.0\textra',
        ]:
            with self.subTest(line=line):
                self.assertEqual(
                    self.exit_message(ls_remote((OTHER, 'refs/tags/v1.0.0')) + line + '\n'),
                    f'unexpected line from git ls-remote: "{line}"',
                )

    def test_real_git_ls_remote_listing(self) -> None:
        """The listing as release.yml makes it, from a scratch repository: lightweight and annotated tags."""
        repo = temp_dir(self)
        env = {
            'PATH': os.environ.get('PATH', ''),
            'HOME': str(repo),
            'GIT_CONFIG_NOSYSTEM': '1',
            'GIT_AUTHOR_NAME': 't',
            'GIT_AUTHOR_EMAIL': 't@example.com',
            'GIT_COMMITTER_NAME': 't',
            'GIT_COMMITTER_EMAIL': 't@example.com',
        }

        def git(*args: str) -> str:
            result = subprocess.run(
                ['git', '-C', str(repo), *args], env=env, capture_output=True, text=True, check=True
            )
            return result.stdout.strip()

        git('init', '-q')
        git('commit', '-q', '--allow-empty', '-m', 'one')
        git('tag', 'v2.9.0')
        git('commit', '-q', '--allow-empty', '-m', 'two')
        git('tag', '-a', '-m', 'release', 'v2.10.0')
        git('tag', 'v3.0.0-rc1')
        git('commit', '-q', '--allow-empty', '-m', 'three')
        head = git('rev-parse', 'HEAD')
        listing = git('ls-remote', '--tags', str(repo), 'refs/tags/v*')
        # The annotated tag shows up twice (tag object, then ^{}).
        self.assertIn('refs/tags/v2.10.0^{}', listing)
        self.assertEqual(self.choose(listing, head=head), ('2.11.0', 'v2.10.0 -> v2.11.0 (minor)\n'))
        # The commit of the annotated tag (listed only on its peeled line) and of the lightweight one: released.
        tagged = git('rev-parse', 'HEAD~1')
        self.assertEqual(self.exit_message(listing, tagged), f'main ({tagged}) is released already as v2.10.0')
        first = git('rev-parse', 'HEAD~2')
        self.assertEqual(self.exit_message(listing, first), f'main ({first}) is released already as v2.9.0')

    def test_invalid_head_exits(self) -> None:
        for head in ['', 'HEAD', 'main', 'a' * 39, 'a' * 41, 'A' * 40, 'g' * 40, 'a' * 63]:
            with self.subTest(head=head):
                self.assertEqual(self.exit_message('', head), f'invalid commit "{head}" (expected a full object name)')


class FindInputTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)

    def test_from_the_input_dir(self) -> None:
        make_inputs(self.root)
        for target in TARGETS:
            with self.subTest(target=target):
                self.assertEqual(make_release.find_input(self.root, target), self.root / f'Hider-{target}')

    def test_default_is_bin_publish(self) -> None:
        make_inputs(self.root, ['linux-x64'])
        with mock.patch.object(make_release, 'publish_dir', return_value=self.root / 'Hider-linux-x64') as publish:
            self.assertEqual(make_release.find_input(None, 'linux-x64'), self.root / 'Hider-linux-x64')
        publish.assert_called_once_with('linux-x64')

    def test_input_dir_wins_over_bin_publish(self) -> None:
        make_inputs(self.root, ['win-x64'])
        with mock.patch.object(make_release, 'publish_dir', side_effect=AssertionError('publish_dir called')):
            self.assertEqual(make_release.find_input(self.root, 'win-x64'), self.root / 'Hider-win-x64')

    def exit_message(self, target: str) -> object:
        with self.assertRaises(SystemExit) as raised:
            make_release.find_input(self.root, target)
        return raised.exception.code

    def test_missing_folder_exits(self) -> None:
        self.assertEqual(
            self.exit_message('linux-arm64'),
            f'No Hider in {self.root / "Hider-linux-arm64"}: publish linux-arm64 first.',
        )

    def test_windows_needs_the_exe(self) -> None:
        folder = self.root / 'Hider-win-x64'
        folder.mkdir()
        (folder / 'Hider').write_text('', encoding='utf-8')
        self.assertEqual(self.exit_message('win-x64'), f'No Hider.exe in {folder}: publish win-x64 first.')

    def test_linux_needs_the_executable_without_extension(self) -> None:
        folder = self.root / 'Hider-linux-x64'
        folder.mkdir()
        (folder / 'Hider.exe').write_text('', encoding='utf-8')
        self.assertEqual(self.exit_message('linux-x64'), f'No Hider in {folder}: publish linux-x64 first.')

    def test_executable_must_be_a_file(self) -> None:
        (self.root / 'Hider-linux-x64' / 'Hider').mkdir(parents=True)
        self.assertIn('No Hider in', str(self.exit_message('linux-x64')))


class TarFilterTest(unittest.TestCase):
    def info(self, name: str, kind: bytes = tarfile.REGTYPE, mode: int = 0o600) -> tarfile.TarInfo:
        info = tarfile.TarInfo(name)
        info.type = kind
        info.mode = mode
        info.uid, info.gid, info.uname, info.gname = 1000, 1000, 'runner', 'docker'
        return info

    def test_owner_is_cleared(self) -> None:
        result = make_release.tar_filter(self.info('Hider-1.0.0-linux-x64/Hider.dll'))
        self.assertEqual((result.uid, result.gid, result.uname, result.gname), (0, 0, '', ''))

    def test_returns_the_same_entry(self) -> None:
        info = self.info('a/b')
        self.assertIs(make_release.tar_filter(info), info)

    def test_modes(self) -> None:
        cases = [
            (self.info('Hider-1.0.0-linux-x64', tarfile.DIRTYPE, 0o700), 0o755),
            (self.info('Hider-1.0.0-linux-x64/Assets', tarfile.DIRTYPE, 0o777), 0o755),
            (self.info('Hider-1.0.0-linux-x64/Hider', mode=0o644), 0o755),
            (self.info('Hider-1.0.0-linux-x64/Hider', mode=0o777), 0o755),
            # Any file named Hider, at any depth.
            (self.info('Hider-1.0.0-linux-x64/sub/Hider', mode=0o600), 0o755),
            (self.info('Hider-1.0.0-linux-x64/Hider.dll', mode=0o755), 0o644),
            (self.info('Hider-1.0.0-linux-x64/libhidercap.so', mode=0o755), 0o644),
            (self.info('Hider-1.0.0-linux-x64/hider', mode=0o755), 0o644),
            (self.info('Hider-1.0.0-linux-x64/Hider.sh', mode=0o755), 0o644),
            (self.info('Hider-1.0.0-linux-x64/Assets/hider.svg', mode=0o666), 0o644),
        ]
        for info, expected in cases:
            with self.subTest(name=info.name, kind=info.type):
                self.assertEqual(make_release.tar_filter(info).mode, expected)


class MakeArchiveTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.src = self.root / 'in'
        (self.src / 'Assets').mkdir(parents=True)
        (self.src / 'empty').mkdir()
        (self.src / 'Hider').write_text('#!/bin/sh\n', encoding='utf-8')
        (self.src / 'Hider').chmod(0o644)
        (self.src / 'Hider.dll').write_bytes(b'MZ' + bytes(1000))
        (self.src / 'Assets' / 'hider.svg').write_text('<svg/>', encoding='utf-8')
        (self.src / 'Assets' / 'b-c.png').write_bytes(b'png')

    def expected_names(self, root: str) -> list[str]:
        """The root folder first, then every path in sorted order (the order make_archive promises)."""
        return [root] + [f'{root}/{p.relative_to(self.src).as_posix()}' for p in sorted(self.src.rglob('*'))]

    def test_zip(self) -> None:
        out = self.root / 'Hider-1.0.0-win-x64.zip'
        make_release.make_archive(self.src, out, 'Hider-1.0.0-win-x64')
        with zipfile.ZipFile(out) as archive:
            names = archive.namelist()
            self.assertEqual([n.rstrip('/') for n in names], self.expected_names('Hider-1.0.0-win-x64'))
            # Folders are entries of their own (so the empty one survives).
            self.assertIn('Hider-1.0.0-win-x64/', names)
            self.assertIn('Hider-1.0.0-win-x64/empty/', names)
            self.assertEqual(archive.read('Hider-1.0.0-win-x64/Hider.dll'), b'MZ' + bytes(1000))
            self.assertEqual(archive.read('Hider-1.0.0-win-x64/Assets/hider.svg'), b'<svg/>')
            for info in archive.infolist():
                if not info.is_dir():
                    self.assertEqual(info.compress_type, zipfile.ZIP_DEFLATED, info.filename)
            self.assertIsNone(archive.testzip())

    def test_zip_is_reproducible(self) -> None:
        first, second = self.root / 'a.zip', self.root / 'b.zip'
        make_release.make_archive(self.src, first, 'r')
        make_release.make_archive(self.src, second, 'r')
        self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_tar_gz(self) -> None:
        out = self.root / 'Hider-1.0.0-linux-x64.tar.gz'
        make_release.make_archive(self.src, out, 'Hider-1.0.0-linux-x64')
        with tarfile.open(out, 'r:gz') as archive:
            members = archive.getmembers()
            self.assertEqual([m.name for m in members], self.expected_names('Hider-1.0.0-linux-x64'))
            modes = {m.name.removeprefix('Hider-1.0.0-linux-x64'): (m.type, m.mode) for m in members}
            self.assertEqual(modes[''], (tarfile.DIRTYPE, 0o755))
            self.assertEqual(modes['/empty'], (tarfile.DIRTYPE, 0o755))
            self.assertEqual(modes['/Hider'], (tarfile.REGTYPE, 0o755))
            self.assertEqual(modes['/Hider.dll'], (tarfile.REGTYPE, 0o644))
            self.assertEqual(modes['/Assets/hider.svg'], (tarfile.REGTYPE, 0o644))
            self.assertTrue(all((m.uid, m.gid, m.uname, m.gname) == (0, 0, '', '') for m in members))
            data = archive.extractfile('Hider-1.0.0-linux-x64/Hider.dll')
            assert data is not None
            self.assertEqual(data.read(), b'MZ' + bytes(1000))

    def test_any_other_name_is_a_tar_gz(self) -> None:
        out = self.root / 'archive.tgz'
        make_release.make_archive(self.src, out, 'r')
        self.assertEqual(out.read_bytes()[:2], b'\x1f\x8b')
        with tarfile.open(out, 'r:gz') as archive:
            self.assertEqual(archive.getnames()[0], 'r')

    def test_source_folder_is_unchanged(self) -> None:
        make_release.make_archive(self.src, self.root / 'x.tar.gz', 'r')
        self.assertEqual(stat.S_IMODE((self.src / 'Hider').stat().st_mode), 0o644)


class Sha256Test(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)

    def test_known_values(self) -> None:
        (self.root / 'empty').write_bytes(b'')
        (self.root / 'abc').write_bytes(b'abc')
        self.assertEqual(make_release.sha256(self.root / 'empty'), EMPTY_SHA256)
        self.assertEqual(make_release.sha256(self.root / 'abc'), ABC_SHA256)

    def test_files_larger_than_one_chunk(self) -> None:
        for size in [(1 << 20) - 1, 1 << 20, (1 << 20) + 1, 3 * (1 << 20) + 5]:
            with self.subTest(size=size):
                data = bytes(i % 251 for i in range(size))
                path = self.root / f'f{size}'
                path.write_bytes(data)
                self.assertEqual(make_release.sha256(path), hashlib.sha256(data).hexdigest())


class RenderTest(unittest.TestCase):
    def setUp(self) -> None:
        self.templates = temp_dir(self)
        enter(self, mock.patch.object(make_release, 'TEMPLATE_DIR', self.templates))

    def template(self, text: str) -> str:
        (self.templates / 't.in').write_bytes(text.encode('utf-8'))
        return 't.in'

    def test_replaces_every_occurrence(self) -> None:
        name = self.template('v=@HIDER_VERSION@ again @HIDER_VERSION@ url=@HIDER_BASE_URL@/x\n')
        self.assertEqual(
            make_release.render(name, {'HIDER_VERSION': '1.0.0', 'HIDER_BASE_URL': 'https://h/v1'}, '\n'),
            'v=1.0.0 again 1.0.0 url=https://h/v1/x\n',
        )

    def test_unused_values_are_fine(self) -> None:
        name = self.template('plain\n')
        self.assertEqual(make_release.render(name, {'HIDER_VERSION': '1.0.0'}, '\n'), 'plain\n')

    def test_leftover_placeholders_exit(self) -> None:
        name = self.template('@HIDER_B@ @HIDER_A@ @HIDER_B@ @HIDER_VERSION@\n')
        with self.assertRaises(SystemExit) as raised:
            make_release.render(name, {'HIDER_VERSION': '1'}, '\n')
        self.assertEqual(raised.exception.code, 't.in: placeholders without a value: @HIDER_A@, @HIDER_B@')

    def test_a_value_that_looks_like_a_placeholder_exits(self) -> None:
        name = self.template('@HIDER_VERSION@\n')
        with self.assertRaises(SystemExit):
            make_release.render(name, {'HIDER_VERSION': '@HIDER_OTHER@'}, '\n')

    def test_non_placeholders_stay(self) -> None:
        text = 'echo @hider_x@ @HIDER_@ user@HIDER_ host @ @@\n'
        self.assertEqual(make_release.render(self.template(text), {}, '\n'), text)

    def test_line_endings(self) -> None:
        name = self.template('a\r\nb\nc\r\n\nd')
        self.assertEqual(make_release.render(name, {}, '\n'), 'a\nb\nc\n\nd')
        self.assertEqual(make_release.render(name, {}, '\r\n'), 'a\r\nb\r\nc\r\n\r\nd')

    def test_values_get_the_line_endings_too(self) -> None:
        name = self.template('@HIDER_X@\n')
        self.assertEqual(make_release.render(name, {'HIDER_X': 'a\nb'}, '\r\n'), 'a\r\nb\r\n')

    def test_reads_utf8(self) -> None:
        name = self.template('# 画面の一部 @HIDER_X@\n')
        self.assertEqual(make_release.render(name, {'HIDER_X': 'é'}, '\n'), '# 画面の一部 é\n')

    def test_missing_template_raises(self) -> None:
        with self.assertRaises(FileNotFoundError):
            make_release.render('missing.in', {}, '\n')


class TemplatesTest(unittest.TestCase):
    """The real templates in scripts/release take exactly the values package gives them."""

    def test_every_template_renders(self) -> None:
        for name in ['install.bat.in', 'install.sh.in', 'release-notes.md.in']:
            with self.subTest(name=name):
                text = make_release.render(name, all_values(), '\n')
                self.assertNotIn('@HIDER_', text)

    def test_install_sh_values(self) -> None:
        text = make_release.render('install.sh.in', all_values('4.5.6'), '\n')
        self.assertTrue(text.startswith('#!/bin/sh\n'))
        self.assertIn("\nVERSION='4.5.6'\n", text)
        self.assertIn("\nBASE_URL='https://github.com/o/r/releases/download/v4.5.6'\n", text)
        self.assertIn("\nSHA256_LINUX_X64='linuxx64linuxx64linuxx64linuxx64'\n", text)
        self.assertIn("\nSHA256_LINUX_ARM64='linuxarm64linuxarm64linuxarm64linuxarm64'\n", text)
        self.assertIn('# Install Hider 4.5.6 (Linux x64 / arm64)', text)
        self.assertTrue(text.endswith('\nmain "$@"\n'))

    def test_install_bat_values(self) -> None:
        text = make_release.render('install.bat.in', all_values('4.5.6'), '\r\n')
        self.assertTrue(text.startswith('@echo off\r\n'))
        self.assertIn("\r\n$version = '4.5.6'\r\n", text)
        self.assertIn("\r\n$baseUrl = 'https://github.com/o/r/releases/download/v4.5.6'\r\n", text)
        self.assertIn("\r\n$sha256 = 'winx64winx64winx64winx64'\r\n", text)
        self.assertIn('rem Install Hider 4.5.6 (Windows x64)', text)

    def test_install_bat_marker(self) -> None:
        text = make_release.render('install.bat.in', all_values(), '\r\n')
        # The batch part finds the PowerShell part by the marker line; its own command line builds the marker from
        # pieces so that the first occurrence in the file is the marker line itself.
        self.assertEqual(text.count('##POWERSHELL##'), 1)
        self.assertIn("$m = '#' + '#POWERSHELL#' + '#'", text)
        batch, powershell = text.split('\r\n##POWERSHELL##\r\n')
        # cmd stops before the marker: the batch part ends with exit /b.
        self.assertEqual(batch.rstrip('\r\n').splitlines()[-1], 'exit /b %HIDER_EXIT%')
        self.assertTrue(powershell.startswith("$ErrorActionPreference = 'Stop'\r\n"))
        self.assertNotIn('%HIDER', powershell)
        self.assertIn('$env:HIDER_SCRIPT', powershell)
        self.assertIn('$env:HIDER_DEST', powershell)
        self.assertIn('$env:HIDER_NO_LAUNCH', powershell)

    def test_release_notes_links(self) -> None:
        text = make_release.render('release-notes.md.in', all_values('4.5.6'), '\n')
        base = 'https://github.com/o/r/releases/download/v4.5.6'
        self.assertIn(f'[install.bat]({base}/install.bat)', text)
        self.assertIn(f'`curl -fsSL {base}/install.sh \\| sh`', text)
        self.assertIn(f'[install.sh]({base}/install.sh)', text)
        self.assertIn('Hider-4.5.6-*.zip', text)


class PackageTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.inputs = make_inputs(self.root / 'in')
        self.out = self.root / 'out' / 'nested'

    def package(self, version: str = '1.2.3', input_dir: Path | None = None) -> Output:
        with captured() as output:
            make_release.package(version, 'owner/repo', input_dir or self.inputs, self.out)
        return output

    def test_assets(self) -> None:
        output = self.package()
        self.assertEqual(
            sorted(p.name for p in self.out.iterdir()),
            [
                'Hider-1.2.3-linux-arm64.tar.gz',
                'Hider-1.2.3-linux-x64.tar.gz',
                'Hider-1.2.3-win-x64.zip',
                'SHA256SUMS.txt',
                'install.bat',
                'install.sh',
                'release-notes.md',
            ],
        )
        self.assertEqual(
            output.out.splitlines(),
            [
                *(
                    f'Packing {self.inputs / f"Hider-{t}"} -> {self.out / make_release.archive_name("1.2.3", t)}'
                    for t in TARGETS
                ),
                f'Release assets for v1.2.3 in {self.out}',
            ],
        )

    def test_checksums(self) -> None:
        self.package()
        lines = (self.out / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines()
        names = [*(make_release.archive_name('1.2.3', t) for t in TARGETS), 'install.bat', 'install.sh']
        # Archives in target order, then the installers; the release notes are not an asset.
        self.assertEqual(lines, [f'{file_sha256(self.out / n)}  {n}' for n in names])
        self.assertNotIn(b'\r', (self.out / 'SHA256SUMS.txt').read_bytes())

    def test_installers_carry_the_archive_checksums(self) -> None:
        self.package()
        sh = (self.out / 'install.sh').read_text(encoding='utf-8')
        bat = (self.out / 'install.bat').read_bytes().decode('utf-8')
        for target in ['linux-x64', 'linux-arm64']:
            digest = file_sha256(self.out / make_release.archive_name('1.2.3', target))
            self.assertIn(f"SHA256_{target.upper().replace('-', '_')}='{digest}'", sh)
        self.assertIn(f"$sha256 = '{file_sha256(self.out / 'Hider-1.2.3-win-x64.zip')}'", bat)
        self.assertIn("BASE_URL='https://github.com/owner/repo/releases/download/v1.2.3'", sh)
        self.assertIn("$baseUrl = 'https://github.com/owner/repo/releases/download/v1.2.3'", bat)

    def test_line_endings_and_modes(self) -> None:
        self.package()
        bat = (self.out / 'install.bat').read_bytes()
        sh = (self.out / 'install.sh').read_bytes()
        self.assertEqual(bat.count(b'\n'), bat.count(b'\r\n'))
        self.assertNotIn(b'\r\r', bat)
        self.assertNotIn(b'\r', sh)
        self.assertNotIn(b'\r', (self.out / 'release-notes.md').read_bytes())
        self.assertEqual(stat.S_IMODE((self.out / 'install.sh').stat().st_mode), 0o755)

    def test_archives_hold_the_inputs_under_a_versioned_folder(self) -> None:
        self.package()
        with zipfile.ZipFile(self.out / 'Hider-1.2.3-win-x64.zip') as archive:
            self.assertIn('Hider-1.2.3-win-x64/Hider.exe', archive.namelist())
        for target in ['linux-x64', 'linux-arm64']:
            with tarfile.open(self.out / f'Hider-1.2.3-{target}.tar.gz') as archive:
                exe = archive.getmember(f'Hider-1.2.3-{target}/Hider')
                self.assertEqual(exe.mode, 0o755)
                data = archive.extractfile(exe)
                assert data is not None
                self.assertEqual(data.read().decode('utf-8'), f'#!/bin/sh\necho "Hider {target}"\n')

    def test_release_notes(self) -> None:
        self.package('2.0.1')
        notes = (self.out / 'release-notes.md').read_text(encoding='utf-8')
        self.assertIn('https://github.com/owner/repo/releases/download/v2.0.1/install.sh', notes)
        self.assertNotIn('@HIDER_', notes)

    def publish_dir(self, target: str) -> Path:
        return self.inputs / f'Hider-{target}'

    def test_default_input_is_bin_publish(self) -> None:
        with (
            mock.patch.object(make_release, 'publish_dir', side_effect=self.publish_dir),
            captured(),
        ):
            make_release.package('1.2.3', 'owner/repo', None, self.out)
        self.assertTrue((self.out / 'Hider-1.2.3-linux-arm64.tar.gz').is_file())

    def test_missing_input_stops_before_the_installers(self) -> None:
        inputs = make_inputs(self.root / 'partial', ['win-x64', 'linux-x64'])
        with self.assertRaises(SystemExit) as raised:
            self.package(input_dir=inputs)
        self.assertEqual(
            raised.exception.code, f'No Hider in {inputs / "Hider-linux-arm64"}: publish linux-arm64 first.'
        )
        self.assertFalse((self.out / 'install.sh').exists())
        self.assertFalse((self.out / 'SHA256SUMS.txt').exists())

    def test_runs_again_into_the_same_folder(self) -> None:
        self.package()
        (self.inputs / 'Hider-linux-x64' / 'new.txt').write_text('new', encoding='utf-8')
        self.package()
        lines = (self.out / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines()
        for line in lines:
            digest, name = line.split('  ')
            self.assertEqual(file_sha256(self.out / name), digest)
        with tarfile.open(self.out / 'Hider-1.2.3-linux-x64.tar.gz') as archive:
            self.assertIn('Hider-1.2.3-linux-x64/new.txt', archive.getnames())


class MainTest(unittest.TestCase):
    def run_main(self, *args: str) -> Output:
        with arguments('make_release.py', *args), captured() as output:
            make_release.main()
        return output

    def exit_code(self, *args: str) -> object:
        with self.assertRaises(SystemExit) as raised, arguments('make_release.py', *args), captured():
            make_release.main()
        return raised.exception.code

    def test_version(self) -> None:
        props = temp_dir(self) / 'p.props'
        props.write_text('<Project><PropertyGroup><Version>7.8.9</Version></PropertyGroup></Project>', encoding='utf-8')
        with mock.patch.object(make_release, 'PROPS_FILE', props):
            self.assertEqual(self.run_main('version').out, '7.8.9\n')

    def test_version_takes_no_arguments(self) -> None:
        self.assertEqual(self.exit_code('version', '1.0.0'), 2)

    def test_bump(self) -> None:
        self.assertEqual(self.run_main('bump', 'minor', '2.1.0').out, '2.2.0\n')
        self.assertEqual(self.run_main('bump', 'patch', '2.1.0').out, '2.1.1\n')
        self.assertEqual(self.run_main('bump', 'major', '2.1.0').out, '3.0.0\n')

    def test_bump_rejects_other_parts(self) -> None:
        self.assertEqual(self.exit_code('bump', 'build', '2.1.0'), 2)
        self.assertEqual(self.exit_code('bump', 'Minor', '2.1.0'), 2)

    def test_bump_needs_both_arguments(self) -> None:
        self.assertEqual(self.exit_code('bump', 'minor'), 2)
        self.assertEqual(self.exit_code('bump'), 2)

    def test_bump_invalid_version(self) -> None:
        self.assertEqual(self.exit_code('bump', 'patch', 'v1'), 'invalid version "v1" (expected MAJOR.MINOR.PATCH)')

    def next_version(self, listing: str, *args: str) -> Output:
        with mock.patch('sys.stdin', io.StringIO(listing)):
            return self.run_main('next-version', *args)

    def test_next_version_reads_stdin(self) -> None:
        listing = ls_remote(
            (OTHER, 'refs/tags/v2.1.0'), (TAG_OBJECT, 'refs/tags/v2.2.0'), (OTHER, 'refs/tags/v2.2.0^{}')
        )
        output = self.next_version(listing, '--head', HEAD)
        # Only the version on stdout (release.yml takes it with $(...)); what was decided on stderr.
        self.assertEqual((output.out, output.err), ('2.3.0\n', 'v2.2.0 -> v2.3.0 (minor)\n'))
        for part, expected in [('minor', '2.3.0'), ('patch', '2.2.1'), ('major', '3.0.0')]:
            with self.subTest(part=part):
                self.assertEqual(self.next_version(listing, '--bump', part, '--head', HEAD).out, f'{expected}\n')

    def test_next_version_without_tags(self) -> None:
        output = self.next_version('', '--head', HEAD, '--bump', 'major')
        version = make_release.props_version()
        self.assertEqual(
            (output.out, output.err), (f'{version}\n', f'No release yet: {version} (Directory.Build.props)\n')
        )

    def test_next_version_released_already(self) -> None:
        with mock.patch('sys.stdin', io.StringIO(ls_remote((HEAD, 'refs/tags/v2.0.0')))):
            self.assertEqual(
                self.exit_code('next-version', '--head', HEAD), f'main ({HEAD}) is released already as v2.0.0'
            )

    def test_next_version_arguments(self) -> None:
        with mock.patch('sys.stdin', io.StringIO('')):
            self.assertEqual(self.exit_code('next-version'), 2)
            self.assertEqual(self.exit_code('next-version', '--head', HEAD, '--bump', 'build'), 2)
            self.assertEqual(self.exit_code('next-version', '--head', HEAD, 'minor'), 2)
            self.assertEqual(
                self.exit_code('next-version', '--head', 'main'), 'invalid commit "main" (expected a full object name)'
            )

    def test_command_is_required(self) -> None:
        self.assertEqual(self.exit_code(), 2)
        self.assertEqual(self.exit_code('publish'), 2)

    def test_package(self) -> None:
        with mock.patch.object(make_release, 'package') as package:
            self.run_main('package', '2.0.0', '--repo', 'o/r', '--input-dir', 'artifacts', '--out-dir', 'release')
        package.assert_called_once_with('2.0.0', 'o/r', Path('artifacts'), Path('release'))

    def test_package_input_dir_is_optional(self) -> None:
        with mock.patch.object(make_release, 'package') as package:
            self.run_main('package', '2.0.0', '--repo', 'o/r', '--out-dir', 'release')
        package.assert_called_once_with('2.0.0', 'o/r', None, Path('release'))

    def test_package_needs_repo_and_out_dir(self) -> None:
        with mock.patch.object(make_release, 'package') as package:
            self.assertEqual(self.exit_code('package', '2.0.0', '--out-dir', 'x'), 2)
            self.assertEqual(self.exit_code('package', '2.0.0', '--repo', 'o/r'), 2)
            self.assertEqual(self.exit_code('package', '--repo', 'o/r', '--out-dir', 'x'), 2)
        package.assert_not_called()

    def test_package_invalid_version(self) -> None:
        with mock.patch.object(make_release, 'package') as package:
            self.assertEqual(
                self.exit_code('package', '2.0', '--repo', 'o/r', '--out-dir', 'x'),
                'invalid version "2.0" (expected MAJOR.MINOR.PATCH)',
            )
        package.assert_not_called()

    def test_package_repository_names(self) -> None:
        valid = ['o/r', 'Owner/Repo', 'my-org/hider.v2', 'a_b/c_d', 'hibicus3258/Hider', '1/2', 'o/.github', 'o/a..b']
        invalid = ['', 'owner', 'o/', '/r', 'o/r/x', 'o r/x', "o'wner/repo", 'o/r;x', 'o/r\n', 'o\\r']
        # The name goes into the installers' download URL: ASCII only, and no path parts . / .. (dots only).
        invalid += ['./..', './r', 'o/.', 'o/..', '.../r', 'ö/r', 'o/r\u0661', 'o/\uff52']
        for repo in valid:
            with self.subTest(repo=repo), mock.patch.object(make_release, 'package') as package:
                self.run_main('package', '1.0.0', '--repo', repo, '--out-dir', 'x')
                package.assert_called_once()
        for repo in invalid:
            with self.subTest(repo=repo), mock.patch.object(make_release, 'package') as package:
                self.assertEqual(
                    self.exit_code('package', '1.0.0', '--repo', repo, '--out-dir', 'x'),
                    f'invalid repository "{repo}" (expected OWNER/NAME)',
                )
                package.assert_not_called()

    def test_package_end_to_end(self) -> None:
        root = temp_dir(self)
        make_inputs(root / 'in')
        args = ['package', '0.0.0', '--repo', 'o/r', '--input-dir', str(root / 'in'), '--out-dir', str(root / 'out')]
        output = self.run_main(*args)
        self.assertTrue(output.out.endswith(f'Release assets for v0.0.0 in {root / "out"}\n'))
        self.assertTrue((root / 'out' / 'SHA256SUMS.txt').is_file())

    def test_help_keeps_the_description_layout(self) -> None:
        with arguments('make_release.py', '--help'), captured() as output, self.assertRaises(SystemExit) as raised:
            make_release.main()
        self.assertEqual(raised.exception.code, 0)
        self.assertIn('\n    python scripts/make_release.py bump minor 2.1.0\n', output.out)
        for command in ['version', 'bump', 'next-version', 'package']:
            self.assertIn(command, output.out)

    def test_as_a_script(self) -> None:
        with captured() as output:
            run_as_main('make_release.py', 'bump', 'patch', '1.2.3')
        self.assertEqual(output.out, '1.2.4\n')
        with captured() as output:
            run_as_main('make_release.py', 'version')
        self.assertEqual(output.out, f'{make_release.props_version()}\n')
        with captured() as output, mock.patch('sys.stdin', io.StringIO(ls_remote((OTHER, 'refs/tags/v5.0.0')))):
            run_as_main('make_release.py', 'next-version', '--head', HEAD, '--bump', 'patch')
        self.assertEqual(output.out, '5.0.1\n')


class ShaSumsFormatTest(unittest.TestCase):
    def test_sha256sum_check_format(self) -> None:
        # "<hex>  <name>": the format sha256sum -c reads (two spaces: text mode).
        root = temp_dir(self)
        make_inputs(root / 'in')
        with captured():
            make_release.package('1.0.0', 'o/r', root / 'in', root / 'out')
        for line in io.StringIO((root / 'out' / 'SHA256SUMS.txt').read_text(encoding='utf-8')):
            self.assertRegex(line, re.compile(r'^[0-9a-f]{64}  [\w.-]+\n$'))
