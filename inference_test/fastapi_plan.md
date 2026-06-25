# FastAPI 서버 구축 계획서

**작성일**: 2026-05-19  
**목적**: 기존 `test_pipeline.py` 추론 코드를 API 서버로 전환하여 WPF 클라이언트와 실시간 통신 기반 마련

---

## 1. 서버 역할 요약

```
[WPF C# 클라이언트]
      │  HTTP POST (이미지 업로드)
      │  WebSocket (실시간 결과 수신)
      ▼
[FastAPI 서버 - Python]
      │
      ├── YOLO: 현수막 위치 탐지
      ├── Qwen2.5-VL: OCR + 불법 판정
      └── JSON 결과 반환 / WebSocket 푸시
```

---

## 2. API 엔드포인트 설계

### [POST] `/analyze` — 이미지 1장 분석

| 항목 | 내용 |
| :--- | :--- |
| 요청 | `multipart/form-data` 이미지 파일 |
| 처리 | YOLO 탐지 → Qwen2.5-VL 판독 |
| 응답 | JSON (탐지 수, 각 박스별 판정 결과) |

**응답 예시:**
```json
{
  "filename": "banner_001.jpg",
  "detections": [
    {
      "box_id": 1,
      "bbox": [120, 45, 890, 210],
      "confidence": 0.97,
      "text": "봄맞이 빅세일 50% 할인",
      "environment": "가로수에 흰색 끈으로 묶여 설치",
      "verdict": "불법",
      "reason": "가로수 끈 고정은 정식 게시대 방식이 아님",
      "result_image_base64": "..."
    }
  ],
  "processed_at": "2026-05-19T12:00:00"
}
```

---

### [WebSocket] `/ws` — 실시간 결과 스트리밍

- WPF 클라이언트가 연결 유지
- 서버가 분석 완료 시 즉시 JSON 푸시
- 연결 끊김 시 자동 재연결 처리

---

### [GET] `/health` — 서버 상태 확인

```json
{ "status": "ok", "model": "Qwen2.5-VL-3B", "device": "cuda" }
```

---

### [GET] `/results` — 최근 탐지 이력 조회 (DB 연동 후)

- 날짜 필터, 판정 결과 필터 파라미터 지원 예정

---

## 3. 파일 구조 설계

```
inference_test/
├── test_pipeline.py          ← 기존 추론 코드 (수정 없음, 재사용)
├── server.py                 ← FastAPI 서버 (신규 작성)
├── models.py                 ← Pydantic 요청/응답 스키마
├── inference_engine.py       ← YOLO + VLM 추론 로직 분리
└── database.py               ← SQLite 연동 (Step 6에서 추가)
```

> **핵심 원칙**: `test_pipeline.py`의 추론 로직을 `inference_engine.py`로 분리하여 재사용.  
> `server.py`는 API 라우팅만 담당하고 추론 로직을 직접 갖지 않음.

---

## 4. 구현 순서 (단계별)

### Step 4-1. `inference_engine.py` 분리 작성
- `test_pipeline.py`에서 `process_image()`, `run_vlm_analysis()` 함수를 모듈로 분리
- 모델(YOLO, VLM)을 서버 시작 시 1회만 로드하는 싱글턴 구조로 변경

### Step 4-2. `server.py` 기본 구조 작성
- FastAPI 앱 생성
- `/health`, `/analyze` 엔드포인트 구현
- 이미지 업로드 → 추론 → JSON 반환 기본 흐름 확인

### Step 4-3. WebSocket 추가
- `/ws` 엔드포인트 구현
- 분석 결과를 WPF 클라이언트에 실시간 푸시하는 로직 추가

### Step 4-4. 결과 이미지 Base64 인코딩 반환
- 바운딩 박스가 그려진 결과 이미지를 Base64로 인코딩하여 JSON에 포함
- WPF에서 디코딩하여 화면에 바로 표시 가능하도록

### Step 4-5. 서버 구동 테스트
- `uvicorn server:app --host 0.0.0.0 --port 8000` 으로 서버 기동
- `curl` 또는 Python `requests`로 `/analyze` 동작 확인

---

## 5. 기술 의존성

```
pip install fastapi uvicorn python-multipart
```

- `fastapi`: API 서버 프레임워크
- `uvicorn`: ASGI 서버 (FastAPI 실행용)
- `python-multipart`: 파일 업로드 지원

---

## 6. WPF 연동 시 통신 흐름 (미리보기)

```
[WPF] 이미지 선택 또는 CCTV 프레임 캡처
   │
   │ POST /analyze (multipart)
   ▼
[FastAPI] YOLO + Qwen2.5-VL 추론
   │
   │ JSON 응답 (또는 WebSocket 푸시)
   ▼
[WPF] 결과 이미지 표시 + 판정 텍스트 카드 업데이트
```

---

## 7. 예상 작업 시간

| Step | 내용 | 예상 시간 |
| :--- | :--- | :--- |
| 4-1 | inference_engine.py 분리 | 30분 |
| 4-2 | server.py 기본 구현 | 1시간 |
| 4-3 | WebSocket 추가 | 1시간 |
| 4-4 | Base64 이미지 반환 | 30분 |
| 4-5 | 테스트 및 디버깅 | 1시간 |

**총 예상**: 약 4~5시간

---

**다음 실행 명령**: `python server.py` 또는 `uvicorn server:app --reload`  
**완료 기준**: `/analyze` 엔드포인트에 이미지를 POST하면 판정 JSON이 정상 반환될 것
