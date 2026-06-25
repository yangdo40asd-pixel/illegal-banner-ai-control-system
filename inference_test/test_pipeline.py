"""
YOLO + Qwen2-VL 불법 현수막 판독 파이프라인 (v2.0 완전 재작성)
=======================================================
실행 방법:
  1. test_images/ 폴더에 테스트할 현수막 이미지를 넣는다.
  2. python test_pipeline.py 로 실행한다.
  3. results_expert/ 폴더에서 결과 이미지를 확인한다.

주요 변경 사항 (v2.0):
  - 미사용 임포트(cv2, numpy) 제거
  - 모델 로드를 main 블록 안으로 이동
  - 불법 판정: "불법" 단순 포함 검사 → "최종판정: 불법" 키워드 정확 매칭으로 수정
  - 텍스트 추출: 단일 정규식 → AI 응답 형식 변동에 강한 다중 패턴 대응
  - 라벨 박스: 고정 500px → 텍스트 길이에 맞는 동적 너비로 수정
  - 에러 핸들링: 한 장 실패해도 전체 루프가 멈추지 않도록 try/except 추가
  - 임시파일: 상대경로 → 스크립트 디렉토리 기준 절대경로로 수정
"""

import os
import sys
import re
import torch
from PIL import Image, ImageDraw, ImageFont
from datetime import datetime
from ultralytics import YOLO
from transformers import AutoModelForImageTextToText, AutoProcessor
from qwen_vl_utils import process_vision_info

# ── Windows 필수 환경변수 ─────────────────────────────
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'
sys.stdout.reconfigure(encoding='utf-8')

# ══════════════════════════════════════════════════════
# 경로 설정
# ══════════════════════════════════════════════════════
BASE_DIR      = r"D:\138.종합 민원 이미지 AI데이터"
YOLO_MODEL    = os.path.join(BASE_DIR, r"yolo26s\runs\banner_s_20260506_1909\weights\best.pt")
VLM_MODEL_ID  = "Qwen/Qwen2.5-VL-3B-Instruct"  # 3B: 10GB VRAM에서 양자화 없이 안정 구동
TEST_IMG_DIR  = os.path.join(BASE_DIR, "inference_test", "test_images")
RESULTS_DIR   = os.path.join(BASE_DIR, "inference_test", "results_qwen25")

# 임시파일은 스크립트 디렉토리 기준 절대경로로 고정 (상대경로 오류 방지)
SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
TEMP_CONTEXT  = os.path.join(SCRIPT_DIR, "_temp_context.jpg")
TEMP_ZOOM     = os.path.join(SCRIPT_DIR, "_temp_zoom.jpg")


# ══════════════════════════════════════════════════════
# 유틸리티 함수
# ══════════════════════════════════════════════════════
def get_contextual_crop(image: Image.Image, box: tuple, padding_ratio: float = 0.6) -> Image.Image:
    """현수막 주변 배경(전봇대, 끈 등)이 보이도록 넉넉하게 자릅니다."""
    img_w, img_h = image.size
    x1, y1, x2, y2 = box
    pad = int(max(x2 - x1, y2 - y1) * padding_ratio)
    return image.crop((
        max(0, x1 - pad),
        max(0, y1 - pad),
        min(img_w, x2 + pad),
        min(img_h, y2 + pad)
    ))


def is_illegal(text: str) -> bool:
    """
    AI 응답에서 최종 판정이 '불법'인지 정확하게 판단합니다.

    [수정 이유]
    기존: "불법" in text  → "불법이 아닙니다" 같은 부정문에도 True 반환하는 치명적 오류 존재.
    개선: "최종판정: 불법" 같이 판정 키워드가 명시적으로 포함된 경우에만 불법으로 처리.
          "최종판정: 합법" 이 있으면 무조건 합법으로 처리.
    """
    # 합법 키워드가 명시적으로 있으면 → 합법 우선 처리
    if re.search(r"(최종\s*판정|판정)\s*[:：]\s*합법", text):
        return False
    # 불법 키워드가 명시적으로 있으면 → 불법
    if re.search(r"(최종\s*판정|판정)\s*[:：]\s*불법", text):
        return True
    # 위 두 패턴 모두 못 찾으면 → 판단 불가(안전하게 합법 처리)
    return False


def extract_text_label(res: str) -> str:
    """
    AI 응답에서 현수막 텍스트 내용을 추출합니다.
    다중 패턴 + 4단계 fallback 강화 버전
    """
    # 1순위: "텍스트:" 계열 키워드 뒤 내용
    patterns = [
        r"텍스트\s*[:：]\s*([^\n]+)",
        r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
        r"내용\s*[:：]\s*([^\n]+)",
        r"문구\s*[:：]\s*([^\n]+)",
    ]
    for pattern in patterns:
        match = re.search(pattern, res, re.MULTILINE)
        if match:
            found = match.group(1).strip().strip("\"'")
            # 괄호 안 설명문(예시 지시어)이면 무시
            if len(found) > 2 and not found.startswith('(') and not found.startswith('['):
                return found[:45]

    # 2순위: 따옴표로 감싸진 텍스트
    quote_match = re.search(r'[\'"\u201c\u2018](.*?)[\'"\u201d\u2019]', res)
    if quote_match:
        found = quote_match.group(1).strip()
        if 2 < len(found) < 60:
            return found[:45]

    # 3순위: **굵게** 강조된 텍스트 (마크다운)
    bold_match = re.search(r'\*\*(.*?)\*\*', res)
    if bold_match:
        found = bold_match.group(1).strip()
        if 2 < len(found) < 60:
            return found[:45]

    # 4순위: 판정 관련 키워드를 제외한 첫 번째 의미 있는 줄
    skip_keywords = ['설치환경', '최종판정', '판정근거', '이미지', '현수막이', '보고서', '분석']
    for line in res.split('\n'):
        line = line.strip()
        if len(line) > 4 and not any(k in line for k in skip_keywords):
            return line[:45]

    return "텍스트 미추출"


def run_vlm_analysis(
    context_image: Image.Image,
    zoom_image: Image.Image,
    processor,
    vlm,
    device: str
) -> str:
    """배경 이미지 + 확대 이미지 두 장을 VLM에 보내어 전문가 판독 결과를 반환합니다."""
    # 확대 이미지 고해상도 업스케일 (가독성 향상)
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
                "두 장의 이미지를 분석해 주세요.\n"
                "첫 번째는 현수막 주변 배경 사진, 두 번째는 현수막을 확대한 사진입니다.\n\n"
                "반드시 아래 [출력 형식]을 그대로 따라서 답해주세요.\n\n"
                "[출력 형식]\n"
                "텍스트: (현수막에 적힌 글자)\n"
                "설치환경: (배경 이미지에서 확인한 설치 방식)\n"
                "최종판정: 불법\n"
                "판정근거: (이유 한 문장)\n\n"
                "[예시 답변]\n"
                "텍스트: 봄맞이 빅세일 50% 할인\n"
                "설치환경: 가로수에 흰색 끈으로 묶여 설치되어 있음\n"
                "최종판정: 불법\n"
                "판정근거: 가로수에 끈으로 고정한 것은 정식 금속 게시대 설치가 아니므로 불법입니다.\n\n"
                "[판단 기준]\n"
                "- 불법: 나무·전봇대·신호등·가로등·울타리 등에 끈으로 묶어 설치한 경우\n"
                "- 합법: 시/구청 지정 금속 게시대 틀 안에 고정 설치된 경우\n"
            )}
        ]
    }]

    text = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    image_inputs, _ = process_vision_info(messages)
    inputs = processor(
        text=[text], images=image_inputs, padding=True, return_tensors="pt"
    ).to(device)

    with torch.no_grad():
        gen_ids = vlm.generate(**inputs, max_new_tokens=200)  # 300→200: 속도 최적화
        # 입력 프롬프트 길이만큼 잘라내고 생성된 부분만 디코딩
        output = processor.batch_decode(
            gen_ids[:, inputs.input_ids.shape[1]:],
            skip_special_tokens=True
        )[0]

    # 임시파일 정리
    for path in [TEMP_CONTEXT, TEMP_ZOOM]:
        if os.path.exists(path):
            os.remove(path)

    return output


def process_image(img_path: str, processor, vlm, yolo, device: str) -> None:
    """이미지 1장에 대해 YOLO 탐지 → VLM 판독 → 결과 이미지 저장을 수행합니다."""
    os.makedirs(RESULTS_DIR, exist_ok=True)
    filename = os.path.basename(img_path)

    print(f"\n{'='*60}")
    print(f"[이미지] {filename}")

    try:
        pil_img = Image.open(img_path).convert("RGB")
    except Exception as e:
        print(f"  [오류] 이미지 열기 실패: {e}")
        return

    # ── YOLO 탐지 ──────────────────────────────────────────
    yolo_results = yolo(img_path, verbose=False)
    boxes = yolo_results[0].boxes

    if len(boxes) == 0:
        print("  [YOLO] 현수막 미탐지 → 건너뜀")
        return

    print(f"  [YOLO] 현수막 {len(boxes)}개 탐지")

    draw = ImageDraw.Draw(pil_img)
    try:
        font = ImageFont.truetype("malgun.ttf", 20)
    except OSError:
        font = ImageFont.load_default()

    # ── 각 박스별 처리 ─────────────────────────────────────
    for i, box in enumerate(boxes.xyxy):
        x1, y1, x2, y2 = map(int, box.tolist())

        context_crop = get_contextual_crop(pil_img, (x1, y1, x2, y2))
        zoom_crop = pil_img.crop((x1, y1, x2, y2))

        print(f"  [VLM] 박스 {i+1}/{len(boxes.xyxy)} 판독 중...")

        try:
            res = run_vlm_analysis(context_crop, zoom_crop, processor, vlm, device)
        except Exception as e:
            print(f"  [오류] VLM 판독 실패: {e}")
            continue

        print(f"  [판독 결과]\n{res}\n")

        # 판정: "최종판정: 불법/합법" 명시 키워드 기반 정확 매칭
        illegal = is_illegal(res)
        color = "#FF4444" if illegal else "#44FF44"
        label_verdict = "불법" if illegal else "합법"

        # 텍스트 내용 추출 (다중 패턴)
        label_text = extract_text_label(res)
        label_full = f"[{label_verdict}] {label_text}"

        # 바운딩 박스 그리기
        draw.rectangle([x1, y1, x2, y2], outline=color, width=5)

        # 라벨 박스: 텍스트 길이에 맞게 동적으로 너비 조정
        try:
            text_bbox = font.getbbox(label_full)
            label_w = text_bbox[2] - text_bbox[0] + 16
        except AttributeError:
            label_w = len(label_full) * 12  # 폰트 미지원 시 추정값

        label_y_top = max(0, y1 - 35)
        draw.rectangle([x1, label_y_top, x1 + label_w, y1], fill=color)
        draw.text((x1 + 5, label_y_top + 4), label_full, fill="white", font=font)

    # ── 결과 저장 ───────────────────────────────────────────
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    save_name = f"{os.path.splitext(filename)[0]}_{timestamp}_result.jpg"
    save_path = os.path.join(RESULTS_DIR, save_name)
    pil_img.save(save_path, quality=95)
    print(f"  [저장] {save_path}")


# ══════════════════════════════════════════════════════
# 메인 실행부 (모델 로드는 여기서만 실행)
# ══════════════════════════════════════════════════════
if __name__ == "__main__":
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"[설정] Device  : {device}")
    print(f"[설정] YOLO    : {YOLO_MODEL}")
    print(f"[설정] VLM     : {VLM_MODEL_ID}")
    print(f"[설정] 결과 폴더: {RESULTS_DIR}\n")

    # 모델 로드 (전역 노출 없이 main 블록 안에서만 초기화)
    print("[로드] YOLO 모델 로딩 중...")
    yolo = YOLO(YOLO_MODEL)

    print(f"[로드] {VLM_MODEL_ID} 로딩 중...")
    vlm = AutoModelForImageTextToText.from_pretrained(
        VLM_MODEL_ID,
        torch_dtype=torch.bfloat16 if device == "cuda" else torch.float32,
        device_map="auto",
        trust_remote_code=True
    )
    processor = AutoProcessor.from_pretrained(VLM_MODEL_ID, trust_remote_code=True)
    print("[로드] 모든 모델 로드 완료!\n")

    # 테스트 이미지 목록 수집
    if not os.path.exists(TEST_IMG_DIR):
        print(f"[오류] 테스트 이미지 폴더 없음: {TEST_IMG_DIR}")
        sys.exit(1)

    image_files = sorted([
        os.path.join(TEST_IMG_DIR, f)
        for f in os.listdir(TEST_IMG_DIR)
        if f.lower().endswith(('.png', '.jpg', '.jpeg'))
    ])

    if not image_files:
        print("[오류] 테스트 이미지가 없습니다.")
        sys.exit(1)

    image_files = image_files[:5]  # 테스트: 5장만 처리
    print(f"[시작] {len(image_files)}장 분석 시작 (5장 제한)...\n")

    success, fail = 0, 0
    for img_path in image_files:
        try:
            process_image(img_path, processor, vlm, yolo, device)
            success += 1
        except Exception as e:
            print(f"  [오류] {os.path.basename(img_path)} 처리 실패: {e}")
            fail += 1

    print(f"\n{'='*60}")
    print(f"[완료] 성공: {success}장 / 실패: {fail}장")
    print(f"[완료] 결과 폴더: {RESULTS_DIR}")
    print(f"{'='*60}")
