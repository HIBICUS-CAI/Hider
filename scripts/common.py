"""Paths and targets shared by build.py, install.py, make_icons.py, check_wayland_list.py and make_release.py."""

from __future__ import annotations

import platform
from pathlib import Path
from typing import Literal

ROOT_DIR = Path(__file__).resolve().parent.parent
PROJECT_FILE = ROOT_DIR / 'src' / 'Hider' / 'Hider.csproj'
NATIVE_HELPER_DIR = ROOT_DIR / 'native' / 'hidercap'
TARGETS = ['win-x64', 'linux-x64', 'linux-arm64']
# The builds of each target (proposal P-14): native AOT, the default, and the regular JIT build (option --jit).
Variant = Literal['aot', 'jit']
AOT: Variant = 'aot'
JIT: Variant = 'jit'
VARIANTS: list[Variant] = [AOT, JIT]


def host_target() -> str | None:
    """Return the target matching this machine, or None if it is not a supported target."""
    system = platform.system()
    machine = platform.machine().lower()
    arch = {'amd64': 'x64', 'x86_64': 'x64', 'aarch64': 'arm64', 'arm64': 'arm64'}.get(machine)
    target = {'Windows': f'win-{arch}', 'Linux': f'linux-{arch}'}.get(system) if arch else None
    return target if target in TARGETS else None


def variant_suffix(variant: Variant) -> str:
    """What follows the target in a build's folder and file names: nothing for the AOT build, -jit for the JIT one."""
    return '' if variant == AOT else f'-{variant}'


def publish_dir(target: str, variant: Variant = AOT) -> Path:
    """Under bin/ so the SDK's default item globs do not pick the output up as project items."""
    return ROOT_DIR / 'bin' / 'publish' / f'{target}{variant_suffix(variant)}'
