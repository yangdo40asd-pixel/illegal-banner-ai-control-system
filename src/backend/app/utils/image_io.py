"""Windows 한글 경로 호환 이미지 I/O + WebSocket 전송용 리사이즈."""
from __future__ import annotations

import base64
from typing import Optional, Tuple

import cv2
import numpy as np

WS_IMAGE_MAX_DIM = 1280
WS_JPEG_QUALITY = 85


def read_image_bgr(path: str) -> Optional[np.ndarray]:
    if not path:
        return None
    try:
        data = np.fromfile(path, dtype=np.uint8)
        if data.size == 0:
            return None
        return cv2.imdecode(data, cv2.IMREAD_COLOR)
    except OSError:
        return None


def write_image_bgr(path: str, img: np.ndarray) -> bool:
    try:
        ok, buf = cv2.imencode(".jpg", img)
        if not ok:
            return False
        buf.tofile(path)
        return True
    except OSError:
        return False


def resize_for_ws(img: np.ndarray, max_dim: int = WS_IMAGE_MAX_DIM) -> Tuple[np.ndarray, float]:
    h, w = img.shape[:2]
    if max(w, h) <= max_dim:
        return img, 1.0
    scale = max_dim / max(w, h)
    new_w = max(1, int(w * scale))
    new_h = max(1, int(h * scale))
    resized = cv2.resize(img, (new_w, new_h), interpolation=cv2.INTER_AREA)
    return resized, scale


def encode_jpeg_data_uri(img: np.ndarray, quality: int = WS_JPEG_QUALITY) -> str:
    ok, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, quality])
    if not ok:
        return ""
    return f"data:image/jpeg;base64,{base64.b64encode(buf).decode('utf-8')}"


def encode_frame_for_ws(img: np.ndarray) -> Tuple[str, int, int, float]:
    """WebSocket 전송용 프레임 — 리사이즈 후 base64."""
    display, scale = resize_for_ws(img)
    b64 = encode_jpeg_data_uri(display)
    h, w = display.shape[:2]
    return b64, w, h, scale


def scale_bbox(x: int, y: int, w: int, h: int, scale: float) -> Tuple[int, int, int, int]:
    if scale >= 1.0:
        return x, y, w, h
    return (
        int(x * scale),
        int(y * scale),
        max(1, int(w * scale)),
        max(1, int(h * scale)),
    )