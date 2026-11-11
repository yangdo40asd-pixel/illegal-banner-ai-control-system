# BannerGuardSystem — AI 기반 불법 현수막 통합관제

**C# WPF 관제 클라이언트와 Python AI 서버를 연동해 현수막 탐지, 이미지 검증, 사람의 최종 판정과 단속 기록·보고서를 지원하는 Windows 데스크톱 애플리케이션입니다.**

[📖 상세 포트폴리오 (Notion)](https://app.notion.com/p/222045186b3b831cbabd81c0a6af7b3f) · [🎬 시연 영상](https://docs.google.com/videos/d/1aZVHS13DnwQFeBqG0gLBE155NHy8MCqOovBZN0tpx9A/edit?scene=id.p#scene=id.p)

## 📸 Preview

- **4채널 CCTV 모니터링:** 로컬 MP4 영상에 YOLO 탐지 박스를 표시합니다.
- **이미지 VLM 검증:** 별도 샘플 이미지를 YOLO로 탐지·크롭한 후 Qwen2.5-VL 분석 결과를 보여줍니다.
- **단속 관리:** 관제관 판정, 기록 조회, 통계, PDF·Excel 보고서를 지원합니다.

실제 UI 스크린샷과 아키텍처 이미지는 [Notion 포트폴리오](https://app.notion.com/p/222045186b3b831cbabd81c0a6af7b3f)에서 볼 수 있습니다.

## 📌 Project Overview

| 항목 | 내용 |
| --- | --- |
| 개발 기간 | 2026.04.06 ~ 2026.06.23 (이후 개선 작업 별도) |
| 개발 인원 | 1인 개발 |
| 담당 역할 | 기획, YOLO 학습·모델 선정, WPF UI, Python 서버, DB·보고서 및 기능 통합 |
| 플랫폼 | Windows Desktop |
| 핵심 목표 | AI 분석 결과를 사람이 검토하고 관리할 수 있는 관제 도구 구현 |

객체 탐지만으로는 현수막의 문구와 주변 맥락을 설명하기 어려워, 영상 탐지와 별도 이미지 검증을 분리하고 **최종 판정은 관제관이 수행**하도록 설계했습니다.

## ✨ Key Features

1. **CCTV Monitoring** — 로컬 MP4 4개를 동시 재생하고 약 3초 간격으로 해당 재생 시점의 프레임을 YOLO로 분석하여 탐지 영역을 표시합니다.
2. **VLM Verification** — 별도 샘플 이미지의 현수막 영역을 YOLO → 크롭 → Qwen2.5-VL 순서로 분석해 문구·맥락에 대한 참고 정보를 제공합니다.
3. **Human-in-the-loop** — 관제관이 합법·불법·재검토를 결정하고 해당 결과를 단속 기록으로 관리합니다.
4. **Statistics & Reports** — SQLite 기반 기록 조회·검색, 기간별 차트, PDF 조치요청서 및 Excel 내보내기를 제공합니다.

> **시연 범위:** CCTV/영상의 탐지 프레임이 VLM으로 자동 전달되는 구조는 아닙니다. CCTV 경로는 YOLO 탐지 전용이고, VLM은 별도의 샘플 이미지 검증 경로에서 사용합니다. 일부 위치·과태료·통계 데이터는 시연용입니다.

## 🛠 Tech Stack

| 분류 | 기술 | 역할 |
| --- | --- | --- |
| Desktop | C#, .NET 9, WPF, MVVM | 관제 UI, 상태·명령, 영상 오버레이 |
| Backend | Python, FastAPI, WebSocket | AI 분석 요청·응답, 백엔드 프로세스 연동 |
| AI | YOLOv26s, Qwen2.5-VL-3B, PyTorch | 현수막 탐지 및 이미지 문구·맥락 분석 |
| Storage | SQLite | 판정·단속 기록 |
| Etc. | LibVLCSharp, QuestPDF, ClosedXML | 영상 재생, PDF·Excel 출력 |

## 🏗 System Architecture

```text
[WPF 관제 클라이언트] ── WebSocket ── [FastAPI AI 서버]
      │                                  ├─ CCTV: MP4 위치별 프레임 → YOLO
      │                                  └─ 별도 이미지: YOLO → Crop → VLM
      ├─ MVVM / 서비스 계층
      ├─ 관제관 최종 판정 → SQLite
      └─ 단속 기록 / 통계 / PDF·Excel
```

WPF가 서버를 자식 프로세스로 실행하거나 이미 실행 중인 서버를 재사용하며, 통신 단절 시 재연결을 시도합니다. 모델 실행과 화면 로직의 책임을 분리했습니다.

## 🔧 Challenges & Solutions

### 1. WPF에서 실행한 Python 서버의 GPU 미인식

- **문제:** Python을 단독 실행할 때와 달리 WPF에서 서버를 자동 실행하면 CPU로 동작해 VLM 분석에 이미지당 80초 이상 걸렸습니다.
- **원인:** 자식 프로세스의 PATH에서 Conda 환경의 실행 파일·DLL 경로가 누락되었습니다.
- **해결:** WPF 백엔드 관리 코드에서 필요한 Conda 경로를 자식 프로세스 PATH 앞에 추가했습니다.
- **결과:** 해당 환경에서 GPU 실행을 회복하고 분석 시간을 약 14초로 개선했습니다.

### 2. YOLO 탐지 박스의 화면 좌표 오차

- **문제:** 원본 프레임의 탐지 좌표가 WPF 영상 표시 영역과 맞지 않았습니다.
- **원인:** 원본 영상과 화면 컨테이너의 크기·비율 및 Letterbox 여백이 달랐습니다.
- **해결:** 원본 FrameWidth/Height, Uniform Scale과 X/Y offset을 적용하여 표시 좌표를 계산했습니다.
- **결과:** 탐지 박스가 실제 영상 표시 영역을 기준으로 배치되도록 수정했습니다.

## 📊 Results

- 자체 학습 **YOLOv26s 현수막 객체 탐지 mAP50 0.991** (검증셋 241장 기준). **불법 여부 분류 정확도를 의미하지 않습니다.**
- 4채널 영상 모니터링, 별도 VLM 이미지 분석, 관제관 판정과 단속 기록 관리 기능 구현.
- GPU 환경 구성과 좌표 변환 등 실제 연동 과정의 문제 해결.

## 🚀 Getting Started

### Requirements

- Windows, [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Python 3.11 이상
- NVIDIA GPU 및 CUDA 드라이버 (AI 추론용)
- 모델 파일 및 데이터 경로의 환경별 설정

### Installation & Run

```bat
setup.bat
run.bat
```

`setup.bat`은 최초 패키지 설치와 WPF 빌드에 사용하며 `run.bat`으로 프로그램을 실행합니다. WPF 앱은 Python AI 서버(`backend/app/main.py`)도 함께 시작합니다. 최초 분석 시 Qwen2.5-VL 모델을 Hugging Face에서 내려받을 수 있으며(약 6GB, 인터넷 필요), 모델·데이터 경로는 환경에 맞게 설정해야 할 수 있습니다.

```text
BannerGuardSystem/   WPF 클라이언트
backend/             FastAPI AI 서버
yolo26s/             YOLO 탐지 모델 관련 파일
```

학습에 사용한 원천 데이터셋은 이 저장소에 포함하지 않았습니다.

## 🔗 Links

- [상세 포트폴리오 — 구조·설계·트러블슈팅·실제 화면](https://app.notion.com/p/222045186b3b831cbabd81c0a6af7b3f)
- [시연 영상](https://docs.google.com/videos/d/1aZVHS13DnwQFeBqG0gLBE155NHy8MCqOovBZN0tpx9A/edit?scene=id.p#scene=id.p)
