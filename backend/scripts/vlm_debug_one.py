"""단일 이미지 VLM raw 출력 덤프."""
import os, sys
BACKEND_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BACKEND_ROOT)

from PIL import Image
import cv2
from app.utils.image_io import read_image_bgr
from app.services.ai_inference_service import AIInferenceService

path = r"C:\Users\admin_25\Downloads\test\image\20260525_133548.jpg"
svc = AIInferenceService()
img_cv = read_image_bgr(path)
pil = Image.fromarray(cv2.cvtColor(img_cv, cv2.COLOR_BGR2RGB))
yolo = svc.yolo(img_cv, verbose=False, device=0)[0].boxes
x1,y1,x2,y2 = map(int, yolo.xyxy[0].tolist())
ctx = svc.get_contextual_crop(pil, (x1,y1,x2,y2))
zoom = pil.crop((x1,y1,x2,y2))
ar = (x2-x1)/(y2-y1)
raw = svc.run_vlm_analysis(ctx, zoom, ar)
print("=== RAW VLM ===")
print(raw)
print("=== EXTRACTED ===")
print(repr(svc.extract_banner_text(raw)))