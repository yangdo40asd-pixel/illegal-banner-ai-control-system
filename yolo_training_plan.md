# YOLOv26s 불법 현수막 탐지 모델 학습 상세 계획서

## 환경 정보 요약

| 항목 | 내용 |
| :--- | :--- |
| **사용 모델** | YOLOv26s (Small) |
| **GPU** | NVIDIA RTX 3080 |
| **데이터셋** | comgong_labels.v1i.yolo26 (Roboflow) |
| **클래스 수** | 1개 (`banners`) |
| **학습 데이터** | Train: 5,781장 / Valid: 241장 |
| **목표 에폭** | 200 Epochs |

---

## 1. 폴더 구조 설계

```
d:\138.종합 민원 이미지 AI데이터\
│
├── comgong_labels.v1i.yolo26 (2)\     ← 데이터셋 원본 폴더
│   ├── train\
│   │   ├── images\  (5,781장)
│   │   └── labels\  (5,781개)
│   ├── valid\
│   │   ├── images\  (241장)
│   │   └── labels\  (241개)
│   ├── test\
│   └── data.yaml
│
├── yolo26s.pt                         ← 기본 모델 가중치 (사전학습)
│
└── yolo26s\                           ← 학습 결과 저장 폴더 (생성 완료)
    ├── runs\                          ← 에폭별 결과 저장
    │   └── banner_s_20240506_XXXX\    ← 날짜+시간 고유 폴더 (덮어쓰기 방지)
    │       ├── weights\
    │       │   ├── best.pt            ← 최고 성능 체크포인트 (최종 결과물)
    │       │   ├── last.pt            ← 마지막 체크포인트
    │       │   ├── epoch050.pt        ← 50 에폭 백업
    │       │   ├── epoch100.pt        ← 100 에폭 백업
    │       │   └── epoch150.pt        ← 150 에폭 백업
    │       ├── results.csv            ← 학습 로그 (에폭별 정확도/손실)
    │       └── results.png            ← 학습 곡선 그래프
    │
    └── csv_backups\                   ← CSV 별도 백업 폴더
        ├── results_ep050.csv
        ├── results_ep100.csv
        └── results_ep150.csv
```

---

## 2. 학습 핵심 파라미터 설정 및 근거

| 파라미터 | 설정값 | 근거 |
| :--- | :---: | :--- |
| `epochs` | **200** | 5,781장의 소규모 데이터셋은 충분한 반복 학습이 필요하여 200으로 설정 |
| `imgsz` | **640** | YOLOv26 표준 입력 해상도. CCTV 원거리 탐지 품질 확보 |
| `batch` | **16** | RTX 3080(10GB VRAM)에서 VLM 동시 구동 여유를 남기기 위한 적정값 |
| `save_period` | **50** | 50 에폭마다 체크포인트 저장. 학습 중 데이터 유실 방지 |
| `patience` | **50** | 50 에폭 동안 성능 향상이 없으면 조기 종료 (비효율 학습 차단) |
| `amp` | **True** | RTX 3080의 Tensor Core를 활용한 혼합 정밀도(FP16) 학습으로 속도·메모리 최적화 |
| `workers` | **4** | CPU 데이터 로딩 병렬화. 너무 높이면 CPU 과열 유발 |
| `device` | **0** | RTX 3080 (GPU 0번) 사용 |
| `exist_ok` | **False** | 동일한 이름의 폴더가 있으면 에러 발생 → 기존 학습 결과 덮어쓰기 원천 봉쇄 |

---

## 3. 학습 실행 스크립트 (`train.py`)

```python
import os
import shutil
import threading
import time
from datetime import datetime
from ultralytics import YOLO

# ────────────────────────────────────────────
# 경로 설정
# ────────────────────────────────────────────
BASE_DIR     = r"d:\138.종합 민원 이미지 AI데이터"
DATASET_YAML = os.path.join(BASE_DIR, "comgong_labels.v1i.yolo26 (2)", "data.yaml")
MODEL_PATH   = os.path.join(BASE_DIR, "yolo26s.pt")
OUTPUT_DIR   = os.path.join(BASE_DIR, "yolo26s", "runs")
BACKUP_DIR   = os.path.join(BASE_DIR, "yolo26s", "csv_backups")

os.makedirs(BACKUP_DIR, exist_ok=True)

# ────────────────────────────────────────────
# 1. 고유 학습 이름 생성 (덮어쓰기 방지)
# ────────────────────────────────────────────
timestamp    = datetime.now().strftime("%Y%m%d_%H%M")
project_name = f"banner_s_{timestamp}"
print(f"[INFO] Training project name: {project_name}")

# ────────────────────────────────────────────
# 2. CSV 자동 백업 스레드 (50 에폭 단위 대략 감지)
# ────────────────────────────────────────────
RESULTS_CSV_PATH = os.path.join(OUTPUT_DIR, project_name, "results.csv")
backup_checkpoints = [50, 100, 150]

def csv_backup_thread():
    """학습 중 50 에폭 단위로 results.csv를 별도 폴더에 백업"""
    backed_up = set()
    while True:
        time.sleep(60)  # 1분마다 체크
        if not os.path.exists(RESULTS_CSV_PATH):
            continue
        # csv 라인 수로 현재 에폭 추정
        with open(RESULTS_CSV_PATH, 'r') as f:
            line_count = sum(1 for _ in f) - 1  # 헤더 제외
        for ep in backup_checkpoints:
            if line_count >= ep and ep not in backed_up:
                dst = os.path.join(BACKUP_DIR, f"results_ep{ep:03d}.csv")
                shutil.copy2(RESULTS_CSV_PATH, dst)
                backed_up.add(ep)
                print(f"[BACKUP] CSV backed up at epoch ~{ep}: {dst}")

backup_thread = threading.Thread(target=csv_backup_thread, daemon=True)
backup_thread.start()

# ────────────────────────────────────────────
# 3. 모델 학습 실행
# ────────────────────────────────────────────
model = YOLO(MODEL_PATH)

results = model.train(
    data=DATASET_YAML,
    epochs=200,
    imgsz=640,
    batch=16,
    save_period=50,       # 50 에폭마다 체크포인트 자동 저장
    patience=50,          # 50 에폭간 개선 없으면 조기 종료
    project=OUTPUT_DIR,
    name=project_name,
    exist_ok=False,       # 덮어쓰기 원천 봉쇄
    amp=True,
    workers=4,
    device=0,
)

print(f"\n[DONE] Best model saved at: {results.save_dir}/weights/best.pt")
```

---

## 4. 예상 학습 흐름 및 소요 시간

```
[START]  학습 시작
   │
   ├─ Epoch 1~50   : 약 20~25분  → epoch050.pt 자동 저장 + CSV 백업
   │
   ├─ Epoch 51~100 : 약 20~25분  → epoch100.pt 자동 저장 + CSV 백업
   │
   ├─ Epoch 101~150: 약 20~25분  → epoch150.pt 자동 저장 + CSV 백업
   │
   └─ Epoch 151~200: 약 20~25분  → best.pt 최종 확정
   
[END]    총 예상 소요 시간: 약 1시간 30분 ~ 2시간
         (Early Stopping 발동 시 더 빠르게 종료 가능)
```

---

## 5. 학습 완료 후 검증 계획

1. **결과 지표 확인**: `results.csv` 내 `mAP50`, `mAP50-95` 수치 확인.
2. **샘플 시각화**: `best.pt`를 로드하여 테스트 이미지에 박스가 정확히 그려지는지 육안 검증.
3. **모델 내보내기**: WPF 연동을 위해 `best.pt`를 ONNX 포맷으로 변환.

---

## 6. 학습 시작 전 체크리스트

- [ ] Python 환경에 `ultralytics` 패키지 설치 확인 (`pip install ultralytics`)
- [ ] `data.yaml` 경로가 실제 데이터셋 폴더와 일치하는지 확인
- [ ] `yolo26s.pt` 파일이 `d:\138.종합 민원 이미지 AI데이터\` 에 있는지 확인
- [ ] `yolo26s\` 폴더가 비어있는지 확인 (충돌 방지)
- [ ] GPU 드라이버 최신 상태인지 확인 (`nvidia-smi` 명령어)
