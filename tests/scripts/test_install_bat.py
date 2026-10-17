"""The Windows installer install.bat (scripts/release/install.bat.in as make_release.py renders it), run by cmd.exe.

Windows only (proposal P-11: skipped elsewhere with the reason; CI runs it in the installer-windows job). Each test
renders its own install.bat (version, base URL and SHA-256 of its case), starts `cmd.exe /c install.bat [folder]` with
HIDER_NO_LAUNCH=1 and LOCALAPPDATA, TEMP and TMP in a scratch folder, and checks the exit code, the messages, the files
and the desktop shortcut (which the installer writes to the real desktop: the test puts back what was there). The
installer's work folder (%TEMP%\\hider-install-*) must be gone after every run. Not covered: the 64-bit check
([Environment]::Is64BitOperatingSystem), which a 64-bit runner can not fail.
"""

from __future__ import annotations

import functools
import hashlib
import http.server
import os
import shutil
import subprocess
import threading
import unittest
from dataclasses import dataclass
from pathlib import Path

import make_release

from .support import temp_dir
from .windows import (
    SKIP_REASON,
    WINDOWS,
    hider_processes_under,
    keep_desktop_shortcut,
    read_shortcut,
    same_path,
    stop_processes_under,
)

VERSION = '9.8.7'
NAME = f'Hider-{VERSION}-win-x64'
ARCHIVE = f'{NAME}.zip'
BASE_URL = 'https://github.com/owner/repo/releases/download/v9.8.7'
# The end of cmd's pause prompt ("Press any key to continue . . . " on an English Windows; the same dots elsewhere).
PAUSE_END = '. . . '


@dataclass
class Result:
    code: int
    out: str
    err: str

    @property
    def errors(self) -> list[str]:
        """The installer's error lines (PowerShell may write other things to stderr, e.g. progress records)."""
        return [line for line in self.err.splitlines() if line.startswith('error: ')]


def default_files() -> dict[str, bytes]:
    return {
        'Hider.exe': b'MZ not really a program',
        'Hider.dll': bytes(range(256)) * 16,
        'Assets/x.png': b'png',
        'Assets/sub/y.txt': b'nested',
    }


@unittest.skipUnless(WINDOWS, SKIP_REASON)
class InstallBatTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(os.path.realpath(temp_dir(self)))
        self.release = self.root / 'release'
        self.temp = self.root / 'temp'
        self.local_app_data = self.root / 'lad'
        for folder in [self.release, self.temp, self.local_app_data]:
            folder.mkdir()
        self.dest = self.root / 'dest'
        self.link = keep_desktop_shortcut(self)
        self.addCleanup(stop_processes_under, self.root)
        self.digest = self.make_zip(self.release)
        self.bat = self.write_bat(self.release, self.digest)

    # Fixtures

    def make_zip(self, folder: Path, files: dict[str, bytes] | None = None) -> str:
        """Hider-<version>-win-x64.zip in folder, made by make_release.make_archive; return its SHA-256."""
        src = self.root / 'zip-src'
        if src.exists():
            shutil.rmtree(src)
        src.mkdir()
        for name, data in (default_files() if files is None else files).items():
            path = src / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        out = folder / ARCHIVE
        make_release.make_archive(src, out, NAME)
        return hashlib.sha256(out.read_bytes()).hexdigest()

    def write_bat(self, folder: Path, digest: str, base_url: str = BASE_URL) -> Path:
        values = {'HIDER_VERSION': VERSION, 'HIDER_BASE_URL': base_url, 'HIDER_SHA256_WIN_X64': digest}
        path = folder / 'install.bat'
        with path.open('w', encoding='utf-8', newline='') as f:
            f.write(make_release.render('install.bat.in', values, '\r\n'))
        return path

    def run_bat(
        self, *args: str, env: dict[str, str | None] | None = None, bat: Path | None = None, cwd: Path | None = None
    ) -> Result:
        variables = dict(os.environ)
        variables.update(
            {
                'HIDER_NO_LAUNCH': '1',
                'LOCALAPPDATA': str(self.local_app_data),
                'TEMP': str(self.temp),
                'TMP': str(self.temp),
            }
        )
        for key, value in (env or {}).items():
            if value is None:
                variables.pop(key, None)
            else:
                variables[key] = value
        # stdin from NUL: the pause after a failed double-click run returns at once.
        result = subprocess.run(
            ['cmd.exe', '/c', str(bat or self.bat), *args],
            env=variables,
            cwd=cwd or self.root,
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            check=False,
            timeout=300,
        )
        return Result(result.returncode, result.stdout, result.stderr)

    # Checks

    def assert_cleaned_up(self) -> None:
        left = [p.name for p in self.temp.iterdir() if p.name.startswith('hider-install-')]
        self.assertEqual(left, [], 'the work folder is removed')

    def assert_failed(self, result: Result, message: str, paused: bool = False) -> None:
        self.assertEqual(result.code, 1, f'stdout:\n{result.out}\nstderr:\n{result.err}')
        self.assertEqual(result.errors, [f'error: {message}'])
        self.assertEqual(result.out.rstrip().endswith(PAUSE_END.rstrip()), paused, result.out)
        self.assertFalse(self.link.exists(), 'no shortcut after a failure')
        self.assert_cleaned_up()

    def assert_installed(self, dest: Path, result: Result) -> None:
        self.assertEqual(result.code, 0, f'stdout:\n{result.out}\nstderr:\n{result.err}')
        self.assertEqual(result.errors, [])
        for name, data in default_files().items():
            self.assertEqual((dest / name).read_bytes(), data, name)
        target, working_dir = read_shortcut(self.link)
        self.assertTrue(same_path(target, dest / 'Hider.exe'), target)
        self.assertTrue(same_path(working_dir, dest), working_dir)
        # A successful run never waits for a key.
        self.assertNotIn(PAUSE_END, result.out)
        self.assert_cleaned_up()

    # C1: the failures

    def test_tampered_archive_is_refused(self) -> None:
        actual = self.make_zip(self.release, {**default_files(), 'Hider.exe': b'MZ tampered'})
        result = self.run_bat(str(self.dest))
        self.assert_failed(result, f'SHA-256 mismatch for {ARCHIVE} (expected {self.digest}, got {actual}).')
        self.assertFalse(self.dest.exists())
        self.assertIn(f'Using {self.release / ARCHIVE}', result.out.splitlines())

    def test_archive_without_the_executable(self) -> None:
        digest = self.make_zip(self.release, {'Assets/x.png': b'png'})
        bat = self.write_bat(self.release, digest)
        result = self.run_bat(str(self.dest), bat=bat)
        self.assert_failed(result, f'{ARCHIVE} does not contain {NAME}\\Hider.exe.')
        self.assertFalse(self.dest.exists())

    def test_running_hider_stops_the_install(self) -> None:
        # A process named Hider: a copy of Windows PowerShell under that name, sleeping.
        running = self.root / 'running'
        running.mkdir()
        exe = Path(shutil.copy2(shutil.which('powershell') or 'powershell.exe', running / 'Hider.exe'))
        process = subprocess.Popen(
            [str(exe), '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120'],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        self.addCleanup(process.wait, 30)
        self.addCleanup(process.kill)
        result = self.run_bat(str(self.dest))
        self.assertIsNone(process.poll(), 'the stand-in Hider ran during the install')
        self.assert_failed(result, 'Hider is running: quit it from the tray menu and run this again.')
        self.assertFalse(self.dest.exists())
        self.assertNotIn('Copying to', result.out)

    def test_no_local_app_data_without_a_folder(self) -> None:
        result = self.run_bat(env={'LOCALAPPDATA': None})
        # Started without arguments (double-click): the window waits for a key; the exit code stays 1.
        self.assert_failed(result, 'LOCALAPPDATA is not set: give the install folder as the argument.', paused=True)
        self.assertEqual(list(self.local_app_data.iterdir()), [])

    def test_copy_failure(self) -> None:
        # Hider.exe in the folder held open without sharing, as by a running Hider.
        self.dest.mkdir()
        locked = self.dest / 'Hider.exe'
        locked.write_bytes(b'old')
        holder = subprocess.Popen(
            [
                'powershell',
                '-NoProfile',
                '-NonInteractive',
                '-Command',
                "$f = [IO.File]::Open($env:HIDER_TEST_LOCKED, 'Open', 'ReadWrite', 'None'); "
                "[Console]::Out.WriteLine('locked'); [Console]::Out.Flush(); Start-Sleep -Seconds 120",
            ],
            env={**os.environ, 'HIDER_TEST_LOCKED': str(locked)},
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            text=True,
        )
        self.addCleanup(holder.wait, 30)
        self.addCleanup(holder.kill)
        assert holder.stdout is not None
        self.assertEqual(holder.stdout.readline().strip(), 'locked')
        result = self.run_bat(str(self.dest))
        self.assertEqual(result.code, 1, result.err)
        (error,) = result.errors
        self.assertTrue(error.startswith('error: Copy failed, quit Hider if it is running and run this again. '), error)
        self.assertFalse(self.link.exists())
        self.assert_cleaned_up()

    def test_failure_after_a_double_click_pauses_and_keeps_the_exit_code(self) -> None:
        # if not "%HIDER_EXIT%"=="0" if "%~1"=="" pause: the pause must not replace the exit code with its own 0.
        actual = self.make_zip(self.release, {**default_files(), 'Hider.exe': b'MZ tampered'})
        result = self.run_bat()
        self.assert_failed(
            result, f'SHA-256 mismatch for {ARCHIVE} (expected {self.digest}, got {actual}).', paused=True
        )
        self.assertFalse((self.local_app_data / 'Programs').exists())

    def test_failure_with_a_folder_does_not_pause(self) -> None:
        self.make_zip(self.release, {**default_files(), 'Hider.exe': b'MZ tampered'})
        result = self.run_bat(str(self.dest))
        self.assertEqual(result.code, 1)
        self.assertNotIn(PAUSE_END, result.out)

    # C2: the successful paths

    def test_installs_into_the_folder(self) -> None:
        result = self.run_bat(str(self.dest))
        self.assert_installed(self.dest, result)
        self.assertEqual(
            result.out.splitlines(),
            [
                f'Using {self.release / ARCHIVE}',
                f'Copying to {self.dest}',
                f'Created shortcut: {self.link}',
                f'Installed Hider {VERSION} to {self.dest}',
            ],
        )
        # The archive next to the installer is copied, not moved.
        self.assertTrue((self.release / ARCHIVE).is_file())

    def test_default_folder_is_local_app_data(self) -> None:
        result = self.run_bat()
        dest = self.local_app_data / 'Programs' / 'Hider'
        self.assert_installed(dest, result)
        self.assertIn(f'Installed Hider {VERSION} to {dest}', result.out.splitlines())

    def test_empty_argument_is_the_default_folder(self) -> None:
        result = self.run_bat('')
        self.assert_installed(self.local_app_data / 'Programs' / 'Hider', result)

    def test_relative_folder_is_made_absolute(self) -> None:
        work = self.root / 'cwd'
        work.mkdir()
        result = self.run_bat('rel\\Hider', cwd=work)
        self.assert_installed(work / 'rel' / 'Hider', result)
        self.assertIn(f'Copying to {work / "rel" / "Hider"}', result.out.splitlines())

    def test_overwrites_an_existing_install(self) -> None:
        (self.dest / 'Assets').mkdir(parents=True)
        (self.dest / 'Hider.exe').write_bytes(b'old')
        (self.dest / 'Assets' / 'x.png').write_bytes(b'old')
        (self.dest / 'Assets' / 'old.png').write_bytes(b'kept')
        result = self.run_bat(str(self.dest))
        self.assert_installed(self.dest, result)
        # Overwrites, does not clean up: files the new version does not have stay.
        self.assertEqual((self.dest / 'Assets' / 'old.png').read_bytes(), b'kept')

    def serve(self, folder: Path) -> tuple[str, list[str]]:
        """Serve folder over HTTP on 127.0.0.1; return the base URL and the list of requested paths."""
        requests: list[str] = []

        class Handler(http.server.SimpleHTTPRequestHandler):
            # Once per request, whatever the status; nothing on stderr.
            def log_request(self, code: int | str = '-', size: int | str = '-') -> None:
                requests.append(self.path)

            def log_message(self, format: str, *args: object) -> None:
                pass

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(Handler, directory=str(folder)))
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(thread.join, 30)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        return f'http://127.0.0.1:{server.server_port}/dl', requests

    def test_downloads_when_the_archive_is_not_next_to_it(self) -> None:
        store = self.root / 'store' / 'dl'
        store.mkdir(parents=True)
        shutil.move(str(self.release / ARCHIVE), store / ARCHIVE)
        base_url, requests = self.serve(self.root / 'store')
        bat = self.write_bat(self.release, self.digest, base_url)
        result = self.run_bat(str(self.dest), bat=bat)
        self.assert_installed(self.dest, result)
        self.assertEqual(result.out.splitlines()[0], f'Downloading {base_url}/{ARCHIVE}')
        self.assertEqual(requests, [f'/dl/{ARCHIVE}'])

    def test_download_not_found(self) -> None:
        (self.release / ARCHIVE).unlink()
        empty = self.root / 'empty'
        empty.mkdir()
        base_url, requests = self.serve(empty)
        bat = self.write_bat(self.release, self.digest, base_url)
        result = self.run_bat(str(self.dest), bat=bat)
        self.assertEqual(result.code, 1, result.err)
        (error,) = result.errors
        # WebClient's WebException, as PowerShell reports a failed .NET call.
        self.assertIn('(404) Not Found', error)
        self.assertEqual(requests, [f'/dl/{ARCHIVE}'])
        self.assertFalse(self.dest.exists())
        self.assertFalse(self.link.exists())
        self.assert_cleaned_up()

    def test_starts_hider_unless_told_not_to(self) -> None:
        # Hider.exe is a copy of Windows PowerShell: started without arguments in its own console, it stays.
        powershell_exe = Path(shutil.which('powershell') or 'powershell.exe')
        files = {**default_files(), 'Hider.exe': powershell_exe.read_bytes()}
        digest = self.make_zip(self.release, files)
        bat = self.write_bat(self.release, digest)
        result = self.run_bat(str(self.dest), bat=bat, env={'HIDER_NO_LAUNCH': None})
        self.assertEqual(result.code, 0, result.err)
        self.assertEqual(result.out.splitlines()[-1], 'Started Hider (tray icon).')
        started = hider_processes_under(self.dest)
        self.assertEqual(len(started), 1, started)
        self.assertTrue(same_path(started[0], self.dest / 'Hider.exe'), started)
        self.assertEqual(hider_processes_under(self.root), started)

    def test_no_launch_does_not_start_it(self) -> None:
        powershell_exe = Path(shutil.which('powershell') or 'powershell.exe')
        digest = self.make_zip(self.release, {**default_files(), 'Hider.exe': powershell_exe.read_bytes()})
        bat = self.write_bat(self.release, digest)
        result = self.run_bat(str(self.dest), bat=bat)
        self.assertEqual(result.code, 0, result.err)
        self.assertNotIn('Started Hider (tray icon).', result.out)
        self.assertEqual(hider_processes_under(self.root), [])


if __name__ == '__main__':
    unittest.main()
