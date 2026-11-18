# 모델

`yolo/best.pt`는 기존 운영 가중치를 해시 변경 없이 이동한 것이다. 모델 식별은 [manifest.json](manifest.json)을 기준으로 한다. `python scripts/verify.py`로 검사한다.

Qwen 가중치는 저장소에 복제하지 않는다. manifest의 모델 ID와 revision을 Hugging Face 캐시에서 읽거나 최초 실행 때 다운로드한다. 대용량 모델의 배포 권한과 출처는 프로젝트 자체 라이선스와 별도로 확인한다.
