import cv2
import json
from ultralytics import YOLO

model = YOLO("best.pt")
cap = cv2.VideoCapture(r"d:\138.종합 민원 이미지 AI데이터\WPF\20260525_133016_1_1.mp4")
fps = cap.get(cv2.CAP_PROP_FPS)

results_dict = {}

frame_idx = 0
while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    # Process every frame
    time_ms = int((frame_idx / fps) * 1000)
    
    res = model(frame, conf=0.4, verbose=False)
    boxes = []
    for r in res:
        for box in r.boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            conf = float(box.conf[0])
            boxes.append({
                "X": int(x1), "Y": int(y1), "Width": int(x2-x1), "Height": int(y2-y1),
                "Confidence": conf
            })
    
    if len(boxes) > 0:
        results_dict[time_ms] = boxes
        
    frame_idx += 1
    if frame_idx % 100 == 0:
        print(f"Processed {frame_idx} frames...")

with open(r"d:\138.종합 민원 이미지 AI데이터\BannerGuardSystem\yolo_cache.json", "w") as f:
    json.dump(results_dict, f)
print("Done!")
