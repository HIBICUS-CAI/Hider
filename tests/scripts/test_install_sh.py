"""The Linux installer install.sh (scripts/release/install.sh.in as make_release.py renders it), run by a real shell.

Every command the installer could reach the network or the system with is a stub in a private PATH: uname (the machine),
pgrep (a running Hider), curl / wget (the download: they copy from a local folder), shasum and nohup (the launch). The
file tools (tar, cp, sed, ...) are the real ones. The stubs log their arguments; the launch runs in the background, so
the test waits for an inherited pipe to close instead of sleeping.
"""

from __future__ import annotations

import hashlib
import os
import shutil
import stat
import subprocess
import unittest
from dataclasses import dataclass
from pathlib import Path

import install
import make_release

from . import SCRIPTS_DIR
from .support import temp_dir

VERSION = '9.8.7'
BASE_URL = 'https://github.com/owner/repo/releases/download/v9.8.7'
RIDS = ['linux-x64', 'linux-arm64']
# Real commands the installer (and the stubs) use, linked into the private PATH.
TOOLS = ['cat', 'chmod', 'cp', 'cut', 'dirname', 'gzip', 'mkdir', 'mktemp', 'rm', 'sed', 'tar', 'sha256sum', 'basename']
SH = shutil.which('sh') or '/bin/sh'


def real(name: str) -> str:
    path = shutil.which(name)
    assert path is not None, f'{name} is needed to run these tests'
    return path


def shells() -> list[str]:
    """The POSIX shells on this machine to run the installer with (sh is dash on Ubuntu, bash elsewhere)."""
    found: dict[str, str] = {}
    for name in ['sh', 'dash', 'bash']:
        path = shutil.which(name)
        if path is not None:
            found.setdefault(os.path.realpath(path), path)
    return list(found.values())


STUBS = {
    'uname': """
case "$1" in
    -s) printf '%s\\n' "${FAKE_UNAME_S:-Linux}" ;;
    -m) printf '%s\\n' "${FAKE_UNAME_M:-x86_64}" ;;
    *) exit 1 ;;
esac
""",
    'pgrep': """
printf 'pgrep %s\\n' "$*" >> "$STUB_LOG"
exit "${FAKE_PGREP_STATUS:-1}"
""",
    'curl': """
printf 'curl %s\\n' "$*" >> "$STUB_LOG"
out=''
while [ $# -gt 1 ]; do
    if [ "$1" = -o ]; then out="$2"; fi
    shift
done
url="$1"
if [ -n "${FAKE_KILL_INSTALLER:-}" ]; then kill -TERM "$PPID"; fi
if [ -n "${FAKE_DOWNLOAD_FAIL:-}" ]; then exit 22; fi
cp "$STORE/${url##*/}" "$out" || exit 22
""",
    'wget': """
printf 'wget %s\\n' "$*" >> "$STUB_LOG"
out=''
while [ $# -gt 1 ]; do
    if [ "$1" = -O ]; then out="$2"; fi
    shift
done
url="$1"
if [ -n "${FAKE_DOWNLOAD_FAIL:-}" ]; then exit 8; fi
cp "$STORE/${url##*/}" "$out" || exit 8
""",
    'shasum': """
printf 'shasum %s\\n' "$*" >> "$STUB_LOG"
[ "$1" = -a ] && [ "$2" = 256 ] || exit 1
exec "$REAL_SHA256SUM" "$3"
""",
    'nohup': """
printf 'nohup %s\\n' "$*" >> "$STUB_LOG"
""",
    # cp that fails when copying into $FAKE_CP_FAIL_DEST (the install folder), like a file in use.
    'cp-failing': """
for last; do :; done
if [ "$last" = "$FAKE_CP_FAIL_DEST/" ]; then
    echo 'cp: cannot create regular file: Text file busy' >&2
    exit 1
fi
exec "$REAL_CP" "$@"
""",
}


@dataclass
class Result:
    code: int
    out: str
    err: str
    log: list[str]


@dataclass
class Machine:
    """Which commands the private PATH offers."""

    curl: bool = True
    wget: bool = False
    pgrep: bool = True
    sha256sum: bool = True
    shasum: bool = False
    cp_fails: bool = False


class InstallerTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(os.path.realpath(temp_dir(self)))
        self.release = self.root / 'release'
        self.store = self.root / 'store'
        self.home = self.root / 'home'
        self.data = self.root / 'data'
        self.tmp = self.root / 'tmp'
        for folder in [self.release, self.store, self.home, self.tmp]:
            folder.mkdir()
        self.log = self.root / 'stub.log'
        self.digests: dict[str, str] = {}
        for rid in RIDS:
            self.digests[rid] = self.make_archive(self.store, rid)
        self.script = self.write_installer(self.release)

    # Fixtures

    def make_archive(
        self, folder: Path, rid: str, files: dict[str, tuple[bytes, int]] | None = None, version: str = VERSION
    ) -> str:
        """Hider-<version>-<rid>.tar.gz in folder, made by make_release.make_archive; return its SHA-256."""
        src = self.root / 'archive-src' / f'{folder.name}-{rid}'
        if src.exists():
            shutil.rmtree(src)
        layout = files if files is not None else self.default_files(rid)
        for name, (data, mode) in layout.items():
            path = src / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            path.chmod(mode)
        src.mkdir(parents=True, exist_ok=True)
        out = folder / f'Hider-{version}-{rid}.tar.gz'
        make_release.make_archive(src, out, f'Hider-{version}-{rid}')
        return hashlib.sha256(out.read_bytes()).hexdigest()

    @staticmethod
    def default_files(rid: str) -> dict[str, tuple[bytes, int]]:
        return {
            'Hider': (f'#!/bin/sh\necho "Hider {rid}"\n'.encode(), 0o644),
            'Assets/hider.svg': (b'<svg/>', 0o644),
            'libhidercap.so': (rid.encode(), 0o644),
        }

    def write_installer(self, folder: Path, digests: dict[str, str] | None = None) -> Path:
        values = {'HIDER_VERSION': VERSION, 'HIDER_BASE_URL': BASE_URL, 'HIDER_SHA256_WIN_X64': '0' * 64}
        for rid, digest in (digests or self.digests).items():
            values[f'HIDER_SHA256_{rid.upper().replace("-", "_")}'] = digest
        path = folder / 'install.sh'
        path.write_text(make_release.render('install.sh.in', values, '\n'), encoding='utf-8')
        path.chmod(0o755)
        return path

    def put_next_to_script(self, rid: str = 'linux-x64') -> Path:
        archive = self.store / f'Hider-{VERSION}-{rid}.tar.gz'
        return Path(shutil.copy2(archive, self.release / archive.name))

    def make_bin(self, machine: Machine) -> Path:
        folder = self.root / 'bin'
        if folder.exists():
            shutil.rmtree(folder)
        folder.mkdir()
        tools = [t for t in TOOLS if t != 'sha256sum' or machine.sha256sum]
        for tool in tools:
            if machine.cp_fails and tool == 'cp':
                continue
            (folder / tool).symlink_to(real(tool))
        stubs = ['uname', 'nohup']
        stubs += ['curl'] if machine.curl else []
        stubs += ['wget'] if machine.wget else []
        stubs += ['pgrep'] if machine.pgrep else []
        stubs += ['shasum'] if machine.shasum else []
        stubs += ['cp-failing'] if machine.cp_fails else []
        for stub in stubs:
            path = folder / ('cp' if stub == 'cp-failing' else stub)
            path.write_text(f'#!{SH}\n{STUBS[stub].lstrip()}', encoding='utf-8')
            path.chmod(0o755)
        return folder

    def run_installer(
        self,
        *args: str,
        machine: Machine | None = None,
        env: dict[str, str | None] | None = None,
        shell: str = SH,
        cwd: Path | None = None,
        stdin: str | None = None,
        script: Path | None = None,
    ) -> Result:
        """Run `sh install.sh <args>` (or the script on stdin: `... | sh`); wait for everything it started."""
        variables = {
            'PATH': str(self.make_bin(machine or Machine())),
            'HOME': str(self.home),
            'TMPDIR': str(self.tmp),
            'XDG_DATA_HOME': str(self.data),
            'HIDER_NO_LAUNCH': '1',
            'STUB_LOG': str(self.log),
            'STORE': str(self.store),
            'REAL_SHA256SUM': real('sha256sum'),
            'REAL_CP': real('cp'),
        }
        for key, value in (env or {}).items():
            if value is None:
                variables.pop(key, None)
            else:
                variables[key] = value
        self.log.write_text('', encoding='utf-8')
        # Piped (curl ... | sh): $0 is the shell's name, as when sh is started from PATH.
        command = ['sh'] if stdin is not None else [shell, str(script or self.script), *args]
        read_end, write_end = os.pipe()
        try:
            process = subprocess.Popen(
                command,
                executable=shell,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                env=variables,
                cwd=cwd or self.root,
                pass_fds=(write_end,),
            )
            os.close(write_end)
            write_end = -1
            out, err = process.communicate((stdin or '').encode('utf-8'), timeout=120)
            # The pipe closes when the installer and everything it started in the background have exited.
            with os.fdopen(read_end, 'rb') as pipe:
                read_end = -1
                pipe.read()
        finally:
            for fd in [read_end, write_end]:
                if fd >= 0:
                    os.close(fd)
        log = self.log.read_text(encoding='utf-8').splitlines()
        return Result(process.returncode, out.decode('utf-8'), err.decode('utf-8'), log)

    # Checks

    def assert_ok(self, result: Result) -> None:
        self.assertEqual(result.code, 0, f'stdout:\n{result.out}\nstderr:\n{result.err}')

    def assert_error(self, result: Result, message: str, code: int = 1) -> None:
        self.assertEqual(result.code, code, result.out)
        self.assertEqual(result.err, f'error: {message}\n')
        self.assert_cleaned_up()

    def assert_cleaned_up(self) -> None:
        self.assertEqual(list(self.tmp.iterdir()), [], 'the work folder is removed')

    def assert_installed(self, dest: Path, rid: str = 'linux-x64', data: Path | None = None) -> None:
        self.assertEqual((dest / 'Hider').read_text(encoding='utf-8'), f'#!/bin/sh\necho "Hider {rid}"\n')
        self.assertEqual(stat.S_IMODE((dest / 'Hider').stat().st_mode), 0o755)
        self.assertEqual((dest / 'libhidercap.so').read_bytes(), rid.encode())
        self.assertTrue((dest / 'Assets' / 'hider.svg').is_file())
        entry = (data or self.data) / 'applications' / 'Hider.desktop'
        self.assertEqual(entry.read_text(encoding='utf-8'), self.expected_entry(dest))
        self.assert_cleaned_up()

    @staticmethod
    def expected_entry(dest: Path) -> str:
        """The entry scripts/install.py writes for the same folder: the two installers agree."""
        return '\n'.join(
            [
                '[Desktop Entry]',
                'Type=Application',
                'Name=Hider',
                'Comment=画面の一部をモザイクやパターンで覆って隠す',
                f'Exec={install.desktop_exec_arg(str(dest / "Hider"))}',
                f'Icon={install.desktop_string(str(dest / "Assets" / "hider.svg"))}',
                'Terminal=false',
                'Categories=Utility;',
                'StartupWMClass=Hider',
                '',
            ]
        )

    def calls(self, result: Result, command: str) -> list[str]:
        return [line for line in result.log if line.split(' ', 1)[0] == command]


class OptionsTest(InstallerTest):
    def test_help(self) -> None:
        for option in ['-h', '--help']:
            with self.subTest(option=option):
                result = self.run_installer(option, env={'FAKE_UNAME_S': 'Darwin'})
                self.assert_ok(result)
                self.assertEqual(
                    result.out,
                    'usage: sh install.sh [-d <folder>]\n'
                    f'Install Hider {VERSION}. Default folder: $XDG_DATA_HOME/Hider (~/.local/share/Hider).\n',
                )
                self.assertEqual(result.err, '')
                self.assertFalse(self.data.exists())
                self.assertEqual(result.log, [])

    def test_unknown_argument(self) -> None:
        for args in [('--force',), ('folder',), ('-d', 'x', 'extra')]:
            with self.subTest(args=args):
                result = self.run_installer(*args)
                self.assertEqual(result.code, 2)
                self.assertEqual(result.out, '')
                self.assertTrue(result.err.startswith('usage: sh install.sh [-d <folder>]\n'))
                self.assertFalse(self.data.exists())

    def test_dest_needs_a_folder(self) -> None:
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                self.assert_error(self.run_installer(option), f'{option} needs a folder')
                self.assertFalse(self.data.exists())

    def test_dest(self) -> None:
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                dest = self.root / f'apps{option}' / 'Hider'
                result = self.run_installer(option, str(dest))
                self.assert_ok(result)
                self.assert_installed(dest)
                self.assertTrue(result.out.endswith(f'Installed Hider {VERSION} to {dest}\n'))
                # The desktop entry still goes to the data home.
                self.assertFalse((self.data / 'Hider').exists())

    def test_relative_dest_is_made_absolute(self) -> None:
        work = self.root / 'cwd'
        work.mkdir()
        result = self.run_installer('-d', 'rel/Hider', cwd=work)
        self.assert_ok(result)
        self.assert_installed(work / 'rel' / 'Hider')
        self.assertIn(f'Copying to {work}/rel/Hider\n', result.out)

    def test_last_dest_wins(self) -> None:
        result = self.run_installer('-d', str(self.root / 'first'), '--dest', str(self.root / 'second'))
        self.assert_ok(result)
        self.assertFalse((self.root / 'first').exists())
        self.assert_installed(self.root / 'second')


class DefaultFolderTest(InstallerTest):
    def test_xdg_data_home(self) -> None:
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertEqual(
            result.out.splitlines()[-3:],
            [
                f'Copying to {self.data}/Hider',
                f'Created desktop entry: {self.data}/applications/Hider.desktop',
                f'Installed Hider {VERSION} to {self.data}/Hider',
            ],
        )
        self.assertEqual(result.err.count('error'), 0)

    def test_without_xdg_data_home(self) -> None:
        for value in [None, '']:
            with self.subTest(value=value):
                result = self.run_installer(env={'XDG_DATA_HOME': value})
                self.assert_ok(result)
                share = self.home / '.local' / 'share'
                self.assert_installed(share / 'Hider', data=share)
                shutil.rmtree(self.home / '.local')


class MachineTest(InstallerTest):
    def test_architectures(self) -> None:
        for machine, rid in [
            ('x86_64', 'linux-x64'),
            ('amd64', 'linux-x64'),
            ('aarch64', 'linux-arm64'),
            ('arm64', 'linux-arm64'),
        ]:
            with self.subTest(machine=machine):
                dest = self.root / f'dest-{machine}'
                result = self.run_installer('-d', str(dest), env={'FAKE_UNAME_M': machine})
                self.assert_ok(result)
                self.assert_installed(dest, rid)
                self.assertIn(f'Downloading {BASE_URL}/Hider-{VERSION}-{rid}.tar.gz\n', result.out)

    def test_unsupported_architecture(self) -> None:
        for machine in ['armv7l', 'i686', 'riscv64', 'X86_64']:
            with self.subTest(machine=machine):
                result = self.run_installer(env={'FAKE_UNAME_M': machine})
                self.assert_error(result, f'unsupported architecture {machine} (x86_64 and aarch64 are supported)')
                self.assertEqual(self.calls(result, 'curl'), [])

    def test_not_linux(self) -> None:
        for system in ['Darwin', 'FreeBSD', 'MINGW64_NT-10.0', 'linux']:
            with self.subTest(system=system):
                result = self.run_installer(env={'FAKE_UNAME_S': system})
                self.assert_error(result, 'this installer is for Linux; on Windows use install.bat')
                self.assertFalse(self.data.exists())

    def test_running_hider_stops_the_install(self) -> None:
        result = self.run_installer(env={'FAKE_PGREP_STATUS': '0'})
        self.assert_error(result, 'Hider is running: quit it from the tray menu and run this again')
        self.assertEqual(self.calls(result, 'pgrep'), ['pgrep -x Hider'])
        self.assertEqual(self.calls(result, 'curl'), [])
        self.assertFalse(self.data.exists())

    def test_hider_not_running(self) -> None:
        result = self.run_installer()
        self.assert_ok(result)
        self.assertEqual(self.calls(result, 'pgrep'), ['pgrep -x Hider'])

    def test_without_pgrep(self) -> None:
        result = self.run_installer(machine=Machine(pgrep=False))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')

    def test_shasum_instead_of_sha256sum(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=False, shasum=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertEqual(len(self.calls(result, 'shasum')), 1)
        self.assertTrue(self.calls(result, 'shasum')[0].startswith('shasum -a 256 '))

    def test_sha256sum_preferred(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=True, shasum=True))
        self.assert_ok(result)
        self.assertEqual(self.calls(result, 'shasum'), [])

    def test_no_checksum_tool(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=False, shasum=False))
        self.assert_error(result, 'sha256sum (coreutils) was not found')
        self.assertEqual(self.calls(result, 'curl'), [])


class DownloadTest(InstallerTest):
    def archive_url(self, rid: str = 'linux-x64') -> str:
        return f'{BASE_URL}/Hider-{VERSION}-{rid}.tar.gz'

    def test_curl(self) -> None:
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        (call,) = self.calls(result, 'curl')
        work_archive = call.split(' -o ', 1)[1].split(' ')[0]
        self.assertEqual(
            call,
            f'curl -fL --proto =https --proto-redir =https --tlsv1.2 --retry 3 -o {work_archive} {self.archive_url()}',
        )
        # Downloaded into the work folder under TMPDIR, which is removed afterwards.
        self.assertTrue(work_archive.startswith(f'{self.tmp}/'))
        self.assertTrue(work_archive.endswith(f'/Hider-{VERSION}-linux-x64.tar.gz'))
        self.assertIn(f'Downloading {self.archive_url()}\n', result.out)

    def test_curl_failing(self) -> None:
        result = self.run_installer(env={'FAKE_DOWNLOAD_FAIL': '1'})
        self.assert_error(result, f'download failed: {self.archive_url()}')
        self.assertFalse(self.data.exists())

    def test_wget(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        (call,) = self.calls(result, 'wget')
        self.assertTrue(call.startswith(f'wget --https-only -O {self.tmp}/'))
        self.assertTrue(call.endswith(f'/Hider-{VERSION}-linux-x64.tar.gz {self.archive_url()}'))

    def test_wget_failing(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=True), env={'FAKE_DOWNLOAD_FAIL': '1'})
        self.assert_error(result, f'download failed: {self.archive_url()}')

    def test_curl_preferred_over_wget(self) -> None:
        result = self.run_installer(machine=Machine(curl=True, wget=True))
        self.assert_ok(result)
        self.assertEqual(len(self.calls(result, 'curl')), 1)
        self.assertEqual(self.calls(result, 'wget'), [])

    def test_no_download_tool(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=False))
        self.assert_error(result, 'curl or wget is needed to download Hider')
        self.assertIn(f'Downloading {self.archive_url()}\n', result.out)

    def test_archive_missing_on_the_server(self) -> None:
        (self.store / f'Hider-{VERSION}-linux-x64.tar.gz').unlink()
        result = self.run_installer()
        self.assertEqual(result.code, 1)
        self.assertTrue(result.err.endswith(f'error: download failed: {self.archive_url()}\n'))
        self.assert_cleaned_up()

    def test_archive_next_to_the_script(self) -> None:
        local = self.put_next_to_script()
        result = self.run_installer(machine=Machine(curl=True, wget=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertIn(f'Using {local}\n', result.out)
        self.assertNotIn('Downloading', result.out)
        self.assertEqual(self.calls(result, 'curl') + self.calls(result, 'wget'), [])
        # Copied, not moved.
        self.assertTrue(local.is_file())

    def test_archive_for_the_other_architecture_is_not_used(self) -> None:
        self.put_next_to_script('linux-arm64')
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider', 'linux-x64')
        self.assertEqual(len(self.calls(result, 'curl')), 1)

    def test_script_started_by_a_relative_path(self) -> None:
        local = self.put_next_to_script()
        result = self.run_installer(cwd=self.release, script=Path('install.sh'))
        self.assert_ok(result)
        self.assertIn(f'Using {local}\n', result.out)
        other = self.root / 'other'
        other.mkdir()
        result = self.run_installer(cwd=other, script=Path('../release/install.sh'))
        self.assert_ok(result)
        self.assertIn(f'Using {local}\n', result.out)

    def test_piped_to_sh_uses_the_current_directory(self) -> None:
        self.put_next_to_script()
        text = self.script.read_text(encoding='utf-8')
        result = self.run_installer(stdin=text, cwd=self.release)
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertIn(f'Using {self.release}/Hider-{VERSION}-linux-x64.tar.gz\n', result.out)

    def test_piped_to_sh_downloads(self) -> None:
        result = self.run_installer(stdin=self.script.read_text(encoding='utf-8'), cwd=self.root)
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertEqual(len(self.calls(result, 'curl')), 1)

    def test_cut_short_download_of_the_script_runs_nothing(self) -> None:
        # curl ... | sh with the connection lost: main is never called, so nothing runs.
        text = self.script.read_text(encoding='utf-8')
        cut = text[: text.index('    tar -xzf')]
        result = self.run_installer(stdin=cut, cwd=self.root)
        self.assertNotEqual(result.code, 0)
        self.assertEqual(result.log, [])
        self.assertEqual(result.out, '')
        self.assertFalse(self.data.exists())
        # Up to the last line but without it: defines main only.
        result = self.run_installer(stdin=text[: text.rindex('main "$@"')], cwd=self.root)
        self.assert_ok(result)
        self.assertEqual((result.log, result.out, result.err), ([], '', ''))
        self.assertFalse(self.data.exists())


class ChecksumTest(InstallerTest):
    def test_mismatch_of_a_download(self) -> None:
        expected = self.digests['linux-x64']
        actual = self.make_archive(self.store, 'linux-x64', {'Hider': (b'tampered', 0o755)})
        result = self.run_installer()
        self.assert_error(
            result, f'SHA-256 mismatch for Hider-{VERSION}-linux-x64.tar.gz (expected {expected}, got {actual})'
        )
        self.assertFalse(self.data.exists())

    def test_mismatch_of_a_local_archive(self) -> None:
        self.make_archive(self.release, 'linux-x64', {'Hider': (b'other', 0o755)})
        result = self.run_installer()
        self.assertEqual(result.code, 1)
        self.assertIn('SHA-256 mismatch', result.err)
        self.assertEqual(self.calls(result, 'curl'), [])
        self.assertFalse(self.data.exists())

    def test_each_architecture_has_its_own_checksum(self) -> None:
        # Swapping the two checksums makes both architectures fail.
        swapped = {'linux-x64': self.digests['linux-arm64'], 'linux-arm64': self.digests['linux-x64']}
        script = self.write_installer(self.root, swapped)
        for machine in ['x86_64', 'aarch64']:
            with self.subTest(machine=machine):
                result = self.run_installer(env={'FAKE_UNAME_M': machine}, script=script)
                self.assertEqual(result.code, 1)
                self.assertIn('SHA-256 mismatch', result.err)

    def test_archive_without_the_executable(self) -> None:
        digest = self.make_archive(self.store, 'linux-x64', {'Assets/hider.svg': (b'<svg/>', 0o644)})
        script = self.write_installer(self.root, {'linux-x64': digest, 'linux-arm64': self.digests['linux-arm64']})
        result = self.run_installer(script=script)
        name = f'Hider-{VERSION}-linux-x64'
        self.assert_error(result, f'{name}.tar.gz does not contain {name}/Hider')
        self.assertFalse(self.data.exists())

    def test_archive_of_another_version(self) -> None:
        # The right file name with a different folder inside.
        other = self.root / 'other'
        other.mkdir()
        self.make_archive(other, 'linux-x64', version='1.0.0')
        archive = self.store / f'Hider-{VERSION}-linux-x64.tar.gz'
        shutil.move(other / 'Hider-1.0.0-linux-x64.tar.gz', archive)
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        script = self.write_installer(self.root, {'linux-x64': digest, 'linux-arm64': self.digests['linux-arm64']})
        result = self.run_installer(script=script)
        self.assertEqual(result.code, 1)
        self.assertIn(f'does not contain Hider-{VERSION}-linux-x64/Hider', result.err)

    def test_corrupt_archive_with_a_matching_checksum(self) -> None:
        archive = self.store / f'Hider-{VERSION}-linux-x64.tar.gz'
        archive.write_bytes(b'not a tarball')
        digest = hashlib.sha256(b'not a tarball').hexdigest()
        script = self.write_installer(self.root, {'linux-x64': digest, 'linux-arm64': self.digests['linux-arm64']})
        result = self.run_installer(script=script)
        # set -e stops at tar, with tar's own message.
        self.assertNotEqual(result.code, 0)
        self.assertNotIn('Installed', result.out)
        self.assertIn('tar', result.err)
        self.assertFalse(self.data.exists())
        self.assert_cleaned_up()


class ExistingInstallTest(InstallerTest):
    def test_replaces_an_existing_install(self) -> None:
        dest = self.data / 'Hider'
        (dest / 'Assets').mkdir(parents=True)
        (dest / 'Hider').write_text('old', encoding='utf-8')
        (dest / 'Hider').chmod(0o700)
        (dest / 'Assets' / 'hider.svg').write_text('old', encoding='utf-8')
        (dest / 'Assets' / 'old.png').write_bytes(b'old')
        apps = self.data / 'applications'
        apps.mkdir()
        (apps / 'Hider.desktop').write_text('[Desktop Entry]\nExec=/old\n', encoding='utf-8')
        (apps / 'other.desktop').write_text('other', encoding='utf-8')
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(dest)
        self.assertEqual((dest / 'Assets' / 'hider.svg').read_bytes(), b'<svg/>')
        # Overwrites, does not clean up: files the new version does not have stay.
        self.assertEqual((dest / 'Assets' / 'old.png').read_bytes(), b'old')
        self.assertEqual((apps / 'other.desktop').read_text(encoding='utf-8'), 'other')

    def test_installing_twice(self) -> None:
        self.assert_ok(self.run_installer())
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')

    def test_sets_the_executable_bit(self) -> None:
        # make_release sets 0755 in the archive; the installer sets it again on the installed file.
        dest = self.data / 'Hider'
        dest.mkdir(parents=True)
        (dest / 'Hider').write_text('old', encoding='utf-8')
        (dest / 'Hider').chmod(0o600)
        self.assert_ok(self.run_installer())
        self.assertEqual(stat.S_IMODE((dest / 'Hider').stat().st_mode), 0o755)

    def test_copy_failure(self) -> None:
        dest = self.data / 'Hider'
        result = self.run_installer(machine=Machine(cp_fails=True), env={'FAKE_CP_FAIL_DEST': str(dest)})
        self.assertEqual(result.code, 1)
        self.assertTrue(result.err.endswith('error: copy failed: quit Hider if it is running and run this again\n'))
        self.assertFalse((self.data / 'applications').exists())
        self.assert_cleaned_up()

    def test_dest_is_a_file(self) -> None:
        self.data.mkdir()
        (self.data / 'Hider').write_text('a file', encoding='utf-8')
        result = self.run_installer()
        # set -e stops at mkdir -p, with its own message.
        self.assertNotEqual(result.code, 0)
        self.assertIn('mkdir', result.err)
        self.assertNotIn('Installed', result.out)
        self.assert_cleaned_up()


class DesktopEntryTest(InstallerTest):
    def test_paths_are_escaped_like_install_py(self) -> None:
        for name in [
            'plain',
            'with space',
            'q"uote',
            'back`tick',
            'dollar$HOME',
            'back\\slash',
            'new\\nline \\t tab',
            '100%',
            '画面',
            "it's",
        ]:
            with self.subTest(name=name):
                dest = self.root / 'odd' / name / 'Hider'
                result = self.run_installer('-d', str(dest))
                self.assert_ok(result)
                self.assert_installed(dest)
                # say prints the path as it is (printf, not echo).
                self.assertIn(f'Copying to {dest}\n', result.out)

    def test_entry_lines(self) -> None:
        self.assert_ok(self.run_installer())
        lines = (self.data / 'applications' / 'Hider.desktop').read_text(encoding='utf-8').splitlines()
        self.assertEqual(lines[4], f'Exec="{self.data}/Hider/Hider"')
        self.assertEqual(lines[5], f'Icon={self.data}/Hider/Assets/hider.svg')
        self.assertEqual(lines[-1], 'StartupWMClass=Hider')


class LaunchTest(InstallerTest):
    def launches(self, env: dict[str, str]) -> list[str]:
        variables: dict[str, str | None] = {'HIDER_NO_LAUNCH': None, 'DISPLAY': None, 'WAYLAND_DISPLAY': None}
        variables.update(env)
        result = self.run_installer(env=variables)
        self.assert_ok(result)
        started = 'Started Hider (tray icon).\n'
        calls = self.calls(result, 'nohup')
        self.assertEqual(result.out.endswith(started), bool(calls))
        return calls

    def test_starts_with_a_display(self) -> None:
        expected = [f'nohup {self.data}/Hider/Hider']
        self.assertEqual(self.launches({'DISPLAY': ':0'}), expected)
        self.assertEqual(self.launches({'WAYLAND_DISPLAY': 'wayland-0'}), expected)
        self.assertEqual(self.launches({'DISPLAY': ':1', 'WAYLAND_DISPLAY': 'wayland-0'}), expected)

    def test_empty_no_launch_still_starts(self) -> None:
        self.assertEqual(len(self.launches({'DISPLAY': ':0', 'HIDER_NO_LAUNCH': ''})), 1)

    def test_no_launch(self) -> None:
        for value in ['1', '0', 'yes']:
            with self.subTest(value=value):
                self.assertEqual(self.launches({'DISPLAY': ':0', 'HIDER_NO_LAUNCH': value}), [])

    def test_no_display(self) -> None:
        self.assertEqual(self.launches({}), [])
        self.assertEqual(self.launches({'DISPLAY': '', 'WAYLAND_DISPLAY': ''}), [])


class InterruptTest(InstallerTest):
    def test_terminated_during_the_download(self) -> None:
        result = self.run_installer(env={'FAKE_KILL_INSTALLER': '1'})
        self.assertEqual(result.code, 130)
        self.assertNotIn('Installed', result.out)
        self.assertFalse(self.data.exists())
        self.assert_cleaned_up()


class ShellsTest(InstallerTest):
    """The main paths under every POSIX shell on the machine (dash is /bin/sh on Ubuntu and Debian)."""

    def test_install_under_each_shell(self) -> None:
        for shell in shells():
            with self.subTest(shell=shell):
                dest = self.root / 'odd' / Path(shell).name / 'a "b" $c `d` \\e \\n 100%'
                result = self.run_installer('-d', str(dest), shell=shell, env={'DISPLAY': ':0', 'HIDER_NO_LAUNCH': ''})
                self.assert_ok(result)
                self.assert_installed(dest)
                self.assertEqual(self.calls(result, 'nohup'), [f'nohup {dest}/Hider'])

    def test_errors_under_each_shell(self) -> None:
        for shell in shells():
            with self.subTest(shell=shell):
                self.assert_error(
                    self.run_installer(shell=shell, env={'FAKE_DOWNLOAD_FAIL': '1'}),
                    f'download failed: {BASE_URL}/Hider-{VERSION}-linux-x64.tar.gz',
                )
                self.assertEqual(self.run_installer('-x', shell=shell).code, 2)


class SyntaxTest(unittest.TestCase):
    """`sh -n` on the template and the rendered script, `bash -n` on the Xvfb smoke test (CI runs shellcheck too)."""

    def check(self, shell: str, path: Path) -> None:
        result = subprocess.run([shell, '-n', str(path)], capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_install_sh(self) -> None:
        root = temp_dir(self)
        rendered = root / 'install.sh'
        values = {'HIDER_VERSION': '1.0.0', 'HIDER_BASE_URL': 'https://x/y'}
        values |= {f'HIDER_SHA256_{k}': '0' * 64 for k in ['WIN_X64', 'LINUX_X64', 'LINUX_ARM64']}
        rendered.write_text(make_release.render('install.sh.in', values, '\n'), encoding='utf-8')
        for shell in shells():
            with self.subTest(shell=shell):
                self.check(shell, SCRIPTS_DIR / 'release' / 'install.sh.in')
                self.check(shell, rendered)

    def test_xvfb_strip_smoke(self) -> None:
        path = SCRIPTS_DIR / 'xvfb-strip-smoke.sh'
        self.assertTrue(path.read_text(encoding='utf-8').startswith('#!/usr/bin/env bash\n'))
        self.check(real('bash'), path)
        self.assertTrue(os.access(path, os.X_OK), 'xvfb-strip-smoke.sh is executable')

    def test_windows_stress_is_a_powershell_script(self) -> None:
        # No PowerShell on the Linux runner: only its shape is checked here (CI's Windows job runs it).
        text = (SCRIPTS_DIR / 'windows-stress.ps1').read_text(encoding='utf-8-sig')
        self.assertTrue(text.startswith('#Requires -Version 7\n'))
        self.assertEqual(text.count('{'), text.count('}'))
        self.assertEqual(text.count('('), text.count(')'))
