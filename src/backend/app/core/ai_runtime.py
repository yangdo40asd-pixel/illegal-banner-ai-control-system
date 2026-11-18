from __future__ import annotations
import asyncio
import threading

_ai_service = None
_ai_load_lock = threading.Lock()
_ai_loading = False
_ai_ready = threading.Event()


def _load_ai_service_sync() -> None:
    global _ai_service, _ai_loading
    with _ai_load_lock:
        if _ai_service is not None:
            _ai_ready.set()
            return
        if _ai_loading:
            return
        _ai_loading = True

    try:
        print("[AI엔진] 모델 로드 시작 (YOLO + VLM)...")
        from app.services.ai_inference_service import AIInferenceService
        service = AIInferenceService()
        with _ai_load_lock:
            _ai_service = service
            _ai_loading = False
        _ai_ready.set()
        print("[AI엔진] 모델 로드 완료")
    except Exception:
        with _ai_load_lock:
            _ai_loading = False
        raise


def start_ai_service_background() -> None:
    if _ai_ready.is_set():
        return
    with _ai_load_lock:
        if _ai_loading or _ai_ready.is_set():
            return
    threading.Thread(
        target=_load_ai_service_sync,
        daemon=True,
        name="ai-model-loader",
    ).start()


def is_ai_service_ready() -> bool:
    return _ai_ready.is_set()


def is_ai_service_loading() -> bool:
    with _ai_load_lock:
        return _ai_loading and not _ai_ready.is_set()


def get_ai_service() -> AIInferenceService:
    if _ai_service is None:
        if not _ai_ready.wait(timeout=600):
            raise TimeoutError("AI 모델 로드 시간 초과 (600초)")
        if _ai_service is None:
            raise RuntimeError("AI 모델 로드 실패")
    return _ai_service


async def wait_for_ai_service() -> None:
    if is_ai_service_ready():
        return
    print("[시뮬레이터] AI 모델 로딩 대기 중...")
    loop = asyncio.get_running_loop()
    await loop.run_in_executor(None, _ai_ready.wait)
    print("[시뮬레이터] AI 모델 준비 완료")
