import os
import uuid
import torch
import threading
from app.core.paths import paths

# OpenMP duplicate warning environment variable
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

from app.inference.model_loader import initialize_models
from app.inference.rules import configure_rules
from app.inference.parsers import TextAnalysisMixin
from app.inference.vlm_analyzer import VlmMixin
from app.inference.yolo_detector import YoloPreviewMixin
from app.inference.pipeline import InferencePipelineMixin
from app.inference.event_mapper import EventMapperMixin

class AIInferenceService(TextAnalysisMixin, VlmMixin, YoloPreviewMixin, InferencePipelineMixin, EventMapperMixin):
    """YOLO26s 탐지와 Qwen2.5-VL 문자·맥락 분석을 조정하는 서비스입니다.
    """
    def __init__(self):
        self.device = self._resolve_device()
        self._configure_gpu()

        initialize_models(self)
        configure_rules(self)

        # 임시 이미지 경로 설정
        self.script_dir = str(paths.runtime_directory("temp"))
        session_id = uuid.uuid4().hex
        self.temp_context = os.path.join(self.script_dir, f"context_{session_id}.jpg")
        self.temp_zoom = os.path.join(self.script_dir, f"zoom_{session_id}.jpg")
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
