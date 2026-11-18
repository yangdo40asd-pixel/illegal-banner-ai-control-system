from __future__ import annotations
import os
import torch
from typing import Optional
from PIL import Image
from qwen_vl_utils import process_vision_info

class VlmMixin:
    def _get_vlm_device(self):
        try:
            return next(self.vlm.parameters()).device
        except StopIteration:
            return torch.device(self.device)

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
