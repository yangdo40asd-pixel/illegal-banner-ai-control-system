"""Verify a published folder from a different cwd, without the development config or AI loading."""
import argparse
import asyncio
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
from urllib.request import urlopen
import uuid

ROOT = Path(__file__).resolve().parents[2]


async def check_websocket(port):
    import websockets
    async with websockets.connect(f'ws://127.0.0.1:{port}/ws/alerts') as connection:
        await connection.send('ping')
        for _ in range(20):
            message = await asyncio.wait_for(connection.recv(), timeout=10)
            if message == 'pong':
                return
        raise AssertionError('No WebSocket pong')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('package', type=Path)
    package = parser.parse_args().package.resolve()
    assert (package / '.bannerguard-root').is_file()
    assert not list(package.rglob('*.db')), 'Database included in package'
    assert not (package / 'configs/paths.local.json').exists()
    assert not (package / 'data/private').exists()
    assert (package / 'src/backend/requirements.lock.txt').is_file()
    assert (package / 'README.md').is_file()
    assert len(list((package / 'data/samples/cctv').glob('*.mp4'))) == 4
    assert list((package / 'data/samples/vlm').glob('*.jpg'))
    assert not list((package / 'src/backend/app').rglob('*.jpg')), 'Cached analysis images included'
    sample_hash = hashlib.sha256((package / 'data/samples/vlm/synthetic_demo.jpg').read_bytes()).digest()
    assert all(hashlib.sha256(file.read_bytes()).digest() == sample_hash
               for file in (package / 'Assets').glob('*.jpg')), 'Private desktop poster included'
    manifest = json.loads((package / 'models/manifest.json').read_text(encoding='utf-8'))
    digest = hashlib.sha256((package / 'models/yolo/best.pt').read_bytes()).hexdigest()
    assert digest == manifest['yolo']['sha256']
    output = ROOT / 'runtime/test-results'
    output.mkdir(parents=True, exist_ok=True)
    probe = package / '.verification'
    subprocess.run(['dotnet', 'build', str(ROOT / 'tests/packaging/PathProbe/PathProbe.csproj'),
                    '-c', 'Release', '-o', str(probe)], check=True, stdout=subprocess.DEVNULL)
    env = {key: value for key, value in os.environ.items() if not key.startswith('BANNERGUARD_')}
    env.update(PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
    with tempfile.TemporaryDirectory(prefix='bannerguard-package-') as cwd:
        result = subprocess.check_output(['dotnet', str(probe / 'PathProbe.dll')], cwd=cwd, env=env, text=True, encoding='utf-8')
        desktop_paths = json.loads(result)
        assert Path(desktop_paths['root']).resolve() == package
        assert Path(desktop_paths['backend']).resolve() == package / 'src/backend/app/main.py'
        assert Path(desktop_paths['database']).resolve() == Path(env['LOCALAPPDATA']) / 'BannerGuard/db/banner_tracking.db'
        code = 'import json; from app.core.paths import paths; print(json.dumps({"root":str(paths.root),"backend":str(paths.resolve("BackendEntry")),"database":str(paths.resolve("DatabasePath")),"cctv":str(paths.resolve("CctvMediaDirectory"))}))'
        env['PYTHONPATH'] = str(package / 'src/backend')
        backend_paths = json.loads(subprocess.check_output([sys.executable, '-c', code], cwd=cwd, env=env, text=True, encoding='utf-8'))
        for key in desktop_paths:
            assert Path(desktop_paths[key]).resolve() == Path(backend_paths[key]).resolve(), key
        with socket.socket() as free_port:
            free_port.bind(('127.0.0.1', 0))
            port = free_port.getsockname()[1]
        run_directory = output / ('package-' + uuid.uuid4().hex[:8])
        run_directory.mkdir()
        env.update(BANNERGUARD_DISABLE_AI='1', BANNERGUARD_API_BASE_URL=f'http://127.0.0.1:{port}',
                   BANNERGUARD_RUNTIME_DIRECTORY=str(run_directory),
                   BANNERGUARD_DATABASE_PATH=str(run_directory / 'isolated.db'))
        with (run_directory / 'server.log').open('w', encoding='utf-8') as log:
            server = subprocess.Popen([sys.executable, '-u', str(package / 'src/backend/app/main.py')], cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 30
                while True:
                    if server.poll() is not None:
                        raise RuntimeError(f'Server stopped; see {run_directory}')
                    try:
                        with urlopen(f'http://127.0.0.1:{port}/health', timeout=1) as response:
                            health = json.load(response)
                        break
                    except OSError:
                        if time.monotonic() >= deadline:
                            raise
                        time.sleep(.2)
                assert health['ai_ready'] is False
                asyncio.run(check_websocket(port))
            finally:
                server.terminate()
                server.wait(timeout=15)
    record = dict(package=str(package), desktop_paths=desktop_paths, backend_paths=backend_paths,
                  model_sha256=digest, http_health='PASS', websocket_ping='PASS',
                  private_data_absent=True, ai_loading='disabled for portability test')
    (output / 'package-verification.json').write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS: published C#/Python paths match; HTTP and WebSocket work from another cwd; private data absent')


if __name__ == '__main__':
    main()
