"""Model construction, separate from text/rule processing."""
import os
import json
import torch
from ultralytics import YOLO
from transformers import AutoModelForImageTextToText, AutoProcessor
from app.core.paths import paths
from app.services.db_service import DatabaseService


def initialize_models(self):
    # 로컬 경로 설정
    self.base_dir = str(paths.root)
    self.db_service = DatabaseService()

    # 1단계: YOLOv26s 모델 로드
    self.model_path = str(paths.resolve("YoloModelPath", "models/yolo/best.pt"))
    if not os.path.exists(self.model_path):
        raise FileNotFoundError(f"현수막 학습 가중치가 없습니다: {self.model_path}")
    print(f"[AI엔진] YOLO 모델 가중치 파일 로딩: {self.model_path}")
    self.yolo = YOLO(self.model_path)
    if self.device == "cuda":
        self.yolo.to("cuda")

    # 3단계: Qwen2.5-VL-3B-Instruct VLM 로드 (GPU VRAM 포화 방지를 위해 4비트 양자화 도입)
    manifest = json.loads(paths.resolve("ModelManifest", "models/manifest.json").read_text(encoding="utf-8"))
    self.vlm_model_id = manifest["vlm"]["id"]
    revision = manifest["vlm"]["revision"]
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
            revision=revision,
            quantization_config=quantization_config,
            device_map="auto",
            trust_remote_code=True
        )
    else:
        self.vlm = AutoModelForImageTextToText.from_pretrained(
            self.vlm_model_id,
            revision=revision,
            torch_dtype=torch.float32,
            device_map="auto",
            trust_remote_code=True
        )
    self.processor = AutoProcessor.from_pretrained(self.vlm_model_id, revision=revision, trust_remote_code=True)
