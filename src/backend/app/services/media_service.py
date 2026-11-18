from __future__ import annotations
import os
import glob
import cv2
import numpy as np
from typing import List, Tuple
from app.core.paths import paths
from app.schemas.detection_schema import BannerDetectionEvent

WPF_DIR = str(paths.resolve("CctvMediaDirectory", "data/samples/cctv"))
VLM_IMAGE_DIR = str(paths.resolve("VlmImageDirectory", "data/samples/vlm"))
VLM_IMAGE_GLOBS = ("*.jpg", "*.jpeg", "*.png", "*.webp", "*.JPG", "*.JPEG", "*.PNG", "*.WEBP")
# 우선 분석할 파일 (폴더 전체 로드 시 맨 앞 순서)
VLM_PRIORITY_FILENAMES = (
    "4c880145205ed9.jpg",
    "60555e04d577fc.jpg",
)

# CCTV 모니터링 전용 — MP4 LIVE 좌표 기준
CCTV_VIDEO_MAP = {
    "CCTV_01": "20260525_133553.mp4",
    "CCTV_02": "20260525_133611.mp4",
    "CCTV_03": "20260525_133838.mp4",
    "CCTV_04": "20260525_133919.mp4",
}


def _open_video_capture(video_path: str):
    """Windows 한글 경로 호환 VideoCapture."""
    if not os.path.isfile(video_path):
        return None

    cap = cv2.VideoCapture(video_path)
    if cap.isOpened():
        return cap
    return None


def _read_video_frame_at_ms(video_path: str, position_ms: int = 0):
    """MP4 특정 재생 시각(ms) 프레임 — LIVE 박스 추적용."""
    cap = _open_video_capture(video_path)
    if cap is None:
        return None, 0, 0

    try:
        if position_ms > 0:
            cap.set(cv2.CAP_PROP_POS_MSEC, float(position_ms))
        ok, frame = cap.read()
        if not ok or frame is None:
            return None, 0, 0
        h, w = frame.shape[:2]
        return frame, w, h
    finally:
        cap.release()


def _read_video_frame(video_path: str):
    """Windows 한글 경로 호환 — MP4 첫 프레임 읽기."""
    return _read_video_frame_at_ms(video_path, 0)


def _read_image_file(image_path: str):
    """jpg/png/webp 정지 이미지 읽기."""
    from app.utils.image_io import read_image_bgr

    img = read_image_bgr(image_path)
    if img is None:
        return None, 0, 0
    h, w = img.shape[:2]
    return img, w, h


def _write_temp_jpeg(frame: np.ndarray, cctv_id: str) -> str:
    ok, buf = cv2.imencode(".jpg", frame)
    if not ok:
        raise RuntimeError(f"{cctv_id} 프레임 JPEG 인코딩 실패")
    path = str(paths.runtime_directory("temp") / f"bannerguard_{cctv_id}_frame.jpg")
    buf.tofile(path)
    return path


def _bbox_to_xyxy(event: BannerDetectionEvent) -> List[int]:
    b = event.BoundingBox
    return [b.X, b.Y, b.X + b.Width, b.Y + b.Height]


def _iou(box_a: List[int], box_b: List[int]) -> float:
    x_a = max(box_a[0], box_b[0])
    y_a = max(box_a[1], box_b[1])
    x_b = min(box_a[2], box_b[2])
    y_b = min(box_a[3], box_b[3])
    inter = max(0, x_b - x_a) * max(0, y_b - y_a)
    area_a = (box_a[2] - box_a[0]) * (box_a[3] - box_a[1])
    area_b = (box_b[2] - box_b[0]) * (box_b[3] - box_b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


def dedupe_detections(events: List[BannerDetectionEvent], iou_thresh: float = 0.45) -> List[BannerDetectionEvent]:
    """겹치는 YOLO 박스 제거 — confidence 높은 것 우선."""
    sorted_events = sorted(events, key=lambda e: e.Confidence, reverse=True)
    kept: List[BannerDetectionEvent] = []
    kept_boxes: List[List[int]] = []

    for event in sorted_events:
        box = _bbox_to_xyxy(event)
        if any(_iou(box, prev) >= iou_thresh for prev in kept_boxes):
            continue
        kept.append(event)
        kept_boxes.append(box)

    return kept


def get_cctv_sources(position_ms: int = 0) -> List[Tuple[str, str, int, int]]:
    """CCTV 전용 — MP4 프레임 (bbox 좌표 = LIVE 영상 기준)."""
    sources = []
    for cctv_id, mp4_name in CCTV_VIDEO_MAP.items():
        mp4_path = os.path.join(WPF_DIR, mp4_name)
        frame, w, h = _read_video_frame_at_ms(mp4_path, position_ms)
        if frame is not None:
            try:
                temp_path = _write_temp_jpeg(frame, cctv_id)
                sources.append((cctv_id, temp_path, w, h))
                print(f"  [CCTV] {cctv_id}: MP4 {mp4_name} → {w}x{h}")
                continue
            except Exception as e:
                print(f"  [CCTV] {cctv_id}: MP4 프레임 저장 실패 ({e})")

    return sources


def _list_vlm_image_files() -> List[str]:
    """VLM 검증 이미지 목록 — VLM_IMAGE_DIR만 스캔, 우선 파일 먼저."""
    if not os.path.isdir(VLM_IMAGE_DIR):
        print(f"  [VLM] {VLM_IMAGE_DIR} — 폴더 없음")
        return []

    files: List[str] = []
    for pattern in VLM_IMAGE_GLOBS:
        files.extend(glob.glob(os.path.join(VLM_IMAGE_DIR, pattern)))
    by_name = {os.path.basename(p).lower(): p for p in set(files)}

    ordered: List[str] = []
    for name in VLM_PRIORITY_FILENAMES:
        path = by_name.get(name.lower())
        if path:
            ordered.append(path)

    for path in sorted(by_name.values(), key=lambda p: os.path.basename(p).lower()):
        if path not in ordered:
            ordered.append(path)

    return ordered


def _resolve_vlm_location_address(source_image: str, image_path: str = "") -> str:
    """VLM 이미지 파일명 → 지도용 주소."""
    poster_map = {
        "20260525_133548.jpg": "인천광역시 부평구 삼산동",
        "20260525_133609.jpg": "인천광역시 부평구 삼산동",
        "20260525_133852.jpg": "인천광역시 남동구 구월동",
        "20260525_133916.jpg": "인천광역시 남동구 구월동",
        "20260526_081956.jpg": "인천광역시 미추홀구 용현동",
        "4c880145205ed9.jpg": "인천광역시 부평구 삼산동",
        "60555e04d577fc.jpg": "인천광역시 남동구 구월동",
    }
    base = os.path.basename(source_image or image_path).lower()
    return poster_map.get(base, "인천광역시 미추홀구 용현동")


def get_vlm_image_sources() -> List[Tuple[str, str, str, int, int]]:
    """VLM 검증 전용 — VLM_IMAGE_DIR 폴더 안 이미지 전부 로드.
    Returns: [(cctv_id, image_path, source_filename, frame_w, frame_h), ...]
    """
    files = _list_vlm_image_files()
    if not files:
        print(f"  [VLM] {VLM_IMAGE_DIR} — 이미지 없음 (jpg/png/webp)")
        return []

    sources: List[Tuple[str, str, str, int, int]] = []
    for index, path in enumerate(files):
        basename = os.path.basename(path)
        cctv_id = f"VLM_{index + 1:02d}"
        _, w, h = _read_image_file(path)
        if w <= 0 or h <= 0:
            print(f"  [VLM] 읽기 실패: {basename}")
            continue
        sources.append((cctv_id, path, basename, w, h))
        print(f"  [VLM] {basename} → {cctv_id} ({w}x{h}) [{index + 1}/{len(files)}]")

    return sources
