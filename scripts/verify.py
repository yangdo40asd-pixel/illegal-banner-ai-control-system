"""Preflight without loading torch, downloading a model, or modifying the DB."""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src' / 'backend'))
from app.core.paths import paths


def main():
    manifest = json.loads(paths.resolve('ModelManifest').read_text(encoding='utf-8'))
    model = paths.resolve('YoloModelPath')
    checks = {
        'backend_entry': paths.resolve('BackendEntry').is_file(),
        'yolo_weights': model.is_file(),
        'yolo_sha256': model.is_file() and hashlib.sha256(model.read_bytes()).hexdigest() == manifest['yolo']['sha256'],
        'cctv_videos': all((paths.resolve('CctvMediaDirectory') / file).is_file() for file in (
            '20260525_133553.mp4', '20260525_133611.mp4', '20260525_133838.mp4', '20260525_133919.mp4')),
        'vlm_directory': paths.resolve('VlmImageDirectory').is_dir(),
    }
    for name, ok in checks.items():
        print(f'{name}: {"PASS" if ok else "MISSING"}')
    print('DatabasePath:', str(paths.resolve('DatabasePath')).encode('ascii', errors='backslashreplace').decode())
    return 0 if all(checks.values()) else 1


if __name__ == '__main__':
    raise SystemExit(main())
