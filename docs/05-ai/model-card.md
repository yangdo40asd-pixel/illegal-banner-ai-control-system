# 모델 카드

상태: 실물·코드 확인 / 최종 검토: 2026-11-17.

| 항목 | 값 |
|---|---|
| 현수막 탐지 | YOLO26s, scale=s |
| 학습 클래스 | banners 1개 |
| 배포 가중치 | models/yolo/best.pt |
| SHA-256 | db9aa382077bb42db1f25c7adfcdc2da1ea28a7db0a0808b1cf21f56e1d67726 |
| 학습 실험 | banner_s_20260506_1909 |
| VLM | Qwen/Qwen2.5-VL-3B-Instruct |
| VLM revision | 66285546d2b821cf421d4f5eb2576359d3770cd3 |
| CUDA 양자화 | 4-bit NF4, bfloat16 연산 |

기준은 [manifest](../../models/manifest.json), 학습 가중치 내부 `yolo26s.yaml`과 로컬 Hugging Face 캐시다. Ultralytics 패키지 버전 숫자와 YOLO 모델 세대는 구별한다.

현수막 학습 가중치가 없으면 일반 사전학습 모델로 조용히 대체하지 않고 오류를 낸다. VLM 문자 인식과 내용 판정은 보조 정보이며 실제 법적 적합성을 단정하는 모델은 아니다.

[평가](evaluation.md) · [데이터](dataset.md)
