# Illegal Banner Detection Assistant Tool Implementation Plan

YOLOv26n(탐지), Florence-2(추론/VLM), 그리고 WPF(UI)를 결합하여 불법 현수막을 스마트하게 찾아내고 관리하는 보조 도구 프로그램을 개발합니다.

## User Review Required

> [!IMPORTANT]
> **하드웨어 사양**: 로컬에서 YOLOv26n과 Florence-2를 동시에 돌리기 위해서는 NVIDIA GPU(최소 RTX 3060 이상 추천)와 CUDA 환경 설정이 필수적입니다. GPU를 사용하실 수 있는 환경인지 확인 부탁드립니다.

> [!NOTE]
> **VLM 판단 로직**: "불법"을 판단하는 기준(예: 나무에 묶임, 지정 게시판 아님 등)을 프롬프트로 정의해야 합니다. 개발 과정에서 여러 테스트를 통해 최적의 명령어를 함께 다듬어 나갈 예정입니다.

## Proposed Changes

### 1. 시스템 아키텍처 및 통신 (Architecture & Communication)
Python 기반의 AI 엔진과 C# 기반의 WPF UI를 효율적으로 연결합니다.

- **Python Backend (FastAPI)**: 
    - YOLOv26n과 Florence-2를 관리합니다.
    - **WebSocket**을 통해 탐지된 이벤트(좌표, 이미지, VLM 분석 결과)를 WPF로 실시간 푸시합니다.
- **WPF Frontend (C#)**:
    - `CommunityToolkit.Mvvm` 패턴을 사용하여 반응형 UI를 구축합니다.
    - Python 서버에서 보낸 이미지를 `BitmapImage`로 변환하여 실시간으로 출력합니다.

---

### 2. 모델 상세 활용 방식 (Model Usage Strategy)

#### [YOLOv26n] - 실시간 객체 탐지
- **역할**: 영상 프레임 내에서 '현수막'의 위치를 초당 30회 이상 탐지합니다.
- **최적화**: TensorRT 또는 ONNX 변환을 통해 로컬 리소스를 최소화합니다.

#### [Florence-2] - 심층 추론 및 분석
- **역할**: YOLO가 탐지한 현수막 사진을 보고 "불법 여부"를 논리적으로 판단합니다.
- **워크플로우**: 
    1. **OCR**: 현수막 내의 상호명, 전화번호 등을 텍스트로 추출.
    2. **Context Analysis**: 현수막이 설치된 주변 환경(나무, 가로등, 펜스 등)을 파악.
    3. **Prompt**: "분석된 텍스트와 설치 환경을 바탕으로 불법 현수막 여부를 JSON 형식으로 출력해줘."

---

### 3. WPF 화면 구성 설계 (UI Layout Design)

- **Main Dashboard**:
    - **좌측 (Live View)**: OpenCV 또는 FFmpeg를 이용한 실시간 스트리밍 및 바운딩 박스 오버레이.
    - **우측 (Detection Gallery)**: 최근 탐지된 '불법 의심' 현수막들의 썸네일 카드 뷰.
- **Detail Inspector (Popup or Bottom Panel)**:
    - **Crop Image**: 해당 현수막만 확대해서 보여줌.
    - **Analysis Text**: VLM이 생성한 불법 판단 근거 리포트.
    - **Manual Entry**: 관리자가 조치 사항(철거 완료, 과태료 부과 등)을 입력하는 폼.

---

### 4. 개발 단계별 구성 (Development Steps)

#### [NEW] [data_converter.py](file:///C:/Users/admin_25/.gemini/antigravity/brain/d595de50-ae3e-42c6-821b-b9b759ac7051/scratch/data_converter.py)
- `TL2.zip`에서 JSON 라벨을 추출하여 YOLO `.txt` 포맷으로 변환.
- `TS2.zip`에서 해당하는 이미지 파일만 추출.

#### [NEW] [fastapi_engine.py](file:///C:/Users/admin_25/.gemini/antigravity/brain/d595de50-ae3e-42c6-821b-b9b759ac7051/scratch/fastapi_engine.py)
- YOLO + VLM 추론을 수행하고 WebSocket으로 데이터를 쏘는 백엔드 서버.

#### [NEW] [MainWindow.xaml](file:///C:/Users/admin_25/.gemini/antigravity/brain/d595de50-ae3e-42c6-821b-b9b759ac7051/BannerApp/MainWindow.xaml)
- 대시보드형 레이아웃 구현.

## Verification Plan

### Automated Tests
1. **데이터 변환 검증**: 변환된 YOLO 라벨 파일이 원본 이미지의 현수막 위치와 일치하는지 시각화 테스트.
2. **모델 추론 테스트**: 샘플 이미지에 대해 YOLO + VLM이 정상적으로 작동하여 결과를 출력하는지 확인.

### Manual Verification
1. **WPF UI 동작 확인**: 실시간 영상 구동 시 렉 없이 현수막 정보가 리스트에 업데이트되는지 확인.
2. **판단 정확도 체크**: 실제 불법 현수막 사진을 넣었을 때 VLM이 "불법"으로 잘 분류하는지 확인.
