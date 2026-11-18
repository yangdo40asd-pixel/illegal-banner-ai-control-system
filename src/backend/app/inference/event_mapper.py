from __future__ import annotations
import os
from typing import List, Tuple
from app.schemas.detection_schema import BannerDetectionEvent

class EventMapperMixin:
    def run_inference_as_event(
        self, image_path: str, cctv_id: str = "CCTV_01", is_automation: bool = True, source_image: str = ""
    ) -> List[BannerDetectionEvent]:
        raw_results = self._run_inference_raw(image_path, cctv_id, is_automation, source_image)
        events: List[BannerDetectionEvent] = []

        for res in raw_results:
            alert = res["alert"]
            bbox = res["bbox"]
            ocr_conf = res["ocr_confidence"]
            is_vlm = res["is_vlm_triggered"]
            vlm_an = res["vlm_analysis"]

            event = BannerDetectionEvent(
                Id=alert.AlertId,
                CctvId=alert.CctvId,
                LocationAddress=alert.LocationAddress,
                Timestamp=alert.DetectedAt,
                TimestampOffset="00:00:00",
                HasBanner=True,
                BoundingBox=bbox,
                Confidence=alert.Confidence,
                OcrText=alert.OcrText,
                OcrConfidence=ocr_conf if alert.OcrText != "텍스트 미추출 (식별 불가)" else 0.0,
                IsVlmTriggered=is_vlm,
                VlmAnalysis=vlm_an,
                IsRepeatOffender=alert.IsRecurring,
                RepeatCount=alert.RecurrenceCount,
                CropImageUri=alert.CroppedImagePath,
                CroppedImageBase64=alert.CroppedImageBase64,
                FullImageBase64=alert.FullImageBase64,
                StatusCode=alert.StatusCode
            )
            events.append(event)

        return events

    def _get_cctv_name(self, cctv_id: str) -> str:
        names = {
            "CCTV_01": "삼산동 A구역",
            "CCTV_02": "삼산동 B구역",
            "CCTV_03": "구월동 A구역",
            "CCTV_04": "구월동 B구역",
            "CCTV_05": "청라 5채널"
        }
        return names.get(cctv_id, f"관제 {cctv_id}")

    def _get_cctv_location(self, cctv_id: str) -> Tuple[str, str, str]:
        loc_map = {
            "CCTV_01": ("부평구", "삼산동", "인천광역시 부평구 삼산동 12"),
            "CCTV_02": ("부평구", "삼산동", "인천광역시 부평구 삼산동 45"),
            "CCTV_03": ("남동구", "구월동", "인천광역시 남동구 구월동 78"),
            "CCTV_04": ("남동구", "구월동", "인천광역시 남동구 구월동 10"),
            "CCTV_05": ("서구", "청라동", "인천광역시 서구 청라동 56")
        }
        return loc_map.get(cctv_id, ("미추홀구", "용현동", "인천광역시 미추홀구 용현동"))

    def _resolve_vlm_location(self, source_image: str) -> Tuple[str, str, str]:
        """VLM 검증 이미지 파일명 → 구·동 주소 (지도 이동용)."""
        poster_map = {
            "20260525_133548.jpg": ("부평구", "삼산동", "인천광역시 부평구 삼산동"),
            "20260525_133609.jpg": ("부평구", "삼산동", "인천광역시 부평구 삼산동"),
            "20260525_133852.jpg": ("남동구", "구월동", "인천광역시 남동구 구월동"),
            "20260525_133916.jpg": ("남동구", "구월동", "인천광역시 남동구 구월동"),
            "20260526_081956.jpg": ("미추홀구", "용현동", "인천광역시 미추홀구 용현동"),
        }
        base = os.path.basename(source_image).lower()
        if base in poster_map:
            return poster_map[base]
        return ("미추홀구", "용현동", "인천광역시 미추홀구 용현동")
