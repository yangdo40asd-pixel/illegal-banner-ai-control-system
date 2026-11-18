# BannerGuardSystem 실행 폴더 안내

Windows용 로컬 영상 관제 데모입니다. Python을 내장한 독립 설치 프로그램이 아니며, 아래 실행 환경을 준비해야 합니다.

## 준비

- .NET 9 Desktop Runtime, Python 3.13, NVIDIA GPU 및 호환 드라이버.
- 이 배포본의 고정 패키지 목록은 Windows/Python 3.13/CUDA 12.6 환경에서 캡처했습니다. 별도 신규 PC 전체 설치 검증은 수행하지 않았습니다.
- 최초 VLM 실행에는 manifest에 지정된 Qwen 모델 다운로드, 인터넷 및 충분한 저장 공간이 필요합니다. 이미 받은 캐시는 재사용합니다.

배포 폴더에서 PowerShell을 열고 실행합니다.

```powershell
py -3.13 -m venv .venv
& ./.venv/Scripts/python.exe -m pip install -r src/backend/requirements.lock.txt
```

이후 `Start.bat`을 실행합니다. 앱은 이 폴더의 `.venv/Scripts/python.exe`를 자동으로 찾습니다. 준비된 다른 Python을 쓰려면 `configs/paths.local.json`의 PythonExecutable 또는 환경변수 BANNERGUARD_PYTHON_EXECUTABLE로 경로를 지정하세요.

## 데이터와 상태

개인 영상·기존 DB·개인 설정을 포함하지 않습니다. 기본 입력은 `data/samples/`의 합성 영상과 이미지입니다. 합성 예제에서 학습 모델의 탐지를 보장하지 않습니다. 실제 영상 경로는 개인 설정에서 바꿀 수 있습니다.

새 DB와 로그는 `%LOCALAPPDATA%/BannerGuard/`에 기록합니다. `.bannerguard-root` 파일과 폴더 구조를 함께 유지하면 실행 폴더를 옮길 수 있습니다.

서버 연결과 AI 모델 준비는 별도 상태입니다. 앱이 서버를 켠 뒤 모델을 준비하므로 처음에는 대기 표시가 나올 수 있습니다. GPU 0%는 유휴 상태일 수 있으며 로그와 분석 결과를 함께 확인하세요.

AI 판정은 사람의 검토를 보조합니다. 실제 행정 판단이나 실시간 운영 CCTV 연결 실적으로 해석하지 마세요. 제3자 구성요소 안내는 `THIRD_PARTY_NOTICES.md`에 있습니다.
