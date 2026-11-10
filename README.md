# BannerGuardSystem

**불법 현수막 AI 통합 관제 시스템**

CCTV와 영상에서 현수막을 탐지하고, 내용을 읽어서 불법 여부를 판정한 뒤, 데스크톱에서 실시간으로 알리고 보고서를 만드는 시스템입니다.

저장소: [yangdo40asd-pixel/illegal-banner-ai-control-system](https://github.com/yangdo40asd-pixel/illegal-banner-ai-control-system)

## 처리 흐름

CCTV/영상 → YOLO 현수막 탐지 → Qwen2.5-VL OCR·불법 판정 → WPF 실시간 알림·목록·PDF 보고서

## 기술 스택

- 데스크톱: C#, WPF, .NET 9, MVVM
- AI 서버: Python, FastAPI, WebSocket
- 모델: YOLO, ONNX, VLM(Qwen2.5-VL)
- 저장: SQLite

## 프로젝트 구조

```
BannerGuardSystem/   WPF 클라이언트 (C#)
backend/             FastAPI AI 서버 (Python)
yolo26s/             YOLO 탐지 모델
```

학습에 사용한 원천 데이터셋은 이 저장소에 포함하지 않습니다.

## 실행 방법

필요한 환경:

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Python 3.11 이상
- NVIDIA GPU와 CUDA 드라이버 (AI 추론용)

```
setup.bat   # 패키지 설치와 WPF 빌드 (최초 1회)
run.bat     # 프로그램 실행
```

WPF 앱이 Python AI 서버(`backend/app/main.py`)를 함께 시작합니다. 처음 분석할 때 Qwen2.5-VL 모델이 Hugging Face에서 내려받아집니다(약 6GB, 인터넷 필요). 모델과 데이터 경로는 실행 환경에 맞게 설정해야 할 수 있습니다.
