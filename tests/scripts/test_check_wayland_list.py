"""scripts/check_wayland_list.py: XWAYLAND-ASSUMPTION markers in src/ against the table in docs/wayland-migration.md."""

from __future__ import annotations

import unittest
from collections import Counter
from pathlib import Path
from unittest import mock

import check_wayland_list
import common

from .support import REPO_DIR, captured, enter, run_as_main, temp_dir

MARK = '// XWAYLAND-ASSUMPTION: test\n'

DOC_HEAD = """# Wayland 移行

## XWayland 前提の箇所

| ファイル | 関数 / 型 | 前提 | 移行時の扱い |
|---|---|---|---|
"""


class FakeRepoTest(unittest.TestCase):
    """Runs the checker against a temporary repository."""

    def setUp(self) -> None:
        self.root = temp_dir(self)
        self.doc = self.root / 'docs' / 'wayland-migration.md'
        self.doc.parent.mkdir()
        enter(
            self,
            mock.patch.object(check_wayland_list, 'ROOT_DIR', self.root),
            mock.patch.object(check_wayland_list, 'DOC', self.doc),
        )

    def source(self, relative: str, markers: int, extra: str = 'class A { }\n') -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(MARK * markers + extra, encoding='utf-8')
        return path

    def write_doc(self, text: str) -> None:
        self.doc.write_text(text, encoding='utf-8')

    @staticmethod
    def row(path: str) -> str:
        return f'| {path} | F | 前提 | 扱い |\n'


class MarkersTest(FakeRepoTest):
    def test_st06_counts_per_file(self) -> None:
        self.source('src/Hider/Platform/Linux/X11WindowHost.cs', 2)
        self.source('src/Hider.Core/Model/Placement.cs', 1)
        self.assertEqual(
            check_wayland_list.markers(),
            Counter({'src/Hider/Platform/Linux/X11WindowHost.cs': 2, 'src/Hider.Core/Model/Placement.cs': 1}),
        )

    def test_st06_counts_markers_on_one_line(self) -> None:
        self.source('src/A.cs', 0, 'int a; // XWAYLAND-ASSUMPTION: x, see XWAYLAND-ASSUMPTION above\n')
        self.assertEqual(check_wayland_list.markers(), Counter({'src/A.cs': 2}))

    def test_st06_files_without_markers_are_left_out(self) -> None:
        self.source('src/A.cs', 0)
        self.assertEqual(check_wayland_list.markers(), Counter())
        self.assertNotIn('src/A.cs', check_wayland_list.markers())

    def test_st06_no_src_folder(self) -> None:
        self.assertEqual(check_wayland_list.markers(), Counter())

    def test_st06_only_cs_files(self) -> None:
        self.source('src/Hider/App.axaml', 1)
        self.source('src/Hider/notes.md', 1)
        self.source('src/Hider/A.csproj', 1)
        self.source('src/Hider/B.cs', 1)
        self.assertEqual(check_wayland_list.markers(), Counter({'src/Hider/B.cs': 1}))

    def test_st06_only_under_src(self) -> None:
        self.source('tests/Hider.Core.Tests/A.cs', 1)
        self.source('tools/fakeportal/B.cs', 1)
        self.assertEqual(check_wayland_list.markers(), Counter())

    def test_st06_skips_bin_and_obj(self) -> None:
        self.source('src/Hider/bin/Release/Gen.cs', 1)
        self.source('src/Hider/obj/Release/net10.0/Gen.g.cs', 1)
        self.source('src/obj/Top.cs', 1)
        # Only whole folder names: a folder merely starting with bin / obj is checked.
        self.source('src/Hider/binary/A.cs', 1)
        self.source('src/Hider/objects/B.cs', 1)
        self.assertEqual(
            check_wayland_list.markers(), Counter({'src/Hider/binary/A.cs': 1, 'src/Hider/objects/B.cs': 1})
        )

    def test_st06_reads_utf8(self) -> None:
        self.source('src/A.cs', 1, '// 日本語のコメント\n')
        self.assertEqual(check_wayland_list.markers(), Counter({'src/A.cs': 1}))

    def test_st06_marker_text_is_case_sensitive(self) -> None:
        self.source('src/A.cs', 0, '// xwayland-assumption: no\n// XWAYLAND ASSUMPTION\n')
        self.assertEqual(check_wayland_list.markers(), Counter())


class TableRowsTest(FakeRepoTest):
    def test_st07_counts_rows_per_file(self) -> None:
        self.write_doc(DOC_HEAD + self.row('src/A.cs') + self.row('src/B.cs') + self.row('src/A.cs'))
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/A.cs': 2, 'src/B.cs': 1}))

    def test_st07_only_the_section(self) -> None:
        self.write_doc(
            '# T\n\n'
            + self.row('src/Before.cs')
            + '## 別の節\n\n'
            + self.row('src/Other.cs')
            + DOC_HEAD.split('\n', 2)[2]
            + self.row('src/In.cs')
            + '### 小見出し\n\n'
            + self.row('src/Sub.cs')
            + '## 次の節\n\n'
            + self.row('src/After.cs')
        )
        # A ### heading stays in the section; the next ## heading ends it.
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/In.cs': 1, 'src/Sub.cs': 1}))

    def test_st07_section_can_appear_again(self) -> None:
        self.write_doc(
            DOC_HEAD + self.row('src/A.cs') + '## 他\n' + self.row('src/X.cs') + DOC_HEAD + self.row('src/B.cs')
        )
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/A.cs': 1, 'src/B.cs': 1}))

    def test_st07_heading_with_trailing_spaces(self) -> None:
        self.write_doc('## XWayland 前提の箇所   \n' + self.row('src/A.cs'))
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/A.cs': 1}))

    def test_st07_heading_must_match(self) -> None:
        for heading in [
            '## XWayland 前提の箇所の続き',
            '### XWayland 前提の箇所',
            '#  XWayland 前提の箇所',
            '## xwayland 前提の箇所',
        ]:
            with self.subTest(heading=heading):
                self.write_doc(heading + '\n' + self.row('src/A.cs'))
                self.assertEqual(check_wayland_list.table_rows(), Counter())

    def test_st07_ignores_header_separator_and_other_rows(self) -> None:
        self.write_doc(
            DOC_HEAD
            + '|src/NoSpace.cs | x |\n'
            + '| tests/A.cs | x |\n'
            + '  | src/Indented.cs | x |\n'
            + 'src/NoPipe.cs\n'
            + 'text mentioning | src/Inline.cs |\n'
            + self.row('src/Real.cs')
        )
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/Real.cs': 1}))

    def test_st07_first_column_is_stripped(self) -> None:
        self.write_doc(DOC_HEAD + '| src/A.cs    |   F   |\n')
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/A.cs': 1}))

    def test_st07_row_without_a_closing_pipe(self) -> None:
        self.write_doc(DOC_HEAD + '| src/A.cs\n')
        self.assertEqual(check_wayland_list.table_rows(), Counter({'src/A.cs': 1}))

    def test_st07_no_section(self) -> None:
        self.write_doc('# T\n' + self.row('src/A.cs'))
        self.assertEqual(check_wayland_list.table_rows(), Counter())

    def test_st07_missing_doc_raises(self) -> None:
        with self.assertRaises(FileNotFoundError):
            check_wayland_list.table_rows()


class MainTest(FakeRepoTest):
    def test_st08_all_listed(self) -> None:
        self.source('src/A.cs', 2)
        self.source('src/B/C.cs', 1)
        self.write_doc(DOC_HEAD + self.row('src/A.cs') + self.row('src/B/C.cs') + self.row('src/A.cs'))
        with captured() as output:
            self.assertEqual(check_wayland_list.main(), 0)
        self.assertEqual(output.out, '3 XWAYLAND-ASSUMPTION marker(s), all listed in wayland-migration.md\n')
        self.assertEqual(output.err, '')

    def test_st08_nothing_anywhere_passes(self) -> None:
        self.write_doc(DOC_HEAD)
        with captured() as output:
            self.assertEqual(check_wayland_list.main(), 0)
        self.assertEqual(output.out, '0 XWAYLAND-ASSUMPTION marker(s), all listed in wayland-migration.md\n')

    def test_st08_mismatches_are_reported_sorted(self) -> None:
        self.source('src/Z.cs', 2)  # one row too few
        self.source('src/M.cs', 1)  # not in the table
        self.source('src/Ok.cs', 1)
        self.write_doc(DOC_HEAD + self.row('src/Z.cs') + self.row('src/Gone.cs') + self.row('src/Ok.cs'))
        with captured() as output:
            self.assertEqual(check_wayland_list.main(), 1)
        self.assertEqual(output.out, '')
        self.assertEqual(
            output.err.splitlines(),
            [
                'src/Gone.cs: 0 marker(s) in the code, 1 row(s) in wayland-migration.md',
                'src/M.cs: 1 marker(s) in the code, 0 row(s) in wayland-migration.md',
                'src/Z.cs: 2 marker(s) in the code, 1 row(s) in wayland-migration.md',
            ],
        )

    def test_st08_too_many_rows(self) -> None:
        self.source('src/A.cs', 1)
        self.write_doc(DOC_HEAD + self.row('src/A.cs') * 3)
        with captured() as output:
            self.assertEqual(check_wayland_list.main(), 1)
        self.assertEqual(output.err, 'src/A.cs: 1 marker(s) in the code, 3 row(s) in wayland-migration.md\n')


class ConstantsTest(unittest.TestCase):
    def test_st08_paths(self) -> None:
        self.assertEqual(check_wayland_list.DOC, REPO_DIR / 'docs' / 'wayland-migration.md')
        self.assertEqual(check_wayland_list.DOC.parents[1], common.ROOT_DIR)
        self.assertEqual(check_wayland_list.MARKER, 'XWAYLAND-ASSUMPTION')
        self.assertEqual(check_wayland_list.SECTION, '## XWayland 前提の箇所')


class RepositoryTest(unittest.TestCase):
    """The real repository passes (CI also runs the script itself)."""

    def test_st08_repository_is_consistent(self) -> None:
        with captured() as output:
            self.assertEqual(check_wayland_list.main(), 0, output.err)
        self.assertTrue(output.out.endswith('all listed in wayland-migration.md\n'))
        self.assertGreater(sum(check_wayland_list.markers().values()), 0)

    def test_st08_as_a_script(self) -> None:
        with captured() as output, self.assertRaises(SystemExit) as raised:
            run_as_main('check_wayland_list.py')
        self.assertEqual(raised.exception.code, 0)
        self.assertIn('XWAYLAND-ASSUMPTION marker(s), all listed', output.out)

    def test_st08_as_a_script_failing(self) -> None:
        root = temp_dir(self)
        (root / 'docs').mkdir()
        (root / 'docs' / 'wayland-migration.md').write_text(DOC_HEAD + '| src/A.cs | x |\n', encoding='utf-8')
        # The script imports ROOT_DIR from common afresh.
        with (
            mock.patch.object(common, 'ROOT_DIR', root),
            captured() as output,
            self.assertRaises(SystemExit) as raised,
        ):
            run_as_main('check_wayland_list.py')
        self.assertEqual(raised.exception.code, 1)
        self.assertIn('src/A.cs: 0 marker(s) in the code, 1 row(s)', output.err)
