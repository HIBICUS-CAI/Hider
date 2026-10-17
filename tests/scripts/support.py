"""Helpers shared by the tests of scripts/."""

from __future__ import annotations

import contextlib
import io
import runpy
import sys
import tempfile
import unittest
from collections.abc import Generator
from pathlib import Path
from unittest import mock

from . import SCRIPTS_DIR

REPO_DIR = SCRIPTS_DIR.parent


def enter(test: unittest.TestCase, *managers: contextlib.AbstractContextManager[object]) -> None:
    """Enter the context managers (mock patches) for the rest of the test: setUp's counterpart of `with`."""
    stack = contextlib.ExitStack()
    test.addCleanup(stack.close)
    for manager in managers:
        stack.enter_context(manager)


def temp_dir(test: unittest.TestCase) -> Path:
    """A new empty folder, removed when the test ends."""
    holder = tempfile.TemporaryDirectory(prefix='hider-scripts-test-')
    test.addCleanup(holder.cleanup)
    return Path(holder.name)


class Output:
    """What the code under test wrote to stdout and stderr."""

    def __init__(self) -> None:
        self.stdout = io.StringIO()
        self.stderr = io.StringIO()

    @property
    def out(self) -> str:
        return self.stdout.getvalue()

    @property
    def err(self) -> str:
        return self.stderr.getvalue()


@contextlib.contextmanager
def captured() -> Generator[Output, None, None]:
    output = Output()
    with contextlib.redirect_stdout(output.stdout), contextlib.redirect_stderr(output.stderr):
        yield output


@contextlib.contextmanager
def arguments(*args: str) -> Generator[None, None, None]:
    """sys.argv for argparse: the program name and then the arguments."""
    with mock.patch.object(sys, 'argv', list(args)):
        yield


def run_as_main(script: str, *args: str) -> None:
    """Run scripts/<script> as `python scripts/<script> <args>` does (its `if __name__ == '__main__'` block).

    The script is executed afresh but imports the already loaded common module, so a patch of common's attributes
    applies to it.
    """
    path = SCRIPTS_DIR / script
    with arguments(str(path), *args):
        runpy.run_path(str(path), run_name='__main__')
