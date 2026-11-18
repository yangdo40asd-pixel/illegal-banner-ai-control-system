# 설치 및 실행

상태: 현행 / 최종 검토: 2026-11-17.

## 검증 환경

Windows, .NET SDK 9.0.318, Python 3.13.15, RTX 3080 10GB. 현재 캡처된 AI 의존성은 PyTorch 2.14.0+cu126, Ultralytics 8.4.150, Transformers 5.17.0이다. 고정 목록은 [requirements.lock.txt](../../src/backend/requirements.lock.txt)에 있다. 이 목록은 실제 동작한 환경에서 생성했으며 깨끗한 신규 환경 설치 시험과는 구분한다.

## 시작

프로젝트 루트에서 실행한다. 특정 드라이브나 폴더 이름은 요구하지 않는다.

```powershell
.\setup.bat
.\.venv\Scripts\python.exe scripts/verify.py
.\run.bat
```

`setup.ps1`는 Python 3.13으로 `.venv`를 만들고 고정 의존성을 설치한 뒤 솔루션을 빌드한다. 다른 인터프리터를 지정하려면 `-Python <python.exe>`를 사용한다. Python을 이미 준비했다면 `-SkipPython`으로 빌드만 수행할 수 있다.

기본 제공 파일은 `data/samples/`의 합성 영상·이미지다. 합성 파일을 다시 만들려면 `.\.venv\Scripts\python.exe scripts/create_samples.py`를 실행한다. 보유한 영상과 이미지는 `configs/paths.local.json`의 `CctvMediaDirectory`, `VlmImageDirectory`에 지정한다.

설정 우선순위는 `BANNERGUARD_<키의 SNAKE_CASE>` 환경변수 → `configs/paths.local.json` → `configs/paths.example.json`이다. 예: `BANNERGUARD_DATABASE_PATH`. 상대 경로는 프로젝트 기준이다. 앱이 서버를 실행할 때 같은 설정을 전달한다.

백엔드만 실행하려면 `start_server.bat`을 사용한다. 최초 Qwen 실행은 manifest에 고정한 revision을 다운로드하므로 네트워크와 충분한 디스크 공간이 필요하다. `HF_HUB_OFFLINE=1`이면 이미 내려받은 캐시만 사용한다.

## 시험

```powershell
dotnet test BannerGuardSystem.sln -c Release
.\.venv\Scripts\python.exe -m pip install -r src/backend/requirements.test.txt
.\.venv\Scripts\python.exe -m pytest tests/backend -q
.\.venv\Scripts\python.exe scripts/smoke_gpu.py
.\.venv\Scripts\python.exe scripts/smoke_gpu.py --vlm
```

GPU 시험은 로컬 모델·미디어·CUDA를 필요로 한다. CPU API 시험에서는 `BANNERGUARD_DISABLE_AI=1`로 모델 초기화를 생략한다. 화면 설정 JSON과 실행 경로 JSON은 용도가 다르며 개인 설정은 Git에 포함하지 않는다.
