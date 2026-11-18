# 프로젝트 개요

상태: 현행 / 최종 검토: 2026-11-17.

BannerGuardSystem은 영상 속 현수막을 탐지하고, 현수막의 문자·맥락을 분석한 뒤 관제관이 검토 결과와 단속 처리 상태를 관리하는 Windows 데스크톱 프로젝트다.

## 해결하려는 문제

여러 영상과 이미지에서 현수막을 직접 찾아 문자를 읽고, 의심 건의 증거와 처리 내역을 서로 다른 도구에서 관리하는 부담을 줄인다. 탐지와 문맥 분석은 AI가 보조하고 최종 검토는 사용자가 수행한다.

## 구현 범위

- 로컬 MP4 4채널 재생, 재생 시각별 YOLO 탐지와 박스 표시
- 폴더 이미지의 문자·맥락 분석 및 합법/불법/재검토 사람 판정
- SQLite 기반 단속 목록, 접수·검토 상태, 유사 이력과 증거 이미지
- PDF·Excel, 기간별 통계 및 설정 관리
- 앱–서버 수명주기, WebSocket 연결, 모델 준비 및 GPU 상태 조회

현재 CCTV 입력은 로컬 데모 영상이다. 공공 CCTV 운영망 접속, 실제 행정 처분 자동화, 법적 판단의 완전 자동화를 완료한 제품으로 설명하지 않는다.

## 기술 구성

WPF/.NET 9, FastAPI, WebSocket, SQLite, Ultralytics YOLO26s, Qwen2.5-VL-3B-Instruct, PyTorch CUDA, LibVLCSharp, QuestPDF, ClosedXML을 사용한다.

[요구사항](../02-requirements/requirements.md) · [아키텍처](../03-architecture/system-architecture.md) · [시험 결과](../06-testing/test-results.md)
