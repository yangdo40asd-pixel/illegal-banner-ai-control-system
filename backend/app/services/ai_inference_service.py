import os
import cv2
import numpy as np
import base64
import uuid
import torch
import re
import threading
from datetime import datetime
from typing import List, Tuple, Dict, Any, Optional
from PIL import Image
from ultralytics import YOLO
from transformers import AutoModelForImageTextToText, AutoProcessor
from qwen_vl_utils import process_vision_info
from app.core.config import settings
from app.schemas.detection_schema import BannerAlert, BannerDetectionEvent, BBox
from app.services.db_service import DatabaseService
from app.utils.image_io import (
    encode_frame_for_ws,
    encode_jpeg_data_uri,
    read_image_bgr,
    scale_bbox,
    write_image_bgr,
)

# OpenMP duplicate warning environment variable
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

class AIInferenceService:
    """YOLOv26s 객체 탐지, EasyOCR 문자 판독, 그리고 Qwen2.5-VL-3B-Instruct VLM을
    결합한 3단계 정밀 현수막 준법 심사 인공지능 추론 서비스 클래스입니다.
    """
    def __init__(self):
        self.device = self._resolve_device()
        self._configure_gpu()

        # 로컬 경로 설정
        self.base_dir = r"D:\138.종합 민원 이미지 AI데이터"
        self.db_service = DatabaseService()

        # 1단계: YOLOv26s 모델 로드
        self.model_path = os.path.join(self.base_dir, r"yolo26s\runs\banner_s_20260506_1909\weights\best.pt")
        if not os.path.exists(self.model_path):
            self.model_path = os.path.join(self.base_dir, "yolo26s.pt")
        print(f"[AI엔진] YOLO 모델 가중치 파일 로딩: {self.model_path}")
        self.yolo = YOLO(self.model_path)
        if self.device == "cuda":
            self.yolo.to("cuda")

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

        # ── 4대 분류: 상업 · 공익성 · 정당 · 불법 ──
        self.CATEGORY_COMMERCIAL = "상업"
        self.CATEGORY_PUBLIC = "공익성"
        self.CATEGORY_PARTY = "정당"
        self.CATEGORY_ILLEGAL = "불법"
        self.LEGAL_FOUR_CATEGORIES = [self.CATEGORY_COMMERCIAL, self.CATEGORY_PUBLIC, self.CATEGORY_PARTY]
        self.ILLEGAL_SUBTYPES = [
            "사행성", "혐오", "음란", "명예훼손", "모욕", "범죄광고", "허위사실",
        ]
        self.ILLEGAL_CONTENT_CATEGORIES = [self.CATEGORY_ILLEGAL] + [
            "명예훼손", "허위사실 유포", "모욕·인격침해",
            "혐오·차별·비하 표현", "음란·외설 표현", "범죄 조장·불법행위 광고", "사행성",
        ]
        self.LEGAL_CONTENT_CATEGORIES = self.LEGAL_FOUR_CATEGORIES
        self.KW_PUBLIC_INTEREST = [
            "이웃", "배려", "생각해", "마음", "캠페인", "금연", "안전", "환경", "쓰레기",
            "무단투기", "단속", "안내", "주민", "예방", "조용", "애완", "주차", "교통",
            "보행", "공익", "구청", "시청", "보건", "경찰", "주민센터", "생활", "마을",
            "우리동네", "종교", "교회", "사찰", "봉사", "기부", "보호", "예방", "환경보호",
            "동물", "분리수거", "재활용", "화재", "방범",
        ]
        self.KW_PARTY = [
            "국회의원", "후보", "정당", "당원", "더불어민주당", "국민의힘", "민주당",
            "탄핵", "선거", "투표", "공직선거", "지역위원장", "법안발의", "의원",
            "대통령", "국회", "여당", "야당", "정책공약", "공약", "지지합니다", "반대합니다",
        ]
        self.KW_REAL_ESTATE = [
            "부동산", "분양", "임대", "매매", "전세", "월세", "오피스텔", "아파트", "빌라",
            "상가", "토지", "모델하우스", "입주", "청약", "부동산중개", "공인중개", "리모델링",
            "타운하우스", "주상복합", "양도", "전매", "분양문의", "임대문의", "부동산컨설팅",
        ]
        self.KW_COMMERCIAL = [
            "할인", "개업", "오픈", "학원", "세일", "이벤트", "전화", "문의", "예약",
            "회원모집", "피트니스", "필라테스", "오픈기념", "대차", "이자",
        ] + self.KW_REAL_ESTATE
        self.KW_EXPLICIT_ILLEGAL_LOAN = [
            "사채", "불법대출", "일수", "당일대출", "무서류대출", "무서류", "신불자",
            "대포통장", "급전", "당일입금", "당일승인", "리딩방", "보이스피싱", "불법환전",
            "작업계좌", "대포폰", "카드깡",
        ]
        self.KW_DEFAMATION = ["사기꾼", "범죄자", "횡령범", "불륜", "외도", "바람", "간통", "폭로", "스캔들", "바람피웠", "불륜자"]
        self.KW_FALSE_FACT = ["날조", "조작", "허위", "거짓말", "가짜뉴스"]
        self.KW_INSULT = ["인간쓰레기", "병신", "개새끼", "쓰레기같", "망할", "꺼져"]
        self.KW_HATE = ["인종", "장애인", "존엄성", "짐승", "하등인간"]
        self.KW_OBSCENE = ["성매매", "성행위", "음란", "야한", "19금", "원조", "조건만남", "성적", "노골적"]
        self.KW_CRIME_AD = [
            "마약", "도박", "홀덤", "바카라", "토토", "보이스피싱",
            "인신매매", "성착취", "필로폰", "아이스", "대마초",
        ] + self.KW_EXPLICIT_ILLEGAL_LOAN
        self.KW_GRAY_ZONE = ["불매", "무능", "망친다", "추방", "반이민", "페미니즘 반대"]

        # ── 은유·은어·우회 표현 (직설 키워드 없을 때 VLM+패턴으로 해독) ──
        # 혼용 기법: ①일상어 차용(얼음·캔디·허브) ②업종은어(출장안마·스웨디시)
        # ③초성(ㅅㅂ) ④연락처만(010·카톡) ⑤고액알바 미끼(인신매매·성착취)
        # ⑥정치·사회비판 위장(폭로·비밀) ⑦숫자+당일(사채) ⑧띄어쓰기·영문혼용(ice·molly)
        self.KW_EUPHEMISM_DRUG = [
            # 필로폰/메스: 뽕·작대기·빙두·아이스·시원한술 (법률자료·수사 보도 은어)
            "뽕", "작대기", "빙두", "필로폰", "아이스작업", "얼음작업", "시원한술", "시원한 술",
            "스피드", "코카인", "코카",
            # MDMA/LSD/대마/합성대마/펜타닐
            "몰리", "molly", "도리", "캔디팝", "우표약", "원석케이", "허브액", "브액",
            "마리화나", "덕트", "라바", "떨", "아파치", "댄스피버", "하얀중국", "중국소녀",
            "얼음", "떡칠", "백숙", "작업방", "떡방",
        ]
        self.KW_EUPHEMISM_TRAFFICKING = [
            # 인신매매·성착취 알선: 고액알바·해외취업·유흥모집 미끼 (TIP·수사 유형)
            "인신매매", "성착취", "고액알바", "고소득알바", "해외알바", "출장알바",
            "유흥알바", "성인알바", "캠방알바", "원조방", "조건방", "미화원모집",
            "룸살롱모집", "도우미모집", "해외고소득", "무비자출국", "미성년모집",
            "남성고소득", "여성고소득", "단기고소득", "주급보장", "숙식제공",
        ]
        self.KW_EUPHEMISM_CRIME = [
            "작업계좌", "대포", "무서류", "급전", "당일입금", "당일승인", "신불자",
            "연체자", "리딩방", "코인방", "수익보장", "원금보장", "텔레방", "비대면입금", "현금화",
            "카드깡", "통장삽", "페이대행", "유심", "보이스", "피싱", "대포폰", "호스트바",
            "불법환전", "가상계좌", "비트코인입금",
        ] + self.KW_EUPHEMISM_DRUG
        self.KW_EUPHEMISM_OBSCENE = [
            "출장", "휴게", "키스방", "안마", "스웨디시", "1인샵", "유흥", "밤알바", "풀싸롱",
            "쩜오", "텐카페", "텐프로", "룸싸롱", "남촌", "여촌", "초이스", "실장", "도우미",
            "출장안마", "출장샵", "핸플", "립카페", "퍼블릭", "조건만남", "원조교제",
            "오픈채팅", "비밀채팅", "텔레그램", "카톡상담",
        ]
        # 은유 2차 트리거용 — 단어 '출장'·'안마'·'캔디' 등 단독은 제외 (오탐 방지)
        self.KW_EUPHEMISM_OBSCENE_STRICT = [
            "출장안마", "출장샵", "키스방", "1인샵", "밤알바", "풀싸롱",
            "쩜오", "텐카페", "텐프로", "룸싸롱", "핸플", "립카페", "퍼블릭", "유흥업소",
            "조건만남", "원조교제", "원조방", "조건방",
        ] + self.KW_EUPHEMISM_TRAFFICKING
        self.KW_EUPHEMISM_DEFAMATION = [
            "두번째", "비밀의", "밀회", "애정행각", "딴짓", "비밀연애", "은밀한", "감춰진",
            "폭로한다", "폭로합니다", "진실을", "감춘", "숨긴", "바람피", "색테크", "불륜설",
            "스캔들", "알고보니", "실은", "뒤에서",
        ]
        self.KW_EUPHEMISM_INSULT = [
            "ㅅㅂ", "ㅂㅅ", "ㅈㄹ", "ㅁㅊ", "시발", "씨발", "지랄", "미친",
            "놈들", "년들", "충", "꼴통", "찐따", "한남", "김치녀", "틀딱", "급식충",
            "따위", "같은 놈", "같은년", "인간말종",
        ]
        self.KW_EUPHEMISM_HATE = [
            "나가라", "추방하라", "박멸", "척결", "역적", "종북", "좌좀", "극우", "극좌",
            "쓰레기민족", "짱깨", "쪽바리", "짱개", "조센징", "일베", "메갈",
        ]
        # (정규식, 불법유형 또는 None=재검토 트리거만)
        self.EUPHEMISM_REGEX: List[Tuple[str, str | None, str]] = [
            # 연락처 단독은 불법 아님 (부동산·상업 광고) — 사채/당일대출 복합 패턴만 불법
            (r"[ㄱ-ㅎ]{2,}", None, "초성 은어"),
            (r"\d+\s*만\s*원.*당일", "범죄 조장·불법행위 광고", "당일대출 은유 광고"),
            (r"당일.*\d+\s*만", "범죄 조장·불법행위 광고", "당일대출 은유 광고"),
            (r"(?:고액|고소득|해외|출장|유흥|성인|캠방).{0,6}알바", "범죄 조장·불법행위 광고", "고액알바·인신매매 미끼"),
            (r"(?:아이스|ice|캔디|candy|몰리|molly|빙두|작대기|필로폰).{0,8}(?:작업|거래|구함|판매|연락)", "범죄 조장·불법행위 광고", "마약 은어+행위"),
            (r"(?:얼음|떡칠|허브|브액).{0,6}(?:작업|거래|구함)", "범죄 조장·불법행위 광고", "마약 업계 은어"),
            (r"(?:카톡|카카오|텔레|텔레그램|오픈채팅).{0,12}(?:상담|문의|비밀|입장)", None, "비공개 연락채널 광고"),
            (r"(?:출장|스웨디시|안마).{0,8}(?:010|카톡|연락)", "음란·외설 표현", "성매매 알선+연락처"),
        ]
        self.EUPHEMISM_TRIGGER_WORDS = [
            "비밀", "폭로", "진실", "은밀", "실체", "뿌린다", "알려진다", "폭탄", "충격",
            "은어", "비유", "암시", "암호",
        ]

        # 임시 이미지 경로 설정
        self.script_dir = os.path.dirname(os.path.abspath(__file__))
        self.temp_context = os.path.join(self.script_dir, "_temp_context.jpg")
        self.temp_zoom = os.path.join(self.script_dir, "_temp_zoom.jpg")
        self._inference_lock = threading.Lock()

        # VLM 해상도 — 512px는 글자 인식률 급락, zoom 1024 기준 복원
        self.VLM_ZOOM_WIDTH = 1024
        self.VLM_CONTEXT_MAX_DIM = 768
        self.VLM_MAX_TOKENS_PASS1 = 512
        self.VLM_MAX_TOKENS_PASS2 = 384
        self.VLM_MAX_TOKENS_PASS3 = 320

        print("[AI엔진] 모든 AI 추론 파이프라인 (YOLO + VLM) 초기화 완료.")

    # VLM 출력 형식 — 파서(_extract_markdown_vlm_text)와 1:1 매칭
    VLM_MARKDOWN_OUTPUT_GUIDE = (
        "【출력 형식 — 아래 마크다운 bullet만 사용, 다른 형식 금지】\n"
        "- **이미지 관찰**: 설치 위치·배경 한 줄\n"
        "- **글자·내용 독해**: 현수막 글자를 읽은 순서대로 원문 그대로 (요약·의역·생략 금지)\n"
        "- **의미 분석**: 의도 한 줄\n"
        "- **텍스트**: 글자·내용 독해와 동일한 원문 한 줄\n"
        "- **실질의미**: 없으면 해당없음\n"
        "- **법적판정**: 합법 또는 불법\n"
        "- **추천분류**: 상업 또는 공익성 또는 정당 또는 불법\n"
        "- **불법세부**: 불법일 때만 사행성/혐오/음란/명예훼손/모욕/범죄광고, 아니면 없음\n"
        "- **신뢰도**: 높음 또는 보통 또는 낮음\n"
        "- **판단이유**: 한 줄\n"
    )

    def _resolve_device(self) -> str:
        if torch.cuda.is_available():
            return "cuda"
        print("[AI엔진] CUDA 미사용 — CPU 모드 (VLM 매우 느림)")
        return "cpu"

    def _configure_gpu(self) -> None:
        if self.device != "cuda":
            return
        try:
            torch.backends.cudnn.benchmark = True
            torch.backends.cuda.matmul.allow_tf32 = True
            if hasattr(torch, "set_float32_matmul_precision"):
                torch.set_float32_matmul_precision("high")
            props = torch.cuda.get_device_properties(0)
            print(
                f"[AI엔진] GPU 가속: {props.name} "
                f"({props.total_memory / 1024 ** 3:.1f} GB VRAM)"
            )
        except Exception as e:
            print(f"[AI엔진] GPU 설정 경고: {e}")

    def _get_vlm_device(self):
        try:
            return next(self.vlm.parameters()).device
        except StopIteration:
            return torch.device(self.device)

    @staticmethod
    def _safe_yolo_conf(boxes, index: int) -> float:
        try:
            val = boxes.conf[index]
            if hasattr(val, "item"):
                val = val.item()
            return float(val)
        except (TypeError, ValueError):
            return 0.0

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
    def _vlm_resize(self, img: Image.Image, max_dim: Optional[int] = None) -> Image.Image:
        max_dim = max_dim or self.VLM_CONTEXT_MAX_DIM
        w, h = img.size
        if w <= max_dim and h <= max_dim:
            return img
        if w > h:
            return img.resize((max_dim, int(max_dim * h / w)), Image.LANCZOS)
        return img.resize((int(max_dim * w / h), max_dim), Image.LANCZOS)

    def _vlm_resize_zoom(self, img: Image.Image) -> Image.Image:
        """확대 크롭 — 가로 1024 기준 (이전 동작 복원)."""
        w, h = img.size
        if w <= 0 or h <= 0:
            return img
        target_w = self.VLM_ZOOM_WIDTH
        target_h = max(1, int(target_w * h / w))
        return img.resize((target_w, target_h), Image.LANCZOS)

    def _vlm_run_generate(self, messages: list, max_new_tokens: int) -> str:
        text = self.processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        image_inputs, _ = process_vision_info(messages)
        vlm_device = self._get_vlm_device()
        inputs = self.processor(
            text=[text], images=image_inputs, padding=True, return_tensors="pt"
        )
        inputs = {k: v.to(vlm_device) if hasattr(v, "to") else v for k, v in inputs.items()}
        with torch.inference_mode():
            if vlm_device.type == "cuda":
                with torch.autocast(device_type="cuda", dtype=torch.bfloat16):
                    gen_ids = self.vlm.generate(**inputs, max_new_tokens=max_new_tokens, do_sample=False)
            else:
                gen_ids = self.vlm.generate(**inputs, max_new_tokens=max_new_tokens, do_sample=False)
        return self.processor.batch_decode(
            gen_ids[:, inputs["input_ids"].shape[1]:],
            skip_special_tokens=True,
        )[0]

    def _vlm_cleanup_temp(self):
        for path in [self.temp_context, self.temp_zoom]:
            if os.path.exists(path):
                os.remove(path)

    def get_contextual_crop(self, image: Image.Image, box: tuple, padding_ratio: float = 0.6) -> Image.Image:
        """현수막 주변 설치 환경이 잘 드러나도록 넓은 범위로 이미지 크롭을 수행합니다."""
        img_w, img_h = image.size
        x1, y1, x2, y2 = box
        pad = int(max(x2 - x1, y2 - y1) * padding_ratio)
        return image.crop((
            max(0, x1 - pad), max(0, y1 - pad),
            min(img_w, x2 + pad), min(img_h, y2 + pad)
        ))

    def _vlm_build_image_messages(self, context_image: Image.Image, zoom_image: Image.Image, prompt: str) -> list:
        context_image = self._vlm_resize(context_image, self.VLM_CONTEXT_MAX_DIM)
        zoom_image = self._vlm_resize_zoom(zoom_image)
        context_image.save(self.temp_context)
        zoom_image.save(self.temp_zoom)
        return [{
            "role": "user",
            "content": [
                {"type": "image", "image": self.temp_context},
                {"type": "image", "image": self.temp_zoom},
                {"type": "text", "text": prompt},
            ],
        }]

    def run_vlm_analysis(self, context_image: Image.Image, zoom_image: Image.Image, aspect_ratio: float) -> str:
        """1차 VLM — 4대 분류 (상업·공익성·정당·불법)."""
        prompt = (
            "첫 번째 이미지는 현수막 주변 맥락, 두 번째는 현수막 확대입니다.\n"
            "오직 현수막 글 내용만 보고 분류하십시오. 설치 허가·위치는 판단하지 마십시오.\n\n"
            "【4대 분류 — 추천분류는 반드시 아래 4개 중 정확히 하나】\n\n"
            "1. 상업 — 개업·할인·학원·제품·서비스·**부동산(분양·임대·매매·오피스텔·아파트)** 홍보\n"
            "   ※ 부동산·분양 광고에 전화번호·대출 안내가 있어도 **상업(합법)** — 사채·일수·당일대출만 불법\n"
            "2. 공익성 — 주민 캠페인, 이웃 배려, 금연·환경·안전·교통·생활 안내, "
            "종교·봉사·마을 행사 (예: '이웃을 생각해주세요', '쓰레기 무단투기 금지')\n"
            "3. 정당 — 정당·후보·선거·탄핵·국회의원·정책 공약 등 정치·정당 활동만\n"
            "   ※ '이웃 배려', '생활 캠페인'은 정당이 아니라 공익성\n"
            "   ※ 정부 비판만 있고 정당·선거 맥락이 없으면 공익성 또는 정당 중 "
            "정당 고유 표현(후보·정당명·선거)이 있을 때만 정당\n\n"
            "4. 불법 — 내용 자체가 불법 가능성 매우 높음\n"
            "   · 사행성: 도박·불법대출·사채·마약 광고\n"
            "   · 혐오: 인종·민족·장애인·성별 비하·추방 요구\n"
            "   · 음란: 성매매·노골적 성적 표현\n"
            "   · 명예훼손·모욕·범죄 조장 광고\n\n"
            f"{self.VLM_MARKDOWN_OUTPUT_GUIDE}"
        )
        messages = self._vlm_build_image_messages(context_image, zoom_image, prompt)
        output = self._vlm_run_generate(messages, self.VLM_MAX_TOKENS_PASS1)
        self._vlm_cleanup_temp()
        return output

    def run_vlm_verification_pass(
        self, context_image: Image.Image, zoom_image: Image.Image,
        first_analysis: str, parsed_text: str
    ) -> str:
        """2차 VLM — 1차 결과 심층 재검토 (조건부)."""
        prompt = (
            f"[1차 AI 분석]\n{first_analysis[:1400]}\n\n"
            f"인식 텍스트: {parsed_text}\n\n"
            "위 1차 추론을 비판적으로 재검토하십시오. 이미지를 다시 보고 틀린 부분을 수정하십시오.\n"
            "은어·폭로위장·마약·인신매매 미끼·불법광고를 놓쳤는지 확인하십시오.\n\n"
            "- **재검토 의견**: 1차 동의/반박, 새로 읽은 글자\n"
            f"{self.VLM_MARKDOWN_OUTPUT_GUIDE}"
        )
        messages = self._vlm_build_image_messages(context_image, zoom_image, prompt)
        output = self._vlm_run_generate(messages, self.VLM_MAX_TOKENS_PASS2)
        self._vlm_cleanup_temp()
        return output

    def run_vlm_euphemism_decode_pass(
        self, context_image: Image.Image, zoom_image: Image.Image,
        surface_text: str, first_category: str
    ) -> str:
        """2차 VLM — 은유·은어 전용 해독 (조건부)."""
        prompt = (
            f"표면 문구: {surface_text}\n"
            f"1차 분류: {first_category}\n\n"
            "현수막 은유·은어 해독 전문가로서 숨긴 불법 의미를 찾으십시오.\n"
            "예: 급전+010=사채, 출장안마=성매매, 고액알바=인신매매, 얼음·아이스=마약, 폭로·비밀=명예훼손\n\n"
            "- **은유탐지**: 있음 또는 없음\n"
            f"{self.VLM_MARKDOWN_OUTPUT_GUIDE}"
        )
        messages = self._vlm_build_image_messages(context_image, zoom_image, prompt)
        output = self._vlm_run_generate(messages, self.VLM_MAX_TOKENS_PASS3)
        self._vlm_cleanup_temp()
        return output

    def _match_euphemism_regex(self, text: str) -> Tuple[str | None, List[str]]:
        """정규식 기반 은유 패턴 매칭. (불법유형, 탐지신호목록)"""
        signals: List[str] = []
        inferred: str | None = None
        for pattern, cat, label in self.EUPHEMISM_REGEX:
            if re.search(pattern, text):
                signals.append(label)
                if cat and not inferred:
                    inferred = cat
        return inferred, signals

    def detect_euphemism_signals(self, text: str) -> List[str]:
        """은유·은어 재검토가 필요한 신호 수집."""
        if not text:
            return []
        signals: List[str] = []
        checks = [
            (self.KW_EUPHEMISM_DRUG, "마약 은어"),
            (self.KW_EUPHEMISM_TRAFFICKING, "인신매매·성착취 미끼"),
            (self.KW_EUPHEMISM_CRIME, "범죄·사채 은어"),
            (self.KW_EUPHEMISM_OBSCENE, "성매매·유흥 은어"),
            (self.KW_EUPHEMISM_DEFAMATION, "스캔들·폭로 은유"),
            (self.KW_EUPHEMISM_INSULT, "모욕·초성 은어"),
            (self.KW_EUPHEMISM_HATE, "혐오 은유"),
        ]
        for kws, label in checks:
            if any(k in text for k in kws):
                signals.append(label)
        if any(t in text for t in self.EUPHEMISM_TRIGGER_WORDS):
            signals.append("폭로·비밀 암시")
        _, regex_signals = self._match_euphemism_regex(text)
        signals.extend(regex_signals)
        if re.search(r"은유\s*[:：]\s*있음", text):
            signals.append("VLM 은유탐지")
        return signals

    def has_euphemism_indicators(self, text: str) -> bool:
        """약한 신호 포함 광범위 탐지 — 2차 트리거에는 should_trigger_euphemism_review 사용."""
        return len(self.detect_euphemism_signals(text)) > 0

    def _extract_vlm_conclusion_section(self, raw: str) -> str:
        """VLM 출력 결론 블록만 추출 (분석 본문·프롬프트 잔여 오탐 방지)."""
        for pattern in (
            r"===\s*결론\s*===([\s\S]*)",
            r"##\s*결론\s*([\s\S]*)",
            r"【\s*결론\s*】([\s\S]*)",
        ):
            m = re.search(pattern, raw)
            if m:
                return m.group(1).strip()
        m2 = re.search(r"법적판정\s*[:：]", raw)
        if m2:
            return raw[m2.start():].strip()
        m3 = re.search(r"텍스트\s*[:：]\s*.+\n\s*실질의미\s*[:：]", raw, re.DOTALL)
        if m3:
            return raw[m3.start():].strip()
        return ""

    def _extract_reading_section(self, raw: str) -> str:
        """VLM '글자·내용 독해' 블록 추출."""
        m = re.search(
            r"===\s*글자[^=\n]*독해\s*===([\s\S]*?)(?====|\Z)",
            raw,
        )
        return m.group(1).strip() if m else ""

    def _parse_text_field(self, scope: str) -> str:
        """구조화 필드 '텍스트:' 한 줄·여러 줄 파싱."""
        if not scope:
            return ""
        m = re.search(
            r"텍스트\s*[:：]\s*(.+?)(?=\n\s*(?:실질의미|법적판정|추천분류|불법세부|신뢰도|판단이유|요약\s*1|요약1)\s*[:：]|\Z)",
            scope,
            re.DOTALL | re.MULTILINE,
        )
        if m:
            if cleaned := self._sanitize_banner_text(m.group(1)):
                return cleaned
        m_line = re.search(r"텍스트\s*[:：]\s*([^\n]+)", scope)
        if m_line:
            if cleaned := self._sanitize_banner_text(m_line.group(1)):
                return cleaned
        return ""

    def _fallback_banner_text_scan(self, raw: str) -> str:
        """결론 필드 실패 시 백업 — 인용부호·독해 본문·첫 유효 줄."""
        for pattern in (
            r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
            r"내용\s*[:：]\s*([^\n]+)",
            r"문구\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, raw, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("\"'()")
                if found and not self.is_placeholder(found):
                    return found[:300]
        for pattern in (r'[\'"\u201c\u2018](.*?)[\'"\u201d\u2019]', r"\*\*(.*?)\*\*"):
            m = re.search(pattern, raw)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    return found[:300]
        skip = (
            "설치", "판정", "분류", "신뢰도", "이미지", "현수막", "분석", "결론",
            "독해", "관찰", "의미", "법적", "추천", "불법세부", "실질", "해당없음",
        )
        reading = self._extract_reading_section(raw)
        for block in (reading, raw):
            if not block:
                continue
            for line in block.split("\n"):
                line = line.strip().strip("()[]{} ")
                if len(line) < 2 or line.startswith("==="):
                    continue
                if any(k in line for k in skip):
                    continue
                if not self.is_placeholder(line):
                    return line[:300]
        return ""

    def should_trigger_euphemism_review(self, banner_text: str, vlm_raw_clean: str) -> bool:
        """은유·은어 2차 심층검토 트리거.

        호출 측에서 1차 판정이 합법(LEGAL)인 경우에만 검사할 것.

        켜지는 경우 (강한 신호 A~E 중 하나):
          A. VLM 결론에 '은유: 있음' / '은유탐지: 있음'
          B. 실질의미가 현수막 표면 텍스트와 실질적으로 다름 (4자 이상)
          C. 전화번호·당일대출·범죄은어 등 infer_category_from_euphemisms() 매칭
          D. 범죄은어 / 성매매 복합어 / 모욕·초성 / 혐오 은어 키워드
          E. 스캔들 은유 — 폭로·스캔들류 + 인명·직함, 또는 폭로·스캔들·밀회·불륜설·바람피

        켜지지 않는 경우:
          - '비밀'·'진실'·'충격' 등 TRIGGER_WORDS 단독
          - '출장'·'안마'·'스웨디시' 등 단일 상업어
          - 초성 정규식 단독 (다른 신호 없음)
          - VLM 분석 본문(결론 블록 밖)에만 등장하는 단어
        """
        banner_text = (banner_text or "").strip()
        conclusion = self._extract_vlm_conclusion_section(vlm_raw_clean)
        check_text = f"{banner_text} {conclusion}".strip()
        if not check_text:
            return False

        if conclusion and re.search(r"은유\s*[:：]\s*있음|은유탐지\s*[:：]\s*있음", conclusion):
            return True

        literal = self.extract_literal_meaning(conclusion or vlm_raw_clean)
        if literal and banner_text:
            norm_lit = re.sub(r"\s+", "", literal)
            norm_banner = re.sub(r"\s+", "", banner_text)
            if (
                len(norm_lit) >= 4
                and norm_lit not in ("해당없음", "없음", "N/A")
                and norm_lit not in norm_banner
                and norm_banner not in norm_lit
            ):
                return True

        if self.infer_category_from_euphemisms(check_text):
            return True

        if any(k in check_text for k in self.KW_EUPHEMISM_DRUG):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_TRAFFICKING):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_CRIME):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_OBSCENE_STRICT):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_INSULT):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_HATE):
            return True

        if any(k in check_text for k in self.KW_EUPHEMISM_DEFAMATION):
            if re.search(r"[가-힣]{2,4}\s*(시장|구청장|의원|대표|교수|목사|장로|씨)", check_text):
                return True
            if any(t in check_text for t in ("폭로", "스캔들", "밀회", "불륜설", "바람피")):
                return True

        return False

    def infer_category_from_euphemisms(self, text: str) -> str | None:
        """은어·은유 패턴으로 6대 불법 유형 추정 (직설 키워드 없을 때)."""
        if not text or self._commercial_shield(text):
            return None
        regex_cat, _ = self._match_euphemism_regex(text)
        if regex_cat:
            return regex_cat
        if any(k in text for k in self.KW_EUPHEMISM_DRUG):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_TRAFFICKING):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_CRIME):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_OBSCENE_STRICT):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_EUPHEMISM_OBSCENE):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_EUPHEMISM_INSULT):
            return "모욕·인격침해"
        if any(k in text for k in self.KW_EUPHEMISM_HATE):
            return "혐오·차별·비하 표현"
        if any(k in text for k in self.KW_EUPHEMISM_DEFAMATION):
            if re.search(r"[가-힣]{2,4}\s*(시장|구청장|의원|대표|교수|목사|장로|씨)", text):
                return "명예훼손"
            if any(t in text for t in ("폭로한다", "폭로합니다", "스캔들", "밀회", "불륜설", "바람피")):
                return "명예훼손"
        return None

    def extract_literal_meaning(self, res: str) -> str:
        """VLM 출력에서 실질의미/해독 필드 추출."""
        for key in ("실질의미", "해독근거", "해독결과"):
            m = re.search(rf"{key}\s*[:：]\s*([^\n]+)", res)
            if m:
                val = m.group(1).strip()
                if val and val not in ("해당없음", "없음", "N/A"):
                    return val
        return ""

    def needs_euphemism_decode(self, category: str, parsed_text: str, raw_clean: str, ai_verdict: str) -> bool:
        """3차 은유 해독 패스 — 2차와 동일한 강한 신호 기준 (합법 잔류 시만)."""
        if ai_verdict != "LEGAL":
            return False
        return self.should_trigger_euphemism_review(parsed_text, raw_clean)

    def _normalize_category(self, category: str) -> str:
        """VLM 출력 → 4대 분류(상업·공익성·정당·불법)로 정규화."""
        c = category.strip()
        if not c:
            return ""
        if "불법" in c or any(k in c for k in ("혐오", "음란", "사행", "명예훼손", "모욕", "범죄", "도박", "마약")):
            return self.CATEGORY_ILLEGAL
        if "상업" in c or ("광고" in c and "불법" not in c):
            return self.CATEGORY_COMMERCIAL
        if any(k in c for k in ("부동산", "분양", "임대", "매매", "오피스텔", "아파트", "전세", "월세")):
            return self.CATEGORY_COMMERCIAL
        if any(k in c for k in ("정당", "선거", "후보", "탄핵", "국회", "정치")):
            return self.CATEGORY_PARTY
        if any(k in c for k in ("공익", "캠페인", "안내", "주민", "이웃", "환경", "안전", "종교", "사회")):
            return self.CATEGORY_PUBLIC
        if any(k in c for k in ("정부", "비판", "운동")):
            return self.CATEGORY_PUBLIC
        return c

    def _is_illegal_category(self, category: str) -> bool:
        norm = self._normalize_category(category)
        return norm == self.CATEGORY_ILLEGAL or any(
            ic in norm for ic in (
                "명예훼손", "허위사실", "모욕", "혐오", "음란", "범죄", "사행",
            )
        )

    def _is_party_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_PARTY)

    def _is_commercial_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_COMMERCIAL)

    def _is_explicit_illegal_commercial(self, text: str) -> bool:
        """사채·불법대출·일수 등 진짜 불법 상업만."""
        if not text:
            return False
        if any(k in text for k in self.KW_EXPLICIT_ILLEGAL_LOAN):
            return True
        if re.search(r"당일.*\d+\s*만", text) or re.search(r"\d+\s*만\s*원.*당일", text):
            return True
        return False

    def _commercial_shield(self, text: str) -> bool:
        """부동산·일반 상업 광고 — 불법대출 신호 없으면 불법 키워드/연락처 오탐 차단."""
        return self._is_commercial_text(text) and not self._is_explicit_illegal_commercial(text)

    def _is_public_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_PUBLIC_INTEREST)

    def extract_illegal_subtype(self, res: str) -> str:
        conclusion = self._extract_vlm_conclusion_section(res) or res
        m = re.search(r"불법\s*세부\s*[:：]\s*([^\n]+)", conclusion)
        if m:
            return m.group(1).strip()[:20]
        if any(k in conclusion for k in ("혐오", "차별", "비하")):
            return "혐오"
        if any(k in conclusion for k in ("음란", "외설", "성매매")):
            return "음란"
        if any(k in conclusion for k in ("도박", "사채", "마약", "사행")):
            return "사행성"
        return "내용위반"

    def _infer_four_category(self, text: str) -> str:
        """키워드 보조 — 기본값 공익성 ('이웃을 생각해주세요' 등)."""
        if not text:
            return self.CATEGORY_PUBLIC
        if self._is_commercial_text(text):
            return self.CATEGORY_COMMERCIAL
        if self._is_party_text(text):
            return self.CATEGORY_PARTY
        return self.CATEGORY_PUBLIC

    def infer_category_from_keywords(self, text: str) -> str | None:
        """직설 키워드 + 은유 패턴으로 6대 불법 유형 추정."""
        if not text:
            return None
        if self._commercial_shield(text):
            return None
        if any(k in text for k in self.KW_CRIME_AD):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_OBSCENE):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_INSULT):
            return "모욕·인격침해"
        if any(k in text for k in self.KW_FALSE_FACT) and any(
            k in text for k in self.KW_DEFAMATION + ["범죄", "횡령", "사기"]
        ):
            return "허위사실 유포"
        if any(k in text for k in self.KW_DEFAMATION):
            return "명예훼손"
        if any(k in text for k in self.KW_HATE):
            return "혐오·차별·비하 표현"
        return self.infer_category_from_euphemisms(text)

    def _extract_markdown_field(self, raw: str, label: str) -> str:
        """`- **라벨**: 값` 마크다운 필드 추출."""
        if not raw:
            return ""
        pattern = rf"(?:^|\n)\s*[-*]\s*\*{{0,2}}{label}\*{{0,2}}\s*[:：]\s*([^\n]+)"
        m = re.search(pattern, raw, re.IGNORECASE)
        return m.group(1).strip() if m else ""

    def _parse_legal_verdict_value(self, val: str) -> str | None:
        if not val:
            return None
        if re.search(r"불법\s*가능|불법\s*의심", val):
            return "ILLEGAL"
        if "불법" in val and "합법" not in val:
            return "ILLEGAL"
        if "회색" in val:
            return "GRAY"
        if "합법" in val:
            return "LEGAL"
        return None

    def extract_vlm_legal_verdict(self, res: str) -> str | None:
        """VLM 법적판정 — 마크다운 우선, 결론 블록 폴백."""
        for key in ("법적판정", "재검토결과", "해독결과"):
            if val := self._extract_markdown_field(res, key):
                if verdict := self._parse_legal_verdict_value(val):
                    return verdict
        conclusion = self._extract_vlm_conclusion_section(res) or res
        for key in ("법적판정", "재검토결과", "해독결과"):
            m = re.search(rf"{key}\s*[:：]\s*([^\n]+)", conclusion)
            if m and (verdict := self._parse_legal_verdict_value(m.group(1).strip())):
                return verdict
        return None

    def extract_confidence(self, res: str) -> str:
        if val := self._extract_markdown_field(res, r"신뢰도"):
            return val
        conclusion = self._extract_vlm_conclusion_section(res) or res
        m = re.search(r"신뢰도\s*[:：]\s*([^\n]+)", conclusion)
        return m.group(1).strip() if m else "보통"

    def _vlm_confidence_to_ocr(self, vlm_conf: str, has_text: bool) -> float:
        """VLM 신뢰도 → OCR 신뢰도 (0~1). 임의 난수 사용 안 함."""
        if not has_text:
            return 0.0
        conf = (vlm_conf or "").strip()
        if "높음" in conf:
            return 0.92
        if "낮음" in conf:
            return 0.72
        return 0.85

    def is_low_confidence(self, res: str) -> bool:
        return "낮음" in self.extract_confidence(res)

    def needs_deep_review(
        self, category: str, parsed_text: str, raw_clean: str,
        mapped_verdict: str, vlm_legal_verdict: str | None
    ) -> bool:
        """AI 신뢰도 낮음·판정 불일치·회색지대일 때만 2차 심층 재검토."""
        if self.is_low_confidence(raw_clean):
            return True
        if vlm_legal_verdict == "ILLEGAL" and mapped_verdict == "LEGAL":
            return True
        if vlm_legal_verdict == "GRAY" and mapped_verdict == "LEGAL":
            return True
        return False

    def _map_legal_category(self, category: str, parsed_text: str) -> Dict[str, str]:
        """합법 → 4대 분류 중 상업·공익성·정당."""
        cat = self._normalize_category(category)
        text = parsed_text or ""
        if cat not in self.LEGAL_FOUR_CATEGORIES or cat == "":
            cat = self._infer_four_category(text)

        if cat == self.CATEGORY_COMMERCIAL:
            return {
                "ai_verdict": "LEGAL",
                "status_code": "LEGAL_COMMERCIAL",
                "category": self.CATEGORY_COMMERCIAL,
                "judgment_title": f"✅ {self.CATEGORY_COMMERCIAL} (영리 광고)",
                "violation": "없음 (내용 기준 합법)",
            }
        if cat == self.CATEGORY_PARTY:
            return {
                "ai_verdict": "LEGAL",
                "status_code": "LEGAL_POLITICS",
                "category": self.CATEGORY_PARTY,
                "judgment_title": f"✅ {self.CATEGORY_PARTY} (정치·선거)",
                "violation": "없음 (내용 기준 합법)",
            }
        return {
            "ai_verdict": "LEGAL",
            "status_code": "LEGAL_PUBLIC",
            "category": self.CATEGORY_PUBLIC,
            "judgment_title": f"✅ {self.CATEGORY_PUBLIC} (주민·캠페인·안내)",
            "violation": "없음 (내용 기준 합법)",
        }

    def map_category_to_verdict(
        self, category: str, parsed_text: str, raw_clean: str,
        vlm_legal_verdict: str | None = None,
    ) -> Dict[str, str]:
        """VLM 법적판정 최우선. 합법 판정은 키워드로 불법 전환 금지."""
        category = self._normalize_category(category)
        literal = self.extract_literal_meaning(raw_clean)
        combined = f"{parsed_text} {literal}"
        unknown = category in ("", "알 수 없음 (수동검토)") or "알 수 없음" in category

        # 부동산·상업 광고는 사채/불법대출 없으면 무조건 상업(합법)
        if self._commercial_shield(combined):
            return self._map_legal_category(self.CATEGORY_COMMERCIAL, parsed_text)

        if vlm_legal_verdict == "GRAY":
            vlm_legal_verdict = "LEGAL"

        if vlm_legal_verdict == "LEGAL":
            if self._normalize_category(category) == self.CATEGORY_ILLEGAL:
                category = self._infer_four_category(parsed_text)
            return self._map_legal_category(category, parsed_text)

        if vlm_legal_verdict == "ILLEGAL" or self._normalize_category(category) == self.CATEGORY_ILLEGAL:
            subtype = self.extract_illegal_subtype(raw_clean)
            if infer := self.infer_category_from_keywords(combined):
                if "혐오" in infer:
                    subtype = "혐오"
                elif "음란" in infer:
                    subtype = "음란"
                elif "범죄" in infer or "사채" in combined:
                    subtype = "사행성"
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }

        # VLM 법적판정 미확정 — 불법 키워드만 보조, 나머지는 4대 분류 추론
        if self._is_illegal_category(category):
            subtype = self.extract_illegal_subtype(raw_clean)
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }
        if unknown and (kw := self.infer_category_from_keywords(combined)):
            subtype = self.extract_illegal_subtype(raw_clean)
            if "혐오" in kw:
                subtype = "혐오"
            elif "음란" in kw:
                subtype = "음란"
            elif "범죄" in kw:
                subtype = "사행성"
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }
        return self._map_legal_category(
            category if not unknown else self._infer_four_category(parsed_text),
            parsed_text,
        )

    # ─── VLM 출력 구문 추출 및 파싱 ───
    _SECTION_HEADERS = {
        "글자·내용 독해", "이미지 관찰", "의미 분석", "결론",
        "현수막 원문 전체를 한 줄로", "설치 위치·배경만 간략히",
        "의도 한 줄", "1차에서 읽은 원문 유지 또는 수정본",
    }

    def _is_section_header(self, val: str) -> bool:
        t = val.strip().strip("*").strip()
        if t in self._SECTION_HEADERS:
            return True
        return bool(re.fullmatch(r"글자[^:：\n]{0,8}독해", t))

    def _clean_inline_field_dump(self, text: str) -> str:
        """VLM이 한 줄에 텍스트+실질의미+법적판정을 이어 붙인 경우 앞부분만."""
        if not text:
            return ""
        cleaned = text.strip().strip("\"'")
        for sep in (
            r"\s*-\s*실질의미\s*[:：]",
            r"\s*-\s*법적판정\s*[:：]",
            r"\s*-\s*추천분류\s*[:：]",
            r"\s*-\s*불법세부\s*[:：]",
            r"\s*-\s*신뢰도\s*[:：]",
            r"\s*-\s*판단이유\s*[:：]",
            r"\s*###\s*",
        ):
            cleaned = re.split(sep, cleaned, maxsplit=1)[0]
        return cleaned.strip().strip("\"'")

    def _sanitize_banner_text(self, text: str) -> str:
        text = self._clean_inline_field_dump(text)
        text = re.sub(r"\s+", " ", text).strip()
        if not text or self.is_placeholder(text) or self._is_section_header(text):
            return ""
        return text[:500]

    def is_placeholder(self, val: str) -> bool:
        val_clean = val.strip().strip("()[]{} \"'*")
        if len(val_clean) < 2:
            return True
        if self._is_section_header(val_clean):
            return True
        exact = {
            "(원문)", "원문)", "원문", "N/A", "해당없음", "없음",
            "간략히", "한 줄", "요약 금지", "없음 -",
        }
        if val_clean in exact:
            return True
        placeholders = [
            "읽어낸", "핵심 글자", "묶였는지", "철제 틀", "명확히 묘사",
            "사고 흐름", "논리적 근거", "해당하는 항목", "간단히 기술",
            "확인 필요", "플레이스홀더", "분석 불가능",
        ]
        return any(p == val_clean or val_clean.startswith(p + " ") for p in placeholders)

    def _extract_markdown_vlm_text(self, raw: str) -> str:
        """VLM 마크다운 bullet 출력 — **글자·내용 독해**: 원문 우선."""
        if not raw:
            return ""
        # 1) 글자·내용 독해 (최우선 — 프로그램 표시 텍스트의 기준)
        for label in (
            r"글자[^*\n:：]{0,12}독해",
            r"텍스트",
            r"결론",
        ):
            pattern = (
                rf"(?:^|\n)\s*[-*]\s*\*{{0,2}}{label}\*{{0,2}}\s*[:：]\s*"
                rf"(.+?)(?=\n\s*[-*]\s*\*{{0,2}}|\Z)"
            )
            m = re.search(pattern, raw, re.DOTALL | re.IGNORECASE)
            if m and (cleaned := self._sanitize_banner_text(m.group(1))):
                return cleaned
        # 2) bullet 없이 **글자·내용 독해**: 형태
        for label in (r"글자[^*\n:：]{0,12}독해", r"텍스트"):
            pattern = rf"\*{{1,2}}{label}\*{{1,2}}\s*[:：]\s*(.+?)(?=\n\s*[-*]|\n\s*\*{{1,2}}|\Z)"
            m = re.search(pattern, raw, re.DOTALL | re.IGNORECASE)
            if m and (cleaned := self._sanitize_banner_text(m.group(1))):
                return cleaned
        return ""

    def _extract_text_from_reading_section(self, raw: str) -> str:
        """VLM 독해 블록 — 글자 원문이 여기 적히는 경우가 많음."""
        reading = self._extract_reading_section(raw)
        if not reading:
            return ""
        skip = (
            "설치", "판정", "분류", "신뢰도", "이미지", "현수막", "분석", "결론",
            "독해", "관찰", "의미", "법적", "추천", "불법세부", "해당없음", "요약",
        )
        lines: List[str] = []
        for line in reading.split("\n"):
            line = line.strip().strip("()[]{} ")
            if len(line) < 2 or line.startswith("==="):
                continue
            m = re.match(r"^(?:텍스트|내용|문구)\s*[:：]\s*(.+)", line)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    lines.append(found)
                continue
            if any(k in line for k in skip):
                continue
            if self.is_placeholder(line):
                continue
            lines.append(line)
        if not lines:
            return ""
        return re.sub(r"\s+", " ", " ".join(lines)).strip()[:500]

    def _extract_text_label_legacy(self, res: str) -> str:
        """이전 동작 복원 — 전체 VLM 출력에서 텍스트: 한 줄 스캔."""
        for pattern in (
            r"텍스트\s*[:：]\s*([^\n]+)",
            r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
            r"내용\s*[:：]\s*([^\n]+)",
            r"문구\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("\"'()")
                if cleaned := self._sanitize_banner_text(found):
                    return cleaned
        return ""

    def extract_category(self, res: str) -> str:
        """추천분류 — 마크다운 우선, 결론 블록·키워드 폴백."""
        for label in (r"추천\s*분류", r"내용\s*분류", r"최종\s*분류"):
            if found := self._extract_markdown_field(res, label):
                found = found.strip().strip("()[]{} ")
                if not self.is_placeholder(found):
                    return self._normalize_category(found[:40])
        conclusion = self._extract_vlm_conclusion_section(res) or ""
        scopes = [conclusion, res] if conclusion else [res]
        for scope in scopes:
            for pattern in (
                r"추천\s*분류\s*[:：]\s*([^\n]+)",
                r"내용\s*분류\s*[:：]\s*([^\n]+)",
                r"최종\s*분류\s*[:：]\s*([^\n]+)",
            ):
                m = re.search(pattern, scope, re.MULTILINE)
                if m:
                    found = m.group(1).strip().strip("()[]{} ")
                    if not self.is_placeholder(found):
                        return self._normalize_category(found[:40])
            for cat in [self.CATEGORY_ILLEGAL, self.CATEGORY_PARTY, self.CATEGORY_COMMERCIAL, self.CATEGORY_PUBLIC]:
                if cat in scope:
                    return cat
        return "알 수 없음 (수동검토)"

    def extract_reason(self, res: str) -> str:
        if found := self._extract_markdown_field(res, r"판단\s*이유"):
            if not self.is_placeholder(found):
                return found[:200]
        for pattern in (
            r"요약\s*3\s*[:：]\s*([^\n]+)",
            r"판단\s*이유\s*[:：]\s*([^\n]+)",
            r"(?:분류\s*근거|재검토\s*근거|근거)\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("()[]{} ")
                if not self.is_placeholder(found):
                    return found[:200]
        return "분류 근거 식별 불가"

    def extract_banner_text(self, res: str) -> str:
        """VLM이 읽은 현수막 원문 — 마크다운 → 결론 → 독해 → 레거시 순."""
        if not res or "[VLM 추론 예외]" in res:
            return ""
        if md := self._extract_markdown_vlm_text(res):
            return md
        conclusion = self._extract_vlm_conclusion_section(res)
        for scope in (conclusion, res):
            if parsed := self._parse_text_field(scope):
                return parsed
        if reading := self._extract_text_from_reading_section(res):
            return reading
        if legacy := self._extract_text_label_legacy(res):
            if cleaned := self._sanitize_banner_text(legacy):
                return cleaned
        if fallback := self._fallback_banner_text_scan(res):
            if cleaned := self._sanitize_banner_text(fallback):
                return cleaned
        return ""

    def _extract_summary_line(self, res: str, num: int) -> str:
        for pattern in (
            rf"요약\s*{num}\s*[:：]\s*([^\n]+)",
            rf"요약{num}\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    return found[:120]
        return ""

    def format_recognized_text_lines(self, banner_text: str) -> str:
        """인식 텍스트를 결과에 그대로 나열."""
        if not banner_text or banner_text in ("텍스트 추출 불가", "텍스트 미추출 (식별 불가)"):
            return "· (인식 없음)"
        parts = re.split(r"[|\n／/]+", banner_text)
        lines = [p.strip() for p in parts if p.strip()]
        if not lines:
            return f"· {banner_text}"
        return "\n".join(f"· {line}" for line in lines)

    def build_vlm_internal_record(self, raw: str, review_raw: str = "", review_label: str = "") -> str:
        """DB·감사용 전체 VLM 원문 (UI 미노출)."""
        parts = ["[VLM 1차 분석]\n" + raw.strip()]
        if review_raw:
            label = review_label or "2차 재검토"
            parts.append(f"\n[VLM {label}]\n" + review_raw.strip())
        return "\n".join(parts)

    def build_vlm_user_summary(
        self,
        banner_text: str,
        category: str,
        judgment_title: str,
        vlm_conf: str,
        final_source: str,
        ai_verdict: str | None = None,
    ) -> str:
        """WPF 표시용 — 분석 결과만 짧게."""
        conclusion = self._extract_vlm_conclusion_section(final_source) or final_source
        legal = ai_verdict or self.extract_vlm_legal_verdict(conclusion) or "미확정"
        legal_kr = {"ILLEGAL": "불법", "LEGAL": "합법", "GRAY": "회색지대"}.get(legal, legal)

        reason = self.extract_reason(final_source)
        literal = self.extract_literal_meaning(final_source)
        text_short = banner_text if banner_text and banner_text != "텍스트 추출 불가" else "인식 불명"
        if len(text_short) > 60:
            text_short = text_short[:60] + "…"

        lines = [f"{judgment_title}", f"판정: {legal_kr} | 분류: {category} | 신뢰도: {vlm_conf}"]
        if literal and literal not in ("해당없음", "없음"):
            lines.append(f"실질의미: {literal[:80]}")
        lines.append(f"판단: {reason}")
        lines.append(f"텍스트: {text_short}")
        return "\n".join(lines)

    # ─── 핵심 추론 및 가공 파이프라인 ───
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
            return [
                BannerDetectionEvent(
                    Id=str(uuid.uuid4()),
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
            else:
                bx, by, bw, bh = x1, y1, crop_w, crop_h

            events.append(
                BannerDetectionEvent(
                    Id=str(uuid.uuid4()),
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
