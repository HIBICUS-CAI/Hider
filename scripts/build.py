#!/usr/bin/env python3
"""Publish Hider as a self-contained executable: native AOT by default, or the regular JIT build (--jit, a trimmed
single file). The AOT build is made on the target machine itself; the JIT build of any target on any host."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys

from common import NATIVE_HELPER_DIR, PROJECT_FILE, TARGETS, VARIANTS, host_target, publish_dir


def parse_args() -> tuple[list[str], str | None, str]:
    """Return the targets to build, the version given with --version (None: <Version> of Directory.Build.props) and the
    build (aot, or jit with --jit)."""
    parser = argparse.ArgumentParser(description=__doc__)
    # No choices= here: with nargs='*', argparse before Python 3.12 checks the empty default list against the choices
    # and rejects a call without targets.
    parser.add_argument(
        'targets',
        nargs='*',
        metavar='target',
        help=f'Targets to build: {", ".join(TARGETS)} or all. Default is the current machine.',
    )
    parser.add_argument(
        '--version',
        metavar='MAJOR.MINOR.PATCH',
        help='Version to build (the release workflow passes the one it tags). '
        'Default: <Version> of Directory.Build.props.',
    )
    parser.add_argument(
        '--jit',
        action='store_true',
        help='Build the regular JIT build (self-contained trimmed single file, buildable for any target) instead of '
        'native AOT (built for this machine only).',
    )
    args = parser.parse_args()
    chosen: list[str] = args.targets
    version: str | None = args.version
    variant = VARIANTS[1] if args.jit else VARIANTS[0]
    # ASCII digits only (\d alone takes other scripts' digits, which MSBuild's version would not).
    if version is not None and re.fullmatch(r'\d+\.\d+\.\d+', version, re.ASCII) is None:
        parser.error(f'invalid version {version} (expected MAJOR.MINOR.PATCH)')
    unknown = [t for t in chosen if t not in (*TARGETS, 'all')]
    if unknown:
        parser.error(f'invalid target {", ".join(unknown)} (choose from {", ".join([*TARGETS, "all"])})')
    if 'all' in chosen:
        targets = list(TARGETS)
    elif chosen:
        targets = list(dict.fromkeys(chosen))
    else:
        host = host_target()
        if host is None:
            sys.exit(f'This machine is not a supported target, specify one of: {", ".join(TARGETS)}')
        targets = [host]
    if variant == VARIANTS[0]:
        host = host_target()
        foreign = [t for t in targets if t != host]
        if foreign:
            sys.exit(
                f'Native AOT does not cross-compile: the AOT build of {", ".join(foreign)} is made on that machine '
                f'(this one is {host or "not a supported target"}). Add --jit for the regular build, which any machine '
                'can make.'
            )
    return targets, version, variant


def parse_targets() -> list[str]:
    return parse_args()[0]


def warn(message: str) -> None:
    print(f'warning: {message}', file=sys.stderr)


def build_native_helper(target: str) -> None:
    """Build libhidercap.so for a Linux target. Hider.csproj ships it next to the executable when it exists.

    The helper is optional at run time (the Screenshot portal grabber is used without it, decision D-6), so on another
    architecture a failed build only skips it, with make's own error output shown. On a matching host a failure is an
    error.
    """
    helper = NATIVE_HELPER_DIR / 'out' / target / 'libhidercap.so'
    helper.unlink(missing_ok=True)
    is_host = target == host_target()
    if shutil.which('make') is None:
        if is_host:
            sys.exit('make was not found in PATH, install build-essential and libpipewire-0.3-dev first.')
        warn(f'make not found, {target} is built without libhidercap.so')
        return
    result = subprocess.run(['make', '-C', str(NATIVE_HELPER_DIR), f'RID={target}'], check=False)
    if result.returncode != 0:
        reason = f'make exited with {result.returncode} (its output is above)'
        if is_host:
            sys.exit(f'Building libhidercap.so for {target} failed: {reason}.')
        warn(
            f'building libhidercap.so for {target} failed: {reason}; {target} is built without it '
            "(a cross compiler and that architecture's libpipewire-0.3-dev are needed)"
        )


def main() -> None:
    targets, version, variant = parse_args()
    # Global properties on the command line: Hider.csproj switches to the JIT build on HiderJit, and Version wins over
    # <Version> of Directory.Build.props.
    variant_args = ['-p:HiderJit=true'] if variant == VARIANTS[1] else []
    version_args = [f'-p:Version={version}'] if version else []
    if shutil.which('dotnet') is None:
        sys.exit('dotnet was not found in PATH, install the .NET 10 SDK first.')
    for target in targets:
        out_dir = publish_dir(target, variant)
        print(f'Building {target} ({variant}) -> {out_dir}')
        if out_dir.exists():
            shutil.rmtree(out_dir)
        if target.startswith('linux'):
            build_native_helper(target)
        command = ['dotnet', 'publish', str(PROJECT_FILE), '-c', 'Release', '-r', target, '-o', str(out_dir)]
        subprocess.run([*command, *variant_args, *version_args], check=True)
    print('Done.')


if __name__ == '__main__':
    main()
