"""scripts/build.py: argument parsing, libhidercap.so and the publish commands (dotnet and make are replaced)."""

from __future__ import annotations

import contextlib
import subprocess
import unittest
from collections.abc import Generator
from pathlib import Path
from unittest import mock

import build
from common import PROJECT_FILE, TARGETS, VARIANTS

from .support import Output, arguments, captured, enter, run_as_main, temp_dir


def completed(returncode: int) -> subprocess.CompletedProcess[bytes]:
    return subprocess.CompletedProcess([], returncode)


@contextlib.contextmanager
def which_only(*found: str) -> Generator[mock.MagicMock, None, None]:
    """shutil.which that finds the given commands (in /usr/bin) and nothing else."""

    def which(name: str) -> str | None:
        return f'/usr/bin/{name}' if name in found else None

    with mock.patch('shutil.which', side_effect=which) as which_mock:
        yield which_mock


class ParseArgsTest(unittest.TestCase):
    """The target and version tests use --jit (any target from any host); the AOT build's own rules are ST53."""

    def parse(self, *args: str) -> tuple[list[str], str | None, str]:
        with arguments('build.py', *args), mock.patch.object(build, 'host_target', return_value='linux-x64'):
            return build.parse_args()

    def parse_jit(self, *args: str) -> tuple[list[str], str | None]:
        targets, version, variant = self.parse(*args, '--jit')
        self.assertEqual(variant, 'jit')
        return targets, version

    def parse_error(self, *args: str) -> str:
        """Parse arguments argparse rejects; return its message (after the usage line)."""
        with (
            arguments('build.py', *args),
            mock.patch.object(build, 'host_target', return_value='linux-x64'),
            captured() as output,
            self.assertRaises(SystemExit) as raised,
        ):
            build.parse_args()
        self.assertEqual(raised.exception.code, 2)
        self.assertEqual(output.out, '')
        self.assertTrue(output.err.startswith('usage: '))
        return output.err.splitlines()[-1]

    def test_st01_each_target(self) -> None:
        for target in TARGETS:
            with self.subTest(target=target):
                self.assertEqual(self.parse_jit(target), ([target], None))

    def test_st01_all_is_every_target_in_order(self) -> None:
        targets, version = self.parse_jit('all')
        self.assertEqual(targets, ['win-x64', 'linux-x64', 'linux-arm64'])
        self.assertIsNone(version)
        # A copy: the caller may change it without changing TARGETS.
        self.assertIsNot(targets, TARGETS)

    def test_st01_all_wins_over_other_targets(self) -> None:
        self.assertEqual(self.parse_jit('linux-arm64', 'all')[0], TARGETS)
        self.assertEqual(self.parse_jit('all', 'win-x64', 'all')[0], TARGETS)

    def test_st01_keeps_the_given_order(self) -> None:
        self.assertEqual(self.parse_jit('linux-arm64', 'win-x64')[0], ['linux-arm64', 'win-x64'])

    def test_st01_drops_repeated_targets(self) -> None:
        self.assertEqual(self.parse_jit('linux-x64', 'win-x64', 'linux-x64', 'win-x64')[0], ['linux-x64', 'win-x64'])

    def test_st01_no_target_builds_the_host(self) -> None:
        for host in TARGETS:
            with (
                self.subTest(host=host),
                arguments('build.py'),
                mock.patch.object(build, 'host_target', return_value=host),
            ):
                self.assertEqual(build.parse_args(), ([host], None, 'aot'))

    def test_st01_no_target_on_an_unsupported_host_exits(self) -> None:
        with (
            arguments('build.py'),
            mock.patch.object(build, 'host_target', return_value=None),
            self.assertRaises(SystemExit) as raised,
        ):
            build.parse_args()
        self.assertEqual(
            raised.exception.code,
            'This machine is not a supported target, specify one of: win-x64, linux-x64, linux-arm64',
        )

    def test_st01_given_targets_do_not_ask_the_host(self) -> None:
        # The JIT build: the AOT build checks the targets against the host (ST53).
        with (
            arguments('build.py', 'win-x64', '--jit'),
            mock.patch.object(build, 'host_target', side_effect=AssertionError('host_target called')),
        ):
            self.assertEqual(build.parse_args(), (['win-x64'], None, 'jit'))

    def test_st01_unknown_target(self) -> None:
        self.assertEqual(
            self.parse_error('osx-x64'),
            'build.py: error: invalid target osx-x64 (choose from win-x64, linux-x64, linux-arm64, all)',
        )

    def test_st01_unknown_targets_are_all_named(self) -> None:
        self.assertIn('invalid target a, b (choose from', self.parse_error('a', 'win-x64', 'b'))

    def test_st01_target_names_are_case_sensitive(self) -> None:
        self.assertIn('invalid target Win-x64, ALL ', self.parse_error('Win-x64', 'ALL'))

    def test_st02_version(self) -> None:
        self.assertEqual(self.parse_jit('win-x64', '--version', '2.1.0'), (['win-x64'], '2.1.0'))
        self.assertEqual(self.parse_jit('--version=10.20.30', 'linux-x64'), (['linux-x64'], '10.20.30'))
        self.assertEqual(self.parse_jit('--version', '0.0.0'), (['linux-x64'], '0.0.0'))

    def test_st02_invalid_version(self) -> None:
        # Non-ASCII digits (Arabic-Indic, fullwidth): \d alone would take them.
        non_ascii = ['\u0661.\u0662.\u0663', '\uff12.\uff11.\uff10', '2.1.\u0660']
        for version in ['2.1', '2.1.0.0', 'v2.1.0', '2.1.0-beta', '2.1.0 ', ' 2.1.0', 'a.b.c', '2..1', '', *non_ascii]:
            with self.subTest(version=version):
                self.assertEqual(
                    self.parse_error('win-x64', '--version', version),
                    f'build.py: error: invalid version {version} (expected MAJOR.MINOR.PATCH)',
                )

    def test_st02_version_is_checked_before_targets(self) -> None:
        self.assertIn('invalid version x', self.parse_error('nope', '--version', 'x'))

    def test_st02_version_needs_a_value(self) -> None:
        self.assertIn('expected one argument', self.parse_error('--version'))

    def test_st03_unknown_option(self) -> None:
        self.assertIn('unrecognized arguments: --release', self.parse_error('win-x64', '--release'))

    def test_st03_help(self) -> None:
        with arguments('build.py', '--help'), captured() as output, self.assertRaises(SystemExit) as raised:
            build.parse_args()
        self.assertEqual(raised.exception.code, 0)
        text = ' '.join(output.out.split())
        self.assertIn('Publish Hider as a self-contained executable: native AOT by default', text)
        self.assertIn('Targets to build: win-x64, linux-x64, linux-arm64 or all. Default is the current machine.', text)
        self.assertIn('--version MAJOR.MINOR.PATCH', text)
        self.assertIn('--jit Build the regular JIT build', text)

    def test_st53_default_is_the_aot_build(self) -> None:
        self.assertEqual(self.parse(), (['linux-x64'], None, 'aot'))
        self.assertEqual(self.parse('linux-x64', '--version', '2.1.0'), (['linux-x64'], '2.1.0', 'aot'))

    def test_st53_jit_option(self) -> None:
        self.assertEqual(self.parse('--jit'), (['linux-x64'], None, 'jit'))
        self.assertEqual(self.parse('--jit', 'win-x64', '--version=1.0.0'), (['win-x64'], '1.0.0', 'jit'))
        self.assertEqual(VARIANTS, ['aot', 'jit'])

    def test_st53_jit_takes_no_value(self) -> None:
        # A word after it is a target.
        self.assertIn('invalid target aot', self.parse_error('--jit', 'aot'))
        self.assertIn('ignored explicit argument', self.parse_error('--jit=yes'))

    def test_st53_aot_builds_the_host_only(self) -> None:
        for host in TARGETS:
            others = [t for t in TARGETS if t != host]
            with (
                self.subTest(host=host),
                arguments('build.py', host),
                mock.patch.object(build, 'host_target', return_value=host),
            ):
                self.assertEqual(build.parse_args(), ([host], None, 'aot'))
            for args in [[others[0]], ['all'], [host, *others]]:
                with (
                    self.subTest(host=host, args=args),
                    arguments('build.py', *args),
                    mock.patch.object(build, 'host_target', return_value=host),
                    self.assertRaises(SystemExit) as raised,
                ):
                    build.parse_args()
                foreign = others[:1] if len(args) == 1 and args[0] != 'all' else others
                self.assertEqual(
                    raised.exception.code,
                    f'Native AOT does not cross-compile: the AOT build of {", ".join(foreign)} is made on that machine '
                    f'(this one is {host}). Add --jit for the regular build, which any machine can make.',
                )

    def test_st53_aot_on_an_unsupported_host_exits(self) -> None:
        with (
            arguments('build.py', 'linux-x64'),
            mock.patch.object(build, 'host_target', return_value=None),
            self.assertRaises(SystemExit) as raised,
        ):
            build.parse_args()
        self.assertEqual(
            raised.exception.code,
            'Native AOT does not cross-compile: the AOT build of linux-x64 is made on that machine '
            '(this one is not a supported target). Add --jit for the regular build, which any machine can make.',
        )

    def test_st53_arguments_are_checked_before_the_host(self) -> None:
        self.assertIn('invalid target nope', self.parse_error('win-x64', 'nope'))
        self.assertIn('invalid version x', self.parse_error('win-x64', '--version', 'x'))


class ParseTargetsTest(unittest.TestCase):
    def test_st01_returns_the_targets_only(self) -> None:
        with arguments('build.py', 'linux-arm64', '--version', '3.0.0', '--jit'):
            self.assertEqual(build.parse_targets(), ['linux-arm64'])

    def test_st01_no_target_is_the_host(self) -> None:
        with arguments('build.py'), mock.patch.object(build, 'host_target', return_value='win-x64'):
            self.assertEqual(build.parse_targets(), ['win-x64'])

    def test_st01_all(self) -> None:
        with arguments('build.py', 'win-x64', 'all', '--jit'):
            self.assertEqual(build.parse_targets(), TARGETS)


class WarnTest(unittest.TestCase):
    def test_st03_writes_to_stderr_with_a_prefix(self) -> None:
        with captured() as output:
            build.warn('something happened')
        self.assertEqual(output.err, 'warning: something happened\n')
        self.assertEqual(output.out, '')


class BuildNativeHelperTest(unittest.TestCase):
    def setUp(self) -> None:
        self.helper_dir = temp_dir(self) / 'hidercap'
        enter(self, mock.patch.object(build, 'NATIVE_HELPER_DIR', self.helper_dir))

    def helper(self, target: str) -> Path:
        return self.helper_dir / 'out' / target / 'libhidercap.so'

    def leave_old_helper(self, target: str) -> None:
        self.helper(target).parent.mkdir(parents=True)
        self.helper(target).write_bytes(b'old')

    def call(self, target: str, host: str | None, make: bool, returncode: int = 0) -> tuple[Output, mock.MagicMock]:
        """Run build_native_helper; return its output and the subprocess.run mock."""

        def run(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[bytes]:
            # The old helper is gone before make runs, so a failed make can not leave it to be shipped.
            self.assertFalse(self.helper(target).exists())
            return completed(returncode)

        with (
            mock.patch.object(build, 'host_target', return_value=host),
            which_only(*(['make'] if make else [])),
            mock.patch('subprocess.run', side_effect=run) as run_mock,
            captured() as output,
        ):
            build.build_native_helper(target)
        return output, run_mock

    def test_st04_runs_make_for_the_target(self) -> None:
        for target in ['linux-x64', 'linux-arm64']:
            for host in ['linux-x64', 'linux-arm64', 'win-x64', None]:
                with self.subTest(target=target, host=host):
                    output, run = self.call(target, host, make=True)
                    run.assert_called_once_with(['make', '-C', str(self.helper_dir), f'RID={target}'], check=False)
                    self.assertEqual(output.err, '')
                    self.assertEqual(output.out, '')

    def test_st04_removes_the_old_helper_first(self) -> None:
        self.leave_old_helper('linux-x64')
        self.leave_old_helper('linux-arm64')
        self.call('linux-x64', 'linux-x64', make=True)
        self.assertFalse(self.helper('linux-x64').exists())
        # Only the target's own helper.
        self.assertTrue(self.helper('linux-arm64').exists())

    def test_st04_without_an_old_helper(self) -> None:
        self.assertFalse(self.helper_dir.exists())
        self.call('linux-x64', 'linux-x64', make=True)

    def test_st04_make_missing_on_the_host_exits(self) -> None:
        self.leave_old_helper('linux-x64')
        with self.assertRaises(SystemExit) as raised:
            self.call('linux-x64', 'linux-x64', make=False)
        self.assertEqual(
            raised.exception.code, 'make was not found in PATH, install build-essential and libpipewire-0.3-dev first.'
        )
        self.assertFalse(self.helper('linux-x64').exists())

    def test_st04_make_missing_for_another_architecture_warns(self) -> None:
        self.leave_old_helper('linux-arm64')
        output, run = self.call('linux-arm64', 'linux-x64', make=False)
        run.assert_not_called()
        self.assertEqual(output.err, 'warning: make not found, linux-arm64 is built without libhidercap.so\n')
        self.assertFalse(self.helper('linux-arm64').exists())

    def test_st04_make_missing_on_an_unsupported_host_warns(self) -> None:
        output, run = self.call('linux-x64', None, make=False)
        run.assert_not_called()
        self.assertEqual(output.err, 'warning: make not found, linux-x64 is built without libhidercap.so\n')

    def test_st04_make_failing_on_the_host_exits(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.call('linux-arm64', 'linux-arm64', make=True, returncode=2)
        self.assertEqual(
            raised.exception.code,
            'Building libhidercap.so for linux-arm64 failed: make exited with 2 (its output is above).',
        )

    def test_st04_make_failing_for_another_architecture_warns(self) -> None:
        output, run = self.call('linux-arm64', 'linux-x64', make=True, returncode=1)
        run.assert_called_once()
        self.assertEqual(
            output.err,
            'warning: building libhidercap.so for linux-arm64 failed: make exited with 1 (its output is above); '
            'linux-arm64 is built without it '
            "(a cross compiler and that architecture's libpipewire-0.3-dev are needed)\n",
        )

    def test_st04_negative_return_code_is_a_failure(self) -> None:
        # A signal: subprocess reports it as a negative return code.
        output, _ = self.call('linux-x64', 'linux-arm64', make=True, returncode=-9)
        self.assertIn('make exited with -9', output.err)


class MainTest(unittest.TestCase):
    def setUp(self) -> None:
        self.publish_root = temp_dir(self) / 'publish'
        self.events: list[str] = []
        enter(
            self,
            mock.patch.object(build, 'publish_dir', side_effect=self.publish_dir),
            mock.patch.object(build, 'build_native_helper', side_effect=self.native_helper),
            mock.patch.object(build, 'host_target', return_value='linux-x64'),
        )

    def publish_dir(self, target: str, variant: str = 'aot') -> Path:
        return self.publish_root / f'{target}-{variant}'

    def native_helper(self, target: str) -> None:
        self.events.append(f'helper {target}')

    def run_main(self, *args: str, dotnet: bool = True, fail: str | None = None) -> tuple[Output, mock.MagicMock]:
        """Run main with dotnet publish replaced; fail: the target whose publish fails."""

        def run(command: list[str], check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertTrue(check)
            target = command[command.index('-r') + 1]
            # The old output was removed before publishing.
            self.assertFalse(Path(command[command.index('-o') + 1]).exists())
            self.events.append(f'publish {target}')
            if target == fail:
                raise subprocess.CalledProcessError(1, command)
            return completed(0)

        with (
            arguments('build.py', *args),
            which_only(*(['dotnet'] if dotnet else [])),
            mock.patch('subprocess.run', side_effect=run) as run_mock,
            captured() as output,
        ):
            build.main()
        return output, run_mock

    def publish_command(self, target: str, *extra: str, variant: str = 'jit') -> list[str]:
        out_dir = str(self.publish_dir(target, variant))
        return ['dotnet', 'publish', str(PROJECT_FILE), '-c', 'Release', '-r', target, '-o', out_dir, *extra]

    def jit_command(self, target: str, *extra: str) -> list[str]:
        return self.publish_command(target, '-p:HiderJit=true', *extra)

    def test_st05_all_targets(self) -> None:
        output, run = self.run_main('all', '--jit')
        self.assertEqual([c.args[0] for c in run.call_args_list], [self.jit_command(t) for t in TARGETS])
        # libhidercap.so is built for the Linux targets only, each before its publish.
        self.assertEqual(
            self.events,
            ['publish win-x64', 'helper linux-x64', 'publish linux-x64', 'helper linux-arm64', 'publish linux-arm64'],
        )
        self.assertEqual(
            output.out.splitlines(),
            [*(f'Building {t} (jit) -> {self.publish_dir(t, "jit")}' for t in TARGETS), 'Done.'],
        )

    def test_st05_version_is_passed_as_a_global_property(self) -> None:
        _, run = self.run_main('win-x64', 'linux-arm64', '--version', '2.3.4', '--jit')
        self.assertEqual(
            [c.args[0] for c in run.call_args_list],
            [self.jit_command('win-x64', '-p:Version=2.3.4'), self.jit_command('linux-arm64', '-p:Version=2.3.4')],
        )
        _, run = self.run_main('--version', '2.3.4')
        self.assertEqual(run.call_args.args[0], self.publish_command('linux-x64', '-p:Version=2.3.4', variant='aot'))

    def test_st05_no_target_builds_the_host(self) -> None:
        _, run = self.run_main()
        self.assertEqual([c.args[0] for c in run.call_args_list], [self.publish_command('linux-x64', variant='aot')])
        self.assertEqual(self.events, ['helper linux-x64', 'publish linux-x64'])

    def test_st05_windows_target_does_not_build_the_helper(self) -> None:
        self.run_main('win-x64', '--jit')
        self.assertEqual(self.events, ['publish win-x64'])

    def test_st05_replaces_the_old_output(self) -> None:
        for variant, args in [('aot', ['linux-x64']), ('jit', ['linux-arm64', '--jit'])]:
            with self.subTest(variant=variant):
                target = args[0]
                old = self.publish_dir(target, variant) / 'Assets'
                old.mkdir(parents=True)
                (old / 'stale.txt').write_text('old', encoding='utf-8')
                self.run_main(*args)
                self.assertFalse(self.publish_dir(target, variant).exists())

    def test_st53_aot_publish(self) -> None:
        # No HiderJit property (Hider.csproj builds native AOT), into the AOT build's folder.
        output, run = self.run_main('linux-x64')
        command = run.call_args.args[0]
        self.assertEqual(command, self.publish_command('linux-x64', variant='aot'))
        self.assertFalse(any('HiderJit' in arg for arg in command))
        self.assertEqual(output.out.splitlines()[0], f'Building linux-x64 (aot) -> {self.publish_dir("linux-x64")}')

    def test_st53_jit_publish(self) -> None:
        output, run = self.run_main('--jit')
        self.assertEqual(run.call_args.args[0], self.jit_command('linux-x64'))
        self.assertEqual(
            output.out.splitlines()[0], f'Building linux-x64 (jit) -> {self.publish_dir("linux-x64", "jit")}'
        )
        self.assertEqual(self.events, ['helper linux-x64', 'publish linux-x64'])

    def test_st53_each_build_has_its_own_folder(self) -> None:
        # A JIT build leaves the AOT build of the same target in place, and the other way round.
        for variant in ['aot', 'jit']:
            other = self.publish_dir('linux-x64', variant)
            other.mkdir(parents=True)
            (other / 'Hider').write_text(variant, encoding='utf-8')
        self.run_main('linux-x64', '--jit')
        self.assertEqual((self.publish_dir('linux-x64', 'aot') / 'Hider').read_text(encoding='utf-8'), 'aot')
        self.assertFalse(self.publish_dir('linux-x64', 'jit').exists())

    def test_st53_aot_of_another_target_exits_before_dotnet(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.run_main('win-x64')
        self.assertIn('Native AOT does not cross-compile', str(raised.exception.code))
        self.assertEqual(self.events, [])

    def test_st05_dotnet_missing_exits_before_building(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.run_main('all', '--jit', dotnet=False)
        self.assertEqual(raised.exception.code, 'dotnet was not found in PATH, install the .NET 10 SDK first.')
        self.assertEqual(self.events, [])

    def test_st05_arguments_are_checked_before_dotnet(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.run_main('nope', dotnet=False)
        self.assertEqual(raised.exception.code, 2)

    def test_st05_failed_publish_stops(self) -> None:
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_main('all', '--jit', fail='linux-x64')
        self.assertEqual(self.events, ['publish win-x64', 'helper linux-x64', 'publish linux-x64'])


class ScriptTest(unittest.TestCase):
    def test_st03_help_as_a_script(self) -> None:
        with captured() as output, self.assertRaises(SystemExit) as raised:
            run_as_main('build.py', '--help')
        self.assertEqual(raised.exception.code, 0)
        self.assertIn('Publish Hider as a self-contained executable: native AOT by default', output.out)

    def test_st03_invalid_target_as_a_script(self) -> None:
        with captured() as output, self.assertRaises(SystemExit) as raised:
            run_as_main('build.py', 'osx-x64')
        self.assertEqual(raised.exception.code, 2)
        self.assertIn('invalid target osx-x64', output.err)
