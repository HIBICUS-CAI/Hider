"""scripts/install.py: copying, the desktop entry, the Windows shortcut command and the install folder.

powershell is replaced; the Windows paths run on any host with IS_WINDOWS patched.
"""

from __future__ import annotations

import base64
import contextlib
import importlib.util
import os
import platform
import shutil
import stat
import subprocess
import unittest
from pathlib import Path
from unittest import mock

import install
from common import TARGETS

from . import SCRIPTS_DIR
from .support import Output, arguments, captured, enter, run_as_main, temp_dir

ENTRY_TEMPLATE = """[Desktop Entry]
Type=Application
Name=Hider
Comment=画面の一部をモザイクやパターンで覆って隠す
Exec={exec_}
Icon={icon}
Terminal=false
Categories=Utility;
StartupWMClass=Hider
"""


def make_build(root: Path, exe: str = 'Hider') -> Path:
    """A fake publish output: the executable (not executable yet), Assets and a library."""
    (root / 'Assets').mkdir(parents=True)
    (root / exe).write_text('#!/bin/sh\n', encoding='utf-8')
    (root / exe).chmod(0o644)
    (root / 'Assets' / 'hider.svg').write_text('<svg/>', encoding='utf-8')
    (root / 'libSkiaSharp.so').write_bytes(b'\x7fELF')
    return root


def mode(path: Path) -> int:
    return stat.S_IMODE(path.stat().st_mode)


class ModuleTest(unittest.TestCase):
    def load_on(self, system: str) -> tuple[bool, str]:
        """Import a separate copy of install.py as if on the given OS; return its IS_WINDOWS and EXE_NAME."""
        spec = importlib.util.spec_from_file_location(f'install_on_{system}', SCRIPTS_DIR / 'install.py')
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        with mock.patch('platform.system', return_value=system):
            spec.loader.exec_module(module)
        is_windows: bool = module.IS_WINDOWS
        exe_name: str = module.EXE_NAME
        return is_windows, exe_name

    def test_windows(self) -> None:
        self.assertEqual(self.load_on('Windows'), (True, 'Hider.exe'))

    def test_linux(self) -> None:
        self.assertEqual(self.load_on('Linux'), (False, 'Hider'))

    def test_other_systems_are_treated_as_linux(self) -> None:
        self.assertEqual(self.load_on('Darwin'), (False, 'Hider'))

    def test_this_machine(self) -> None:
        self.assertEqual(install.IS_WINDOWS, platform.system() == 'Windows')
        self.assertEqual(install.EXE_NAME, 'Hider.exe' if install.IS_WINDOWS else 'Hider')


class CopyFilesTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.src = make_build(self.root / 'src')

    def test_copies_the_tree(self) -> None:
        dest = self.root / 'a' / 'b' / 'Hider'
        with captured() as output:
            install.copy_files(self.src, dest)
        self.assertEqual(output.out, f'Copying {self.src} -> {dest}\n')
        self.assertEqual(
            sorted(p.relative_to(dest).as_posix() for p in dest.rglob('*')),
            ['Assets', 'Assets/hider.svg', 'Hider', 'libSkiaSharp.so'],
        )
        self.assertEqual((dest / 'libSkiaSharp.so').read_bytes(), b'\x7fELF')

    def test_overwrites_into_an_existing_folder(self) -> None:
        dest = self.root / 'dest'
        (dest / 'Assets').mkdir(parents=True)
        (dest / 'Hider').write_text('old', encoding='utf-8')
        (dest / 'Assets' / 'old.png').write_bytes(b'old')
        (dest / 'settings-not-here.txt').write_text('kept', encoding='utf-8')
        with captured():
            install.copy_files(self.src, dest)
        self.assertEqual((dest / 'Hider').read_text(encoding='utf-8'), '#!/bin/sh\n')
        # Files the new build does not have stay (only overwriting, no cleanup).
        self.assertTrue((dest / 'Assets' / 'old.png').is_file())
        self.assertEqual((dest / 'settings-not-here.txt').read_text(encoding='utf-8'), 'kept')
        self.assertTrue((dest / 'Assets' / 'hider.svg').is_file())

    def check_failure(self, error: BaseException | None, src: Path | None = None) -> str:
        """copy_files exits with the hint and the error; return the exit message."""
        patcher = mock.patch('shutil.copytree', side_effect=error) if error else contextlib.nullcontext()
        with patcher, captured() as output, self.assertRaises(SystemExit) as raised:
            install.copy_files(src or self.src, self.root / 'dest')
        # The Copying line comes first, so it shows what was being copied.
        self.assertTrue(output.out.startswith('Copying '))
        message = raised.exception.code
        assert isinstance(message, str)
        self.assertTrue(message.startswith('Copy failed, close Hider if it is running and retry.\n'))
        return message

    def test_missing_source_exits(self) -> None:
        message = self.check_failure(None, self.root / 'missing')
        self.assertIn(str(self.root / 'missing'), message)

    def test_os_error_exits(self) -> None:
        message = self.check_failure(PermissionError(13, 'Permission denied', 'Hider'))
        self.assertIn('Permission denied', message)

    def test_shutil_error_exits(self) -> None:
        message = self.check_failure(shutil.Error([('a', 'b', 'Text file busy')]))
        self.assertIn('Text file busy', message)

    def test_other_errors_are_not_caught(self) -> None:
        with (
            mock.patch('shutil.copytree', side_effect=ValueError('bug')),
            captured(),
            self.assertRaises(ValueError),
        ):
            install.copy_files(self.src, self.root / 'dest')


class InstallWindowsTest(unittest.TestCase):
    SCRIPT = '; '.join(
        [
            "$ErrorActionPreference = 'Stop'",
            "$link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hider.lnk'",
            '$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($link)',
            '$shortcut.TargetPath = $env:HIDER_TARGET_PATH',
            '$shortcut.WorkingDirectory = $env:HIDER_WORKING_DIR',
            '$shortcut.Save()',
            "Write-Output ('Created shortcut: ' + $link)",
        ]
    )

    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.src = make_build(self.root / 'src', 'Hider.exe')
        self.dest = self.root / "Program's $files" / 'Hider'
        enter(self, mock.patch.object(install, 'EXE_NAME', 'Hider.exe'))

    def call(self, returncode: int = 0) -> tuple[Output, mock.MagicMock]:
        def run(command: list[str], env: dict[str, str], check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertFalse(check)
            # The files are in place before the shortcut is made.
            self.assertTrue((self.dest / 'Hider.exe').is_file())
            return subprocess.CompletedProcess(command, returncode)

        with (
            mock.patch.dict(os.environ, {'HIDER_TEST_SENTINEL': 'kept'}),
            mock.patch('subprocess.run', side_effect=run) as run_mock,
            captured() as output,
        ):
            install.install_windows(self.src, self.dest)
        return output, run_mock

    def test_runs_powershell_with_the_encoded_script(self) -> None:
        output, run = self.call()
        run.assert_called_once()
        command: list[str] = run.call_args.args[0]
        self.assertEqual(
            command[:-1], ['powershell', '-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand']
        )
        self.assertEqual(base64.b64decode(command[-1]).decode('utf-16-le'), self.SCRIPT)
        self.assertEqual(output.out, f'Copying {self.src} -> {self.dest}\n')

    def test_paths_go_through_the_environment(self) -> None:
        _, run = self.call()
        env: dict[str, str] = run.call_args.kwargs['env']
        self.assertEqual(env['HIDER_TARGET_PATH'], str(self.dest / 'Hider.exe'))
        self.assertEqual(env['HIDER_WORKING_DIR'], str(self.dest))
        # The rest of the environment is passed on, and the process's own environment is not changed.
        self.assertEqual(env['HIDER_TEST_SENTINEL'], 'kept')
        self.assertNotIn('HIDER_TARGET_PATH', os.environ)
        self.assertNotIn(str(self.dest), base64.b64decode(run.call_args.args[0][-1]).decode('utf-16-le'))

    def test_copies_the_files(self) -> None:
        self.call()
        self.assertTrue((self.dest / 'Assets' / 'hider.svg').is_file())

    def test_failed_shortcut_exits(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.call(returncode=1)
        self.assertEqual(raised.exception.code, 'Failed to create the desktop shortcut.')

    def test_failed_copy_does_not_run_powershell(self) -> None:
        with (
            mock.patch('subprocess.run') as run,
            captured(),
            self.assertRaises(SystemExit),
        ):
            install.install_windows(self.root / 'missing', self.dest)
        run.assert_not_called()


class DesktopStringTest(unittest.TestCase):
    def test_doubles_backslashes(self) -> None:
        self.assertEqual(install.desktop_string('C:\\a\\b'), 'C:\\\\a\\\\b')
        self.assertEqual(install.desktop_string('\\\\'), '\\\\\\\\')

    def test_leaves_everything_else(self) -> None:
        for value in ['', '/home/u/.local/share/Hider', 'a "b" $c `d` %f 画面', 'semi;colon\ttab']:
            with self.subTest(value=value):
                self.assertEqual(install.desktop_string(value), value)


class DesktopExecArgTest(unittest.TestCase):
    CASES: tuple[tuple[str, str], ...] = (
        ('/opt/Hider/Hider', '"/opt/Hider/Hider"'),
        ('/home/u/My Apps/Hider', '"/home/u/My Apps/Hider"'),
        ('', '""'),
        # " ` $ get a backslash, which the string rule then doubles.
        ('/a"b', '"/a\\\\"b"'),
        ('/a`b', '"/a\\\\`b"'),
        ('/a$b', '"/a\\\\$b"'),
        # A backslash gets a backslash (2), and the string rule doubles both (4).
        ('/a\\b', '"/a\\\\\\\\b"'),
        ('/100%/Hider', '"/100%%/Hider"'),
        ('/%f%%', '"/%%f%%%%"'),
        ('/画面/Hider', '"/画面/Hider"'),
        ("/it's/Hider", '"/it\'s/Hider"'),
        ('/$"`\\%', '"/\\\\$\\\\"\\\\`\\\\\\\\%%"'),
    )

    def test_escapes(self) -> None:
        for path, expected in self.CASES:
            with self.subTest(path=path):
                self.assertEqual(install.desktop_exec_arg(path), expected)


class InstallLinuxTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.src = make_build(self.root / 'src')

    def call(self, dest: Path, data_home: Path) -> Output:
        with captured() as output:
            install.install_linux(self.src, dest, data_home)
        return output

    def entry(self, dest: Path) -> str:
        return ENTRY_TEMPLATE.format(
            exec_=install.desktop_exec_arg(str(dest / 'Hider')),
            icon=install.desktop_string(str(dest / 'Assets' / 'hider.svg')),
        )

    def test_installs_and_writes_the_desktop_entry(self) -> None:
        dest = self.root / 'data' / 'Hider'
        data_home = self.root / 'data'
        output = self.call(dest, data_home)
        entry_path = data_home / 'applications' / 'Hider.desktop'
        self.assertEqual(output.out, f'Copying {self.src} -> {dest}\nCreated desktop entry: {entry_path}\n')
        self.assertEqual(mode(dest / 'Hider'), 0o755)
        self.assertTrue((dest / 'Assets' / 'hider.svg').is_file())
        self.assertEqual(entry_path.read_bytes().decode('utf-8'), self.entry(dest))
        self.assertEqual(entry_path.read_text(encoding='utf-8').splitlines()[4], f'Exec="{dest}/Hider"')

    def test_entry_uses_lf_and_utf8(self) -> None:
        self.call(self.root / 'Hider', self.root / 'data')
        data = (self.root / 'data' / 'applications' / 'Hider.desktop').read_bytes()
        self.assertNotIn(b'\r', data)
        self.assertTrue(data.endswith(b'StartupWMClass=Hider\n'))
        self.assertIn('Comment=画面の一部をモザイクやパターンで覆って隠す\n'.encode(), data)

    def test_data_home_and_dest_can_differ(self) -> None:
        dest = self.root / 'elsewhere' / 'Hider'
        self.call(dest, self.root / 'deep' / 'data' / 'home')
        entry = self.root / 'deep' / 'data' / 'home' / 'applications' / 'Hider.desktop'
        self.assertEqual(entry.read_text(encoding='utf-8'), self.entry(dest))
        self.assertFalse((self.root / 'deep' / 'data' / 'home' / 'Hider').exists())

    def test_replaces_an_existing_install_and_entry(self) -> None:
        dest = self.root / 'data' / 'Hider'
        dest.mkdir(parents=True)
        (dest / 'Hider').write_text('old', encoding='utf-8')
        apps = self.root / 'data' / 'applications'
        apps.mkdir()
        (apps / 'Hider.desktop').write_text('[Desktop Entry]\nExec=/old/Hider\n', encoding='utf-8')
        (apps / 'other.desktop').write_text('other', encoding='utf-8')
        self.call(dest, self.root / 'data')
        self.assertEqual((dest / 'Hider').read_text(encoding='utf-8'), '#!/bin/sh\n')
        self.assertEqual((apps / 'Hider.desktop').read_text(encoding='utf-8'), self.entry(dest))
        self.assertEqual((apps / 'other.desktop').read_text(encoding='utf-8'), 'other')

    def test_escapes_the_paths(self) -> None:
        dest = self.root / 'a "b" $c `d` \\e 100%' / 'Hider'
        self.call(dest, self.root / 'data')
        lines = (self.root / 'data' / 'applications' / 'Hider.desktop').read_text(encoding='utf-8').splitlines()
        self.assertEqual(
            lines[4], 'Exec="' + str(self.root) + '/a \\\\"b\\\\" \\\\$c \\\\`d\\\\` \\\\\\\\e 100%%/Hider/Hider"'
        )
        self.assertEqual(lines[5], 'Icon=' + str(self.root) + '/a "b" $c `d` \\\\e 100%/Hider/Assets/hider.svg')

    def test_missing_executable_fails(self) -> None:
        (self.src / 'Hider').unlink()
        with self.assertRaises(FileNotFoundError):
            self.call(self.root / 'Hider', self.root / 'data')


class MainTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.home = self.root / 'home'
        enter(
            self,
            mock.patch.object(install, 'publish_dir', side_effect=self.publish_dir),
            mock.patch.object(install, 'host_target', return_value='linux-x64'),
            mock.patch.object(Path, 'home', return_value=self.home),
            mock.patch.dict(os.environ),
        )
        os.environ.pop('XDG_DATA_HOME', None)
        os.environ.pop('LOCALAPPDATA', None)

    def publish_dir(self, target: str) -> Path:
        return self.root / 'publish' / target

    def build(self, target: str = 'linux-x64', exe: str = 'Hider') -> Path:
        return make_build(self.root / 'publish' / target, exe)

    def run_main(self, *args: str) -> tuple[Output, mock.MagicMock, mock.MagicMock]:
        """Run main with install_linux / install_windows replaced; return the output and both mocks."""
        with (
            arguments('install.py', *args),
            mock.patch.object(install, 'install_linux') as linux,
            mock.patch.object(install, 'install_windows') as windows,
            captured() as output,
        ):
            install.main()
        return output, linux, windows

    def exit_message(self, *args: str) -> object:
        with self.assertRaises(SystemExit) as raised:
            self.run_main(*args)
        return raised.exception.code

    def windows(self) -> None:
        enter(
            self,
            mock.patch.object(install, 'IS_WINDOWS', True),
            mock.patch.object(install, 'EXE_NAME', 'Hider.exe'),
            mock.patch.object(install, 'host_target', return_value='win-x64'),
        )

    def test_unsupported_machine_exits(self) -> None:
        with mock.patch.object(install, 'host_target', return_value=None):
            self.assertEqual(self.exit_message(), f'This machine is not a supported target ({", ".join(TARGETS)}).')

    def test_without_a_build_exits(self) -> None:
        src = self.root / 'publish' / 'linux-x64'
        self.assertEqual(
            self.exit_message(), f'No build found in {src}, run "python scripts/build.py linux-x64" first.'
        )

    def test_build_of_another_target_does_not_count(self) -> None:
        self.build('linux-arm64')
        self.assertIn('No build found', str(self.exit_message()))

    def test_executable_must_be_a_file(self) -> None:
        (self.root / 'publish' / 'linux-x64' / 'Hider').mkdir(parents=True)
        self.assertIn('No build found', str(self.exit_message()))

    def test_linux_default_is_xdg_data_home(self) -> None:
        src = self.build()
        os.environ['XDG_DATA_HOME'] = str(self.root / 'xdg')
        output, linux, windows = self.run_main()
        linux.assert_called_once_with(src, self.root / 'xdg' / 'Hider', self.root / 'xdg')
        windows.assert_not_called()
        self.assertEqual(output.out, f'Installed to {self.root / "xdg" / "Hider"}\n')

    def test_linux_without_xdg_data_home_uses_local_share(self) -> None:
        src = self.build()
        _, linux, _ = self.run_main()
        share = self.home / '.local' / 'share'
        linux.assert_called_once_with(src, share / 'Hider', share)

    def test_linux_empty_xdg_data_home_uses_local_share(self) -> None:
        src = self.build()
        os.environ['XDG_DATA_HOME'] = ''
        _, linux, _ = self.run_main()
        share = self.home / '.local' / 'share'
        linux.assert_called_once_with(src, share / 'Hider', share)

    def test_linux_dest(self) -> None:
        src = self.build()
        os.environ['XDG_DATA_HOME'] = str(self.root / 'xdg')
        for option in ['-d', '--dest']:
            with self.subTest(option=option):
                output, linux, _ = self.run_main(option, str(self.root / 'apps' / 'H'))
                # The desktop entry still goes to the data home.
                linux.assert_called_once_with(src, self.root / 'apps' / 'H', self.root / 'xdg')
                self.assertEqual(output.out, f'Installed to {self.root / "apps" / "H"}\n')

    def test_linux_relative_dest_is_made_absolute(self) -> None:
        src = self.build()
        _, linux, _ = self.run_main('--dest', 'rel/Hider')
        dest: Path = linux.call_args.args[1]
        self.assertEqual(dest, Path.cwd() / 'rel' / 'Hider')
        self.assertTrue(dest.is_absolute())
        self.assertEqual(linux.call_args.args[0], src)

    def test_windows_default_is_local_app_data(self) -> None:
        self.windows()
        src = self.build('win-x64', 'Hider.exe')
        os.environ['LOCALAPPDATA'] = str(self.root / 'lad')
        output, linux, windows = self.run_main()
        windows.assert_called_once_with(src, self.root / 'lad' / 'Programs' / 'Hider')
        linux.assert_not_called()
        self.assertEqual(output.out, f'Installed to {self.root / "lad" / "Programs" / "Hider"}\n')

    def test_windows_without_local_app_data_exits(self) -> None:
        self.windows()
        self.build('win-x64', 'Hider.exe')
        for value in [None, '']:
            with self.subTest(value=value):
                if value is not None:
                    os.environ['LOCALAPPDATA'] = value
                self.assertEqual(self.exit_message(), 'LOCALAPPDATA is not set: give the install folder with --dest.')

    def test_windows_dest(self) -> None:
        self.windows()
        src = self.build('win-x64', 'Hider.exe')
        _, _, windows = self.run_main('-d', str(self.root / 'H'))
        windows.assert_called_once_with(src, self.root / 'H')
        _, _, windows = self.run_main('--dest', 'rel')
        windows.assert_called_once_with(src, Path.cwd() / 'rel')

    def test_windows_needs_the_exe(self) -> None:
        self.windows()
        self.build('win-x64', 'Hider')
        self.assertIn('run "python scripts/build.py win-x64" first', str(self.exit_message()))

    def test_unknown_argument(self) -> None:
        self.build()
        self.assertEqual(self.exit_message('--force'), 2)

    def test_help(self) -> None:
        with self.assertRaises(SystemExit) as raised, captured() as output, arguments('install.py', '-h'):
            install.main()
        self.assertEqual(raised.exception.code, 0)
        text = ' '.join(output.out.split())
        self.assertIn('Install the published Hider for the current machine', text)
        self.assertIn('Default: %LOCALAPPDATA%\\Programs\\Hider on Windows', text)
        self.assertIn('$XDG_DATA_HOME/Hider (~/.local/share/Hider) on Linux.', text)


class EndToEndTest(unittest.TestCase):
    """main with the real install_linux, into temporary folders."""

    def test_linux(self) -> None:
        root = temp_dir(self)
        make_build(root / 'publish' / 'linux-x64')
        with (
            mock.patch.object(install, 'IS_WINDOWS', False),
            mock.patch.object(install, 'EXE_NAME', 'Hider'),
            mock.patch.object(install, 'publish_dir', return_value=root / 'publish' / 'linux-x64'),
            mock.patch.object(install, 'host_target', return_value='linux-x64'),
            mock.patch.dict(os.environ, {'XDG_DATA_HOME': str(root / 'data')}),
            arguments('install.py'),
            captured() as output,
        ):
            install.main()
        self.assertEqual(mode(root / 'data' / 'Hider' / 'Hider'), 0o755)
        self.assertTrue((root / 'data' / 'applications' / 'Hider.desktop').is_file())
        self.assertTrue(output.out.endswith(f'Installed to {root / "data" / "Hider"}\n'))


class ScriptTest(unittest.TestCase):
    def test_help_as_a_script(self) -> None:
        with captured() as output, self.assertRaises(SystemExit) as raised:
            run_as_main('install.py', '--help')
        self.assertEqual(raised.exception.code, 0)
        self.assertIn('Install the published Hider', output.out)
