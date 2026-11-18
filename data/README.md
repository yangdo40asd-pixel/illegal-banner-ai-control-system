# 데이터

- `samples/`: 생성 코드로 재현되는 합성 영상·이미지. 실제 학습 성능의 근거가 아니다.
- `private/demo/`: 기존 데모 영상과 이미지, 로컬 보존 및 Git 제외.
- `private/training/`: 학습 이미지·라벨, 로컬 보존 및 Git 제외.
- `private/archives/`: 원본 압축본.
- `private/legacy-models/`: 이전 모델의 보관본. 삭제하지 않았다.

합성 자료 생성: `python scripts/create_samples.py`. 기존 개인 파일을 덮어쓰지 않는다. 데이터 출처와 학습 구성은 [데이터 문서](../docs/05-ai/dataset.md)에 있다.
