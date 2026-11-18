from __future__ import annotations
import os
import uuid
from datetime import datetime
from typing import List
from app.schemas.detection_schema import BannerDetectionEvent, BBox
from app.utils.image_io import encode_frame_for_ws, encode_jpeg_data_uri, read_image_bgr, scale_bbox

class YoloPreviewMixin:
    @staticmethod
    def _safe_yolo_conf(boxes, index: int) -> float:
        try:
            val = boxes.conf[index]
            if hasattr(val, "item"):
                val = val.item()
            return float(val)
        except (TypeError, ValueError):
            return 0.0

    def run_yolo_preview_as_event(
        self, image_path: str, cctv_id: str = "CCTV_01", source_image: str = ""
    ) -> List[BannerDetectionEvent]:
        """YOLO+이미지만 빠르게 반환 — VLM/OCR/DB 스킵. Feed 즉시 표시용."""
        if not os.path.exists(image_path):
            return []

        img_cv = read_image_bgr(image_path)
        if img_cv is None:
            return []

        if cctv_id.startswith("VLM_"):
            full_img_b64, _, _, display_scale = encode_frame_for_ws(img_cv)
        else:
            full_img_b64 = encode_jpeg_data_uri(img_cv)
            display_scale = 1.0

        img_h, img_w, _ = img_cv.shape
        yolo_device = 0 if self.device == "cuda" else "cpu"
        yolo_results = self.yolo(img_cv, verbose=False, device=yolo_device)
        boxes = yolo_results[0].boxes

        if cctv_id.startswith("VLM_"):
            _, _, full_address = self._resolve_vlm_location(source_image or image_path)
        else:
            _, _, full_address = self._get_cctv_location(cctv_id)

        if boxes is None or len(boxes) == 0:
            # CCTV LIVE: 미탐지 시 빈 배열 → WPF가 박스 제거(영상 추적)
            if not cctv_id.startswith("VLM_"):
                return []
            raw_name = os.path.splitext(os.path.basename(source_image or image_path))[0]
            return [
                BannerDetectionEvent(
                    Id=f"vlm_{raw_name}_none",
                    CctvId=cctv_id,
                    LocationAddress=full_address,
                    Timestamp=datetime.now(),
                    HasBanner=False,
                    BoundingBox=BBox(X=0, Y=0, Width=0, Height=0),
                    Confidence=0.0,
                    OcrText="탐지 대기",
                    OcrConfidence=0.0,
                    IsVlmTriggered=False,
                    VlmAnalysis="AI 분석 중...",
                    IsRepeatOffender=False,
                    RepeatCount=0,
                    CropImageUri="",
                    CroppedImageBase64="",
                    FullImageBase64=full_img_b64,
                    StatusCode="LEGAL_PUBLIC",
                )
            ]

        events: List[BannerDetectionEvent] = []
        for i, box in enumerate(boxes.xyxy):
            x1, y1, x2, y2 = map(int, box.tolist())
            conf = self._safe_yolo_conf(boxes, i)

            x1 = max(0, x1)
            y1 = max(0, y1)
            x2 = min(img_w, x2)
            y2 = min(img_h, y2)

            crop_w = x2 - x1
            crop_h = y2 - y1
            if crop_w <= 0 or crop_h <= 0:
                continue

            crop_img = img_cv[y1:y2, x1:x2]
            crop_b64 = encode_jpeg_data_uri(crop_img)

            if cctv_id.startswith("VLM_"):
                bx, by, bw, bh = scale_bbox(x1, y1, crop_w, crop_h, display_scale)
                raw_name = os.path.splitext(os.path.basename(source_image or image_path))[0]
                preview_id = f"vlm_{raw_name}_{i}"
            else:
                bx, by, bw, bh = x1, y1, crop_w, crop_h
                preview_id = str(uuid.uuid4())

            events.append(
                BannerDetectionEvent(
                    Id=preview_id,
                    CctvId=cctv_id,
                    LocationAddress=full_address,
                    Timestamp=datetime.now(),
                    HasBanner=True,
                    BoundingBox=BBox(X=bx, Y=by, Width=bw, Height=bh),
                    Confidence=conf,
                    OcrText="탐지됨",
                    OcrConfidence=0.0,
                    IsVlmTriggered=False,
                    VlmAnalysis="AI 분석 중...",
                    IsRepeatOffender=False,
                    RepeatCount=0,
                    CropImageUri="",
                    CroppedImageBase64=crop_b64,
                    FullImageBase64=full_img_b64,
                    StatusCode="PENDING_VLM",
                )
            )

        return events
