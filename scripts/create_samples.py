"""Generate synthetic public demo inputs; never overwrite private or existing files."""
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
out = root/'data/samples'
(out/'cctv').mkdir(parents=True, exist_ok=True)
(out/'vlm').mkdir(parents=True, exist_ok=True)
canvas = Image.new('RGB', (640,360), '#466452')
draw = ImageDraw.Draw(canvas)
draw.rectangle((90,100,550,220), fill='white', outline='#DB493A', width=6)
draw.text((135,135), 'COMMUNITY EVENT', font=ImageFont.load_default(size=30), fill='#17354C')
draw.text((152,185), 'SYNTHETIC TEST BANNER', font=ImageFont.load_default(size=18), fill='#17354C')
image = out/'vlm/synthetic_demo.jpg'
if not image.exists(): canvas.save(image)
frame = cv2.cvtColor(np.array(canvas), cv2.COLOR_RGB2BGR)
for name in ['20260525_133553.mp4','20260525_133611.mp4','20260525_133838.mp4','20260525_133919.mp4']:
    path = out/'cctv'/name
    if path.exists(): continue
    writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*'mp4v'), 10, (640,360))
    if not writer.isOpened(): raise RuntimeError(f'Cannot encode: {path}')
    try:
        for _ in range(30): writer.write(frame)
    finally: writer.release()
print('Synthetic public samples ready')
