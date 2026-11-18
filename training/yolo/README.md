# YOLO 학습 재현

현재 가중치의 학습 원본은 `banner_s_20260506_1909` 실행이다. 결과는 `experiments/results/yolo`에 보존돼 있다.

```powershell
python training/yolo/train.py --data training/yolo/configs/dataset.example.yaml --model data/private/pretrained/yolo26s.pt
```

원천 데이터를 `data/private/training/banners`에 준비한다. 실행 시 루트 기준 상대 경로를 실제 절대 경로로 해석한 YAML을 `runtime/training`에 만들며 원본 실험 설정을 덮어쓰지 않는다. 기존 200 epoch, batch 16 등의 학습 설정은 유지했다. 이번 리팩토링에서는 재학습을 실행하지 않았다.
