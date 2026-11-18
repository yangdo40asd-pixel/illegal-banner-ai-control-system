"""Capture only the runtime dependency closure from the tested Python environment."""
from importlib.metadata import distribution, version
from pathlib import Path
from packaging.requirements import Requirement
from packaging.utils import canonicalize_name

root = Path(__file__).resolve().parents[1]
backend = root/'src/backend'
direct = [line.strip() for line in (backend/'requirements.in').read_text().splitlines() if line.strip()]
queue = list(direct)
locked = {}
while queue:
    name = canonicalize_name(queue.pop())
    if name in locked: continue
    package = distribution(name)
    locked[name] = package.version
    for item in package.requires or []:
        dependency = Requirement(item)
        if dependency.marker is None or dependency.marker.evaluate({'extra': ''}): queue.append(dependency.name)
index = '--extra-index-url https://download.pytorch.org/whl/cu126\n'
(backend/'requirements.lock.txt').write_text('# Captured Windows / Python 3.13 runtime closure; see docs/04-development.\n' + index + '\n'.join(f'{name}=={v}' for name,v in sorted(locked.items()))+'\n',encoding='utf-8')
(backend/'requirements.txt').write_text('\n'.join(f'{name}=={version(name)}' for name in direct)+'\n',encoding='utf-8')
test_names = ['pytest', 'fastapi', 'uvicorn', 'pydantic', 'websockets', 'pillow', 'numpy', 'opencv-python', 'psutil', 'httpx']
(backend/'requirements.test.txt').write_text('\n'.join(f'{name}=={version(name)}' for name in test_names)+'\n',encoding='utf-8')
print(f'Pinned {len(locked)} runtime packages; captured environment is not a clean-install certification')
