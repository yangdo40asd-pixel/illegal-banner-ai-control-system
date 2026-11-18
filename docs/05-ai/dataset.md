# 데이터 구성

상태: 로컬 출처 파일 확인 / 최종 검토: 2026-11-17.

학습 데이터 원본 안내에는 Roboflow `hanyangcomgong/comgong_labels`, 버전 1, 6,263개 이미지와 CC BY 4.0 표기가 있다. 출처는 [Roboflow 데이터 페이지](https://universe.roboflow.com/hanyangcomgong/comgong_labels)이며 실제 재배포 전 원본 표시와 포함 자료의 범위를 확인한다.

데이터셋 폴더의 README는 640×640 크기 변환과 증강 이력을 기록한다. 원본 train/valid/test 및 라벨은 `data/private/training/banners`에 보존하고 저장소에 통째로 추가하지 않는다. 실행 설정 예시는 [dataset.example.yaml](../../training/yolo/configs/dataset.example.yaml)을 사용한다.

기존 데모 MP4·이미지는 `data/private/demo/`에 있다. 공개 샘플은 `scripts/create_samples.py`가 만든 합성 자료로 구별하며 학습·성능 평가에 사용한 원천 데이터로 취급하지 않는다.
