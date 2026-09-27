"""Serve the generated Uno application at the same /GridSpace/ base path as Pages."""
import argparse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import urllib.parse

parser = argparse.ArgumentParser()
parser.add_argument('--directory', default='artifacts/site')
parser.add_argument('--port', type=int, default=4173)
args = parser.parse_args()
root = Path(args.directory).resolve()

class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.dll': 'application/octet-stream', '.webcil': 'application/octet-stream', '.mjs': 'text/javascript'}
    def __init__(self, *values, **kwargs):
        super().__init__(*values, directory=str(root), **kwargs)
    def do_GET(self):
        if urllib.parse.urlsplit(self.path).path == '/GridSpace':
            self.send_response(302); self.send_header('Location', '/GridSpace/'); self.end_headers(); return
        super().do_GET()
    def translate_path(self, path):
        if path.startswith('/GridSpace/'):
            path = path[len('/GridSpace'):]
        return super().translate_path(path)
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()

print(f'Serving {root} at http://127.0.0.1:{args.port}/GridSpace/', flush=True)
ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
