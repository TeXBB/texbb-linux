#!/usr/bin/env python3
"""Validate checked-in notices against the dependencies in the actual publish."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parent.parent
publish = Path(sys.argv[1]).resolve() if len(sys.argv) == 2 else root / '.codex-tmp/publish/linux-x64'
legal = publish / 'legal'
deps = json.loads((publish / 'TeXBB.deps.json').read_text(encoding='utf-8-sig'))
inventory = json.loads((legal / 'DEPENDENCIES.json').read_text(encoding='utf-8-sig'))
sources = json.loads((legal / 'SOURCES.json').read_text(encoding='utf-8-sig'))
actual = {name.removeprefix('runtimepack.') for name, data in deps['libraries'].items() if data['type'] != 'project'}
recorded = {item['Package'] for item in inventory}
if actual != recorded or len(recorded) != len(inventory):
    raise SystemExit(f'License inventory differs from published dependencies: missing={actual - recorded}, stale={recorded - actual}')
known = {item['File'] for item in sources}
for item in inventory:
    if not item['LicenseFiles'] or not set(item['LicenseFiles']).issubset(known):
        raise SystemExit(f'Missing license mapping: {item["Package"]}')
for item in sources:
    if Path(item['File']).name != item['File']:
        raise SystemExit('License filename must be a basename.')
    path = legal / item['File']
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest.lower() != item['SHA256'].lower():
        raise SystemExit(f'License hash mismatch: {path}')
print(f'PASS: {len(actual)} published dependency versions and {len(sources)} original license/notice files verified.')
