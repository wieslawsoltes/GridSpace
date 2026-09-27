"""Collect Uno's actual static publish output, preserving the project base path."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from datetime import datetime, timezone

source, destination = map(Path, sys.argv[1:3])
candidates = sorted(source.rglob('index.html'), key=lambda p: len(p.parts))
if not candidates:
    raise SystemExit('Uno publish did not produce index.html; refusing to deploy a placeholder.')
root = candidates[0].parent
if destination.exists():
    shutil.rmtree(destination)
shutil.copytree(root, destination)
(destination / '.nojekyll').touch()
sha = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
(destination / 'build-info.json').write_text(json.dumps({'application': 'GridSpace', 'commit': sha, 'builtAt': datetime.now(timezone.utc).isoformat(), 'runtime': 'Uno Platform / .NET WebAssembly / SkiaSharp'}, indent=2), encoding='utf-8')
if not any(destination.rglob('*.wasm')) and not any(destination.rglob('*.wasm.br')):
    raise SystemExit('Published output has no WebAssembly binary.')
print(f'Collected {sum(p.is_file() for p in destination.rglob("*"))} files from {root} for {sha}')
