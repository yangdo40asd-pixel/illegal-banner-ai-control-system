"""VLM 폴더 자체 테스트 — 텍스트 추출·분류 결과 출력."""
from __future__ import annotations

import glob
import os
import sys
import time

BACKEND_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BACKEND_ROOT)

IMAGE_DIR = r"C:\Users\admin_25\Downloads\test\image"
GLOBS = ("*.jpg", "*.jpeg", "*.png", "*.webp", "*.JPG", "*.JPEG", "*.PNG", "*.WEBP")


def collect_images() -> list[str]:
    files: list[str] = []
    for pattern in GLOBS:
        files.extend(glob.glob(os.path.join(IMAGE_DIR, pattern)))
    return sorted(set(files))


def main() -> int:
    from app.services.ai_inference_service import AIInferenceService

    images = collect_images()
    if not images:
        print(f"[TEST] 이미지 없음: {IMAGE_DIR}")
        return 1

    print(f"[TEST] 이미지 {len(images)}장 — {IMAGE_DIR}")
    print("[TEST] 모델 로딩 중...")
    t0 = time.time()
    svc = AIInferenceService()
    print(f"[TEST] 로딩 완료 ({time.time() - t0:.1f}s) device={svc.device}")

    ok_count = 0
    fail_count = 0

    for i, path in enumerate(images):
        name = os.path.basename(path)
        cctv_id = f"VLM_{i + 1:02d}"
        print(f"\n{'=' * 60}")
        print(f"[{i + 1}/{len(images)}] {name} ({cctv_id})")
        t1 = time.time()
        try:
            results = svc._run_inference_raw(path, cctv_id=cctv_id, is_automation=True)
            elapsed = time.time() - t1
            if not results:
                print(f"  YOLO 탐지 없음 ({elapsed:.1f}s)")
                fail_count += 1
                continue
            for j, res in enumerate(results, 1):
                alert = res["alert"]
                text = alert.OcrText or ""
                good = text not in (
                    "",
                    "텍스트 추출 불가",
                    "텍스트 미추출 (식별 불가)",
                )
                if good:
                    ok_count += 1
                else:
                    fail_count += 1
                print(f"  박스#{j} conf={alert.Confidence:.3f} ({elapsed:.1f}s)")
                print(f"  텍스트: {text}")
                print(f"  분류/상태: {alert.StatusCode} | {alert.AiVerdict}")
                vlm = (res.get("vlm_analysis") or "").split("\n")[0]
                if vlm:
                    print(f"  VLM요약: {vlm}")
        except Exception as e:
            print(f"  오류: {e}")
            fail_count += 1

    print(f"\n{'=' * 60}")
    print(f"[TEST] 완료 — 텍스트 추출 성공 {ok_count}건 / 실패·미탐지 {fail_count}건")
    return 0 if ok_count > 0 and fail_count == 0 else (1 if ok_count == 0 else 0)


if __name__ == "__main__":
    raise SystemExit(main())