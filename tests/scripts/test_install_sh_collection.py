"""What test_install_sh's load_tests collects, checked from outside that module.

A check inside test_install_sh can not catch a load_tests that drops the module's own tests, since it drops the check
too. This one builds the expected test ids from the module's classes, independently of load_tests: every TestCase of
the module as it is, every class derived from InstallerTest once per shell (<Class>_<shell>) and never unexpanded, and
InstallerTest itself not at all. Then it compares them with what the loader collects through load_tests.
"""

from __future__ import annotations

import inspect
import unittest
from collections.abc import Iterator

from . import test_install_sh as module


def ids(suite: unittest.TestSuite) -> Iterator[str]:
    for test in suite:
        if isinstance(test, unittest.TestSuite):
            yield from ids(test)
        else:
            yield test.id()


def expected_ids(loader: unittest.TestLoader) -> set[str]:
    expected: set[str] = set()
    for name, case in vars(module).items():
        if not inspect.isclass(case) or not issubclass(case, unittest.TestCase):
            continue
        if case.__module__ != module.__name__ or case is module.InstallerTest:
            continue
        methods = loader.getTestCaseNames(case)
        if issubclass(case, module.InstallerTest):
            expected |= {f'{module.__name__}.{name}_{shell.name}.{m}' for shell in module.SHELLS for m in methods}
        else:
            expected |= {f'{module.__name__}.{name}.{m}' for m in methods}
    return expected


class CollectionTest(unittest.TestCase):
    def test_st52_every_test_of_the_module_runs_once_and_installer_tests_once_per_shell(self) -> None:
        loader = unittest.TestLoader()
        collected = list(ids(loader.loadTestsFromModule(module)))
        expected = expected_ids(loader)
        self.assertEqual(len(collected), len(set(collected)), 'a test is collected twice')
        self.assertEqual(set(collected), expected)
        # The check is not empty on either side: the module has other tests (SyntaxTest, ParametrisationTest) and
        # installer tests.
        self.assertTrue(any('.SyntaxTest.' in test for test in expected))
        self.assertTrue(any('.OptionsTest_' in test for test in expected))


if __name__ == '__main__':
    unittest.main()
