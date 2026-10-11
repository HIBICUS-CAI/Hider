import argparse, base64, os, platform, re, shutil, subprocess, sys
from common import TARGETS, host_target, publish_dir

parser = argparse.ArgumentParser(description='Install the published Hider for the current machine (overwrites existing files).')
parser.add_argument('-d', '--dest', help='Install folder. Default: %%LOCALAPPDATA%%\\Programs\\Hider on Windows, '
                                         '$XDG_DATA_HOME/Hider (~/.local/share/Hider) on Linux.')
args = parser.parse_args()

target = host_target()
if target is None:
    sys.exit(f'This machine is not a supported target ({", ".join(TARGETS)}).')

is_windows = platform.system() == 'Windows'
exe_name = 'Hider.exe' if is_windows else 'Hider'
src_dir = publish_dir(target)
if not os.path.isfile(os.path.join(src_dir, exe_name)):
    sys.exit(f'No build found in {src_dir}, run "python scripts/build.py {target}" first.')


def copy_files(dest_dir):
    print(f'Copying {src_dir} -> {dest_dir}', flush=True)
    try:
        shutil.copytree(src_dir, dest_dir, dirs_exist_ok=True)
    except (shutil.Error, OSError) as e:
        sys.exit(f'Copy failed, close Hider if it is running and retry.\n{e}')


def install_windows(dest_dir):
    copy_files(dest_dir)
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
    env['HIDER_TARGET_PATH'] = os.path.join(dest_dir, exe_name)
    env['HIDER_WORKING_DIR'] = dest_dir
    encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
    command = ['powershell', '-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand', encoded]
    if subprocess.run(command, env=env).returncode != 0:
        sys.exit('Failed to create the desktop shortcut.')


def desktop_string(value):
    # Desktop Entry spec escape rule for string values.
    return value.replace('\\', '\\\\')


def desktop_exec_arg(path):
    # Desktop Entry spec: quote the argument and backslash-escape " ` $ \ inside it, apply the string
    # escape rule on top, and double % so it is not read as a field code.
    quoted = '"' + re.sub(r'(["`$\\])', r'\\\1', path) + '"'
    return desktop_string(quoted).replace('%', '%%')


def install_linux(dest_dir, data_home):
    copy_files(dest_dir)
    exe_path = os.path.join(dest_dir, exe_name)
    os.chmod(exe_path, 0o755)
    # A desktop entry makes Hider searchable from the Super key and pinnable to the dock.
    # StartupWMClass matches the WM_CLASS set in Program.cs so the running window groups with the pin.
    apps_dir = os.path.join(data_home, 'applications')
    os.makedirs(apps_dir, exist_ok=True)
    entry_path = os.path.join(apps_dir, 'Hider.desktop')
    with open(entry_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join([
            '[Desktop Entry]',
            'Type=Application',
            'Name=Hider',
            'Comment=Cover part of the screen with a plain window',
            f'Exec={desktop_exec_arg(exe_path)}',
            f'Icon={desktop_string(os.path.join(dest_dir, "Assets", "hider.svg"))}',
            'Terminal=false',
            'Categories=Utility;',
            'StartupWMClass=Hider',
            '',
        ]))
    print(f'Created desktop entry: {entry_path}')


if is_windows:
    dest = os.path.abspath(args.dest or os.path.join(os.environ['LOCALAPPDATA'], 'Programs', 'Hider'))
    install_windows(dest)
else:
    data_home = os.environ.get('XDG_DATA_HOME') or os.path.join(os.path.expanduser('~'), '.local', 'share')
    dest = os.path.abspath(args.dest or os.path.join(data_home, 'Hider'))
    install_linux(dest, data_home)

print(f'Installed to {dest}')
