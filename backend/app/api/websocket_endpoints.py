import os
import glob
import asyncio
from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from typing import List
from app.services.ai_inference_service import AIInferenceService

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

# AI 서비스 싱글톤 인스턴스 레이지 로딩
_ai_service = None

def get_ai_service() -> AIInferenceService:
    """AIInferenceService 싱글톤 인스턴스를 반환합니다.
    필요할 때만 로딩(지연 초기화)하여 초기 임포트 블로킹을 방지합니다.
    """
    global _ai_service
    if _ai_service is None:
        _ai_service = AIInferenceService()
    return _ai_service

async def run_real_ai_simulator(websocket: WebSocket):
    """inference_test/test_images 디렉토리 내의 실제 이미지들을 순회하여
    YOLO+EasyOCR 실시간 AI 파이프라인 분석을 돌리고,
    WPF 클라이언트로 실시간 경보 메시지를 푸시합니다.
    """
    try:
        # AI 서비스 로드
        ai_engine = get_ai_service()
        
        # 실제 CCTV 채널 배경에 해당하는 에셋 파일 매핑
        assets_dir = r"D:\138.종합 민원 이미지 AI데이터\BannerGuardSystem\Assets"
        cctv_mappings = [
            ("CCTV_01", os.path.join(assets_dir, "20260525_133548.jpg")),
            ("CCTV_02", os.path.join(assets_dir, "20260525_133609.jpg")),
            ("CCTV_03", os.path.join(assets_dir, "20260525_133852.jpg")),
            ("CCTV_04", os.path.join(assets_dir, "20260525_133916.jpg")),
        ]

        print(f"[시뮬레이터] {len(cctv_mappings)}개의 에셋 이미지를 채널별로 대응하여 AI 분석 시뮬레이션을 구동합니다.")
        
        idx = 0

        while True:
            # 1. 8초 대기 후 분석 시작 (최초 시작은 연결 3초 후 실행)
            await asyncio.sleep(8 if idx > 0 else 3)
            
            cctv_id, img_path = cctv_mappings[idx % len(cctv_mappings)]
            
            if not os.path.exists(img_path):
                print(f"[시뮬레이터] 경고: 에셋 이미지 파일을 찾을 수 없습니다: {img_path}")
                idx += 1
                continue
            
            print(f"[시뮬레이터] 프레임 분석 가동: {os.path.basename(img_path)} (채널: {cctv_id})")

            # AI 연산은 CPU/GPU 연산을 동반하므로 비차단(non-blocking) 스레드풀에서 실행
            loop = asyncio.get_running_loop()
            events = await loop.run_in_executor(None, ai_engine.run_inference_as_event, img_path, cctv_id)

            print(f"[시뮬레이터] 분석 완료. 현수막 검출 건수: {len(events)}")

            # 검출된 모든 현수막 경보 이벤트를 클라이언트로 순차 전송
            for event in events:
                # model_dump_json은 pydantic v2 표준 직렬화 방식입니다.
                await websocket.send_text(event.model_dump_json())
                # 다중 경보가 있을 때 UI 가독성을 위해 미세한 딜레이 부여
                await asyncio.sleep(0.5)

            idx += 1
            
    except asyncio.CancelledError:
        print("[시뮬레이터] 시뮬레이션 태스크가 취소되었습니다.")
    except Exception as e:
        print(f"[시뮬레이터] 시뮬레이션 루프 실행 오류: {e}")

@router.websocket("/ws/alerts")
async def websocket_endpoint(websocket: WebSocket):
    """WPF 클라이언트가 접속하는 WebSocket 라우트입니다.
    실제 AI 모델 추론을 가동하는 백그라운드 태스크를 시작합니다.
    """
    await manager.connect(websocket)
    
    # 각 WebSocket 연결마다 독립적인 실시간 AI 추론 시뮬레이터 시작
    simulator_task = asyncio.create_task(run_real_ai_simulator(websocket))
    
    try:
        while True:
            # 연결 유지 및 핑퐁 처리
            data = await websocket.receive_text()
            if data == "ping":
                await websocket.send_text("pong")
    except WebSocketDisconnect:
        manager.disconnect(websocket)
    finally:
        # 웹소켓이 끊어지면 실행 중인 시뮬레이터 태스크 취소
        simulator_task.cancel()
