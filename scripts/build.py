#!/usr/bin/env python3
"""Publish Hider as a self-contained executable: native AOT by default, or the regular JIT build (--jit, a trimmed
single file). The AOT build is made for the machine this runs on only; the JIT build of any target on any host."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys

from common import AOT, JIT, NATIVE_HELPER_DIR, PROJECT_FILE, TARGETS, Variant, host_target, publish_dir, symbols_dir


def parse_args() -> tuple[list[str], str | None, Variant]:
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
    variant: Variant = JIT if args.jit else AOT
    # ASCII digits only (\d alone takes other scripts' digits, which MSBuild's version would not).
    if version is not None and re.fullmatch(r'\d+\.\d+\.\d+', version, re.ASCII) is None:
        parser.error(f'invalid version {version} (expected MAJOR.MINOR.PATCH)')
    unknown = [t for t in chosen if t not in (*TARGETS, 'all')]
    if unknown:
        parser.error(f'invalid target {", ".join(unknown)} (choose from {", ".join([*TARGETS, "all"])})')
    # The host matters for the default target and for the AOT build only: the JIT build of given targets never asks.
    host = host_target() if variant == AOT or not chosen else None
    if 'all' in chosen:
        targets = list(TARGETS)
    elif chosen:
        targets = list(dict.fromkeys(chosen))
    elif host is None:
        sys.exit(f'This machine is not a supported target, specify one of: {", ".join(TARGETS)}')
    else:
        targets = [host]
    if variant == AOT:
        foreign = [t for t in targets if t != host]
        if foreign:
            here = f'this one is {host}' if host else 'this one is not a supported target'
            sys.exit(
                f'build.py makes the AOT build for the machine it runs on only ({here}): build {", ".join(foreign)} '
                'on such a machine, or add --jit for the regular build, which any machine can make for every target.'
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
    variant_args = ['-p:HiderJit=true'] if variant == JIT else []
    version_args = [f'-p:Version={version}'] if version else []
    if shutil.which('dotnet') is None:
        sys.exit('dotnet was not found in PATH, install the .NET 10 SDK first.')
    # ILCompiler links the AOT build with clang on Linux; without it the build fails late, in ILCompiler's own words.
    if variant == AOT and any(t.startswith('linux') for t in targets) and shutil.which('clang') is None:
        sys.exit(
            'clang was not found in PATH: the AOT build on Linux needs clang and zlib1g-dev '
            '(or add --jit for the regular build).'
        )
    for target in targets:
        out_dir = publish_dir(target, variant)
        print(f'Building {target} ({variant}) -> {out_dir}')
        # The AOT build also leaves its native symbols in their own folder (Hider.csproj, HiderSymbolsDir).
        old_dirs = [out_dir, symbols_dir(target)] if variant == AOT else [out_dir]
        for old in old_dirs:
            if old.exists():
                shutil.rmtree(old)
        if target.startswith('linux'):
            build_native_helper(target)
        command = ['dotnet', 'publish', str(PROJECT_FILE), '-c', 'Release', '-r', target, '-o', str(out_dir)]
        symbols_args = [f'-p:HiderSymbolsDir={symbols_dir(target)}'] if variant == AOT else []
        subprocess.run([*command, *variant_args, *symbols_args, *version_args], check=True)
    print('Done.')


if __name__ == '__main__':
    main()
