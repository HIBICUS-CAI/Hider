"""scripts/common.py: paths, targets and the host target."""

from __future__ import annotations

import ast
import os
import sys
import typing
import unittest
from unittest import mock

import common

from . import SCRIPTS_DIR
from .support import REPO_DIR


class PathsTest(unittest.TestCase):
    def test_st09_root_dir_is_the_repository(self) -> None:
        self.assertEqual(common.ROOT_DIR, REPO_DIR)
        self.assertTrue((common.ROOT_DIR / 'Hider.slnx').is_file())
        self.assertTrue(common.ROOT_DIR.is_absolute())

    def test_st09_project_file_is_the_app_project(self) -> None:
        self.assertEqual(common.PROJECT_FILE, REPO_DIR / 'src' / 'Hider' / 'Hider.csproj')
        self.assertTrue(common.PROJECT_FILE.is_file())

    def test_st09_native_helper_dir_has_the_makefile(self) -> None:
        self.assertEqual(common.NATIVE_HELPER_DIR, REPO_DIR / 'native' / 'hidercap')
        self.assertTrue((common.NATIVE_HELPER_DIR / 'Makefile').is_file())

    def test_st09_targets_are_the_three_supported_rids_in_order(self) -> None:
        self.assertEqual(common.TARGETS, ['win-x64', 'linux-x64', 'linux-arm64'])

    def test_st09_publish_dir_is_under_bin_publish(self) -> None:
        for target in common.TARGETS:
            with self.subTest(target=target):
                self.assertEqual(common.publish_dir(target), REPO_DIR / 'bin' / 'publish' / target)

    def test_st09_publish_dir_takes_any_name(self) -> None:
        self.assertEqual(common.publish_dir('osx-arm64'), REPO_DIR / 'bin' / 'publish' / 'osx-arm64')


class VariantsTest(unittest.TestCase):
    def test_st54_variants_are_aot_then_jit(self) -> None:
        # The first is the default build (native AOT); the JIT build is the option --jit of the scripts.
        self.assertEqual((common.AOT, common.JIT), ('aot', 'jit'))
        self.assertEqual(common.VARIANTS, [common.AOT, common.JIT])
        self.assertEqual(typing.get_args(common.Variant), ('aot', 'jit'))

    def test_st54_suffix(self) -> None:
        self.assertEqual(common.variant_suffix('aot'), '')
        self.assertEqual(common.variant_suffix('jit'), '-jit')

    def test_st54_publish_dir_of_each_build(self) -> None:
        for target in common.TARGETS:
            with self.subTest(target=target):
                self.assertEqual(common.publish_dir(target, 'aot'), REPO_DIR / 'bin' / 'publish' / target)
                self.assertEqual(common.publish_dir(target, 'jit'), REPO_DIR / 'bin' / 'publish' / f'{target}-jit')
                # The AOT build is the default.
                self.assertEqual(common.publish_dir(target), common.publish_dir(target, 'aot'))


class SymbolsTest(unittest.TestCase):
    def test_st61_symbols_dir_is_next_to_the_publish_outputs(self) -> None:
        for target in common.TARGETS:
            with self.subTest(target=target):
                self.assertEqual(common.symbols_dir(target), REPO_DIR / 'bin' / 'publish' / f'{target}-symbols')

    def test_st61_symbols_file(self) -> None:
        self.assertEqual(common.symbols_file('win-x64'), 'Hider.pdb')
        self.assertEqual(common.symbols_file('linux-x64'), 'Hider.dbg')
        self.assertEqual(common.symbols_file('linux-arm64'), 'Hider.dbg')


HOST_CASES: tuple[tuple[str, str, str | None], ...] = (
    ('Windows', 'AMD64', 'win-x64'),
    ('Windows', 'amd64', 'win-x64'),
    ('Windows', 'x86_64', 'win-x64'),
    ('Linux', 'x86_64', 'linux-x64'),
    ('Linux', 'X86_64', 'linux-x64'),
    ('Linux', 'amd64', 'linux-x64'),
    ('Linux', 'aarch64', 'linux-arm64'),
    ('Linux', 'arm64', 'linux-arm64'),
    ('Linux', 'ARM64', 'linux-arm64'),
    # win-arm64 is a valid name but not a supported target.
    ('Windows', 'ARM64', None),
    ('Darwin', 'x86_64', None),
    ('Darwin', 'arm64', None),
    ('FreeBSD', 'amd64', None),
    ('Linux', 'i686', None),
    ('Linux', 'armv7l', None),
    ('Linux', 'riscv64', None),
    ('Windows', 'x86', None),
    ('Linux', '', None),
    ('', 'x86_64', None),
)


class HostTargetTest(unittest.TestCase):
    def test_st10_maps_system_and_machine(self) -> None:
        for system, machine, expected in HOST_CASES:
            with (
                self.subTest(system=system, machine=machine),
                mock.patch('platform.system', return_value=system),
                mock.patch('platform.machine', return_value=machine),
            ):
                self.assertEqual(common.host_target(), expected)

    def test_st10_result_is_always_a_target_or_none(self) -> None:
        for system, machine, _ in HOST_CASES:
            with (
                mock.patch('platform.system', return_value=system),
                mock.patch('platform.machine', return_value=machine),
            ):
                target = common.host_target()
            self.assertTrue(target is None or target in common.TARGETS)

    def test_st10_this_machine_on_a_ci_runner(self) -> None:
        # Expected from what GitHub Actions says about the runner (RUNNER_OS, RUNNER_ARCH), not from platform again. A
        # development machine can be anything (macOS, 32-bit, ...), so it has no fixed expectation.
        if os.environ.get('GITHUB_ACTIONS') != 'true':
            self.skipTest('the expected target comes from the GitHub Actions runner (RUNNER_OS / RUNNER_ARCH)')
        runner_os = {'Linux': 'linux', 'Windows': 'win'}[os.environ['RUNNER_OS']]
        runner_arch = {'X64': 'x64', 'ARM64': 'arm64'}[os.environ['RUNNER_ARCH']]
        self.assertEqual(common.host_target(), f'{runner_os}-{runner_arch}')


class ScriptFilesTest(unittest.TestCase):
    """The scripts run directly as well as with python (CLAUDE.md): shebang and executable bit."""

    def test_st11_entry_points_are_executable(self) -> None:
        for name in [
            'build.py',
            'install.py',
            'make_icons.py',
            'check_wayland_list.py',
            'make_release.py',
            'update_lock_files.py',
        ]:
            with self.subTest(name=name):
                path = SCRIPTS_DIR / name
                self.assertTrue(path.read_text(encoding='utf-8').startswith('#!/usr/bin/env python3\n'))
                self.assertTrue(os.access(path, os.X_OK))

    def test_st11_common_is_a_module_only(self) -> None:
        self.assertFalse((SCRIPTS_DIR / 'common.py').read_text(encoding='utf-8').startswith('#!'))

    def test_st11_standard_library_only(self) -> None:
        allowed = set(sys.stdlib_module_names) | {'common', '__future__'}
        for path in sorted(SCRIPTS_DIR.glob('*.py')):
            with self.subTest(name=path.name):
                tree = ast.parse(path.read_text(encoding='utf-8'))
                for node in ast.walk(tree):
                    if isinstance(node, ast.Import):
                        names = [alias.name for alias in node.names]
                    elif isinstance(node, ast.ImportFrom) and node.module is not None:
                        names = [node.module]
                    else:
                        continue
                    for name in names:
                        self.assertIn(name.split('.')[0], allowed, f'{path.name} imports {name}')

    def test_st11_parses_as_python_3_10(self) -> None:
        for path in sorted(SCRIPTS_DIR.glob('*.py')):
            with self.subTest(name=path.name):
                ast.parse(path.read_text(encoding='utf-8'), feature_version=(3, 10))
