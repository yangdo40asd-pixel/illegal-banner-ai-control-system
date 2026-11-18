"""
YOLOv26s 불법 현수막 탐지 모델 학습 스크립트
============================================
- 모델    : YOLOv26s
- GPU     : RTX 3080 최적화 (batch=16, amp=True)
- 에폭    : 200 (Early Stopping patience=50)
- 백업    : 50 에폭마다 체크포인트 + CSV 자동 백업
- 안전    : 날짜+시간 기반 고유 폴더명으로 덮어쓰기 방지
"""

import os
import sys

# ══════════════════════════════════════════════
# [Fix #1] torch import 전 환경변수 설정
# Windows 환경에서 OpenMP 충돌 방지 (필수)
# ══════════════════════════════════════════════
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

import shutil
import threading
import time
from datetime import datetime
from ultralytics import YOLO
from pathlib import Path
import argparse
import yaml

# ══════════════════════════════════════════════
# 경로 설정
# ══════════════════════════════════════════════
BASE_DIR = str(Path(__file__).resolve().parents[2])
DATASET_YAML = os.path.join(BASE_DIR, "training", "yolo", "configs", "dataset.example.yaml")
MODEL_PATH = os.path.join(BASE_DIR, "data", "private", "pretrained", "yolo26s.pt")
OUTPUT_DIR = os.path.join(BASE_DIR, "experiments", "results", "yolo")
BACKUP_DIR = os.path.join(BASE_DIR, "runtime", "training-backups")

# ══════════════════════════════════════════════
# CSV 자동 백업 스레드
# ══════════════════════════════════════════════
BACKUP_EPOCHS = [50, 100, 150, 200]

def csv_backup_worker(results_csv_path: str, timestamp: str):
    """1분마다 results.csv를 확인하여 50 에폭 단위로 백업 폴더에 복사"""
    backed_up = set()
    while True:
        time.sleep(60)
        if not os.path.exists(results_csv_path):
            continue
        try:
            with open(results_csv_path, 'r') as f:
                epoch_count = sum(1 for _ in f) - 1  # 헤더 제외 = 현재 에폭 수
            for ep in BACKUP_EPOCHS:
                if epoch_count >= ep and ep not in backed_up:
                    dst = os.path.join(BACKUP_DIR, f"results_ep{ep:03d}_{timestamp}.csv")
                    shutil.copy2(results_csv_path, dst)
                    backed_up.add(ep)
                    print(f"[BACKUP] CSV backup done (epoch ~{ep}): {dst}")
        except Exception as e:
            print(f"[BACKUP] Backup error: {e}")


# ══════════════════════════════════════════════
# [Fix #2] Windows 멀티프로세싱 필수 가드
# workers > 0 사용 시 반드시 필요
# 이 블록 없으면 자식 프로세스가 train.py 전체를
# 반복 실행하여 크래시 발생 (Windows 한정)
# ══════════════════════════════════════════════
if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', default=DATASET_YAML)
    parser.add_argument('--model', default=MODEL_PATH)
    options = parser.parse_args()
    MODEL_PATH = options.model
    if not os.path.isfile(MODEL_PATH):
        raise SystemExit(f"Pretrained weights not found: {MODEL_PATH}")
    with open(options.data, encoding='utf-8') as config_file:
        dataset = yaml.safe_load(config_file)
    dataset['path'] = str((Path(BASE_DIR) / dataset['path']).resolve())
    resolved = Path(BASE_DIR) / 'runtime' / 'training' / 'dataset-resolved.yaml'
    resolved.parent.mkdir(parents=True, exist_ok=True)
    resolved.write_text(yaml.safe_dump(dataset, allow_unicode=True), encoding='utf-8')
    DATASET_YAML = str(resolved)

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    os.makedirs(BACKUP_DIR, exist_ok=True)

    # 고유 학습 이름 생성 (덮어쓰기 방지)
    timestamp    = datetime.now().strftime("%Y%m%d_%H%M")
    project_name = f"banner_s_{timestamp}"

    print(f"[INFO] Training project : {project_name}")
    print(f"[INFO] Output directory : {OUTPUT_DIR}\\{project_name}")
    print(f"[INFO] Model path       : {MODEL_PATH}")
    print(f"[INFO] Dataset yaml     : {DATASET_YAML}")

    # CSV 백업 스레드 시작
    results_csv_path = os.path.join(OUTPUT_DIR, project_name, "results.csv")
    backup_thread = threading.Thread(
        target=csv_backup_worker,
        args=(results_csv_path, timestamp),
        daemon=True
    )
    backup_thread.start()
    print("[INFO] CSV auto-backup thread started (every 50 epochs)\n")

    # 모델 로드
    print("[INFO] Loading model...")
    model = YOLO(MODEL_PATH)

    # ── 학습 실행 ──────────────────────────────
    print("[INFO] Starting training... (Ctrl+C to stop)\n")
    results = model.train(
        data        = DATASET_YAML,  # 데이터셋 설정 파일
        epochs      = 200,           # 최대 에폭 수
        imgsz       = 640,           # 입력 이미지 크기 (YOLOv26 표준)
        batch       = 16,            # RTX 3080 적정 배치 사이즈
        save_period = 50,            # 50 에폭마다 체크포인트 저장
        patience    = 50,            # 50 에폭간 개선 없으면 조기 종료
        project     = OUTPUT_DIR,    # 결과 저장 루트 폴더
        name        = project_name,  # 고유 학습 이름 (날짜+시간)
        exist_ok    = False,         # 동일 폴더 존재 시 에러 (덮어쓰기 차단)
        amp         = True,          # RTX 3080 Tensor Core 활용 (속도+메모리 최적화)
        workers     = 0,             # [Fix #3] Windows는 0이 안전 (멀티프로세싱 비활성화)
        device      = 0,             # GPU 0번 (RTX 3080) 사용
        verbose     = True,          # 에폭별 결과 출력
    )

    # ── 학습 완료 요약 ──────────────────────────
    best_model = os.path.join(str(results.save_dir), "weights", "best.pt")
    print("\n" + "="*50)
    print("[DONE] Training complete!")
    print(f"[DONE] Best model : {best_model}")
    print(f"[DONE] Result dir : {results.save_dir}")
    print(f"[DONE] CSV backup : {BACKUP_DIR}")
    print("="*50)
