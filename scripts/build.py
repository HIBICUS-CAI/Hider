import argparse, os, shutil, subprocess, sys
from common import NATIVE_HELPER_DIR, PROJECT_FILE, TARGETS, host_target, publish_dir

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



def build_native_helper(target):
    """Build libhidercap.so for a Linux target. Hider.csproj ships it next to the executable when it exists.

    The helper is optional at run time (the Screenshot portal grabber is used without it, decision D-6), so a
    missing cross toolchain only skips it. On a matching host a failure is an error.
    """
    helper = os.path.join(NATIVE_HELPER_DIR, 'out', target, 'libhidercap.so')
    if os.path.exists(helper):
        os.remove(helper)
    is_host = target == host_target()
    if shutil.which('make') is None:
        if is_host:
            sys.exit('make was not found in PATH, install build-essential and libpipewire-0.3-dev first.')
        print(f'warning: make not found, {target} is built without libhidercap.so')
        return
    result = subprocess.run(['make', '-C', NATIVE_HELPER_DIR, f'RID={target}'])
    if result.returncode != 0:
        if is_host:
            sys.exit(f'Building libhidercap.so for {target} failed.')
        print(f'warning: no cross toolchain for {target}, it is built without libhidercap.so')


env = os.environ.copy()
# Avalonia.BuildServices otherwise sends build telemetry.
env['AVALONIA_TELEMETRY_OPTOUT'] = '1'

for target in targets:
    out_dir = publish_dir(target)
    print(f'Building {target} -> {out_dir}')
    if os.path.exists(out_dir):
        shutil.rmtree(out_dir)
    if target.startswith('linux'):
        build_native_helper(target)
    subprocess.run(['dotnet', 'publish', PROJECT_FILE, '-c', 'Release', '-r', target, '-o', out_dir], check=True, env=env)

print('Done.')
