#!/usr/bin/env python3
"""Check that docs/wayland-migration.md lists every XWAYLAND-ASSUMPTION marker in src/ (CLAUDE.md).

Each file with markers must have as many rows in the table under "XWayland 前提の箇所" as it has markers, and every
row must point to a file that still has one. Run by CI (python-lint job).

    python scripts/check_wayland_list.py
"""

from __future__ import annotations

import sys
from collections import Counter

from common import ROOT_DIR

MARKER = 'XWAYLAND-ASSUMPTION'
DOC = ROOT_DIR / 'docs' / 'wayland-migration.md'
SECTION = '## XWayland 前提の箇所'


def markers() -> Counter[str]:
    """Count the markers per file (repository-relative path) in the C# sources under src/."""
    found: Counter[str] = Counter()
    for path in sorted((ROOT_DIR / 'src').rglob('*.cs')):
        parts = path.relative_to(ROOT_DIR).parts
        if 'bin' in parts or 'obj' in parts:
            continue
        count = path.read_text(encoding='utf-8').count(MARKER)
        if count:
            found['/'.join(parts)] += count
    return found


def table_rows() -> Counter[str]:
    """Count the table rows per file (the first column) in the section of the document."""
    rows: Counter[str] = Counter()
    in_section = False
    for line in DOC.read_text(encoding='utf-8').splitlines():
        if line.startswith('## '):
            in_section = line.strip() == SECTION
            continue
        if not in_section or not line.startswith('| src/'):
            continue
        rows[line.split('|')[1].strip()] += 1
    return rows


def main() -> int:
    code, doc = markers(), table_rows()
    problems = [
        f'{path}: {code.get(path, 0)} marker(s) in the code, {doc.get(path, 0)} row(s) in {DOC.name}'
        for path in sorted(set(code) | set(doc))
        if code.get(path, 0) != doc.get(path, 0)
    ]
    for problem in problems:
        print(problem, file=sys.stderr)
    if not problems:
        print(f'{sum(code.values())} {MARKER} marker(s), all listed in {DOC.name}')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
