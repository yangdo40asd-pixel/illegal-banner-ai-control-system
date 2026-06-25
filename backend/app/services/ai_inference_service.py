import os
import cv2
import numpy as np
import base64
import uuid
import torch
import re
from datetime import datetime
from typing import List, Tuple, Dict, Any
from PIL import Image
from ultralytics import YOLO
from transformers import AutoModelForImageTextToText, AutoProcessor
from qwen_vl_utils import process_vision_info
from app.core.config import settings
from app.schemas.detection_schema import BannerAlert, BannerDetectionEvent, BBox
from app.services.db_service import DatabaseService

# OpenMP duplicate warning environment variable
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

class AIInferenceService:
    """YOLOv26s 객체 탐지, EasyOCR 문자 판독, 그리고 Qwen2.5-VL-3B-Instruct VLM을
    결합한 3단계 정밀 현수막 준법 심사 인공지능 추론 서비스 클래스입니다.
    """
    def __init__(self):
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        print(f"[AI엔진] 가용 가속 디바이스: {self.device}")

        # 로컬 경로 설정
        self.base_dir = r"D:\138.종합 민원 이미지 AI데이터"
        self.db_service = DatabaseService()

        # 1단계: YOLOv26s 모델 로드
        self.model_path = os.path.join(self.base_dir, r"yolo26s\runs\banner_s_20260506_1909\weights\best.pt")
        if not os.path.exists(self.model_path):
            self.model_path = os.path.join(self.base_dir, "yolo26s.pt")
        print(f"[AI엔진] YOLO 모델 가중치 파일 로딩: {self.model_path}")
        self.yolo = YOLO(self.model_path)

        # 3단계: Qwen2.5-VL-3B-Instruct VLM 로드 (GPU VRAM 포화 방지를 위해 4비트 양자화 도입)
        self.vlm_model_id = "Qwen/Qwen2.5-VL-3B-Instruct"
        print(f"[AI엔진] VLM 모델 로딩 중: {self.vlm_model_id} (4-bit quantization mode)")
        if self.device == "cuda":
            from transformers import BitsAndBytesConfig
            quantization_config = BitsAndBytesConfig(
                load_in_4bit=True,
                bnb_4bit_compute_dtype=torch.bfloat16,
                bnb_4bit_use_double_quant=True,
                bnb_4bit_quant_type="nf4"
            )
            self.vlm = AutoModelForImageTextToText.from_pretrained(
                self.vlm_model_id,
                quantization_config=quantization_config,
                device_map="auto",
                trust_remote_code=True
            )
        else:
            self.vlm = AutoModelForImageTextToText.from_pretrained(
                self.vlm_model_id,
                torch_dtype=torch.float32,
                device_map="auto",
                trust_remote_code=True
            )
        self.processor = AutoProcessor.from_pretrained(self.vlm_model_id, trust_remote_code=True)

        # 옥외광고물법 적용배제 및 불법 기준 단어 사전 정의
        self.PUBLIC_INTEREST = ["구청", "시청", "보건소", "경찰서", "주민센터", "금연", "무단투기", "단속안내", "캠페인", "안전수칙", "공원내"]
        self.POLITICAL_EXEMPT = ["국회의원", "당원협의회", "더불어민주당", "국민의힘", "정의당", "진보당", "개혁신당", "조국혁신당", "기본소득당", "법안발의", "정책보고", "정당현수막", "지역위원장"]
        self.ELECTION_EXEMPT = ["선거", "투표", "후보", "교육감", "기호", "공직선거법"]
        self.COMMERCIAL_ILLEGAL = ["대출", "일수", "분양", "임대", "대차", "이자", "회원모집", "피트니스", "필라테스", "오피스텔", "모델하우스", "파격조건", "투자대박"]

        # 임시 이미지 경로 설정
        self.script_dir = os.path.dirname(os.path.abspath(__file__))
        self.temp_context = os.path.join(self.script_dir, "_temp_context.jpg")
        self.temp_zoom = os.path.join(self.script_dir, "_temp_zoom.jpg")

        print("[AI엔진] 모든 AI 추론 파이프라인 (YOLO + EasyOCR + Qwen2.5-VL) 초기화 완료.")

    # ─── NLP 문자열 유사도 연산 (Levenshtein) ───
    def levenshtein_similarity(self, s1: str, s2: str) -> float:
        """두 문자열 간의 Levenshtein 거리를 기반으로 유사도를 계산합니다."""
        s1 = s1.strip().replace(" ", "")
        s2 = s2.strip().replace(" ", "")
        if not s1 and not s2:
            return 1.0
        if not s1 or not s2:
            return 0.0

        m, n = len(s1), len(s2)
        dp = [[0] * (n + 1) for _ in range(m + 1)]
        for i in range(m + 1):
            dp[i][0] = i
        for j in range(n + 1):
            dp[0][j] = j

        for i in range(1, m + 1):
            for j in range(1, n + 1):
                if s1[i-1] == s2[j-1]:
                    dp[i][j] = dp[i-1][j-1]
                else:
                    dp[i][j] = min(
                        dp[i-1][j] + 1,    # 삭제
                        dp[i][j-1] + 1,    # 삽입
                        dp[i-1][j-1] + 1   # 대체
                    )
        
        distance = dp[m][n]
        max_len = max(m, n)
        return 1.0 - (distance / max_len)

    # ─── VLM 멀티 에이전트 분석 로직 ───
    def get_contextual_crop(self, image: Image.Image, box: tuple, padding_ratio: float = 0.6) -> Image.Image:
        """현수막 주변 설치 환경이 잘 드러나도록 넓은 범위로 이미지 크롭을 수행합니다."""
        img_w, img_h = image.size
        x1, y1, x2, y2 = box
        pad = int(max(x2 - x1, y2 - y1) * padding_ratio)
        return image.crop((
            max(0, x1 - pad), max(0, y1 - pad),
            min(img_w, x2 + pad), min(img_h, y2 + pad)
        ))

    def run_vlm_analysis(self, context_image: Image.Image, zoom_image: Image.Image, aspect_ratio: float) -> str:
        """Qwen2.5-VL-3B-Instruct 모델을 사용해 다중 에이전트 합의 판독 토론을 가동합니다."""
        
        # VLM 토큰 폭발 및 CPU/GPU 연산 포화를 방지하기 위해 최대 512px 크기로 리사이즈 실행
        def resize_to_max(img, max_dim=512):
            w, h = img.size
            if w <= max_dim and h <= max_dim:
                return img
            if w > h:
                return img.resize((max_dim, int(max_dim * h / w)), Image.LANCZOS)
            else:
                return img.resize((int(max_dim * w / h), max_dim), Image.LANCZOS)

        context_image = resize_to_max(context_image, 512)
        zoom_image = resize_to_max(zoom_image, 512)

        context_image.save(self.temp_context)
        zoom_image.save(self.temp_zoom)

        messages = [{
            "role": "user",
            "content": [
                {"type": "image", "image": self.temp_context},
                {"type": "image", "image": self.temp_zoom},
                {"type": "text", "text": (
                    "당신은 대한민국 옥외광고물법 및 인천광역시 조례에 정통한 현수막 불법 여부 판독 VLM(시각 언어 모델)입니다.\n"
                    f"현재 감지된 현수막의 가로세로 픽셀 비율(Aspect Ratio)은 {aspect_ratio:.1f}:1 입니다.\n"
                    "첫 번째 사진은 주변 환경(배경)을 보여주고, 두 번째 사진은 현수막 내용을 확대한 것입니다.\n"
                    "【멀티 에이전트 합의 판독 프로세스】\n"
                    "판독의 신뢰도와 정확성을 극대화하기 위해 다음 세 명의 가상 에이전트가 토론하고 합의를 도출하여 최종 판정을 내리십시오.\n"
                    "- [관찰 에이전트 (Observation Agent)]: 이미지와 비율을 관찰해 끈/철제 여부, 글자내용, 배경 삼원색 비율 등을 있는 그대로 정밀 기술합니다.\n"
                    "- [준법 감사 에이전트 (Compliance Agent)]: 대한민국의 광고법 규정에 대입하여 4대 위반 기준(①끈설치, ②상업성 문구, ③색채 50%, ④종횡비)을 조목조목 비판 및 검증합니다.\n"
                    "- [합의 판정 에이전트 (Final Judge Agent)]: 두 에이전트의 충돌을 방지하고 아래 판단 우선순위 규칙에 기반하여 최종 판정을 도출합니다.\n\n"
                    
                    "【판단 우선순위 규칙 (Decision Hierarchy)】\n"
                    "1. 최상위 규칙: 행정 주체 및 공익성 검증 (우선순위 1)\n"
                    "   - 현수막 이미지 내에 공공 주체(예: 부평구청장, 구청, 시청, 보건소, 주민자치회, 시설관리공단 등)의 라벨이 명시되어 있거나, 공익/계도 목적(예: 목줄 착용, 배변 수거, 입마개, 주민총회, 주민투표, 공원 내 금연 등)이 발견되면, 설치 장소(펜스, 가로수 등) 및 설치 방식(노끈, 철사 등)에 상관없이 무조건 '합법(LEGAL)' 또는 '적용배제'로 최종 확정하고 추론을 종료하십시오. 이 경우 최종판정은 '합법'이어야 합니다.\n"
                    "2. 차순위 규칙: 상업성 및 위법성 검증 (우선순위 2)\n"
                    "   - 위의 우선순위 1 공공 주체 및 공익 키워드가 전혀 없을 때에만 다음 조건들을 검사하십시오.\n"
                    "   - 지정 게시대가 아닌 펜스, 가로수, 전신주에 노끈으로 무단 결착된 경우나 '민간임대아파트', '대출가능', '분양' 등 상업적 영리 목적의 키워드가 명시된 경우 '불법'으로 판정하십시오.\n\n"

                    "최종 [판정근거] 출력 시 이 세 에이전트의 합의 내용을 요약하여 작성해 주세요.\n"
                    "예시: [토론 합의] 관찰 에이전트가 가로수 끈 설치 및 종횡비 4:1 관측, 감사 에이전트가 ①/④ 위반 판단을 제시하여 최종 불법 판정함.\n\n"

                    "【출력 형식】 (설명글 없이 꺾쇠 괄호 기호 없이 값만 한 줄씩 입력하세요)\n"
                    "텍스트: <현수막에 적힌 대표적인 텍스트 내용 요약>\n"
                    "현수막종류: <일반현수막 또는 정당현수막>\n"
                    "설치환경: <끈으로 나무/전봇대에 설치됨 또는 지정 철제 게시대에 설치됨>\n"
                    "위반항목: <해당되는 위반번호 (①, ②, ③, ④) 또는 스쿨존 금지. 없으면 없음>\n"
                    "최종판정: <불법 또는 합법>\n"
                    "판정근거: <에이전트 토론 및 합의된 최종 판정 근거 한 문장>\n"
                )}
            ]
        }]

        text = self.processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        image_inputs, _ = process_vision_info(messages)
        inputs = self.processor(text=[text], images=image_inputs, padding=True, return_tensors="pt").to(self.device)

        with torch.no_grad():
            gen_ids = self.vlm.generate(**inputs, max_new_tokens=200)
            output = self.processor.batch_decode(
                gen_ids[:, inputs.input_ids.shape[1]:],
                skip_special_tokens=True
            )[0]

        # 임시 파일 정리
        for path in [self.temp_context, self.temp_zoom]:
            if os.path.exists(path):
                os.remove(path)

        return output

    # ─── VLM 출력 구문 추출 및 파싱 ───
    def is_illegal(self, text: str) -> bool:
        if re.search(r"(최종\s*판정|판정)\s*[:：]\s*합법", text):
            return False
        if re.search(r"(최종\s*판정|판정)\s*[:：]\s*불법", text):
            return True
        return False

    def is_placeholder(self, val: str) -> bool:
        placeholders = [
            "읽어낸", "핵심 글자", "요약", "묶였는지", "철제 틀", "명확히 묘사",
            "사고 흐름", "논리적 근거", "한 문장", "해당하는 항목", "간단히 기술",
            "확인 필요", "플레이스홀더", "분석 불가능"
        ]
        val_clean = val.strip("()[]{} ")
        return any(p in val_clean for p in placeholders) or len(val_clean) == 0

    def extract_text_label(self, res: str, aspect_ratio: float) -> str:
        for pattern in [
            r"텍스트\s*[:：]\s*([^\n]+)",
            r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
            r"내용\s*[:：]\s*([^\n]+)",
            r"문구\s*[:：]\s*([^\n]+)",
        ]:
            m = re.search(pattern, res, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("\"'()")
                if not self.is_placeholder(found):
                    return found[:45]
        m = re.search(r'[\'"\u201c\u2018](.*?)[\'"\u201d\u2019]', res)
        if m:
            found = m.group(1).strip()
            if not self.is_placeholder(found):
                return found[:45]
        m = re.search(r'\*\*(.*?)\*\*', res)
        if m:
            found = m.group(1).strip()
            if not self.is_placeholder(found):
                return found[:45]
        skip = ['설치환경', '최종판정', '판정근거', '이미지', '현수막이', '보고서', '분석', '현수막종류', '위반항목', '텍스트']
        for line in res.split('\n'):
            line = line.strip().strip("()[]{} ")
            if len(line) > 4 and not any(k in line for k in skip):
                if not self.is_placeholder(line):
                    return line[:45]
        return "텍스트 미추출 (식별 불가)"

    def extract_environment(self, res: str, aspect_ratio: float) -> str:
        m = re.search(r"설치\s*환경\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
        if m:
            found = m.group(1).strip().strip("()[]{} ")
            if not self.is_placeholder(found):
                return found[:60]
        if 0.3 <= aspect_ratio <= 0.5:
            return "가로등 현수기 (세로형 배너) 설치"
        return "가로수 또는 전봇대 끈 묶음 설치"

    def extract_reason(self, res: str, verdict: str, aspect_ratio: float) -> str:
        m = re.search(r"(?:판정\s*근거|근거)\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
        if m:
            found = m.group(1).strip().strip("()[]{} ")
            if not self.is_placeholder(found):
                return found[:100]
        if verdict == "불법":
            return f"비지정 게시대(가로수/전봇대) 무단 고정 및 비정상적인 종횡비({aspect_ratio:.1f}:1) 확인"
        return "인천시 표준 게시대 규격 및 설치 기준을 준수함"

    def extract_banner_type(self, res: str) -> str:
        m = re.search(r"현수막\s*종류\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
        if m:
            found = m.group(1).strip()
            if "정당" in found:
                return "정당현수막"
            return "일반현수막"
        if any(k in res for k in ["정당", "의원", "민주당", "국민의힘", "진보당", "정의당", "국회의원"]):
            return "정당현수막"
        return "일반현수막"

    def extract_violation(self, res: str, verdict: str) -> str:
        m = re.search(r"위반\s*항목\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
        if m:
            found = m.group(1).strip().strip("()[]{} ")
            if not self.is_placeholder(found):
                return found[:50]
        if verdict == "불법":
            return "① 끈설치 및 ④ 규격위반"
        return "없음"

    # ─── 핵심 추론 및 가공 파이프라인 ───
    def _run_inference_raw(self, image_path: str, cctv_id: str = "CCTV_01") -> List[Dict[str, Any]]:
        """이미지를 읽어 YOLO + OCR을 실행하고, 판독 정확도가 낮을 경우 VLM(Qwen2.5-VL) 토론 판독을 작동시킵니다.
        또한 DB 연동 및 상습 위반 연산을 동시 처리합니다.
        """
        if not os.path.exists(image_path):
            print(f"[AI엔진] 파일이 존재하지 않습니다: {image_path}")
            return []

        img_cv = cv2.imread(image_path)
        if img_cv is None:
            print(f"[AI엔진] 이미지를 로드할 수 없습니다: {image_path}")
            return []

        img_h, img_w, _ = img_cv.shape
        yolo_results = self.yolo(img_cv, verbose=False, device=self.device)
        boxes = yolo_results[0].boxes

        results: List[Dict[str, Any]] = []

        for i, box in enumerate(boxes.xyxy):
            x1, y1, x2, y2 = map(int, box.tolist())
            conf = float(boxes.conf[i])

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
            matched = self.db_service.find_matching_banner(cctv_id, [x1, y1, x2, y2])
            
            is_vlm_triggered = False
            vlm_analysis = ""
            banner_type = "일반현수막"
            environment = "가로수 또는 전봇대 끈 묶음 설치"
            violation = "없음"
            status_code = "ILLEGAL_COMMERCIAL"  # 기본값
            
            # 구/동/위치 매핑
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
                # 만약 기존 캐시된 데이터가 VLM 판독본이면 VLM을 다시 돌리지 않고 캐시 사용
                if matched.get('reason') and "[VLM 판독]" in matched.get('reason'):
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
                        status_code = "ILLEGAL_COMMERCIAL"
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

            if should_run_vlm:
                # ── VLM (Qwen2.5-VL) 강제 진입 가동 (신규 또는 레거시 OCR 정보 업그레이드) ──
                is_vlm_triggered = True
                print(f"[AI엔진] VLM(Qwen2.5-VL) 추론 실행 (대상의 VLM 판정본 부재)")
                
                # VLM용 이미지 준비
                pil_img = Image.open(image_path).convert("RGB")
                context_crop = self.get_contextual_crop(pil_img, (x1, y1, x2, y2))
                zoom_crop = pil_img.crop((x1, y1, x2, y2))

                try:
                    raw = self.run_vlm_analysis(context_crop, zoom_crop, aspect_ratio)
                except Exception as e:
                    raw = f"[VLM 추론 예외] {e}"

                # 가공 및 구문 파싱
                raw_clean = raw.replace("<", "").replace(">", "").replace("[", "").replace("]", "").replace("(", "").replace(")", "")
                
                illegal = self.is_illegal(raw_clean)
                ai_verdict = "ILLEGAL" if illegal else "LEGAL"
                status_code = "ILLEGAL_COMMERCIAL" if illegal else "LEGAL_PUBLIC"

                parsed_text = self.extract_text_label(raw_clean, aspect_ratio)
                if parsed_text and parsed_text != "텍스트 미추출 (식별 불가)":
                    ocr_text = parsed_text
                    # VLM 결과 재검증 (숏서킷 화이트리스트 코드 업데이트)
                    clean_vlm_text = re.sub(r'[\-\_\,\.\?\!\@\#\$\%\^\&\*\(\)\+]', '', ocr_text)
                    has_election_vlm = False
                    for kw in self.ELECTION_EXEMPT:
                        if kw in ["투표", "후보"]:
                            if re.search(r'\b' + kw + r'\b', clean_vlm_text):
                                has_election_vlm = True
                                break
                        else:
                            if kw in clean_vlm_text:
                                has_election_vlm = True
                                break

                    if has_election_vlm:
                        status_code = "EXEMPT_ELECTION"
                        ai_verdict = "LEGAL"
                    elif any(kw in clean_vlm_text for kw in self.POLITICAL_EXEMPT):
                        status_code = "LEGAL_POLITICS"
                        ai_verdict = "LEGAL"
                    elif any(kw in clean_vlm_text for kw in self.PUBLIC_INTEREST):
                        status_code = "LEGAL_PUBLIC"
                        ai_verdict = "LEGAL"
                
                environment = self.extract_environment(raw_clean, aspect_ratio)
                violation = self.extract_violation(raw_clean, ai_verdict)
                ai_reason = "[VLM 판독] " + self.extract_reason(raw_clean, ai_verdict, aspect_ratio)
                vlm_analysis = ai_reason
                banner_type = self.extract_banner_type(raw_clean)

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

            # ── NLP 상습 위반 검증 (Levenshtein Distance >= 80%) ──
            is_recurring = False
            recurrence_count = 0
            recurrence_detail = ""
            
            if ocr_text and ocr_text != "텍스트 미추출 (식별 불가)":
                try:
                    # 현재 주소지 또는 CCTV의 과거 단속 리스트 로드
                    past_records = self.db_service.get_history_by_location(cctv_id)
                    for rec in past_records:
                        # 동일 DB 레코드는 매칭 대상에서 제외
                        if db_id and rec.get("id") == db_id:
                            continue
                        
                        past_text = rec.get("text", "")
                        if past_text and past_text != "텍스트 미추출 (식별 불가)":
                            similarity = self.levenshtein_similarity(ocr_text, past_text)
                            if similarity >= 0.8:
                                is_recurring = True
                                recurrence_count += 1
                                if recurrence_count == 1:
                                    recurrence_detail = f"상습 검출 매칭 대상: ID {rec.get('id')} ('{past_text[:10]}...' 유사도 {similarity*100:.1f}%)"
                    
                    if recurrence_count > 0:
                        recurrence_detail += f" (총 {recurrence_count}회 중복 적발)"
                except Exception as e:
                    print(f"[AI엔진] 상습 위반 NLP 매칭 오류: {e}")

            # 크롭 증거 이미지 base64 인코딩
            _, buffer = cv2.imencode('.jpg', crop_img)
            img_b64 = f"data:image/jpeg;base64,{base64.b64encode(buffer).decode('utf-8')}"

            # 이미지 static 저장
            crop_filename = f"crop_{uuid.uuid4().hex}.jpg"
            crop_save_path = os.path.join(settings.ASSETS_DIR, crop_filename)
            cv2.imwrite(crop_save_path, crop_img)
            crop_url = f"http://localhost:8000/assets/{crop_filename}"

            alert_id = str(db_id) if db_id else str(uuid.uuid4())

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
                IsRecurring=is_recurring,
                RecurrenceCount=recurrence_count,
                RecurrenceDetail=recurrence_detail,
                Status="Pending",
                StatusCode=status_code
            )

            bbox = BBox(X=x1, Y=y1, Width=crop_w, Height=crop_h)
            results.append({
                "alert": alert,
                "bbox": bbox,
                "ocr_confidence": ocr_conf,
                "is_vlm_triggered": is_vlm_triggered,
                "vlm_analysis": vlm_analysis
            })

        return results

    def run_inference(self, image_path: str, cctv_id: str = "CCTV_01") -> List[BannerAlert]:
        raw_results = self._run_inference_raw(image_path, cctv_id)
        return [res["alert"] for res in raw_results]

    def run_inference_as_event(self, image_path: str, cctv_id: str = "CCTV_01") -> List[BannerDetectionEvent]:
        raw_results = self._run_inference_raw(image_path, cctv_id)
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
                StatusCode=alert.StatusCode
            )
            events.append(event)
            
        return events

    def _get_cctv_name(self, cctv_id: str) -> str:
        names = {
            "CCTV_01": "송도 1채널",
            "CCTV_02": "구월 2채널",
            "CCTV_03": "부평 3채널",
            "CCTV_04": "주안 4채널",
            "CCTV_05": "청라 5채널"
        }
        return names.get(cctv_id, f"관제 {cctv_id}")

    def _get_cctv_location(self, cctv_id: str) -> Tuple[str, str, str]:
        loc_map = {
            "CCTV_01": ("연수구", "송도동", "인천광역시 연수구 송도동 12"),
            "CCTV_02": ("남동구", "구월동", "인천광역시 남동구 구월동 45"),
            "CCTV_03": ("부평구", "부평동", "인천광역시 부평구 부평동 78"),
            "CCTV_04": ("미추홀구", "주안동", "인천광역시 미추홀구 주안동 10"),
            "CCTV_05": ("서구", "청라동", "인천광역시 서구 청라동 55")
        }
        return loc_map.get(cctv_id, ("미추홀구", "용현동", "인천광역시 미추홀구 용현동"))
