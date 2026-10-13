#!/usr/bin/env python3
"""Release helpers for the release run of CI (.github/workflows/ci.yml, input "release").

    python scripts/make_release.py set-version 2.0.1
    python scripts/make_release.py package 2.0.1 --repo OWNER/NAME --input-dir artifacts --out-dir release

set-version rewrites <Version> in Directory.Build.props. package turns the publish outputs (one folder per target, named
Hider-<rid> as CI's publish artifacts are, or bin/publish/<rid>) into the release assets: one archive per target, the
installers install.bat / install.sh (they download that release's archive and check its SHA-256), SHA256SUMS.txt and
release-notes.md.
"""

from __future__ import annotations

import argparse
import hashlib
import re
import sys
import tarfile
import zipfile
from pathlib import Path

from common import ROOT_DIR, TARGETS, publish_dir

PROPS_FILE = ROOT_DIR / 'Directory.Build.props'
TEMPLATE_DIR = ROOT_DIR / 'scripts' / 'release'
VERSION_PATTERN = re.compile(r'\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?')
PROPS_VERSION = re.compile(r'<Version>[^<]*</Version>')
PLACEHOLDER = re.compile(r'@HIDER_[A-Z0-9_]+@')


def tag_name(version: str) -> str:
    return f'v{version}'


def archive_name(version: str, target: str) -> str:
    extension = 'zip' if target.startswith('win') else 'tar.gz'
    return f'Hider-{version}-{target}.{extension}'


def check_version(version: str) -> str:
    if VERSION_PATTERN.fullmatch(version) is None:
        sys.exit(f'invalid version "{version}" (expected MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-PRERELEASE)')
    return version


def set_version(version: str) -> None:
    text = PROPS_FILE.read_text(encoding='utf-8')
    if len(PROPS_VERSION.findall(text)) != 1:
        sys.exit(f'{PROPS_FILE} must have exactly one <Version> element')
    PROPS_FILE.write_text(PROPS_VERSION.sub(f'<Version>{version}</Version>', text), encoding='utf-8')
    print(f'{PROPS_FILE.name}: Version = {version}')


def find_input(input_dir: Path | None, target: str) -> Path:
    src = input_dir / f'Hider-{target}' if input_dir else publish_dir(target)
    exe = 'Hider.exe' if target.startswith('win') else 'Hider'
    if not (src / exe).is_file():
        sys.exit(f'No {exe} in {src}: publish {target} first.')
    return src


def tar_filter(info: tarfile.TarInfo) -> tarfile.TarInfo:
    """Fixed owner and modes: artifacts downloaded from Actions lose the executable bit, so set it here."""
    info.uid = info.gid = 0
    info.uname = info.gname = ''
    executable = info.isdir() or Path(info.name).name == 'Hider'
    info.mode = 0o755 if executable else 0o644
    return info


def make_archive(src: Path, out: Path, root: str) -> None:
    """Pack src under the folder root (directories included), entries in sorted order."""
    paths = sorted(src.rglob('*'))
    if out.name.endswith('.zip'):
        with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.write(src, root)
            for path in paths:
                archive.write(path, f'{root}/{path.relative_to(src).as_posix()}')
        return
    with tarfile.open(out, 'w:gz') as archive:
        archive.add(src, root, recursive=False, filter=tar_filter)
        for path in paths:
            archive.add(path, f'{root}/{path.relative_to(src).as_posix()}', recursive=False, filter=tar_filter)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            digest.update(chunk)
    return digest.hexdigest()


def render(template: str, values: dict[str, str], newline: str) -> str:
    text = (TEMPLATE_DIR / template).read_text(encoding='utf-8')
    for key, value in values.items():
        text = text.replace(f'@{key}@', value)
    left = sorted(set(PLACEHOLDER.findall(text)))
    if left:
        sys.exit(f'{template}: placeholders without a value: {", ".join(left)}')
    return text.replace('\r\n', '\n').replace('\n', newline)


def package(version: str, repo: str, input_dir: Path | None, out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    tag = tag_name(version)
    values = {
        'HIDER_VERSION': version,
        'HIDER_BASE_URL': f'https://github.com/{repo}/releases/download/{tag}',
    }
    assets: list[Path] = []
    for target in TARGETS:
        src = find_input(input_dir, target)
        out = out_dir / archive_name(version, target)
        print(f'Packing {src} -> {out}', flush=True)
        make_archive(src, out, f'Hider-{version}-{target}')
        values[f'HIDER_SHA256_{target.upper().replace("-", "_")}'] = sha256(out)
        assets.append(out)
    # cmd.exe reads batch files with CRLF line endings reliably; the shell script keeps LF.
    for template, name, newline in [('install.bat.in', 'install.bat', '\r\n'), ('install.sh.in', 'install.sh', '\n')]:
        out = out_dir / name
        with out.open('w', encoding='utf-8', newline='') as f:
            f.write(render(template, values, newline))
        assets.append(out)
    (out_dir / 'install.sh').chmod(0o755)
    sums = out_dir / 'SHA256SUMS.txt'
    sums.write_text(''.join(f'{sha256(p)}  {p.name}\n' for p in assets), encoding='utf-8')
    # Not an asset: the body of the GitHub Release.
    (out_dir / 'release-notes.md').write_text(render('release-notes.md.in', values, '\n'), encoding='utf-8')
    print(f'Release assets for {tag} in {out_dir}')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    set_parser = commands.add_parser('set-version', help='Rewrite <Version> in Directory.Build.props.')
    set_parser.add_argument('version')
    package_parser = commands.add_parser('package', help='Make the release assets.')
    package_parser.add_argument('version')
    package_parser.add_argument('--repo', required=True, help='GitHub repository OWNER/NAME the release belongs to.')
    package_parser.add_argument(
        '--input-dir', type=Path, help='Folder with one Hider-<rid> folder per target. Default: bin/publish/<rid>.'
    )
    package_parser.add_argument('--out-dir', type=Path, required=True)
    args = parser.parse_args()

    command: str = args.command
    version = check_version(args.version)
    if command == 'set-version':
        set_version(version)
    else:
        repo: str = args.repo
        if re.fullmatch(r'[\w.-]+/[\w.-]+', repo) is None:
            sys.exit(f'invalid repository "{repo}" (expected OWNER/NAME)')
        input_dir: Path | None = args.input_dir
        out_dir: Path = args.out_dir
        package(version, repo, input_dir, out_dir)


if __name__ == '__main__':
    main()
