"""Helpers of the Windows-only tests (test_install_bat, test_install_windows): PowerShell and the desktop shortcut.

Those tests run the real install.bat and PowerShell, so they skip on other systems (proposal P-11) and CI runs them in
the installer-windows job.
"""

from __future__ import annotations

import base64
import os
import subprocess
import sys
import unittest
from pathlib import Path

WINDOWS = sys.platform == 'win32'
# The processes named Hider whose executable lies under $env:HIDER_TEST_ROOT. Filtered by name rather than with
# `Get-Process -Name Hider -ErrorAction SilentlyContinue`: when none runs, that records an error, and Windows PowerShell
# -Command then exits 1 although nothing failed.
HIDER_UNDER_ROOT = (
    "Get-Process | Where-Object { $_.ProcessName -eq 'Hider' -and $_.Path -and "
    '$_.Path.StartsWith($env:HIDER_TEST_ROOT, [StringComparison]::OrdinalIgnoreCase) }'
)
SKIP_REASON = 'Windows only: runs install.bat / Windows PowerShell (CI: the installer-windows job)'


def powershell(script: str, env: dict[str, str] | None = None) -> str:
    """Run a Windows PowerShell script (values through environment variables); return its standard output, stripped."""
    result = subprocess.run(
        ['powershell', '-NoProfile', '-NonInteractive', '-Command', script],
        env={**os.environ, **(env or {})},
        stdin=subprocess.DEVNULL,
        capture_output=True,
        text=True,
        check=False,
        timeout=120,
    )
    if result.returncode != 0:
        raise AssertionError(f'powershell failed ({result.returncode}): {script}\n{result.stdout}\n{result.stderr}')
    return result.stdout.strip()


def desktop() -> Path:
    """The folder the installers put Hider.lnk in, asked the way they ask."""
    return Path(powershell("[Environment]::GetFolderPath('Desktop')"))


def read_shortcut(link: Path) -> tuple[str, str]:
    """TargetPath and WorkingDirectory of a .lnk, read back through WScript.Shell. Passed as Base64 of UTF-8: PowerShell
    writes its output in the console's code page, which may lack letters of the paths."""
    out = powershell(
        '$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:HIDER_TEST_LINK); '
        "$v = $s.TargetPath + '|' + $s.WorkingDirectory; "
        'Write-Output ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($v)))',
        {'HIDER_TEST_LINK': str(link)},
    )
    target, working_dir = base64.b64decode(out, validate=True).decode('utf-8').split('|', 1)
    return target, working_dir


def same_path(a: str | Path, b: str | Path) -> bool:
    return os.path.normcase(os.path.normpath(str(a))) == os.path.normcase(os.path.normpath(str(b)))


def keep_desktop_shortcut(test: unittest.TestCase) -> Path:
    """Remove the desktop's Hider.lnk for the test and put back whatever was there afterwards; return its path."""
    link = desktop() / 'Hider.lnk'
    saved = link.read_bytes() if link.exists() else None

    def restore() -> None:
        if saved is None:
            link.unlink(missing_ok=True)
        else:
            link.write_bytes(saved)

    test.addCleanup(restore)
    link.unlink(missing_ok=True)
    return link


def stop_processes_under(folder: Path) -> None:
    """Stop the processes named Hider started from executables under folder (never a real Hider elsewhere), and wait
    for them to end so that their files can be removed."""
    powershell(
        f'{HIDER_UNDER_ROOT} | ForEach-Object {{ Stop-Process -Id $_.Id -Force; $null = $_.WaitForExit(30000) }}',
        {'HIDER_TEST_ROOT': str(folder)},
    )


def hider_processes_under(folder: Path) -> list[str]:
    """Paths of the running processes named Hider started from under folder."""
    out = powershell(
        f'{HIDER_UNDER_ROOT} | ForEach-Object {{ $_.Path }}',
        {'HIDER_TEST_ROOT': str(folder)},
    )
    return out.splitlines()
