"""The Linux installer install.sh (scripts/release/install.sh.in as make_release.py renders it), run by a real shell.

Every command the installer could reach the network or the system with is a stub in a private PATH: uname (the machine),
pgrep (a running Hider), curl / wget (the download: they copy from a local folder), shasum and nohup (the launch). The
file tools (tar, cp, sed, ...) are the real ones. The stubs log their arguments; the launch runs in the background, so
the test waits for an inherited pipe to close instead of sleeping.

Every test runs under each POSIX shell found (load_tests): dash (sh on Debian / Ubuntu), bash started as sh (sh on
Fedora / Arch: POSIX mode) and as bash, and busybox sh (Alpine). On GitHub Actions all of them must be there.
"""

from __future__ import annotations

import hashlib
import os
import select
import shutil
import stat
import subprocess
import sys
import time
import unittest
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import ClassVar
from unittest import mock

import install
import make_release

from . import SCRIPTS_DIR
from .support import temp_dir

VERSION = '9.8.7'
BASE_URL = 'https://github.com/owner/repo/releases/download/v9.8.7'
RIDS = ['linux-x64', 'linux-arm64']
# Real commands the installer (and the stubs) use, linked into the private PATH.
TOOLS = ['cat', 'chmod', 'cp', 'cut', 'dirname', 'gzip', 'mkdir', 'mktemp', 'rm', 'sed', 'tar', 'sha256sum', 'basename']
# The stubs' interpreter.
SH = shutil.which('sh') or '/bin/sh'
# How long whatever the installer started in the background may outlive it.
BACKGROUND_TIMEOUT_S = 30


def real(name: str) -> str:
    path = shutil.which(name)
    assert path is not None, f'{name} is needed to run these tests'
    return path


@dataclass(frozen=True)
class Shell:
    """A shell to run the installer with: the executable and the name it is started as (argv[0]: bash started as sh
    runs in POSIX mode, busybox picks its applet by it)."""

    name: str
    path: str
    argv0: str = 'sh'


def shells() -> list[Shell]:
    """The POSIX shells on this machine to run the installer with; on GitHub Actions all of them must be found."""
    found: list[Shell] = []
    sh = shutil.which('sh')
    if sh is not None:
        found.append(Shell('sh', sh))
    for name, argv0, label in [('dash', 'sh', 'dash'), ('bash', 'sh', 'bash_as_sh'), ('bash', 'bash', 'bash')]:
        path = shutil.which(name)
        if path is not None:
            found.append(Shell(label, path, argv0))
    busybox = shutil.which('busybox')
    if busybox is not None:
        found.append(Shell('busybox', busybox))
    if os.environ.get('GITHUB_ACTIONS') == 'true':
        missing = {'sh', 'dash', 'bash_as_sh', 'bash', 'busybox'} - {shell.name for shell in found}
        assert not missing, f'shells missing on the runner: {sorted(missing)} (python-lint installs busybox)'
    # The same program started the same way once only (sh is a link to dash on Ubuntu).
    unique: dict[tuple[str, str], Shell] = {}
    for shell in found:
        unique.setdefault((os.path.realpath(shell.path), shell.argv0), shell)
    return list(unique.values())


SHELLS = shells()
DEFAULT_SHELL = SHELLS[0]


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
    # load_tests makes one subclass per shell; run by name, a test uses the first one (sh).
    shell: ClassVar[Shell] = DEFAULT_SHELL

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
        argv0 = self.shell.argv0
        command = [argv0] if stdin is not None else [argv0, str(script or self.script), *args]
        read_end, write_end = os.pipe()
        try:
            process = subprocess.Popen(
                command,
                executable=self.shell.path,
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
            # The pipe closes when the installer and everything it started in the background have exited; something
            # left running fails the test instead of hanging it.
            deadline = time.monotonic() + BACKGROUND_TIMEOUT_S
            while True:
                ready, _, _ = select.select([read_end], [], [], max(0.0, deadline - time.monotonic()))
                if not ready:
                    self.fail(f'something the installer started still runs {BACKGROUND_TIMEOUT_S} s after it ended')
                if not os.read(read_end, 4096):
                    break
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
    def test_st23_help(self) -> None:
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

    def test_st23_unknown_argument(self) -> None:
        for args in [('--force',), ('folder',), ('-d', 'x', 'extra')]:
            with self.subTest(args=args):
                result = self.run_installer(*args)
                self.assertEqual(result.code, 2)
                self.assertEqual(result.out, '')
                self.assertTrue(result.err.startswith('usage: sh install.sh [-d <folder>]\n'))
                self.assertFalse(self.data.exists())

    def test_st23_dest_needs_a_folder(self) -> None:
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                self.assert_error(self.run_installer(option), f'{option} needs a folder')
                self.assertFalse(self.data.exists())

    def test_st23_dest(self) -> None:
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                dest = self.root / f'apps{option}' / 'Hider'
                result = self.run_installer(option, str(dest))
                self.assert_ok(result)
                self.assert_installed(dest)
                self.assertTrue(result.out.endswith(f'Installed Hider {VERSION} to {dest}\n'))
                # The desktop entry still goes to the data home.
                self.assertFalse((self.data / 'Hider').exists())

    def test_st23_relative_dest_is_made_absolute(self) -> None:
        work = self.root / 'cwd'
        work.mkdir()
        result = self.run_installer('-d', 'rel/Hider', cwd=work)
        self.assert_ok(result)
        self.assert_installed(work / 'rel' / 'Hider')
        self.assertIn(f'Copying to {work}/rel/Hider\n', result.out)

    def test_st23_empty_dest_is_the_default(self) -> None:
        # As install.bat "" and scripts/install.py --dest '' do.
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                result = self.run_installer(option, '')
                self.assert_ok(result)
                self.assert_installed(self.data / 'Hider')

    def test_st23_dest_with_an_equals_sign_is_an_unknown_argument(self) -> None:
        # Only the forms of the usage line: -d <folder> and --dest <folder>.
        for args in [('--dest=x',), ('-dx',), ('-d=x',)]:
            with self.subTest(args=args):
                result = self.run_installer(*args)
                self.assertEqual((result.code, result.out), (2, ''))
                self.assertTrue(result.err.startswith('usage: sh install.sh [-d <folder>]\n'))
                self.assertFalse(self.data.exists())
                self.assertFalse((self.root / 'x').exists())

    def test_st23_help_after_a_dest(self) -> None:
        # The arguments are read in order: -h ends the run before anything is installed.
        result = self.run_installer('-d', str(self.root / 'x'), '-h')
        self.assert_ok(result)
        self.assertTrue(result.out.startswith('usage: sh install.sh [-d <folder>]\n'))
        self.assertFalse((self.root / 'x').exists())
        self.assertEqual(result.log, [])

    def test_st23_last_dest_wins(self) -> None:
        result = self.run_installer('-d', str(self.root / 'first'), '--dest', str(self.root / 'second'))
        self.assert_ok(result)
        self.assertFalse((self.root / 'first').exists())
        self.assert_installed(self.root / 'second')


class DefaultFolderTest(InstallerTest):
    def test_st24_xdg_data_home(self) -> None:
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

    def test_st24_without_xdg_data_home(self) -> None:
        for value in [None, '']:
            with self.subTest(value=value):
                result = self.run_installer(env={'XDG_DATA_HOME': value})
                self.assert_ok(result)
                share = self.home / '.local' / 'share'
                self.assert_installed(share / 'Hider', data=share)
                shutil.rmtree(self.home / '.local')

    def test_st24_xdg_data_home_without_home(self) -> None:
        result = self.run_installer(env={'HOME': None})
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')

    def test_st24_neither_xdg_data_home_nor_home(self) -> None:
        # e.g. env -i sh install.sh: a message of ours, not the shell's "HOME: parameter not set" (set -u).
        message = 'neither XDG_DATA_HOME nor HOME is set: the desktop entry goes under one of them'
        envs: list[dict[str, str | None]] = [
            {'XDG_DATA_HOME': None, 'HOME': None},
            {'XDG_DATA_HOME': '', 'HOME': None},
            # Empty: before, the entry (and the default folder) went under /.local/share.
            {'XDG_DATA_HOME': '', 'HOME': ''},
        ]
        for env in envs:
            for args in [(), ('-d', str(self.root / 'dest'))]:
                with self.subTest(env=env, args=args):
                    if env['HOME'] == '' and os.geteuid() == 0:
                        self.skipTest('not as root: a regression would really write to /.local/share')
                    result = self.run_installer(*args, env=env)
                    self.assert_error(result, message)
                    self.assertEqual(self.calls(result, 'curl'), [])
                    self.assertFalse((self.root / 'dest').exists())


class MachineTest(InstallerTest):
    def test_st25_architectures(self) -> None:
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

    def test_st25_unsupported_architecture(self) -> None:
        for machine in ['armv7l', 'i686', 'riscv64', 'X86_64']:
            with self.subTest(machine=machine):
                result = self.run_installer(env={'FAKE_UNAME_M': machine})
                self.assert_error(result, f'unsupported architecture {machine} (x86_64 and aarch64 are supported)')
                self.assertEqual(self.calls(result, 'curl'), [])

    def test_st25_not_linux(self) -> None:
        for system in ['Darwin', 'FreeBSD', 'MINGW64_NT-10.0', 'linux']:
            with self.subTest(system=system):
                result = self.run_installer(env={'FAKE_UNAME_S': system})
                self.assert_error(result, 'this installer is for Linux; on Windows use install.bat')
                self.assertFalse(self.data.exists())

    def test_st25_running_hider_stops_the_install(self) -> None:
        result = self.run_installer(env={'FAKE_PGREP_STATUS': '0'})
        self.assert_error(result, 'Hider is running: quit it from the tray menu and run this again')
        self.assertEqual(self.calls(result, 'pgrep'), ['pgrep -x Hider'])
        self.assertEqual(self.calls(result, 'curl'), [])
        self.assertFalse(self.data.exists())

    def test_st25_hider_not_running(self) -> None:
        result = self.run_installer()
        self.assert_ok(result)
        self.assertEqual(self.calls(result, 'pgrep'), ['pgrep -x Hider'])

    def test_st25_without_pgrep(self) -> None:
        result = self.run_installer(machine=Machine(pgrep=False))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')

    def test_st25_shasum_instead_of_sha256sum(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=False, shasum=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertEqual(len(self.calls(result, 'shasum')), 1)
        self.assertTrue(self.calls(result, 'shasum')[0].startswith('shasum -a 256 '))

    def test_st25_sha256sum_preferred(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=True, shasum=True))
        self.assert_ok(result)
        self.assertEqual(self.calls(result, 'shasum'), [])

    def test_st25_no_checksum_tool(self) -> None:
        result = self.run_installer(machine=Machine(sha256sum=False, shasum=False))
        self.assert_error(result, 'sha256sum (coreutils) was not found')
        self.assertEqual(self.calls(result, 'curl'), [])


class DownloadTest(InstallerTest):
    def archive_url(self, rid: str = 'linux-x64') -> str:
        return f'{BASE_URL}/Hider-{VERSION}-{rid}.tar.gz'

    def test_st26_curl(self) -> None:
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

    def test_st26_curl_failing(self) -> None:
        result = self.run_installer(env={'FAKE_DOWNLOAD_FAIL': '1'})
        self.assert_error(result, f'download failed: {self.archive_url()}')
        self.assertFalse(self.data.exists())

    def test_st26_wget(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        (call,) = self.calls(result, 'wget')
        self.assertTrue(call.startswith(f'wget --https-only -O {self.tmp}/'))
        self.assertTrue(call.endswith(f'/Hider-{VERSION}-linux-x64.tar.gz {self.archive_url()}'))

    def test_st26_wget_failing(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=True), env={'FAKE_DOWNLOAD_FAIL': '1'})
        self.assert_error(result, f'download failed: {self.archive_url()}')

    def test_st26_curl_preferred_over_wget(self) -> None:
        result = self.run_installer(machine=Machine(curl=True, wget=True))
        self.assert_ok(result)
        self.assertEqual(len(self.calls(result, 'curl')), 1)
        self.assertEqual(self.calls(result, 'wget'), [])

    def test_st26_no_download_tool(self) -> None:
        result = self.run_installer(machine=Machine(curl=False, wget=False))
        self.assert_error(result, 'curl or wget is needed to download Hider')
        self.assertIn(f'Downloading {self.archive_url()}\n', result.out)

    def test_st26_archive_missing_on_the_server(self) -> None:
        (self.store / f'Hider-{VERSION}-linux-x64.tar.gz').unlink()
        result = self.run_installer()
        self.assertEqual(result.code, 1)
        self.assertTrue(result.err.endswith(f'error: download failed: {self.archive_url()}\n'))
        self.assert_cleaned_up()

    def test_st26_archive_next_to_the_script(self) -> None:
        local = self.put_next_to_script()
        result = self.run_installer(machine=Machine(curl=True, wget=True))
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertIn(f'Using {local}\n', result.out)
        self.assertNotIn('Downloading', result.out)
        self.assertEqual(self.calls(result, 'curl') + self.calls(result, 'wget'), [])
        # Copied, not moved.
        self.assertTrue(local.is_file())

    def test_st26_archive_for_the_other_architecture_is_not_used(self) -> None:
        self.put_next_to_script('linux-arm64')
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider', 'linux-x64')
        self.assertEqual(len(self.calls(result, 'curl')), 1)

    def test_st26_script_started_by_a_relative_path(self) -> None:
        local = self.put_next_to_script()
        result = self.run_installer(cwd=self.release, script=Path('install.sh'))
        self.assert_ok(result)
        self.assertIn(f'Using {local}\n', result.out)
        other = self.root / 'other'
        other.mkdir()
        result = self.run_installer(cwd=other, script=Path('../release/install.sh'))
        self.assert_ok(result)
        self.assertIn(f'Using {local}\n', result.out)

    def test_st26_piped_to_sh_uses_the_current_directory(self) -> None:
        self.put_next_to_script()
        text = self.script.read_text(encoding='utf-8')
        result = self.run_installer(stdin=text, cwd=self.release)
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertIn(f'Using {self.release}/Hider-{VERSION}-linux-x64.tar.gz\n', result.out)

    def test_st26_piped_to_sh_downloads(self) -> None:
        result = self.run_installer(stdin=self.script.read_text(encoding='utf-8'), cwd=self.root)
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')
        self.assertEqual(len(self.calls(result, 'curl')), 1)

    def test_st26_cut_short_download_of_the_script_runs_nothing(self) -> None:
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
    def test_st27_mismatch_of_a_download(self) -> None:
        expected = self.digests['linux-x64']
        actual = self.make_archive(self.store, 'linux-x64', {'Hider': (b'tampered', 0o755)})
        result = self.run_installer()
        self.assert_error(
            result, f'SHA-256 mismatch for Hider-{VERSION}-linux-x64.tar.gz (expected {expected}, got {actual})'
        )
        self.assertFalse(self.data.exists())

    def test_st27_mismatch_of_a_local_archive(self) -> None:
        self.make_archive(self.release, 'linux-x64', {'Hider': (b'other', 0o755)})
        result = self.run_installer()
        self.assertEqual(result.code, 1)
        self.assertIn('SHA-256 mismatch', result.err)
        self.assertEqual(self.calls(result, 'curl'), [])
        self.assertFalse(self.data.exists())

    def test_st27_each_architecture_has_its_own_checksum(self) -> None:
        # Swapping the two checksums makes both architectures fail.
        swapped = {'linux-x64': self.digests['linux-arm64'], 'linux-arm64': self.digests['linux-x64']}
        script = self.write_installer(self.root, swapped)
        for machine in ['x86_64', 'aarch64']:
            with self.subTest(machine=machine):
                result = self.run_installer(env={'FAKE_UNAME_M': machine}, script=script)
                self.assertEqual(result.code, 1)
                self.assertIn('SHA-256 mismatch', result.err)

    def test_st27_archive_without_the_executable(self) -> None:
        digest = self.make_archive(self.store, 'linux-x64', {'Assets/hider.svg': (b'<svg/>', 0o644)})
        script = self.write_installer(self.root, {'linux-x64': digest, 'linux-arm64': self.digests['linux-arm64']})
        result = self.run_installer(script=script)
        name = f'Hider-{VERSION}-linux-x64'
        self.assert_error(result, f'{name}.tar.gz does not contain {name}/Hider')
        self.assertFalse(self.data.exists())

    def test_st27_archive_of_another_version(self) -> None:
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

    def test_st27_corrupt_archive_with_a_matching_checksum(self) -> None:
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
    def test_st28_replaces_an_existing_install(self) -> None:
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

    def test_st28_installing_twice(self) -> None:
        self.assert_ok(self.run_installer())
        result = self.run_installer()
        self.assert_ok(result)
        self.assert_installed(self.data / 'Hider')

    def test_st28_sets_the_executable_bit(self) -> None:
        # make_release sets 0755 in the archive; the installer sets it again on the installed file.
        dest = self.data / 'Hider'
        dest.mkdir(parents=True)
        (dest / 'Hider').write_text('old', encoding='utf-8')
        (dest / 'Hider').chmod(0o600)
        self.assert_ok(self.run_installer())
        self.assertEqual(stat.S_IMODE((dest / 'Hider').stat().st_mode), 0o755)

    def test_st28_copy_failure(self) -> None:
        dest = self.data / 'Hider'
        result = self.run_installer(machine=Machine(cp_fails=True), env={'FAKE_CP_FAIL_DEST': str(dest)})
        self.assertEqual(result.code, 1)
        self.assertTrue(result.err.endswith('error: copy failed: quit Hider if it is running and run this again\n'))
        self.assertFalse((self.data / 'applications').exists())
        self.assert_cleaned_up()

    def test_st28_dest_is_a_file(self) -> None:
        self.data.mkdir()
        (self.data / 'Hider').write_text('a file', encoding='utf-8')
        result = self.run_installer()
        # set -e stops at mkdir -p, with its own message.
        self.assertNotEqual(result.code, 0)
        self.assertIn('mkdir', result.err)
        self.assertNotIn('Installed', result.out)
        self.assert_cleaned_up()


class DesktopEntryTest(InstallerTest):
    def test_st29_paths_are_escaped_like_install_py(self) -> None:
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

    def test_st29_entry_lines(self) -> None:
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

    def test_st30_starts_with_a_display(self) -> None:
        expected = [f'nohup {self.data}/Hider/Hider']
        self.assertEqual(self.launches({'DISPLAY': ':0'}), expected)
        self.assertEqual(self.launches({'WAYLAND_DISPLAY': 'wayland-0'}), expected)
        self.assertEqual(self.launches({'DISPLAY': ':1', 'WAYLAND_DISPLAY': 'wayland-0'}), expected)

    def test_st30_empty_no_launch_still_starts(self) -> None:
        self.assertEqual(len(self.launches({'DISPLAY': ':0', 'HIDER_NO_LAUNCH': ''})), 1)

    def test_st30_no_launch(self) -> None:
        for value in ['1', '0', 'yes']:
            with self.subTest(value=value):
                self.assertEqual(self.launches({'DISPLAY': ':0', 'HIDER_NO_LAUNCH': value}), [])

    def test_st30_no_display(self) -> None:
        self.assertEqual(self.launches({}), [])
        self.assertEqual(self.launches({'DISPLAY': '', 'WAYLAND_DISPLAY': ''}), [])


class InterruptTest(InstallerTest):
    def test_st30_terminated_during_the_download(self) -> None:
        result = self.run_installer(env={'FAKE_KILL_INSTALLER': '1'})
        self.assertEqual(result.code, 130)
        self.assertNotIn('Installed', result.out)
        self.assertFalse(self.data.exists())
        self.assert_cleaned_up()


class ShellsTest(InstallerTest):
    """The awkward paths together (load_tests runs this under every shell like the rest), and the parametrisation."""

    def test_st31_runs_under_the_shell_asked_for(self) -> None:
        # The process's executable, bash's options (posix when started as sh), and a command found through PATH (the
        # uname stub).
        probe = self.root / 'probe.sh'
        readlink = real('readlink')
        probe.write_text(
            f'printf "%s|%s|%s\\n" "$({readlink} /proc/$$/exe)" "${{SHELLOPTS:-}}" "$(uname -s)"\n', encoding='utf-8'
        )
        result = self.run_installer(script=probe, env={'FAKE_UNAME_S': 'from-path'})
        exe, options, uname = result.out.rstrip('\n').split('|')
        self.assertEqual(os.path.realpath(exe), os.path.realpath(self.shell.path))
        if Path(os.path.realpath(self.shell.path)).name == 'bash':
            self.assertEqual('posix' in options.split(':'), self.shell.argv0 == 'sh')
        # Commands come from PATH: a busybox built with FEATURE_SH_STANDALONE (Debian's busybox-static) would run its
        # own applets and bypass the stubs.
        self.assertEqual(uname, 'from-path')

    def test_st31_something_left_running_fails_instead_of_hanging(self) -> None:
        # Output to /dev/null as the launch's nohup does: only the inherited pipe still sees the process.
        script = self.root / 'leftover.sh'
        script.write_text(f'{real("sleep")} 3 > /dev/null 2>&1 &\n', encoding='utf-8')
        start = time.monotonic()
        with (
            mock.patch.object(sys.modules[__name__], 'BACKGROUND_TIMEOUT_S', 0.5),
            self.assertRaises(AssertionError) as raised,
        ):
            self.run_installer(script=script)
        self.assertEqual(str(raised.exception), 'something the installer started still runs 0.5 s after it ended')
        self.assertLess(time.monotonic() - start, 3)

    def test_st31_odd_folder_and_launch(self) -> None:
        dest = self.root / 'odd' / self.shell.name / 'a "b" $c `d` \\e \\n 100%'
        result = self.run_installer('-d', str(dest), env={'DISPLAY': ':0', 'HIDER_NO_LAUNCH': ''})
        self.assert_ok(result)
        self.assert_installed(dest)
        self.assertEqual(self.calls(result, 'nohup'), [f'nohup {dest}/Hider'])


class SyntaxTest(unittest.TestCase):
    """`sh -n` on the template and the rendered script, `bash -n` on the Xvfb smoke test (CI runs shellcheck too)."""

    def check(self, shell: Shell, path: Path) -> None:
        result = subprocess.run(
            [shell.argv0, '-n', str(path)], executable=shell.path, capture_output=True, text=True, check=False
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_st32_install_sh(self) -> None:
        root = temp_dir(self)
        rendered = root / 'install.sh'
        values = {'HIDER_VERSION': '1.0.0', 'HIDER_BASE_URL': 'https://x/y'}
        values |= {f'HIDER_SHA256_{k}': '0' * 64 for k in ['WIN_X64', 'LINUX_X64', 'LINUX_ARM64']}
        rendered.write_text(make_release.render('install.sh.in', values, '\n'), encoding='utf-8')
        for shell in SHELLS:
            with self.subTest(shell=shell.name):
                self.check(shell, SCRIPTS_DIR / 'release' / 'install.sh.in')
                self.check(shell, rendered)

    def test_st32_xvfb_strip_smoke(self) -> None:
        path = SCRIPTS_DIR / 'xvfb-strip-smoke.sh'
        self.assertTrue(path.read_text(encoding='utf-8').startswith('#!/usr/bin/env bash\n'))
        self.check(Shell('bash', real('bash'), 'bash'), path)
        self.assertTrue(os.access(path, os.X_OK), 'xvfb-strip-smoke.sh is executable')

    def test_st32_windows_stress_is_a_powershell_script(self) -> None:
        # Only its shape here: CI's python-lint parses it (PowerShell's parser, PSScriptAnalyzer), publish runs it.
        text = (SCRIPTS_DIR / 'windows-stress.ps1').read_text(encoding='utf-8-sig')
        self.assertTrue(text.startswith('#Requires -Version 7\n'))
        self.assertEqual(text.count('{'), text.count('}'))
        self.assertEqual(text.count('('), text.count(')'))


class ParametrisationTest(unittest.TestCase):
    def test_st31_installer_cases_reach_every_depth(self) -> None:
        # A class derived from an installer test class runs under every shell too, not only under the first one.
        class Child(InstallerTest):
            pass

        class Grandchild(Child):
            pass

        cases = installer_cases(InstallerTest)
        self.assertLess(cases.index(Child), cases.index(Grandchild))
        self.assertEqual(installer_cases(Child), [Grandchild])
        self.assertEqual(installer_cases(Grandchild), [])
        # Classes that are not the module's own (these two, the per-shell ones) are not parametrised again.
        self.assertNotIn(Child, module_installer_cases())

    def test_st31_a_class_added_to_the_module_later_runs_under_every_shell(self) -> None:
        # The cases are collected when load_tests runs, not when the module is imported: a class the module gets
        # after that (here put into its namespace by hand) is parametrised too, and so is its subclass.
        class Later(InstallerTest):
            def test_nothing(self) -> None:  # only collected, never run
                pass

        class LaterChild(Later):
            pass

        namespace = vars(sys.modules[__name__])
        namespace['Later'] = Later
        namespace['LaterChild'] = LaterChild
        try:
            suite = load_tests(unittest.TestLoader(), unittest.TestSuite(), None)
            names = {type(test).__name__ for test in flatten(suite)}
        finally:
            del namespace['Later']
            del namespace['LaterChild']
        for case in ['Later', 'LaterChild']:
            self.assertEqual({n for n in names if n.startswith(f'{case}_')}, {f'{case}_{s.name}' for s in SHELLS})

    def test_st31_every_installer_class_of_the_module_runs_under_every_shell(self) -> None:
        # Both ways: each class of the module derived from InstallerTest is parametrised, and only those.
        own = {
            value
            for value in vars(sys.modules[__name__]).values()
            if isinstance(value, type) and issubclass(value, InstallerTest) and value is not InstallerTest
        }
        self.assertEqual(set(module_installer_cases()), own)
        suite = load_tests(unittest.TestLoader(), unittest.TestSuite(), None)
        names = {type(test).__name__ for test in flatten(suite)}
        self.assertEqual(names, {f'{case.__name__}_{s.name}' for case in own for s in SHELLS})


def installer_cases(base: type[InstallerTest]) -> list[type[InstallerTest]]:
    """Every class derived from base, at any depth (a subclass of a subclass runs once per shell too)."""
    cases: list[type[InstallerTest]] = []
    for case in base.__subclasses__():
        cases += [case, *installer_cases(case)]
    return cases


def module_installer_cases() -> list[type[InstallerTest]]:
    """The module's own classes with installer runs, at any depth: not the per-shell subclasses load_tests makes, nor
    classes defined inside a test. Collected when load_tests runs, so a class defined later in the module counts too."""
    module = vars(sys.modules[__name__])
    return [case for case in installer_cases(InstallerTest) if module.get(case.__name__) is case]


def per_shell(case: type[InstallerTest], shell: Shell) -> type[InstallerTest]:
    return type(f'{case.__name__}_{shell.name}', (case,), {'shell': shell, '__module__': __name__})


def load_tests(loader: unittest.TestLoader, standard: unittest.TestSuite, pattern: str | None) -> unittest.TestSuite:
    """unittest's load_tests protocol (discover, or the module by name): every installer test once per shell, named
    <Class>_<shell>, and every other test of the module as unittest found it (a TestCase added later runs too)."""
    cases = module_installer_cases()
    parametrised = {InstallerTest, *cases}
    suite = unittest.TestSuite(test for test in flatten(standard) if type(test) not in parametrised)
    for case in cases:
        for shell in SHELLS:
            suite.addTests(loader.loadTestsFromTestCase(per_shell(case, shell)))
    return suite


def flatten(suite: unittest.TestSuite) -> Iterator[unittest.TestCase]:
    for test in suite:
        if isinstance(test, unittest.TestSuite):
            yield from flatten(test)
        else:
            assert isinstance(test, unittest.TestCase)
            yield test
