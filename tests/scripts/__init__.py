"""Unit tests for scripts/ (standard library unittest only, like the scripts themselves).

    python -m unittest discover -s tests/scripts -t .

Importing this package puts scripts/ on sys.path, so the tests import the scripts the way the scripts import common.
External commands (dotnet, make, powershell, curl, ...) are replaced; the tests build nothing and use no network.
"""

from __future__ import annotations

import sys
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parents[2] / 'scripts'
if str(SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_DIR))
