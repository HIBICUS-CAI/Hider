"""scripts/update_lock_files.py: the restores that write both builds' lock files (dotnet is replaced)."""

from __future__ import annotations

import subprocess
import unittest
from unittest import mock

import update_lock_files
from common import PROJECT_FILE

from .support import REPO_DIR, arguments, captured, run_as_main


class CommandsTest(unittest.TestCase):
    def test_st60_solution_then_the_jit_build(self) -> None:
        write = ['--force-evaluate', '-p:RestoreLockedMode=false']
        self.assertEqual(
            update_lock_files.commands(),
            [
                ['dotnet', 'restore', str(REPO_DIR / 'Hider.slnx'), *write],
                ['dotnet', 'restore', str(PROJECT_FILE), *write, '-p:HiderJit=true'],
            ],
        )
        self.assertTrue(update_lock_files.SOLUTION_FILE.is_file())


class MainTest(unittest.TestCase):
    def run_main(self, *args: str, dotnet: bool = True, fail: int | None = None) -> tuple[str, list[list[str]]]:
        """Run main with dotnet replaced; fail: the index of the restore that fails. Return stdout and the commands."""
        ran: list[list[str]] = []

        def run(command: list[str], check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertTrue(check)
            ran.append(command)
            if len(ran) - 1 == fail:
                raise subprocess.CalledProcessError(1, command)
            return subprocess.CompletedProcess(command, 0)

        with (
            arguments('update_lock_files.py', *args),
            mock.patch('shutil.which', return_value='/usr/bin/dotnet' if dotnet else None),
            mock.patch('subprocess.run', side_effect=run),
            captured() as output,
        ):
            update_lock_files.main()
        return output.out, ran

    def test_st60_runs_both_restores_in_order(self) -> None:
        out, ran = self.run_main()
        self.assertEqual(ran, update_lock_files.commands())
        self.assertEqual(out.splitlines(), [*(' '.join(c) for c in update_lock_files.commands()), 'Done.'])

    def test_st60_failed_restore_stops(self) -> None:
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_main(fail=0)

    def test_st60_dotnet_missing_exits(self) -> None:
        with self.assertRaises(SystemExit) as raised:
            self.run_main(dotnet=False)
        self.assertEqual(raised.exception.code, 'dotnet was not found in PATH, install the .NET 10 SDK first.')

    def test_st60_takes_no_arguments(self) -> None:
        with (
            arguments('update_lock_files.py', 'linux-x64'),
            mock.patch('subprocess.run') as run,
            captured() as output,
            self.assertRaises(SystemExit) as raised,
        ):
            update_lock_files.main()
        self.assertEqual(raised.exception.code, 2)
        self.assertIn('unrecognized arguments: linux-x64', output.err)
        run.assert_not_called()

    def test_st60_help_as_a_script(self) -> None:
        with captured() as output, self.assertRaises(SystemExit) as raised:
            run_as_main('update_lock_files.py', '--help')
        self.assertEqual(raised.exception.code, 0)
        self.assertIn('Write the NuGet lock files again', ' '.join(output.out.split()))
