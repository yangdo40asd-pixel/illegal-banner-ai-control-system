import os
import glob
import uuid
import base64
import asyncio
import threading
import tempfile
from datetime import datetime
import cv2
import numpy as np
from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from typing import List, Tuple
from app.services.ai_inference_service import AIInferenceService
from app.schemas.detection_schema import BannerDetectionEvent, ChannelDetectionBatch, BBox

router = APIRouter()

class ConnectionManager:
    """여러 WebSocket 클라이언트 세션을 연결 및 해제하고,
    브로드캐스트와 개별 전송 기능을 제공하는 매니저 클래스입니다.
    """
    def __init__(self):
        self.active_connections: List[WebSocket] = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)
        print(f"[WS] 클라이언트 접속 허용: {websocket.client} (총 연결: {len(self.active_connections)})")

    def disconnect(self, websocket: WebSocket):
        if websocket in self.active_connections:
            self.active_connections.remove(websocket)
        print(f"[WS] 클라이언트 연결 해제: {websocket.client} (남은 연결: {len(self.active_connections)})")

    async def send_personal_message(self, message: str, websocket: WebSocket):
        await websocket.send_text(message)

    async def broadcast(self, message: str):
        disconnected = []
        for connection in self.active_connections:
            try:
                await connection.send_text(message)
            except Exception as e:
                print(f"[WS] 메세지 전송 에러 ({connection.client}): {e}")
                disconnected.append(connection)
                
        for conn in disconnected:
            self.disconnect(conn)

manager = ConnectionManager()

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
        service = AIInferenceService()
        with _ai_load_lock:
            _ai_service = service
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

WPF_DIR = r"d:\138.종합 민원 이미지 AI데이터\WPF"
WPF_IMAGE_DIR = r"C:\Users\admin_25\Downloads\test\image"
ASSETS_DIR = r"d:\138.종합 민원 이미지 AI데이터\BannerGuardSystem\Assets"

VLM_IMAGE_GLOBS = ("*.jpg", "*.jpeg", "*.png", "*.webp", "*.JPG", "*.JPEG", "*.PNG", "*.WEBP")

# CCTV 모니터링 전용 — MP4 LIVE 좌표 기준
CCTV_VIDEO_MAP = {
    "CCTV_01": "20260525_133553.mp4",
    "CCTV_02": "20260525_133611.mp4",
    "CCTV_03": "20260525_133838.mp4",
    "CCTV_04": "20260525_133919.mp4",
}

# WPF/image 파일명 → 채널 매핑 (파일명 일치 시)
CCTV_IMAGE_MAP = {
    "CCTV_01": "20260525_133548.jpg",
    "CCTV_02": "20260525_133609.jpg",
    "CCTV_03": "20260525_133852.jpg",
    "CCTV_04": "20260525_133916.jpg",
}


def _read_video_frame(video_path: str):
    """Windows 한글 경로 호환 — MP4 첫 프레임 읽기."""
    if not os.path.isfile(video_path):
        return None, 0, 0

    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        data = np.fromfile(video_path, dtype=np.uint8)
        cap = cv2.VideoCapture()
        if not cap.open(cv2.imdecode(data, cv2.IMREAD_COLOR)):
            return None, 0, 0

    ok, frame = cap.read()
    cap.release()
    if not ok or frame is None:
        return None, 0, 0

    h, w = frame.shape[:2]
    return frame, w, h


def _read_image_file(image_path: str):
    """jpg/png/webp 정지 이미지 읽기."""
    from app.utils.image_io import read_image_bgr

    img = read_image_bgr(image_path)
    if img is None:
        return None, 0, 0
    h, w = img.shape[:2]
    return img, w, h


def _write_temp_jpeg(frame: np.ndarray, cctv_id: str) -> str:
    ok, buf = cv2.imencode(".jpg", frame)
    if not ok:
        raise RuntimeError(f"{cctv_id} 프레임 JPEG 인코딩 실패")
    path = os.path.join(tempfile.gettempdir(), f"bannerguard_{cctv_id}_frame.jpg")
    buf.tofile(path)
    return path


def _bbox_to_xyxy(event: BannerDetectionEvent) -> List[int]:
    b = event.BoundingBox
    return [b.X, b.Y, b.X + b.Width, b.Y + b.Height]


def _iou(box_a: List[int], box_b: List[int]) -> float:
    x_a = max(box_a[0], box_b[0])
    y_a = max(box_a[1], box_b[1])
    x_b = min(box_a[2], box_b[2])
    y_b = min(box_a[3], box_b[3])
    inter = max(0, x_b - x_a) * max(0, y_b - y_a)
    area_a = (box_a[2] - box_a[0]) * (box_a[3] - box_a[1])
    area_b = (box_b[2] - box_b[0]) * (box_b[3] - box_b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


def dedupe_detections(events: List[BannerDetectionEvent], iou_thresh: float = 0.45) -> List[BannerDetectionEvent]:
    """겹치는 YOLO 박스 제거 — confidence 높은 것 우선."""
    sorted_events = sorted(events, key=lambda e: e.Confidence, reverse=True)
    kept: List[BannerDetectionEvent] = []
    kept_boxes: List[List[int]] = []

    for event in sorted_events:
        box = _bbox_to_xyxy(event)
        if any(_iou(box, prev) >= iou_thresh for prev in kept_boxes):
            continue
        kept.append(event)
        kept_boxes.append(box)

    return kept


def get_cctv_sources() -> List[Tuple[str, str, int, int]]:
    """CCTV 전용 — MP4 첫 프레임 (bbox 좌표 = LIVE 영상 기준)."""
    sources = []
    for cctv_id, mp4_name in CCTV_VIDEO_MAP.items():
        mp4_path = os.path.join(WPF_DIR, mp4_name)
        frame, w, h = _read_video_frame(mp4_path)
        if frame is not None:
            try:
                temp_path = _write_temp_jpeg(frame, cctv_id)
                sources.append((cctv_id, temp_path, w, h))
                print(f"  [CCTV] {cctv_id}: MP4 {mp4_name} → {w}x{h}")
                continue
            except Exception as e:
                print(f"  [CCTV] {cctv_id}: MP4 프레임 저장 실패 ({e})")

    return sources


def get_vlm_image_sources() -> List[Tuple[str, str, str, int, int]]:
    """VLM 검증 전용 — WPF/image 폴더 jpg·png·webp 자동 수집.
    Returns: (cctv_id, image_path, source_filename, frame_w, frame_h)
    """
    os.makedirs(WPF_IMAGE_DIR, exist_ok=True)

    files: List[str] = []
    for pattern in VLM_IMAGE_GLOBS:
        files.extend(glob.glob(os.path.join(WPF_IMAGE_DIR, pattern)))
    files = sorted(set(files))

    if not files:
        print(f"  [VLM] {WPF_IMAGE_DIR} — 이미지 없음 (jpg/png/webp)")
        return []

    sources: List[Tuple[str, str, str, int, int]] = []
    for i, path in enumerate(files):
        basename = os.path.basename(path)
        # 이미지마다 고유 ID — CCTV_01~04 재사용 시 Feed 항목이 덮어써지는 문제 방지
        cctv_id = f"VLM_{i + 1:02d}"
        _, w, h = _read_image_file(path)
        if w <= 0 or h <= 0:
            print(f"  [VLM] 읽기 실패: {basename}")
            continue
        sources.append((cctv_id, path, basename, w, h))
        print(f"  [VLM] {basename} → {cctv_id} ({w}x{h})")

    return sources


def _make_vlm_no_detection_event(
    cctv_id: str, image_path: str, source_image: str
) -> BannerDetectionEvent:
    """YOLO 미탐지 이미지도 Feed에 1행 표시."""
    from app.utils.image_io import encode_frame_for_ws

    frame, _, _ = _read_image_file(image_path)
    full_b64 = ""
    if frame is not None:
        full_b64, _, _, _ = encode_frame_for_ws(frame)

    return BannerDetectionEvent(
        Id=str(uuid.uuid4()),
        CctvId=cctv_id,
        LocationAddress=f"VLM 검증 ({source_image})",
        Timestamp=datetime.now(),
        HasBanner=False,
        BoundingBox=BBox(X=0, Y=0, Width=0, Height=0),
        Confidence=0.0,
        OcrText="현수막 미탐지",
        OcrConfidence=0.0,
        IsVlmTriggered=False,
        VlmAnalysis="YOLO 현수막 탐지 없음 — 전체 이미지 수동 확인 필요",
        IsRepeatOffender=False,
        RepeatCount=0,
        CropImageUri="",
        CroppedImageBase64="",
        FullImageBase64=full_b64,
        StatusCode="LEGAL_PUBLIC",
    )


async def _run_yolo_preview_and_send(
    websocket: WebSocket,
    ai_engine: AIInferenceService,
    message_type: str,
    cctv_id: str,
    image_path: str,
    frame_w: int,
    frame_h: int,
    source_image: str = "",
):
    events = await asyncio.to_thread(
        ai_engine.run_yolo_preview_as_event, image_path, cctv_id, source_image
    )
    deduped = dedupe_detections(events)
    if message_type == "vlm_image_batch" and not deduped:
        deduped = [_make_vlm_no_detection_event(cctv_id, image_path, source_image)]
    batch = ChannelDetectionBatch(
        MessageType=message_type,
        CctvId=cctv_id,
        FrameWidth=frame_w,
        FrameHeight=frame_h,
        SourceImage=source_image,
        Detections=deduped,
    )
    await websocket.send_text(batch.model_dump_json())
    print(f"[시뮬레이터] [VLM 프리뷰] {cctv_id} img={source_image} → {len(deduped)}건 ({frame_w}x{frame_h})")


async def _run_inference_and_send(
    websocket: WebSocket,
    ai_engine: AIInferenceService,
    message_type: str,
    cctv_id: str,
    image_path: str,
    frame_w: int,
    frame_h: int,
    is_automation: bool,
    source_image: str = "",
):
    events = await asyncio.to_thread(
        ai_engine.run_inference_as_event, image_path, cctv_id, is_automation
    )
    deduped = dedupe_detections(events)
    if message_type == "vlm_image_batch" and not deduped:
        deduped = [_make_vlm_no_detection_event(cctv_id, image_path, source_image)]
    batch = ChannelDetectionBatch(
        MessageType=message_type,
        CctvId=cctv_id,
        FrameWidth=frame_w,
        FrameHeight=frame_h,
        SourceImage=source_image,
        Detections=deduped,
    )
    await websocket.send_text(batch.model_dump_json())
    label = "VLM" if message_type == "vlm_image_batch" else "CCTV"
    extra = f" img={source_image}" if source_image else ""
    phase = " 완료" if message_type == "vlm_image_batch" else ""
    print(f"[시뮬레이터] [{label}{phase}] {cctv_id}{extra} → {len(deduped)}건 ({frame_w}x{frame_h})")


def _is_automation_enabled(websocket: WebSocket) -> bool:
    return getattr(websocket, "is_automation_enabled", True)


def _request_pipeline_run(websocket: WebSocket) -> None:
    run_event = getattr(websocket, "_pipeline_run_event", None)
    if run_event is not None:
        run_event.set()


async def _run_detection_pipeline(websocket: WebSocket, ai_engine: AIInferenceService) -> None:
    """CCTV + VLM 1회 실행 (반복 없음)."""
    if not _is_automation_enabled(websocket):
        return

    is_automation = True

    # ── 1) VLM 검증 우선 (WPF VLM 탭 대기 시간 단축) ──
    vlm_sources = get_vlm_image_sources()
    if vlm_sources:
        print(f"[시뮬레이터] VLM 추론 ({len(vlm_sources)}장) — 완료 후에만 Feed 반영")
        for cctv_id, image_path, source_name, frame_w, frame_h in vlm_sources:
            if not _is_automation_enabled(websocket):
                print("[시뮬레이터] 자동화 OFF — VLM 추론 중단")
                return
            try:
                await _run_inference_and_send(
                    websocket, ai_engine, "vlm_image_batch",
                    cctv_id, image_path, frame_w, frame_h, is_automation,
                    source_image=source_name,
                )
            except Exception as e:
                print(f"[시뮬레이터] VLM {source_name} 오류: {e}")

    # ── 2) CCTV 모니터링: MP4 ──
    cctv_sources = get_cctv_sources()
    if cctv_sources:
        print(f"[시뮬레이터] CCTV 추론 ({len(cctv_sources)}채널)")
        for cctv_id, image_path, frame_w, frame_h in cctv_sources:
            if not _is_automation_enabled(websocket):
                print("[시뮬레이터] 자동화 OFF — CCTV 추론 중단")
                return
            try:
                await _run_inference_and_send(
                    websocket, ai_engine, "channel_batch",
                    cctv_id, image_path, frame_w, frame_h, is_automation,
                )
            except Exception as e:
                print(f"[시뮬레이터] CCTV {cctv_id} 오류: {e}")

    print("[시뮬레이터] 1회 분석 완료 — 대기 중 (자동화 ON 시 재실행)")


async def run_real_ai_simulator(websocket: WebSocket):
    """연결 시 1회 분석. 자동화 ON 토글 시에만 다시 1회 실행 (로테이션 없음)."""
    try:
        await wait_for_ai_service()
        ai_engine = get_ai_service()
        run_event = asyncio.Event()
        setattr(websocket, "_pipeline_run_event", run_event)
        print("[시뮬레이터] CCTV=MP4 | VLM=WPF\\image (1회 실행, 반복 없음)")

        while True:
            await run_event.wait()
            run_event.clear()
            if _is_automation_enabled(websocket):
                await _run_detection_pipeline(websocket, ai_engine)

    except asyncio.CancelledError:
        print("[시뮬레이터] 시뮬레이션 태스크가 취소되었습니다.")
    except Exception as e:
        print(f"[시뮬레이터] 실행 오류: {e}")


@router.websocket("/ws/alerts")
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    setattr(websocket, "is_automation_enabled", True)
    simulator_task = asyncio.create_task(run_real_ai_simulator(websocket))

    try:
        while True:
            data = await websocket.receive_text()
            if data == "ping":
                await websocket.send_text("pong")
            else:
                try:
                    import json
                    msg = json.loads(data)
                    if msg.get("command") == "toggle_automation":
                        is_auto = msg.get("value", True)
                        setattr(websocket, "is_automation_enabled", is_auto)
                        state = "ON (1회 분석)" if is_auto else "OFF (분석 중단)"
                        print(f"[WS] 자동화 {state}")
                        if is_auto:
                            _request_pipeline_run(websocket)
                    elif msg.get("command") == "human_verdict":
                        from app.services.db_service import DatabaseService
                        db = DatabaseService()
                        db.save_human_verdict(msg)
                        print(
                            f"[WS] 관제관 판정: {msg.get('verdict')} "
                            f"→ {msg.get('statusCode')} (alert={msg.get('alertId')})"
                        )
                except Exception as e:
                    print(f"[WS] 메시지 처리 오류: {e}")
    except WebSocketDisconnect:
        manager.disconnect(websocket)
    finally:
        simulator_task.cancel()