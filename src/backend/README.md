# FastAPI 백엔드

루트 `start_server.bat` 또는 설정된 Python으로 `app/main.py`를 실행한다. API는 `/health`, `/ws/alerts`이며 공통 `configs`를 읽는다.

- `core`: 경로·AI 수명주기·공유 DB 마이그레이션
- `api`: 명령 수신·배치 송신
- `services`: 분석 조정·미디어·텔레메트리·기존 호환 서비스
- `inference`: 모델 초기화·순수 규칙/파서·VLM·YOLO·결과 변환
- `repositories`: SQLite 저장 구현. 기존 services/db_service import는 호환 진입점으로 유지한다.

독립 패키지 선언은 pyproject.toml, 동작 환경 고정은 requirements.lock.txt다. CPU 시험만 준비할 때는 requirements.test.txt를 사용한다. 전체 실행은 모델·미디어·CUDA 설정이 추가로 필요하다.
