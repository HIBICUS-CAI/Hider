"""scripts/install.py on Windows with the real Windows PowerShell: the desktop shortcut it makes.

test_install.py checks the command line install.py builds with powershell replaced; here the PowerShell part
(WScript.Shell, CreateShortcut, -OutputFormat Text, the paths through environment variables) really runs, and the .lnk
is read back. Windows only (proposal P-11: skipped elsewhere with the reason; CI runs it in the installer-windows job).
"""

from __future__ import annotations

import os
import unittest
from pathlib import Path
from unittest import mock

import install

from .support import arguments, captured, temp_dir
from .test_install import make_build
from .windows import SKIP_REASON, WINDOWS, keep_desktop_shortcut, read_shortcut, same_path


@unittest.skipUnless(WINDOWS, SKIP_REASON)
class InstallWindowsRealTest(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(os.path.realpath(temp_dir(self)))
        self.src = make_build(self.root / 'src', 'Hider.exe')
        self.link = keep_desktop_shortcut(self)

    def assert_shortcut(self, dest: Path) -> None:
        target, working_dir = read_shortcut(self.link)
        self.assertTrue(same_path(target, dest / 'Hider.exe'), target)
        self.assertTrue(same_path(working_dir, dest), working_dir)

    def test_st33_install_windows_makes_the_shortcut(self) -> None:
        # Characters PowerShell would read in a script ($, quote, space) reach it through the environment only.
        dest = self.root / "Program's $files (x)" / 'Hider'
        with captured() as output:
            install.install_windows(self.src, dest)
        self.assertEqual(output.out, f'Copying {self.src} -> {dest}\n')
        self.assertTrue((dest / 'Hider.exe').is_file())
        self.assertTrue((dest / 'Assets' / 'hider.svg').is_file())
        self.assert_shortcut(dest)

    def test_st33_replaces_an_existing_shortcut(self) -> None:
        with captured():
            install.install_windows(self.src, self.root / 'first')
            install.install_windows(self.src, self.root / 'second')
        self.assert_shortcut(self.root / 'second')

    def test_st33_main_into_local_app_data(self) -> None:
        local_app_data = self.root / 'lad'
        with (
            mock.patch.object(install, 'publish_dir', return_value=self.src),
            mock.patch.dict(os.environ, {'LOCALAPPDATA': str(local_app_data)}),
            arguments('install.py'),
            captured() as output,
        ):
            install.main()
        dest = local_app_data / 'Programs' / 'Hider'
        self.assertTrue(output.out.endswith(f'Installed to {dest}\n'))
        self.assertTrue((dest / 'Hider.exe').is_file())
        self.assert_shortcut(dest)


if __name__ == '__main__':
    unittest.main()
