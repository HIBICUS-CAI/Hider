import argparse, os, shutil, subprocess, sys
from common import PROJECT_FILE, TARGETS, host_target, publish_dir

parser = argparse.ArgumentParser(description='Publish Hider as a self-contained executable. Any target can be built from any host.')
parser.add_argument('targets', nargs='*', choices=TARGETS + ['all'], metavar='target',
                    help=f'Targets to build: {", ".join(TARGETS)} or all. Default is the current machine.')
args = parser.parse_args()

if 'all' in args.targets:
    targets = TARGETS
elif args.targets:
    targets = list(dict.fromkeys(args.targets))
else:
    host = host_target()
    if host is None:
        sys.exit(f'This machine is not a supported target, specify one of: {", ".join(TARGETS)}')
    targets = [host]

if shutil.which('dotnet') is None:
    sys.exit('dotnet was not found in PATH, install the .NET 10 SDK first.')

env = os.environ.copy()
# Avalonia.BuildServices otherwise sends build telemetry.
env['AVALONIA_TELEMETRY_OPTOUT'] = '1'

for target in targets:
    out_dir = publish_dir(target)
    print(f'Building {target} -> {out_dir}')
    if os.path.exists(out_dir):
        shutil.rmtree(out_dir)
    subprocess.run(['dotnet', 'publish', PROJECT_FILE, '-c', 'Release', '-r', target, '-o', out_dir], check=True, env=env)

print('Done.')
