# HTTP·WebSocket 계약

상태: 현행 / 최종 검토: 2026-11-17.

기본 주소는 `http://localhost:8000`, WebSocket은 `ws://localhost:8000/ws/alerts`이며 설정으로 변경한다.

| 종류 | 메시지 | 의미 |
|---|---|---|
| HTTP | GET /health | ai_ready, ai_loading, GPU·VRAM·서버 자원 |
| 명령 | cctv_analyze | cctvId, positionMs에 해당하는 영상 프레임 분석 |
| 명령 | toggle_automation | value로 자동 분석 활성 상태 및 배치 요청 |
| 명령 | human_verdict | alertId, verdict, statusCode 등 사람 판정 |
| 결과 | channel_batch | CCTV 채널 탐지 목록 |
| 결과 | vlm_image_batch | 원본 이미지별 VLM 결과 |
| 유지 | ping / pong | 연결 응답 확인 |

```json
{"command":"cctv_analyze","cctvId":"CCTV_01","positionMs":3000}
```

```json
{"MessageType":"channel_batch","CctvId":"CCTV_01","FrameWidth":1920,"FrameHeight":1080,"SourceImage":"","Detections":[]}
```

결과 JSON은 PascalCase를 유지한다. BoundingBox는 해당 이미지/프레임 좌표이며 이미지 축소와 함께 좌표 변환이 필요하다. 이미지에는 Base64 데이터가 포함될 수 있다. 시간 형식과 필드 정의의 기준은 [Python schema](../../src/backend/app/schemas/detection_schema.py) 및 C# Models다.

AI 분류 코드와 사람 판정 `LEGAL/ILLEGAL/REVIEW`, 단속 처리 상태는 별개다. empty Detections도 정상 결과이며 이전 박스를 제거해야 한다.
