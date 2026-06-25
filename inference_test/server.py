"""
server.py — FastAPI 불법 현수막 탐지 API 서버
====================================================
실행 방법:
  pip install fastapi uvicorn python-multipart
  python server.py
  또는: uvicorn server:app --host 0.0.0.0 --port 8000 --reload

엔드포인트:
  GET  /health        → 서버 및 모델 상태 확인
  POST /analyze       → 이미지 업로드 후 탐지 + 판정 결과 반환
  WS   /ws            → WebSocket 실시간 결과 스트리밍 (Step 4-3)
  GET  /results       → 최근 탐지 이력 조회 (Step 4-5 DB 연동 후)
"""

import os
import sys
import io
import json
import uvicorn
from datetime import datetime
from fastapi import FastAPI, File, UploadFile, WebSocket, WebSocketDisconnect, HTTPException, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from PIL import Image
from contextlib import asynccontextmanager

import inference_engine as engine
import database

# ── Windows 필수 환경변수 ────────────────────────
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'
sys.stdout.reconfigure(encoding='utf-8')


# ══════════════════════════════════════════════════
# WebSocket 연결 관리자
# ══════════════════════════════════════════════════
class ConnectionManager:
    """WebSocket 연결 목록을 관리합니다."""
    def __init__(self):
        self.active: list[WebSocket] = []

    async def connect(self, ws: WebSocket):
        await ws.accept()
        self.active.append(ws)
        print(f"[WS] 클라이언트 연결됨 (현재 {len(self.active)}개)")

    def disconnect(self, ws: WebSocket):
        self.active.remove(ws)
        print(f"[WS] 클라이언트 연결 해제 (현재 {len(self.active)}개)")

    async def broadcast(self, data: dict):
        """연결된 모든 클라이언트에게 JSON을 전송합니다."""
        message = json.dumps(data, ensure_ascii=False)
        disconnected = []
        for ws in self.active:
            try:
                await ws.send_text(message)
            except Exception:
                disconnected.append(ws)
        for ws in disconnected:
            self.active.remove(ws)


manager = ConnectionManager()


# ══════════════════════════════════════════════════
# 앱 시작/종료 이벤트 (lifespan)
# ══════════════════════════════════════════════════
@asynccontextmanager
async def lifespan(app: FastAPI):
    """서버 시작 시 모델을 1회 로드합니다."""
    print("[서버] 시작 중 — AI 모델 로딩...")
    engine.load_models()
    print("[서버] 준비 완료! http://0.0.0.0:8000")
    yield
    print("[서버] 종료 중...")


# ══════════════════════════════════════════════════
# FastAPI 앱 생성
# ══════════════════════════════════════════════════
app = FastAPI(
    title="불법 현수막 탐지 API",
    description="YOLOv26s + Qwen2.5-VL-3B 기반 현수막 분석 서버",
    version="1.0.0",
    lifespan=lifespan
)

# CORS 설정 (WPF에서 HTTP 요청 허용)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ══════════════════════════════════════════════════
# 엔드포인트
# ══════════════════════════════════════════════════

@app.get("/health", summary="서버 상태 확인")
async def health():
    """서버와 AI 모델의 현재 상태를 반환합니다."""
    return {
        "status": "ok" if engine.is_loaded() else "loading",
        "model":  "Qwen2.5-VL-3B-Instruct",
        "yolo":   "YOLOv26s (mAP50 99.2%)",
        "device": "cuda" if engine._device == "cuda" else "cpu",
        "time":   datetime.now().isoformat()
    }


@app.post("/analyze", summary="이미지 분석 — 현수막 탐지 + 불법 판정")
async def analyze(
    file: UploadFile = File(...),
    cctv_id: str = Query("CCTV_01", description="CCTV 카메라 식별자")
):
    """
    이미지 파일을 업로드하면 YOLO + VLM으로 분석하여 결과를 반환합니다.
    - **cctv_id**: 단속 카메라 고유 식별자 (DB 트래킹용)
    - **탐지**: 현수막 위치 (바운딩 박스 + 신뢰도)
    - **OCR**: 현수막에 적힌 텍스트
    - **판정**: 불법 / 합법
    - **결과 이미지**: 바운딩 박스가 그려진 이미지 (Base64)
    """
    if not engine.is_loaded():
        raise HTTPException(status_code=503, detail="모델 로딩 중입니다. 잠시 후 다시 시도하세요.")

    # 파일 확장자 검사
    if not file.filename.lower().endswith(('.jpg', '.jpeg', '.png')):
        raise HTTPException(status_code=400, detail="JPG 또는 PNG 이미지만 지원합니다.")

    try:
        contents = await file.read()
        pil_img = Image.open(io.BytesIO(contents)).convert("RGB")
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"이미지 열기 실패: {e}")

    try:
        result = engine.analyze_image(pil_img, cctv_id=cctv_id)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"분석 중 오류 발생: {e}")

    response = {
        "filename":        file.filename,
        "cctv_id":         cctv_id,
        "detection_count": result["detection_count"],
        "detections":      result["detections"],
        "result_image_base64": result["result_image_base64"],
        "processed_at":    datetime.now().isoformat()
    }

    # WebSocket으로도 동시에 브로드캐스트 (연결된 클라이언트가 있을 경우)
    if manager.active:
        broadcast_data = {k: v for k, v in response.items() if k != "result_image_base64"}
        await manager.broadcast(broadcast_data)

    return JSONResponse(content=response)


@app.get("/results", summary="최근 탐지 이력 조회")
async def get_results():
    """SQLite DB에 추적 및 누적된 모든 현수막 내역을 조회합니다."""
    try:
        data = database.get_all_tracked_banners()
        return {"status": "success", "count": len(data), "results": data}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"DB 조회 실패: {e}")


@app.post("/analyze_video", summary="동영상 분석 — 현수막 다중 트래킹 및 판정")
async def analyze_video(
    file: UploadFile = File(...),
    cctv_id: str = Query("CCTV_01", description="CCTV 카메라 식별자")
):
    """
    동영상 파일(.mp4 등)을 업로드하면 백엔드에서 1초 단위 프레임으로 쪼개 분석합니다.
    - **cctv_id**: 단속 구역 식별자 (DB 트래킹용)
    - **중복 제어**: IoU 추적을 통해 영상 안에서 동일한 현수막은 단 1회만 VLM 판독을 수행합니다.
    """
    import uuid
    if not engine.is_loaded():
        raise HTTPException(status_code=503, detail="모델 로딩 중입니다. 잠시 후 다시 시도하세요.")

    if not file.filename.lower().endswith(('.mp4', '.avi', '.mov', '.mkv')):
        raise HTTPException(status_code=400, detail="지원하는 비디오 형식이 아닙니다 (mp4, avi, mov, mkv만 허용).")

    # 비디오 임시 파일 저장
    temp_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), f"_temp_video_{uuid.uuid4().hex}_{file.filename}")
    try:
        with open(temp_path, "wb") as f:
            content = await file.read()
            f.write(content)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"비디오 업로드 임시 저장 실패: {e}")

    try:
        # 비디오 분석 수행
        result = engine.analyze_video(temp_path, cctv_id=cctv_id)
        if "error" in result:
            raise HTTPException(status_code=500, detail=result["error"])
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"비디오 분석 중 오류 발생: {e}")
    finally:
        # 임시 파일 정리
        if os.path.exists(temp_path):
            os.remove(temp_path)

    # WebSocket 브로드캐스트
    if manager.active:
        broadcast_data = {
            "filename": result["video_filename"],
            "cctv_id": cctv_id,
            "detection_count": result["unique_banners_count"],
            "detections": result["detections"],
            "processed_at": datetime.now().isoformat()
        }
        await manager.broadcast(broadcast_data)

    return JSONResponse(content={
        "filename": result["video_filename"],
        "detection_count": result["unique_banners_count"],
        "detections": result["detections"],
        "result_image_base64": "",  # 동영상은 결과 합성 이미지가 없으므로 공백 반환
        "processed_video_path": result.get("processed_video_path", ""),
        "processed_at": datetime.now().isoformat()
    })


# ══════════════════════════════════════════════════
# WebSocket 엔드포인트 (Step 4-3)
# ══════════════════════════════════════════════════
@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    """
    WPF 클라이언트가 연결하여 실시간으로 분석 결과를 수신합니다.
    /analyze 호출 시 결과가 자동으로 이 채널을 통해 푸시됩니다.
    """
    await manager.connect(websocket)
    try:
        while True:
            # 클라이언트로부터 ping 수신 대기 (연결 유지용)
            data = await websocket.receive_text()
            if data == "ping":
                await websocket.send_text("pong")
    except WebSocketDisconnect:
        manager.disconnect(websocket)


# ══════════════════════════════════════════════════
# 서버 실행
# ══════════════════════════════════════════════════
if __name__ == "__main__":
    uvicorn.run(
        "server:app",
        host="0.0.0.0",
        port=8000,
        reload=False,   # 운영 시 False (모델 중복 로딩 방지)
        log_level="info"
    )
