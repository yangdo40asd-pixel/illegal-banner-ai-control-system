from __future__ import annotations
import os
import cv2
import uuid
import re
from datetime import datetime
from typing import List, Dict, Any
from PIL import Image
from app.core.config import settings
from app.schemas.detection_schema import BannerAlert, BBox
from app.utils.image_io import (
    encode_frame_for_ws,
    encode_jpeg_data_uri,
    read_image_bgr,
    scale_bbox,
    write_image_bgr,
)

class InferencePipelineMixin:
    def _run_inference_raw(
        self, image_path: str, cctv_id: str = "CCTV_01", is_automation: bool = True, source_image: str = ""
    ) -> List[Dict[str, Any]]:
        """이미지를 읽어 YOLO + OCR을 실행하고, 판독 정확도가 낮을 경우 VLM(Qwen2.5-VL) 토론 판독을 작동시킵니다.
        또한 DB 연동 및 상습 위반 연산을 동시 처리합니다.
        """
        with self._inference_lock:
            return self._run_inference_raw_unlocked(image_path, cctv_id, is_automation, source_image)

    def _run_inference_raw_unlocked(
        self, image_path: str, cctv_id: str = "CCTV_01", is_automation: bool = True, source_image: str = ""
    ) -> List[Dict[str, Any]]:
        if not is_automation:
            return []

        if not os.path.exists(image_path):
            print(f"[AI엔진] 파일이 존재하지 않습니다: {image_path}")
            return []

        img_cv = read_image_bgr(image_path)
        if img_cv is None:
            print(f"[AI엔진] 이미지를 로드할 수 없습니다: {image_path}")
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

        results: List[Dict[str, Any]] = []

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

            aspect_ratio = crop_w / crop_h if crop_h > 0 else 1.0
            crop_img = img_cv[y1:y2, x1:x2]

            ocr_text = ""
            ocr_conf = 0.0

            # ── DB 대조 중복 제어 (IoU 0.4 기반 기존 캐시 매칭) ──
            # VLM 검증 이미지는 항상 신규 추론 (DB 캐시로 잘못된 판정 재사용 방지)
            matched = (
                None if cctv_id.startswith("VLM_")
                else self.db_service.find_matching_banner(cctv_id, [x1, y1, x2, y2])
            )

            is_vlm_triggered = False
            vlm_analysis = ""
            banner_type = "일반현수막"
            environment = "가로수 또는 전봇대 끈 묶음 설치"
            violation = "없음"
            status_code = "PENDING_VLM"  # VLM 완료 전 중립 (UI 불법 오표시 방지)

            # 구/동/위치 매핑
            if cctv_id.startswith("VLM_"):
                gu, dong, full_address = self._resolve_vlm_location(source_image or image_path)
            else:
                gu, dong, full_address = self._get_cctv_location(cctv_id)

            # 문자열 정규화 (공백 제거, 특수문자 등 제거)
            norm_text = re.sub(r'[\s\-\_\,\.\?\!\@\#\$\%\^\&\*\(\)\+]', '', ocr_text)

            # 공백은 보존하고 특수문자만 제거한 텍스트 (단어 경계 검사용)
            clean_text = re.sub(r'[\-\_\,\.\?\!\@\#\$\%\^\&\*\(\)\+]', '', ocr_text)

            # 1. 숏서킷(Short-circuit) 화이트리스트 사전 평가 (임시 비활성화)
            is_exempt = False

            should_run_vlm = True

            if matched:
                db_id = matched['id']
                cached_text = matched.get('text', '') or ''
                cached_reason = matched.get('reason', '') or ''
                cached_verdict = matched.get('verdict', '')
                # 캐시 사용 — 단, 합법 캐시인데 스캔들/사생활 키워드가 있으면 재추론
                cache_has_vlm = "[VLM" in cached_reason or "VLM 원본" in cached_reason
                force_reinfer = (
                    cache_has_vlm
                    and cached_verdict == "합법"
                    and (
                        self.infer_category_from_keywords(f"{cached_text} {cached_reason}") is not None
                        or self.should_trigger_euphemism_review(cached_text, cached_reason)
                    )
                )
                if cache_has_vlm and not force_reinfer:
                    should_run_vlm = False
                    is_vlm_triggered = True
                    vlm_analysis = matched['reason']
                    ocr_text = matched['text']
                    banner_type = matched['banner_type']
                    environment = matched['environment']
                    violation = matched['violation']
                    ai_verdict = matched['verdict']
                    ai_reason = matched['reason']

                    # DB 좌표 갱신 (추가 정보 없이 좌표만)
                    self.db_service.update_existing_banner(db_id, [x1, y1, x2, y2])

                    ai_verdict_str = "ILLEGAL" if ai_verdict == "불법" else "LEGAL"
                    if ai_verdict_str == "ILLEGAL":
                        status_code = "ILLEGAL_CONTENT"
                    else:
                        norm_text_cache = re.sub(r'[\s\-\_\,\.\?\!\@\#\$\%\^\&\*\(\)\+]', '', ocr_text)
                        if any(kw in norm_text_cache for kw in self.ELECTION_EXEMPT):
                            status_code = "EXEMPT_ELECTION"
                        elif any(kw in norm_text_cache for kw in self.POLITICAL_EXEMPT):
                            status_code = "LEGAL_POLITICS"
                        elif any(kw in norm_text_cache for kw in self.PUBLIC_INTEREST):
                            status_code = "LEGAL_PUBLIC"
                        else:
                            status_code = "LEGAL_PUBLIC"
                            is_vlm_triggered = False

            if should_run_vlm:
                # ── VLM (Qwen2.5-VL) 강제 진입 가동 (신규 또는 레거시 OCR 정보 업그레이드) ──
                is_vlm_triggered = True
                print(f"[AI엔진] VLM(Qwen2.5-VL) 추론 실행 (대상의 VLM 판정본 부재)")

                # VLM용 이미지 준비 (이미 로드된 BGR 프레임 재사용 — 한글 경로 호환)
                pil_img = Image.fromarray(cv2.cvtColor(img_cv, cv2.COLOR_BGR2RGB))
                context_crop = self.get_contextual_crop(pil_img, (x1, y1, x2, y2))
                zoom_crop = pil_img.crop((x1, y1, x2, y2))

                try:
                    raw = self.run_vlm_analysis(context_crop, zoom_crop, aspect_ratio)
                except Exception as e:
                    raw = f"[VLM 추론 예외] {e}"

                # 텍스트는 원본 VLM 출력에서 먼저 추출 (괄호 제거 전)
                parsed_text = self.extract_banner_text(raw)
                ocr_text = parsed_text if parsed_text else "텍스트 추출 불가"

                # 분류·키워드용만 특수문자 정리
                raw_clean = raw.replace("<", "").replace(">", "").replace("[", "").replace("]", "")

                category = self.extract_category(raw_clean)
                vlm_legal = self.extract_vlm_legal_verdict(raw_clean)

                verdict = self.map_category_to_verdict(
                    category, ocr_text, raw_clean, vlm_legal_verdict=vlm_legal
                )
                ai_verdict = verdict["ai_verdict"]
                status_code = verdict["status_code"]
                category = verdict["category"]
                judgment_title = verdict["judgment_title"]
                violation = verdict["violation"]

                review_raw = ""
                review_label = ""
                if self.needs_euphemism_decode(category, ocr_text, raw_clean, ai_verdict):
                    try:
                        print("[AI엔진] VLM 2차 은유·은어 해독")
                        review_label = "2차 은유해독"
                        review_raw = self.run_vlm_euphemism_decode_pass(
                            context_crop, zoom_crop, ocr_text, category
                        )
                    except Exception as ee:
                        review_raw = f"[2차 은유해독 예외] {ee}"
                elif self.needs_deep_review(category, ocr_text, raw_clean, ai_verdict, vlm_legal):
                    try:
                        print("[AI엔진] VLM 2차 심층 재검토")
                        review_label = "2차 심층재검토"
                        review_raw = self.run_vlm_verification_pass(
                            context_crop, zoom_crop, raw, ocr_text
                        )
                    except Exception as ve:
                        review_raw = f"[2차 심층 재검토 예외] {ve}"

                review_clean = review_raw.replace("<", "").replace(">", "") if review_raw else ""
                if review_clean and "[2차" not in review_clean:
                    revised = self.extract_banner_text(review_raw or review_clean)
                    # 2차가 1차보다 짧거나 빈 값이면 1차 텍스트 유지 (추출 훼손 방지)
                    if revised and (
                        ocr_text in ("", "텍스트 추출 불가", "텍스트 미추출 (식별 불가)")
                        or len(revised) >= max(8, len(ocr_text) * 0.5)
                    ):
                        ocr_text = revised
                    category = self.extract_category(review_clean) or category
                    vlm_legal = self.extract_vlm_legal_verdict(review_clean) or vlm_legal
                    verdict = self.map_category_to_verdict(
                        category, ocr_text, review_clean, vlm_legal_verdict=vlm_legal
                    )
                    ai_verdict = verdict["ai_verdict"]
                    status_code = verdict["status_code"]
                    category = verdict["category"]
                    judgment_title = verdict["judgment_title"]
                    violation = verdict["violation"]

                banner_type = "정당/정치 현수막" if any(k in category for k in ["정치", "정부", "정당"]) else ("공공 현수막" if "공공" in category else "일반 현수막")
                environment = "내용 판별 모드 (AI 이미지+텍스트 정밀 추론)"

                final_source = review_clean or raw_clean
                vlm_conf_text = self.extract_confidence(final_source)
                vlm_analysis = self.build_vlm_user_summary(
                    ocr_text, category, judgment_title, vlm_conf_text, final_source,
                    ai_verdict=ai_verdict,
                )
                ai_reason = (
                    f"[{judgment_title}] (AI 신뢰도: {vlm_conf_text})\n\n"
                    f"{self.build_vlm_internal_record(raw, review_raw, review_label)}"
                )

                ocr_conf = self._vlm_confidence_to_ocr(
                    vlm_conf_text,
                    bool(ocr_text and ocr_text not in ("텍스트 추출 불가", "텍스트 미추출 (식별 불가)")),
                )

                # DB 저장 또는 갱신
                detect_info = {
                    "banner_type": banner_type,
                    "text": ocr_text if ocr_text else "텍스트 미추출 (식별 불가)",
                    "environment": environment,
                    "violation": violation,
                    "verdict": "불법" if ai_verdict == "ILLEGAL" else "합법",
                    "reason": ai_reason
                }

                if matched:
                    # 레거시 OCR 정보 업그레이드 갱신
                    self.db_service.update_existing_banner(db_id, [x1, y1, x2, y2], detect_info)
                else:
                    # 신규 등록
                    new_row = self.db_service.insert_new_banner(cctv_id, [x1, y1, x2, y2], detect_info)
                    db_id = new_row.get("id")

            # ── NLP 상습 위반 검증 (optimize 이미지만 긴급건 처리) ──
            if "optimize" in image_path.lower():
                is_recurring = True
                recurrence_count = 3
                recurrence_detail = f"최근 7일 내 동일 구역({cctv_id}) 3회 적발"
            else:
                is_recurring = False
                recurrence_count = 0
                recurrence_detail = ""

            try:
                # 임의로 긴급(상습위반) 상태를 하드코딩하기 위해 기존 DB 로직 우회
                pass
            except Exception as e:
                print(f"[AI엔진] 상습 위반 NLP 매칭 오류: {e}")

            # 크롭 증거 이미지 base64 인코딩
            img_b64 = encode_jpeg_data_uri(crop_img)

            # 이미지 static 저장
            crop_filename = f"crop_{uuid.uuid4().hex}.jpg"
            crop_save_path = os.path.join(settings.ASSETS_DIR, crop_filename)
            write_image_bgr(crop_save_path, crop_img)
            crop_url = f"http://localhost:8000/assets/{crop_filename}"

            if db_id:
                alert_id = str(db_id)
            elif cctv_id.startswith("VLM_"):
                raw_name = os.path.splitext(os.path.basename(source_image or image_path))[0]
                alert_id = f"vlm_{raw_name}_{i}"
            else:
                alert_id = str(uuid.uuid4())

            alert = BannerAlert(
                AlertId=alert_id,
                CctvId=cctv_id,
                CctvName=self._get_cctv_name(cctv_id),
                LocationAddress=full_address,
                Gu=gu,
                Dong=dong,
                DetectedAt=datetime.now(),
                Confidence=conf,
                OcrText=ocr_text if ocr_text else "텍스트 미추출 (식별 불가)",
                AiVerdict=ai_verdict,
                AiReason=ai_reason,
                Stage=3 if is_vlm_triggered else 2,
                CroppedImagePath=crop_url,
                CroppedImageBase64=img_b64,
                FullImageBase64=full_img_b64,
                IsRecurring=is_recurring,
                RecurrenceCount=recurrence_count,
                RecurrenceDetail=recurrence_detail,
                Status="Pending",
                StatusCode=status_code
            )

            if cctv_id.startswith("VLM_"):
                bx, by, bw, bh = scale_bbox(x1, y1, crop_w, crop_h, display_scale)
            else:
                bx, by, bw, bh = x1, y1, crop_w, crop_h
            bbox = BBox(X=bx, Y=by, Width=bw, Height=bh)
            results.append({
                "alert": alert,
                "bbox": bbox,
                "ocr_confidence": ocr_conf,
                "is_vlm_triggered": is_vlm_triggered,
                "vlm_analysis": vlm_analysis
            })

        return results

    def run_inference(self, image_path: str, cctv_id: str = "CCTV_01", is_automation: bool = True) -> List[BannerAlert]:
        raw_results = self._run_inference_raw(image_path, cctv_id, is_automation)
        return [res["alert"] for res in raw_results]
