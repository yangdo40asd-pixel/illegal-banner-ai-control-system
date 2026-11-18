from __future__ import annotations
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
from typing import List, Tuple, Dict, Any, Optional
from app.core.ai_runtime import (start_ai_service_background, is_ai_service_ready, is_ai_service_loading, get_ai_service, wait_for_ai_service)
from app.services.media_service import (
    WPF_DIR,
    VLM_IMAGE_DIR,
    CCTV_VIDEO_MAP,
    _open_video_capture,
    _read_video_frame_at_ms,
    _read_video_frame,
    _read_image_file,
    _write_temp_jpeg,
    _bbox_to_xyxy,
    _iou,
    dedupe_detections,
    get_cctv_sources,
    _list_vlm_image_files,
    _resolve_vlm_location_address,
    get_vlm_image_sources,
)
from app.schemas.detection_schema import BannerDetectionEvent, ChannelDetectionBatch, BBox
from app.core.paths import paths

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

def _make_vlm_no_detection_event(
    cctv_id: str, image_path: str, source_image: str
) -> BannerDetectionEvent:
    """YOLO 미탐지 이미지도 Feed에 1행 표시."""
    from app.utils.image_io import encode_frame_for_ws

    frame, _, _ = _read_image_file(image_path)
    full_b64 = ""
    if frame is not None:
        full_b64, _, _, _ = encode_frame_for_ws(frame)

    raw_name = os.path.splitext(os.path.basename(source_image or image_path))[0]
    return BannerDetectionEvent(
        Id=f"vlm_{raw_name}_none",
        CctvId=cctv_id,
        LocationAddress=_resolve_vlm_location_address(source_image, image_path),
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
    if message_type == "channel_batch":
        deduped = [
            e for e in deduped
            if e.HasBanner and e.BoundingBox.Width > 0 and e.BoundingBox.Height > 0
        ]
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
        ai_engine.run_inference_as_event, image_path, cctv_id, is_automation, source_image
    )
    deduped = dedupe_detections(events)
    if message_type == "channel_batch":
        deduped = [
            e for e in deduped
            if e.HasBanner and e.BoundingBox.Width > 0 and e.BoundingBox.Height > 0
        ]
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

    # ── 1) CCTV 모니터링 우선: MP4 YOLO-only (channel_batch, 제출용 화면 즉시 반영) ──
    cctv_sources = get_cctv_sources()
    if cctv_sources:
        print(f"[시뮬레이터] CCTV YOLO 추론 ({len(cctv_sources)}채널)")
        for cctv_id, image_path, frame_w, frame_h in cctv_sources:
            if not _is_automation_enabled(websocket):
                print("[시뮬레이터] 자동화 OFF — CCTV 추론 중단")
                return
            try:
                await _run_yolo_preview_and_send(
                    websocket, ai_engine, "channel_batch",
                    cctv_id, image_path, frame_w, frame_h,
                )
            except Exception as e:
                print(f"[시뮬레이터] CCTV {cctv_id} 오류: {e}")

    # ── 2) VLM 검증: test/image 폴더 전체 (완료 후 Feed 반영) ──
    vlm_sources = get_vlm_image_sources()
    if vlm_sources:
        print(f"[시뮬레이터] VLM 추론 ({len(vlm_sources)}장) — 폴더 전체")
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

    print("[시뮬레이터] 1회 분석 완료 — 대기 중 (자동화 ON 시 재실행)")


_cctv_channel_locks: Dict[str, asyncio.Lock] = {}


async def _analyze_cctv_at_position(websocket: WebSocket, cctv_id: str, position_ms: int) -> None:
    """VLC 재생 시각에 맞춰 MP4 프레임 YOLO → channel_batch (LIVE 추적)."""
    lock = _cctv_channel_locks.setdefault(cctv_id, asyncio.Lock())
    if lock.locked():
        # 이전 프레임 분석이 진행 중이면 작업 폭주/OOM 방지를 위해 오래된 프레임 스킵
        return

    async with lock:
        try:
            mp4_name = CCTV_VIDEO_MAP.get(cctv_id)
            if not mp4_name:
                return

            ai_engine = get_ai_service()
            mp4_path = os.path.join(WPF_DIR, mp4_name)
            frame, w, h = await asyncio.to_thread(_read_video_frame_at_ms, mp4_path, max(0, position_ms))
            if frame is None:
                return

            temp_path = await asyncio.to_thread(_write_temp_jpeg, frame, f"{cctv_id}_{position_ms}")
            await _run_yolo_preview_and_send(
                websocket, ai_engine, "channel_batch",
                cctv_id, temp_path, w, h,
            )
        except Exception as e:
            print(f"[CCTV-Live] {cctv_id} @ {position_ms}ms 오류: {e}")


async def run_real_ai_simulator(websocket: WebSocket):
    """연결 시 1회 분석. 자동화 ON 토글 시에만 다시 1회 실행 (로테이션 없음)."""
    try:
        await wait_for_ai_service()
        ai_engine = get_ai_service()
        run_event = asyncio.Event()
        run_event.set() # Trigger initial run
        setattr(websocket, "_pipeline_run_event", run_event)
        print(f"[시뮬레이터] CCTV=MP4 | VLM={VLM_IMAGE_DIR} (폴더 전체 자동 로드)")

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
                    elif msg.get("command") == "cctv_analyze":
                        cctv_id = msg.get("cctvId") or msg.get("cctv_id") or ""
                        position_ms = int(msg.get("positionMs") or msg.get("position_ms") or 0)
                        if cctv_id:
                            asyncio.create_task(
                                _analyze_cctv_at_position(websocket, cctv_id, position_ms)
                            )
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
