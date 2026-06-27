from pydantic import BaseModel
from datetime import datetime
from typing import List, Literal, Optional

class BBox(BaseModel):
    X: int
    Y: int
    Width: int
    Height: int

class BannerDetectionEvent(BaseModel):
    Id: str
    CctvId: str
    LocationAddress: str
    Timestamp: datetime
    TimestampOffset: str = "00:00:00"  # C# TimeSpan과 1:1 파싱을 호환하기 위해 string 포맷 사용
    HasBanner: bool
    BoundingBox: BBox
    Confidence: float = 0.0
    OcrText: str
    OcrConfidence: float
    IsVlmTriggered: bool
    VlmAnalysis: str
    IsRepeatOffender: bool
    RepeatCount: int
    CropImageUri: str
    CroppedImageBase64: Optional[str] = ""
    FullImageBase64: Optional[str] = ""
    StatusCode: str = "PENDING_VLM"


class ChannelDetectionBatch(BaseModel):
    """channel_batch=CCTV(MP4), vlm_image_batch=VLM검증(WPF/image)."""
    MessageType: Literal["channel_batch", "vlm_image_batch"] = "channel_batch"
    CctvId: str
    FrameWidth: int = 1920
    FrameHeight: int = 1080
    SourceImage: str = ""
    Detections: List[BannerDetectionEvent] = []


class BannerAlert(BaseModel):
    AlertId: str
    CctvId: str
    CctvName: str
    LocationAddress: str
    Gu: str
    Dong: str
    DetectedAt: datetime
    Confidence: float
    OcrText: str
    AiVerdict: str  # "ILLEGAL" | "LEGAL" | "UNCERTAIN"
    AiReason: str
    Stage: int  # 1=YOLO, 2=OCR, 3=VLM
    CroppedImagePath: str
    CroppedImageBase64: str = ""
    FullImageBase64: str = ""
    IsRecurring: bool = False
    RecurrenceCount: int = 0
    RecurrenceDetail: str = ""
    Status: str = "Pending"  # Pending | Confirmed | Rejected
    ConfirmedByUser: Optional[str] = None
    ConfirmedAt: Optional[datetime] = None
    StatusCode: str = "PENDING_VLM"
