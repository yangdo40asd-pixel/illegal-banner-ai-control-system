# 검증 계획

상태: 현행 / 최종 검토: 2026-11-17.

시험은 다음 계층으로 나눈다.

1. 순수 로직: 문자 파서, 법적판정 코드, 경로 우선순위
2. DB: 임시 SQLite의 공통 스키마, 이전 판정 정규화, 저장·중복·상태 전이
3. 계약: PascalCase 배치, HTTP health, WebSocket ping/pong
4. 데스크톱 회귀: 기존 6범주 검증 프로그램을 MSTest에서 실행
5. GPU smoke: 실제 가중치·CUDA·캐시 VLM의 단일 이미지 분석
6. UI: CCTV·VLM·단속 화면의 전환과 결과 표시
7. 배포: 다른 경로와 소스 없는 실행 폴더에서 경로·의존 자산 확인

`dotnet test`는 새로운 동작 시험과 기존 회귀 adapter를 실행한다. CI는 비공개 미디어를 요구하는 `LegacyRegression` 범주를 제외한다. Python 시험은 `pytest.ini`에 정의돼 있으며 torch나 모델이 없어도 순수 파서와 API import가 가능하다.

GPU·UI 시험은 CPU 시험 통과만으로 대체하지 않는다. 기존 소스 문자열 검사도 전체 화면 동작을 증명하지 않는다.

[결과](test-results.md) · [요구사항 추적](traceability.md)
