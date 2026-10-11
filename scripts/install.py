#!/usr/bin/env python3
"""Install the published Hider for the current machine (overwrites existing files)."""

from __future__ import annotations

import argparse
import base64
import os
import platform
import re
import shutil
import subprocess
import sys
from pathlib import Path

from common import TARGETS, host_target, publish_dir

IS_WINDOWS = platform.system() == 'Windows'
EXE_NAME = 'Hider.exe' if IS_WINDOWS else 'Hider'


def copy_files(src_dir: Path, dest_dir: Path) -> None:
    print(f'Copying {src_dir} -> {dest_dir}', flush=True)
    try:
        shutil.copytree(src_dir, dest_dir, dirs_exist_ok=True)
    except (shutil.Error, OSError) as e:
        sys.exit(f'Copy failed, close Hider if it is running and retry.\n{e}')


def install_windows(src_dir: Path, dest_dir: Path) -> None:
    copy_files(src_dir, dest_dir)
    # WScript.Shell is available on every Windows install, so no extra Python package is needed.
    # Paths go through environment variables and the script is passed encoded to avoid quoting issues.
    script = '; '.join([
        "$ErrorActionPreference = 'Stop'",
        "$link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hider.lnk'",
        '$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($link)',
        '$shortcut.TargetPath = $env:HIDER_TARGET_PATH',
        '$shortcut.WorkingDirectory = $env:HIDER_WORKING_DIR',
        '$shortcut.Save()',
        "Write-Output ('Created shortcut: ' + $link)",
    ])
    env = os.environ.copy()
    env['HIDER_TARGET_PATH'] = str(dest_dir / EXE_NAME)
    env['HIDER_WORKING_DIR'] = str(dest_dir)
    encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
    command = ['powershell', '-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand', encoded]
    if subprocess.run(command, env=env, check=False).returncode != 0:
        sys.exit('Failed to create the desktop shortcut.')


def desktop_string(value: str) -> str:
    """Desktop Entry spec escape rule for string values."""
    return value.replace('\\', '\\\\')


def desktop_exec_arg(path: str) -> str:
    """Desktop Entry spec: quote the argument and backslash-escape " ` $ \\ inside it, apply the string escape rule on
    top, and double % so it is not read as a field code."""
    quoted = '"' + re.sub(r'(["`$\\])', r'\\\1', path) + '"'
    return desktop_string(quoted).replace('%', '%%')


def install_linux(src_dir: Path, dest_dir: Path, data_home: Path) -> None:
    copy_files(src_dir, dest_dir)
    exe_path = dest_dir / EXE_NAME
    exe_path.chmod(0o755)
    # A desktop entry makes Hider searchable from the Super key and pinnable to the dock.
    # StartupWMClass matches the WM_CLASS set in Program.cs so the running window groups with the pin.
    apps_dir = data_home / 'applications'
    apps_dir.mkdir(parents=True, exist_ok=True)
    entry_path = apps_dir / 'Hider.desktop'
    entry_path.write_text('\n'.join([
        '[Desktop Entry]',
        'Type=Application',
        'Name=Hider',
        'Comment=画面の一部をモザイクやパターンで覆って隠す',
        f'Exec={desktop_exec_arg(str(exe_path))}',
        f'Icon={desktop_string(str(dest_dir / "Assets" / "hider.svg"))}',
        'Terminal=false',
        'Categories=Utility;',
        'StartupWMClass=Hider',
        '',
    ]), encoding='utf-8')
    print(f'Created desktop entry: {entry_path}')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('-d', '--dest', help='Install folder. Default: %%LOCALAPPDATA%%\\Programs\\Hider on Windows, '
                                             '$XDG_DATA_HOME/Hider (~/.local/share/Hider) on Linux.')
    dest_arg: str | None = parser.parse_args().dest

    target = host_target()
    if target is None:
        sys.exit(f'This machine is not a supported target ({", ".join(TARGETS)}).')
    src_dir = publish_dir(target)
    if not (src_dir / EXE_NAME).is_file():
        sys.exit(f'No build found in {src_dir}, run "python scripts/build.py {target}" first.')

    if IS_WINDOWS:
        if dest_arg is None:
            local_app_data = os.environ.get('LOCALAPPDATA')
            if not local_app_data:
                sys.exit('LOCALAPPDATA is not set: give the install folder with --dest.')
            dest = Path(local_app_data) / 'Programs' / 'Hider'
        else:
            dest = Path(dest_arg)
        dest = dest.absolute()
        install_windows(src_dir, dest)
    else:
        data_home = Path(os.environ.get('XDG_DATA_HOME') or Path.home() / '.local' / 'share')
        dest = Path(dest_arg or data_home / "Hider").absolute()
        install_linux(src_dir, dest, data_home)
    print(f'Installed to {dest}')


if __name__ == '__main__':
    main()
