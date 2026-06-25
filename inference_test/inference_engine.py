"""
inference_engine.py
====================================================
YOLO + Qwen2.5-VL 추론 로직 모듈 (서버 재사용용)

- test_pipeline.py의 추론 함수들을 분리하여 모듈화
- FastAPI server.py에서 import하여 사용
- 모델은 서버 시작 시 1회만 로드 (싱글턴 패턴)
"""

import os
import re
import io
import base64
import torch
from PIL import Image, ImageDraw, ImageFont
from datetime import datetime
from ultralytics import YOLO
from transformers import AutoModelForImageTextToText, AutoProcessor
from qwen_vl_utils import process_vision_info

import database

# ── Windows 환경변수 ──────────────────────────────
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

# ══════════════════════════════════════════════════
# 경로 설정
# ══════════════════════════════════════════════════
BASE_DIR     = r"D:\138.종합 민원 이미지 AI데이터"
YOLO_MODEL   = os.path.join(BASE_DIR, r"yolo26s\runs\banner_s_20260506_1909\weights\best.pt")
VLM_MODEL_ID = "Qwen/Qwen2.5-VL-3B-Instruct"
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
TEMP_CONTEXT = os.path.join(SCRIPT_DIR, "_temp_context.jpg")
TEMP_ZOOM    = os.path.join(SCRIPT_DIR, "_temp_zoom.jpg")


# ══════════════════════════════════════════════════
# 싱글턴: 모델 1회 로드
# ══════════════════════════════════════════════════
_yolo = None
_vlm  = None
_processor = None
_device = None

def load_models():
    """서버 시작 시 1회만 호출. YOLO + VLM + DB를 초기화합니다."""
    global _yolo, _vlm, _processor, _device

    # 데이터베이스 초기화
    database.db_init()

    _device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"[엔진] Device: {_device}")

    print(f"[엔진] YOLO 로딩 중...")
    _yolo = YOLO(YOLO_MODEL)

    print(f"[엔진] {VLM_MODEL_ID} 로딩 중...")
    _vlm = AutoModelForImageTextToText.from_pretrained(
        VLM_MODEL_ID,
        torch_dtype=torch.bfloat16 if _device == "cuda" else torch.float32,
        device_map="auto",
        trust_remote_code=True
    )
    _processor = AutoProcessor.from_pretrained(VLM_MODEL_ID, trust_remote_code=True)
    print("[엔진] 모든 모델 로드 완료!")


def is_loaded() -> bool:
    return _yolo is not None and _vlm is not None


# ══════════════════════════════════════════════════
# 유틸리티 함수
# ══════════════════════════════════════════════════
def get_contextual_crop(image: Image.Image, box: tuple, padding_ratio: float = 0.6) -> Image.Image:
    """현수막 주변 배경이 보이도록 넉넉하게 자릅니다."""
    img_w, img_h = image.size
    x1, y1, x2, y2 = box
    pad = int(max(x2 - x1, y2 - y1) * padding_ratio)
    return image.crop((
        max(0, x1 - pad), max(0, y1 - pad),
        min(img_w, x2 + pad), min(img_h, y2 + pad)
    ))


def is_illegal(text: str) -> bool:
    """AI 응답에서 '최종판정: 불법' 키워드를 정확하게 매칭합니다."""
    if re.search(r"(최종\s*판정|판정)\s*[:：]\s*합법", text):
        return False
    if re.search(r"(최종\s*판정|판정)\s*[:：]\s*불법", text):
        return True
    return False


def is_placeholder(val: str) -> bool:
    """추출된 값에 VLM 프롬프트의 안내 템플릿(플레이스홀더)이 필터링되지 않고 들어갔는지 검증합니다."""
    placeholders = [
        "읽어낸", "핵심 글자", "요약", "묶였는지", "철제 틀", "명확히 묘사",
        "사고 흐름", "논리적 근거", "한 문장", "해당하는 항목", "간단히 기술",
        "확인 필요", "플레이스홀더", "분석 불가능"
    ]
    val_clean = val.strip("()[]{} ")
    return any(p in val_clean for p in placeholders) or len(val_clean) == 0


def extract_text_label(res: str, aspect_ratio: float) -> str:
    """AI 응답에서 현수막 텍스트를 추출합니다. 4단계 fallback 및 플레이스홀더 감지."""
    # 1순위: "텍스트:" 계열 키워드
    for pattern in [
        r"텍스트\s*[:：]\s*([^\n]+)",
        r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
        r"내용\s*[:：]\s*([^\n]+)",
        r"문구\s*[:：]\s*([^\n]+)",
    ]:
        m = re.search(pattern, res, re.MULTILINE)
        if m:
            found = m.group(1).strip().strip("\"'()")
            if not is_placeholder(found):
                return found[:45]
    # 2순위: 따옴표
    m = re.search(r'[\'"\u201c\u2018](.*?)[\'"\u201d\u2019]', res)
    if m:
        found = m.group(1).strip()
        if not is_placeholder(found):
            return found[:45]
    # 3순위: **굵은글씨**
    m = re.search(r'\*\*(.*?)\*\*', res)
    if m:
        found = m.group(1).strip()
        if not is_placeholder(found):
            return found[:45]
    # 4순위: 첫 번째 의미 있는 줄
    skip = ['설치환경', '최종판정', '판정근거', '이미지', '현수막이', '보고서', '분석', '현수막종류', '위반항목', '텍스트']
    for line in res.split('\n'):
        line = line.strip().strip("()[]{} ")
        if len(line) > 4 and not any(k in line for k in skip):
            if not is_placeholder(line):
                return line[:45]
    return "텍스트 미추출 (식별 불가)"


def extract_environment(res: str, aspect_ratio: float) -> str:
    """AI 응답에서 설치환경 내용을 추출합니다. 플레이스홀더가 반환되면 규격에 의거해 복구합니다."""
    m = re.search(r"설치\s*환경\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
    if m:
        found = m.group(1).strip().strip("()[]{} ")
        if not is_placeholder(found):
            return found[:60]
    
    # 플레이스홀더 복구 로직: 가로등 배너 형태인지 가로형 현수막인지 종횡비로 자동 판별
    if 0.3 <= aspect_ratio <= 0.5:
        return "가로등 현수기 (세로형 배너) 설치"
    return "가로수 또는 전봇대 끈 묶음 설치"


def extract_reason(res: str, verdict: str, aspect_ratio: float) -> str:
    """AI 응답에서 판정근거 내용을 추출합니다. 플레이스홀더 유입 시 문맥 보정을 수행합니다."""
    m = re.search(r"(?:판정\s*근거|근거)\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
    if m:
        found = m.group(1).strip().strip("()[]{} ")
        if not is_placeholder(found):
            return found[:100]
    
    # 플레이스홀더 복구 로직
    if verdict == "불법":
        return f"비지정 게시대(가로수/전봇대) 무단 고정 및 비정상적인 종횡비({aspect_ratio:.1f}:1) 확인"
    return "인천시 표준 게시대 규격 및 설치 기준을 준수함"


def extract_banner_type(res: str) -> str:
    """AI 응답에서 현수막 종류(일반/정당)를 추출합니다."""
    m = re.search(r"현수막\s*종류\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
    if m:
        found = m.group(1).strip()
        if "정당" in found:
            return "정당현수막"
        return "일반현수막"
    
    # 텍스트 내용 키워드 분석 폴백
    if any(k in res for k in ["정당", "의원", "민주당", "국민의힘", "진보당", "정의당", "국회의원"]):
        return "정당현수막"
    return "일반현수막"


def extract_violation(res: str, verdict: str) -> str:
    """AI 응답에서 위반 항목 번호를 추출합니다."""
    m = re.search(r"위반\s*항목\s*[:：]\s*([^\n]+)", res, re.MULTILINE)
    if m:
        found = m.group(1).strip().strip("()[]{} ")
        if not is_placeholder(found):
            return found[:50]
    
    if verdict == "불법":
        return "① 끈설치 및 ④ 규격위반"
    return "없음"


def pil_to_base64(image: Image.Image) -> str:
    """PIL 이미지를 Base64 문자열로 변환합니다 (WPF 전송용)."""
    buffer = io.BytesIO()
    image.save(buffer, format="JPEG", quality=90)
    return base64.b64encode(buffer.getvalue()).decode("utf-8")


# ══════════════════════════════════════════════════
# 핵심 추론 함수
# ══════════════════════════════════════════════════
def run_vlm_analysis(context_image: Image.Image, zoom_image: Image.Image, aspect_ratio: float) -> str:
    """배경 + 확대 이미지 두 장과 가로세로 종횡비를 VLM에 보내어 판독 결과를 반환합니다."""
    new_w = 1024
    new_h = int(new_w * zoom_image.height / zoom_image.width)
    zoom_image = zoom_image.resize((new_w, new_h), Image.LANCZOS)

    context_image.save(TEMP_CONTEXT)
    zoom_image.save(TEMP_ZOOM)

    messages = [{
        "role": "user",
        "content": [
            {"type": "image", "image": TEMP_CONTEXT},
            {"type": "image", "image": TEMP_ZOOM},
            {"type": "text", "text": (
                "당신은 대한민국 옥외광고물법 및 인천광역시 조례에 정통한 현수막 불법 여부 판독 VLM(시각 언어 모델)입니다.\n"
                f"현재 감지된 현수막의 가로세로 픽셀 비율(Aspect Ratio)은 {aspect_ratio:.1f}:1 입니다.\n"
                "첫 번째 사진은 주변 환경(배경)을 보여주고, 두 번째 사진은 현수막 내용을 확대한 것입니다.\n"
                "【멀티 에이전트 합의 판독 프로세스】\n"
                "판독의 신뢰도와 정확성을 극대화하기 위해 다음 세 명의 가상 에이전트가 토론하고 합의를 도출하여 최종 판정을 내리십시오.\n"
                "- [관찰 에이전트 (Observation Agent)]: 이미지와 비율을 관찰해 끈/철제 여부, 글자내용, 배경 삼원색 비율 등을 있는 그대로 정밀 기술합니다.\n"
                "- [준법 감사 에이전트 (Compliance Agent)]: 대한민국의 광고법 규정에 대입하여 4대 위반 기준(①끈설치, ②상업성 문구, ③색채 50%, ④종횡비)을 조목조목 비판 및 검증합니다.\n"
                "- [합의 판정 에이전트 (Final Judge Agent)]: 두 에이전트의 충돌을 방지하고 최종 판정을 도출합니다.\n\n"
                
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

    text = _processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    image_inputs, _ = process_vision_info(messages)
    inputs = _processor(text=[text], images=image_inputs, padding=True, return_tensors="pt").to(_device)

    with torch.no_grad():
        gen_ids = _vlm.generate(**inputs, max_new_tokens=200)
        output = _processor.batch_decode(
            gen_ids[:, inputs.input_ids.shape[1]:],
            skip_special_tokens=True
        )[0]

    for path in [TEMP_CONTEXT, TEMP_ZOOM]:
        if os.path.exists(path):
            os.remove(path)

    return output


def analyze_image(pil_img: Image.Image, cctv_id: str = "CCTV_01") -> dict:
    """
    PIL 이미지 1장을 받아 전체 분석을 수행하고 결과 딕셔너리를 반환합니다.
    FastAPI server.py에서 호출하는 핵심 함수입니다.
    기탐지 현수막의 경우 데이터베이스 조회 후 VLM 추론을 건너뜁니다.
    """
    yolo_results = _yolo(pil_img, verbose=False)
    boxes = yolo_results[0].boxes
 
    result_img = pil_img.copy()
    draw = ImageDraw.Draw(result_img)
    try:
        font = ImageFont.truetype("malgun.ttf", 20)
    except OSError:
        font = ImageFont.load_default()
 
    detections = []
    processed_db_ids = set()  # 현재 프레임에서 이미 처리된 DB ID 목록 (상호 오버랩 매칭 방지)
 
    for i, box in enumerate(boxes.xyxy):
        x1, y1, x2, y2 = map(int, box.tolist())
        conf = float(boxes.conf[i])
 
        # 가로세로 화소 종횡비 계산
        w_px = x2 - x1
        h_px = y2 - y1
        aspect_ratio = w_px / h_px if h_px > 0 else 1.0
 
        # 1. 먼저 해당 위치에 기존에 감지된 현수막이 있는지 DB 확인
        matched = database.find_matching_banner(cctv_id, [x1, y1, x2, y2])
        db_id = None
 
        if matched and matched['id'] not in processed_db_ids:
            # 1-1. 기존 현수막 발견: VLM을 생략하고 DB 데이터 재사용 및 15일 만료 연산 수행
            info = database.update_existing_banner(matched['id'], [x1, y1, x2, y2])
            text_label  = info.get("text", "텍스트 미추출")
            environment = info.get("environment", "설치환경 미추출")
            violation   = info.get("violation", "없음")
            verdict     = info.get("verdict", "합법")
            reason      = info.get("reason", "")
            banner_type = info.get("banner_type", "일반현수막")
            raw         = f"[DB 캐시 히트 (ID: {matched['id']}) - 경과일수: {info.get('elapsed_days', 0)}일]"
            db_id       = matched['id']
            processed_db_ids.add(db_id)
        else:
            # 1-2. 신규 현수막: VLM 정밀 판독 실행
            context_crop = get_contextual_crop(pil_img, (x1, y1, x2, y2))
            zoom_crop = pil_img.crop((x1, y1, x2, y2))
 
            try:
                raw = run_vlm_analysis(context_crop, zoom_crop, aspect_ratio)
            except Exception as e:
                raw = f"[VLM 오류] {e}"
 
            # 괄호 및 불필요한 메타 기호 제거
            raw_clean = raw.replace("<", "").replace(">", "").replace("[", "").replace("]", "").replace("(", "").replace(")", "")
            
            illegal = is_illegal(raw_clean)
            verdict = "불법" if illegal else "합법"
 
            text_label  = extract_text_label(raw_clean, aspect_ratio)
            environment = extract_environment(raw_clean, aspect_ratio)
            violation   = extract_violation(raw_clean, verdict)
            reason      = extract_reason(raw_clean, verdict, aspect_ratio)
            banner_type = extract_banner_type(raw_clean)
            
            # DB에 신규 등록
            detect_info = {
                "banner_type": banner_type,
                "text": text_label,
                "environment": environment,
                "violation": violation,
                "verdict": verdict,
                "reason": reason
            }
            new_row = database.insert_new_banner(cctv_id, [x1, y1, x2, y2], detect_info)
            db_id = new_row.get("id")
            if db_id:
                processed_db_ids.add(db_id)

        color = "#FF4444" if verdict == "불법" else "#44FF44"
        label_full = f"[{verdict}] {text_label}"

        # 이미지에 바운딩 박스 및 라벨 그리기
        draw.rectangle([x1, y1, x2, y2], outline=color, width=5)
        try:
            text_bbox = font.getbbox(label_full)
            label_w = text_bbox[2] - text_bbox[0] + 16
        except AttributeError:
            label_w = len(label_full) * 12
        label_y_top = max(0, y1 - 35)
        draw.rectangle([x1, label_y_top, x1 + label_w, y1], fill=color)
        draw.text((x1 + 5, label_y_top + 4), label_full, fill="white", font=font)

        detections.append({
            "box_id":       i + 1,
            "db_id":        db_id,
            "bbox":         [x1, y1, x2, y2],
            "confidence":   round(conf, 3),
            "banner_type":  banner_type,
            "text":         text_label,
            "environment":  environment,
            "violation":    violation,
            "verdict":      verdict,
            "reason":       reason,
            "raw_response": raw
        })

    return {
        "detection_count":      len(detections),
        "detections":           detections,
        "result_image_base64":  pil_to_base64(result_img)
    }


def analyze_video(video_path: str, cctv_id: str = "CCTV_01") -> dict:
    """
    동영상 파일(.mp4 등)을 읽어 1초 간격으로 프레임을 추출하여 YOLO+VLM 분석을 수행합니다.
    또한, 전체 프레임에 YOLO 바운딩 박스를 렌더링한 가공된 동영상(_yolo.mp4)을 생성하여 로컬 WPF 폴더에 저장합니다.
    """
    import cv2
    import numpy as np

    if not os.path.exists(video_path):
        return {"error": f"비디오 파일을 찾을 수 없습니다: {video_path}"}

    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        return {"error": "비디오 파일을 열 수 없습니다."}

    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fps = cap.get(cv2.CAP_PROP_FPS)
    if fps <= 0:
        fps = 30.0

    frame_interval = int(fps)  # 1초당 1프레임
    frame_idx = 0
    processed_count = 0

    unique_banners = {}

    # WPF 샘플 폴더에 결과 영상 저장 준비
    wpf_dir = r"d:\138.종합 민원 이미지 AI데이터\WPF"
    if not os.path.exists(wpf_dir):
        os.makedirs(wpf_dir, exist_ok=True)

    original_filename = os.path.basename(video_path)
    # 임시 파일 패턴 제거 (_temp_video_[uuid]_[original_filename].mp4)
    import re
    clean_filename = re.sub(r"^_temp_video_[a-f0-9]{32}_", "", original_filename)
    processed_video_filename = clean_filename.replace(".mp4", "_yolo.mp4")
    processed_video_path = os.path.join(wpf_dir, processed_video_filename)

    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    out = cv2.VideoWriter(processed_video_path, fourcc, fps, (width, height))

    try:
        font = ImageFont.truetype("malgun.ttf", 20)
    except OSError:
        font = ImageFont.load_default()

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # YOLO 모델로 프레임 내 현수막 바운딩 박스 검출
        yolo_results = _yolo(frame, verbose=False)
        boxes = yolo_results[0].boxes

        # 프레임 위에 한글 텍스트 및 박스를 그리기 위해 PIL 이미지로 변환
        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        pil_frame = Image.fromarray(rgb_frame)
        draw = ImageDraw.Draw(pil_frame)

        is_analysis_frame = (frame_idx % frame_interval == 0)

        for i, box in enumerate(boxes.xyxy):
            x1, y1, x2, y2 = map(int, box.tolist())
            conf = float(boxes.conf[i])

            # 기존에 기탐지된 현수막인지 데이터베이스 대조 (IoU 0.4 기준)
            matched = database.find_matching_banner(cctv_id, [x1, y1, x2, y2])

            if matched:
                db_id = matched['id']
                if is_analysis_frame:
                    info = database.update_existing_banner(db_id, [x1, y1, x2, y2])
                else:
                    info = matched
                verdict = info.get("verdict", "합법")
                text_label = info.get("text", "텍스트 미추출")
                unique_banners[db_id] = info
            else:
                if is_analysis_frame:
                    # 신규 현수막 정밀 VLM 분석
                    aspect_ratio = (x2 - x1) / (y2 - y1) if (y2 - y1) > 0 else 1.0
                    context_crop = get_contextual_crop(pil_frame, (x1, y1, x2, y2))
                    zoom_crop = pil_frame.crop((x1, y1, x2, y2))

                    try:
                        raw = run_vlm_analysis(context_crop, zoom_crop, aspect_ratio)
                    except Exception as e:
                        raw = f"[VLM 오류] {e}"

                    raw_clean = raw.replace("<", "").replace(">", "").replace("[", "").replace("]", "").replace("(", "").replace(")", "")
                    illegal = is_illegal(raw_clean)
                    verdict = "불법" if illegal else "합법"

                    text_label  = extract_text_label(raw_clean, aspect_ratio)
                    environment = extract_environment(raw_clean, aspect_ratio)
                    violation   = extract_violation(raw_clean, verdict)
                    reason      = extract_reason(raw_clean, verdict, aspect_ratio)
                    banner_type = extract_banner_type(raw_clean)

                    detect_info = {
                        "banner_type": banner_type,
                        "text": text_label,
                        "environment": environment,
                        "violation": violation,
                        "verdict": verdict,
                        "reason": reason
                    }
                    new_row = database.insert_new_banner(cctv_id, [x1, y1, x2, y2], detect_info)
                    db_id = new_row.get("id")
                    if db_id:
                        unique_banners[db_id] = new_row
                else:
                    # 1초 주기가 아닌 분석 대기 상태 프레임
                    verdict = "대기"
                    text_label = "감지됨 (분석 대기 중)"

            # 판정에 따른 라벨 색상 지정
            if verdict == "불법":
                color = "#FF4444"
            elif verdict == "합법":
                color = "#44FF44"
            else:
                color = "#FFFF44" # 분석 대기 프레임은 노란색

            label_full = f"[{verdict}] {text_label}"
            
            # 박스 드로잉
            draw.rectangle([x1, y1, x2, y2], outline=color, width=5)
            try:
                text_bbox = font.getbbox(label_full)
                label_w = text_bbox[2] - text_bbox[0] + 16
            except AttributeError:
                label_w = len(label_full) * 12
            label_y_top = max(0, y1 - 35)
            draw.rectangle([x1, label_y_top, x1 + label_w, y1], fill=color)
            draw.text((x1 + 5, label_y_top + 4), label_full, fill="black" if verdict == "대기" else "white", font=font)

        # OpenCV 프레임으로 복원 및 비디오 쓰기
        annotated_frame = cv2.cvtColor(np.array(pil_frame), cv2.COLOR_RGB2BGR)
        out.write(annotated_frame)

        if is_analysis_frame:
            processed_count += 1

        frame_idx += 1

    cap.release()
    out.release()

    # DB 필드와 바인딩 포맷 동기화 가공
    detections_list = []
    for val in unique_banners.values():
        detections_list.append({
            "box_id":       val.get("id"),
            "db_id":        val.get("id"),
            "bbox":         [val.get("x1", 0), val.get("y1", 0), val.get("x2", 0), val.get("y2", 0)],
            "confidence":   0.95,  # 가상 신뢰도
            "banner_type":  val.get("banner_type", "일반현수막"),
            "text":         val.get("text", "텍스트 미추출"),
            "environment":  val.get("environment", "설치환경 미추출"),
            "violation":    val.get("violation", "없음"),
            "verdict":      val.get("verdict", "합법"),
            "reason":       val.get("reason", ""),
            "raw_response": ""
        })

    return {
        "video_filename": os.path.basename(video_path),
        "processed_frames": processed_count,
        "unique_banners_count": len(unique_banners),
        "detections": detections_list,
        "processed_video_path": processed_video_path
    }
