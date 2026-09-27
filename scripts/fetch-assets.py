"""Fetch content-pinned, OFL-licensed assets without relying on one download endpoint."""
import base64
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1] / 'src/GridSpace.App/Assets/Fonts'
ASSETS = {
    'Carlito-Regular.ttf': '427b95989fee609ab683d800ad00e22a4b14ecad',
    'Carlito-Bold.ttf': '67543b5a1efe4b910317116b02e8e9e01659692e',
    'Carlito-Italic.ttf': '8dc3c84edfc8f0b6c6acdbd112abfda827b484ac',
    'Carlito-BoldItalic.ttf': 'e2c5d8803828d9a3373459d71cf18ec06753cef5',
    'OFL.txt': '8d6b170e4d9a5580a34471dc4ac3e8fc5fbbd712',
}

def git_hash(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()

def download(url, api=False):
    headers = {'User-Agent': 'GridSpace-asset-fetcher'}
    if api:
        headers['Accept'] = 'application/vnd.github+json'
        if os.environ.get('GH_TOKEN'):
            headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=45) as response:
        data = response.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024:
        raise RuntimeError('Asset response exceeds the download limit.')
    return base64.b64decode(json.loads(data)['content']) if api else data

def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    for name, expected in ASSETS.items():
        path = ROOT / name
        if path.exists() and git_hash(path.read_bytes()) == expected:
            continue
        # The raw endpoint avoids shared unauthenticated API quotas on hosted runners.
        # It is only a transport: the immutable expected content hash is always authoritative.
        sources = [
            (f'https://raw.githubusercontent.com/google/fonts/main/ofl/carlito/{name}', False),
            (f'https://api.github.com/repos/google/fonts/git/blobs/{expected}', True),
        ]
        last_error = None
        complete = False
        for attempt in range(3):
            for url, api in sources:
                try:
                    data = download(url, api)
                    if git_hash(data) != expected:
                        raise RuntimeError('Asset content hash mismatch: ' + name)
                    temporary = path.with_suffix(path.suffix + '.tmp')
                    temporary.write_bytes(data)
                    temporary.replace(path)
                    print('Verified', name, expected, flush=True)
                    complete = True
                    break
                except Exception as error:
                    last_error = error
            if complete:
                break
            time.sleep(2 ** attempt)
        if not complete:
            raise RuntimeError('Could not retrieve verified asset ' + name) from last_error

if __name__ == '__main__':
    main()
