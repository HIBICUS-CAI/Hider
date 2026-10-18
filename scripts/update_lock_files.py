#!/usr/bin/env python3
"""Write the NuGet lock files again after a package version or HiderPublishPackVersion (Directory.Build.targets)
changed: packages.lock.json of every project of the solution (src/Hider's is the AOT build's) and
src/Hider/packages.jit.lock.json (the JIT build's). CI restores in locked mode and fails on lock files that differ."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys

from common import PROJECT_FILE, ROOT_DIR

SOLUTION_FILE = ROOT_DIR / 'Hider.slnx'


def commands() -> list[list[str]]:
    """The restores that write the lock files: the solution (AOT build), then src/Hider as the JIT build."""
    # Never in locked mode, which Directory.Build.props turns on where CI=true: these restores are meant to write.
    write = ['--force-evaluate', '-p:RestoreLockedMode=false']
    return [
        ['dotnet', 'restore', str(SOLUTION_FILE), *write],
        ['dotnet', 'restore', str(PROJECT_FILE), *write, '-p:HiderJit=true'],
    ]


def main() -> None:
    argparse.ArgumentParser(description=__doc__).parse_args()
    if shutil.which('dotnet') is None:
        sys.exit('dotnet was not found in PATH, install the .NET 10 SDK first.')
    for command in commands():
        print(' '.join(command), flush=True)
        subprocess.run(command, check=True)
    print('Done.')


if __name__ == '__main__':
    main()
