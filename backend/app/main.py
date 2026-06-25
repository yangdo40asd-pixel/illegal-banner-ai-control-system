import os
import sys
import asyncio
from contextlib import asynccontextmanager

# 프로젝트 루트(backend/) 경로를 sys.path에 추가하여 모듈을 항상 정상적으로 참조할 수 있도록 함
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
os.environ['KMP_DUPLICATE_LIB_OK'] = 'True'

import uvicorn
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from app.core.config import settings
from app.api import websocket_endpoints

@asynccontextmanager
async def lifespan(app: FastAPI):
    """FastAPI 구동 시점에 YOLO 및 EasyOCR 모델을 스레드풀에서 미리 안전하게 로드합니다."""
    print("[서버] 시작 중 — AI 모델 미리 로드 시작...")
    from app.api.websocket_endpoints import get_ai_service
    loop = asyncio.get_running_loop()
    await loop.run_in_executor(None, get_ai_service)
    print("[서버] AI 모델 미리 로딩 성공!")
    yield
    print("[서버] 종료 중...")

app = FastAPI(
    title=settings.PROJECT_NAME,
    description="FastAPI Backend for BannerGuardSystem (Phase 2)",
    version=settings.API_VERSION,
    lifespan=lifespan
)

# CORS 설정 - WPF 클라이언트 및 다양한 프론트엔드의 접근 허용
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Static Files 마운트 - 크롭 스냅샷 이미지 파일을 HTTP로 제공하기 위함
app.mount("/assets", StaticFiles(directory=settings.ASSETS_DIR), name="assets")

# WebSocket 라우터 등록 (/ws/alerts)
app.include_router(websocket_endpoints.router)

@app.get("/")
async def root():
    return {
        "message": f"Welcome to {settings.PROJECT_NAME}",
        "status": "online",
        "docs_url": "/docs"
    }

@app.get("/health")
async def health():
    return {
        "status": "healthy",
        "environment": "real_inference"
    }

if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host=settings.HOST,
        port=settings.PORT,
        reload=False
    )
