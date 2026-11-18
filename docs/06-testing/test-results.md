# 리팩토링 검증 결과

검증 기간: 2026-11-17~18. 구조 변경 전후 비교 기록이며 전체 데이터 정확도 평가가 아니다.

| 검증 | 결과 | 근거 |
|---|---|---|
| 변경 전 Release 빌드 | 통과 | 기존 NU1701 호환성 경고 존재 |
| 변경 전 기존 콘솔 시험 | 통과 | 6개 범주, 종료 0 |
| 경로 통합·소스 이동·DB 이동 후 기존 회귀 | 통과 | p2/p3/p4 로그 |
| DB 보존 | 통과 | 5개 테이블 행 수 일치, integrity_check=ok |
| YOLO 가중치 보존 | 통과 | manifest SHA-256 일치 |
| Python 메서드 추출 | 통과 | 이동한 메서드의 AST 본문 원본과 동일 |
| MSTest | 6개 통과 | 기존 6범주 회귀 adapter + 독립 동작 시험 5개, Excel 파일 재개방 및 실제 값 포함 |
| Python CPU 시험 | 11개 통과 | 파서·설정·DB·HTTP·WebSocket |
| 실제 CUDA YOLO | 통과 | predictor/result device=cuda:0, 현수막 1개 탐지 |
| 실제 VLM | 단일 샘플 통과 | 최신 코드·고정 revision 재시험 약 45.31초, 분석 이벤트 1개 |
| UI 실행 | 주요 화면 열림 확인 | computer-use로 CCTV·VLM·단속 전환 확인 |
| 별도 경로 배포본 | 통과 | 공백·한글 및 다른 cwd에서 실제 C# 경로 해석기와 Python 결과 일치, HTTP·WS ping 성공 |
| 배포 개인정보 제외 | 통과 | DB·개인 설정·data/private·과거 이미지 캐시 없음, 가중치·합성 포스터 해시 및 미디어 검사 |
| 문서 링크 | 통과 | scripts/check_docs.py, 현행 문서 대상, archive의 과거 링크 제외 |
| 신규 환경 전체 설치 | 미실행 | lock은 현재 동작 환경을 캡처한 것 |
| 원격 CI | 미실행 | workflow 작성, push/원격 실행은 수행하지 않음 |

DB 행 수: tracked_banners 177, citizen_reports 7, human_reviews 0, enforcement_records 2, similar_enforcement_reviews 2. 이는 이동 직전·직후 비교이며 이후 사용자 실행으로 증가할 수 있다.

원시 로그·TRX·JUnit·GPU JSON은 로컬 `runtime/logs`와 `runtime/test-results`에 있다.

UI 검증에서 Python의 cp949 출력으로 서버가 종료되는 문제를 발견해 자식 프로세스 UTF-8 및 서버 출력 인코딩을 명시했다. 실제 개인 입력 Feed 8개 중 7개 분석과 1개 미탐지를 관찰했다. 모든 화면 버튼 수동 검증은 별도 범위로 구분한다.

11월 18일 공백·한글 경로의 실행 폴더에서도 앱 시작 → 자식 서버 자동 기동 → 모델 준비를 확인했다. `/health`의 ai_ready=true, CUDA RTX 3080, 실제 GPU 사용률과 합성 영상 4채널 재생·약 96% 탐지 박스가 표시됐다. 이는 새 PC 설치나 탐지 정확도 평가를 대신하지 않는다.
