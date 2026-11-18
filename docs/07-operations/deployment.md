# 실행 폴더와 배포

상태: 현행 절차 / 최종 검토: 2026-11-18.

개발 실행은 루트 `run.bat`, 서버 단독 실행은 `start_server.bat`이다. 로그·DB·임시 이미지·시험 결과는 `runtime/`에 모이고 개인 설정은 `configs/paths.local.json`에 둔다.

`scripts/packaging/package.ps1`는 WPF publish와 Python 소스·공통 설정·SQL·YOLO 가중치·합성 샘플을 포함한 로컬 실행 폴더를 만든다. 이 방식은 Python 인터프리터와 AI 환경을 별도로 필요로 한다. Python까지 내장한 독립 설치 프로그램으로 표현하지 않는다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/packaging/package.ps1
```

산출물은 `runtime/dist/BannerGuardSystem/`이다. 사용자 PC에서는 설정의 PythonExecutable 또는 BANNERGUARD_PYTHON_EXECUTABLE로 준비된 인터프리터를 지정한다. 쓰기 경로는 사용자 데이터 폴더를 사용하도록 배포 설정을 만든다. 개인 영상·실제 DB·소스 백업은 포함하지 않는다.

동봉된 README의 순서로 `.venv`를 만들고 `src/backend/requirements.lock.txt`를 설치하면 앱이 해당 Python을 찾는다. `Start.bat`이 앱 진입점이다. 기본 DB·로그는 `%LOCALAPPDATA%/BannerGuard/`에 기록한다. 배포 스크립트는 기존 목적지를 덮어쓰지 않으므로 재생성 시 다른 `-Destination`을 지정한다.

공백·한글 경로에서 확인하려면 다음 검증기를 사용한다. C# 실제 경로 해석기를 배포 폴더에서 실행하고 Python 결과와 비교하며, 별도의 임시 DB 경로에서 `/health`와 WebSocket ping을 검사한다. 모델은 이 시험에서 로드하지 않는다.

```powershell
python scripts/packaging/verify_package.py "runtime/dist/BannerGuardSystem"
```

검증기는 배포 폴더 안에 `.verification/` 진단 산출물을 만든다. 검증에는 .NET SDK와 Python API 시험 의존성이 필요하다. 최종 배포용 폴더는 검증 폴더와 구분해 새 목적지에 생성할 수 있다. 현재 로컬 시험은 새 PC의 전체 설치 시험과 구분한다.

오류 점검 순서: 설정 경로 → 가중치 해시 → 미디어 존재 → `/health` → WebSocket → AI 결과. GPU 0%만 보고 모델 미사용으로 판단하지 않는다. NVML 수치는 GPU 장치 전체의 사용률이다.

[현재 제약](known-issues.md) · [설치](../04-development/getting-started.md)
