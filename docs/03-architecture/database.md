# 데이터 저장 구조

상태: 현행 / 최종 검토: 2026-11-17.

| 테이블 | 역할 |
|---|---|
| tracked_banners | 탐지 위치·문자·판정·관측 이력 |
| human_reviews | 사람 판정과 검토자·시각·메모 |
| enforcement_records | 단속번호·증거·접수 및 처리 상태 |
| similar_enforcement_reviews | 단속 건 사이 유사도 검토 |
| citizen_reports | 이전 신고 기능의 보존 테이블, 현행 주요 세 화면과 구분 |

`enforcement_records.alert_id`는 사건 식별자이고, 유사 이력은 단속번호를 참조한다. 기존 구현은 모든 관계를 외래키로 강제하지 않으므로 개념 관계와 실제 제약을 혼동하지 않는다.

스키마 기준은 [001-initial.sql](../../database/migrations/001-initial.sql), 이동 당시 원형은 [schema.sql](../../database/schema.sql)이다. C#과 Python이 동일 SQL을 쓰기 잠금 안에서 적용하며 `user_version=1`을 확인한다. 이전 한글 판정값의 정규화는 재실행 가능하게 유지했다.

DB 이동 시 원본 5개 테이블의 행 수와 새 DB가 일치했고 `integrity_check=ok`였다. 원본은 실험 보관 폴더와 외부 백업에 보존한다. 운영 DB와 시험 DB는 `runtime/db`와 `runtime/test-results`로 분리한다.
