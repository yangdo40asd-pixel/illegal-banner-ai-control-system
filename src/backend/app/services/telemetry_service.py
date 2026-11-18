import os
import psutil

_nvml_initialized = False
_nvml_available = False

def _init_nvml_if_needed():
    global _nvml_initialized, _nvml_available
    if not _nvml_initialized:
        _nvml_initialized = True
        try:
            import pynvml
            pynvml.nvmlInit()
            _nvml_available = True
        except Exception as e:
            _nvml_available = False

def get_system_telemetry() -> dict:
    """FastAPI 및 하드웨어(GPU, CPU, RAM) 텔레메트리 메트릭을 수집하여 반환합니다."""
    telemetry = {
        "cuda_available": False,
        "gpu_name": "N/A",
        "gpu_utilization": 0.0,
        "vram_used_mb": 0.0,
        "vram_total_mb": 0.0,
        "server_cpu_pct": 0.0,
        "server_ram_mb": 0.0,
    }

    # 1. 프로세스 CPU 및 RAM 점유율
    try:
        proc = psutil.Process(os.getpid())
        telemetry["server_cpu_pct"] = round(proc.cpu_percent(interval=None), 1)
        telemetry["server_ram_mb"] = round(proc.memory_info().rss / 1024 / 1024, 1)
    except Exception:
        pass

    # 2. PyTorch CUDA 가용성 및 디바이스명
    try:
        import torch
        telemetry["cuda_available"] = bool(torch.cuda.is_available())
        if telemetry["cuda_available"]:
            telemetry["gpu_name"] = str(torch.cuda.get_device_name(0))
    except Exception:
        pass

    # 3. NVML 라이브러리를 통한 실시간 GPU 사용률 및 VRAM 추출
    try:
        _init_nvml_if_needed()
        if _nvml_available:
            import pynvml
            handle = pynvml.nvmlDeviceGetHandleByIndex(0)
            util = pynvml.nvmlDeviceGetUtilizationRates(handle)
            mem = pynvml.nvmlDeviceGetMemoryInfo(handle)
            name = pynvml.nvmlDeviceGetName(handle)
            if isinstance(name, bytes):
                name = name.decode("utf-8")
            if name:
                telemetry["gpu_name"] = name
            telemetry["gpu_utilization"] = float(util.gpu)
            telemetry["vram_used_mb"] = round(mem.used / 1024 / 1024, 1)
            telemetry["vram_total_mb"] = round(mem.total / 1024 / 1024, 1)
        elif telemetry["cuda_available"]:
            import torch
            free_b, total_b = torch.cuda.mem_get_info(0)
            telemetry["vram_total_mb"] = round(total_b / 1024 / 1024, 1)
            telemetry["vram_used_mb"] = round((total_b - free_b) / 1024 / 1024, 1)
    except Exception:
        pass

    return telemetry
