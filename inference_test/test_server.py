import requests
import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

img_dir = r"D:\138.종합 민원 이미지 AI데이터\inference_test\test_images"
img_file = sorted(os.listdir(img_dir))[0]
img_path = os.path.join(img_dir, img_file)

print(f"[테스트] 이미지: {img_file}")
print("-" * 50)

with open(img_path, "rb") as f:
    r = requests.post(
        "http://localhost:8000/analyze",
        files={"file": (img_file, f, "image/jpeg")}
    )

if r.status_code != 200:
    print(f"[오류] 상태코드: {r.status_code}")
    print(r.text)
    sys.exit(1)

data = r.json()
print(f"[결과] 탐지된 현수막 수: {data['detection_count']}개")
print(f"[결과] 처리 시각: {data['processed_at']}")
print()

for d in data["detections"]:
    print(f"  [박스 {d['box_id']}]")
    print(f"    신뢰도  : {d['confidence']}")
    print(f"    판정    : {d['verdict']}")
    print(f"    텍스트  : {d['text']}")
    print(f"    설치환경: {d['environment']}")
    print(f"    판정근거: {d['reason']}")
    print()

print(f"[결과] Base64 이미지 크기: {len(data['result_image_base64'])} chars")
print("[완료] 서버 /analyze 정상 작동 확인!")
