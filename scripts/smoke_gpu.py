"""Explicit local GPU smoke check. Never touches the production database."""
import argparse
import json
import os
import sys
import time
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'src/backend'))
from app.core.paths import paths


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--vlm', action='store_true', help='Load the cached VLM and run a full image analysis')
    args = parser.parse_args()
    os.environ['BANNERGUARD_DATABASE_PATH'] = str(root / 'runtime/test-results/gpu-smoke.db')
    os.environ['HF_HUB_OFFLINE'] = '1'
    import torch
    if not torch.cuda.is_available():
        raise SystemExit('CUDA unavailable; GPU test not performed')
    image = sorted(paths.resolve('VlmImageDirectory').glob('*.jpg'))[0]
    start = time.perf_counter()
    if args.vlm:
        from app.services.ai_inference_service import AIInferenceService
        service = AIInferenceService()
        events = service.run_inference_as_event(str(image), 'VLM_SMOKE', True, image.name)
        result = {'mode':'vlm','events':len(events),'completed_vlm':any(e.IsVlmTriggered for e in events)}
        if not result['completed_vlm']: raise RuntimeError('VLM did not complete')
    else:
        from ultralytics import YOLO
        from app.utils.image_io import read_image_bgr
        model = YOLO(str(paths.resolve('YoloModelPath')))
        outputs = model(read_image_bgr(str(image)), device=0, verbose=False)
        result = {'mode':'yolo','detections':len(outputs[0].boxes),'predictor_device':str(model.predictor.device),'result_device':str(outputs[0].boxes.data.device)}
        if outputs[0].boxes.data.device.type != 'cuda': raise RuntimeError('Prediction did not use CUDA')
        if not result['detections']: raise RuntimeError('No detection in reference image')
    result.update(gpu=torch.cuda.get_device_name(0), elapsed_seconds=round(time.perf_counter()-start,2), image=image.name)
    directory = paths.runtime_directory('test-results')
    (directory / f'gpu-{result["mode"]}.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result, ensure_ascii=True))


if __name__ == '__main__':
    main()
