# 138.종합 민원 이미지 AI데이터

**BannerGuardSystem** — 불법 현수막 AI 관제 WPF 데스크톱

CCTV/영상 → YOLO 현수막 탐지 → Qwen2.5-VL OCR·불법 판정 → WPF 실시간 알림·목록·PDF 보고서

---

## 다른 PC에서 받아서 실행하기 (3단계)

### 1. GitHub Desktop으로 Clone

| 항목 | 값 |
|---|---|
| Repository | `yangdo40asd-pixel/138.-AI-` |
| **Local path** | `D:\138.종합 민원 이미지 AI데이터` |

> AI 백엔드 경로가 `D:\138.종합 민원 이미지 AI데이터`로 고정되어 있습니다.  
> **반드시 위 경로로 clone** 하세요. (다른 경로면 AI가 모델을 못 찾습니다)

### 2. 사전 설치 (한 번만)

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Python 3.11+ 또는 Anaconda (권장: `banner_env` 환경)
- NVIDIA GPU + CUDA 드라이버

### 3. 설치 & 실행

```
setup.bat   ← 더블클릭 (패키지 설치 + WPF 빌드, 최초 1회)
run.bat     ← 더블클릭 (프로그램 실행)
```

- WPF가 Python AI 서버(`backend/app/main.py`)를 **자동 기동**합니다.
- **최초 AI 분석 시** Qwen2.5-VL 모델이 HuggingFace에서 자동 다운로드됩니다 (~6GB, 인터넷 필요).

---

## 프로젝트 구조

```
BannerGuardSystem/   WPF 클라이언트 (C#)
backend/             FastAPI AI 서버 (Python)
yolo26s/runs/.../best.pt   YOLO 탐지 모델 (Git 포함)
inference_test/      추론 엔진·판정 규칙 문서
```

---

## 로컬에만 있는 것 (GitHub 미포함)

| 폴더 | 설명 |
|---|---|
| `01.데이터/` | AI 학습 원천 데이터 (~28GB) |
| `comgong_labels.../` | YOLO 학습 데이터셋 |
| `BannerGuardSystem/bin/` | 빌드 결과 (`setup.bat`으로 생성) |
| `florence2/` | 구버전 모델 (미사용) |

---

## 문제 해결

| 증상 | 확인 |
|---|---|
| AI 서버 안 켜짐 | `banner_tracking.db` 옆 `backend_manager.log` 확인 |
| 모델 못 찾음 | clone 경로가 `D:\138.종합 민원 이미지 AI데이터` 인지 확인 |
| 빌드 실패 | .NET 9 SDK 설치 여부 확인 |
| GPU 오류 | NVIDIA 드라이버 + CUDA, `torch` CUDA 버전 확인 |