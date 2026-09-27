"""Fetch pinned, OFL-licensed application fonts. Never copies fonts from the host OS."""
import base64
import hashlib
import json
from pathlib import Path
import time
import urllib.request

root = Path(__file__).resolve().parents[1] / 'src/GridSpace.App/Assets/Fonts'
root.mkdir(parents=True, exist_ok=True)
assets = {
    'Carlito-Regular.ttf': '427b95989fee609ab683d800ad00e22a4b14ecad',
    'Carlito-Bold.ttf': '67543b5a1efe4b910317116b02e8e9e01659692e',
    'Carlito-Italic.ttf': '8dc3c84edfc8f0b6c6acdbd112abfda827b484ac',
    'Carlito-BoldItalic.ttf': 'e2c5d8803828d9a3373459d71cf18ec06753cef5',
    'OFL.txt': '8d6b170e4d9a5580a34471dc4ac3e8fc5fbbd712',
}
def git_hash(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()
for name, expected in assets.items():
    path = root / name
    if path.exists() and git_hash(path.read_bytes()) == expected:
        continue
    request = urllib.request.Request(f'https://api.github.com/repos/google/fonts/git/blobs/{expected}', headers={'User-Agent': 'GridSpace-asset-fetcher', 'Accept': 'application/vnd.github+json'})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                record = json.load(response)
            data = base64.b64decode(record['content'])
            if git_hash(data) != expected:
                raise RuntimeError('Font content hash mismatch: ' + name)
            temporary = path.with_suffix(path.suffix + '.tmp'); temporary.write_bytes(data); temporary.replace(path)
            print('Verified', name, expected)
            break
        except Exception:
            if attempt == 3:
                raise
            time.sleep(2 ** attempt)
