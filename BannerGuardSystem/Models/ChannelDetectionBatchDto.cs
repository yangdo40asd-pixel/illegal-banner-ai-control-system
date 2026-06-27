using System.Collections.Generic;

namespace BannerGuardSystem.Models;

/// <summary>
/// channel_batch = CCTV(MP4) 오버레이용,
/// vlm_image_batch = VLM검증(WPF/image) Feed용.
/// </summary>
public class ChannelDetectionBatchDto
{
    public string MessageType { get; set; } = "channel_batch";
    public string CctvId { get; set; } = string.Empty;
    public int FrameWidth { get; set; } = 1920;
    public int FrameHeight { get; set; } = 1080;
    public string SourceImage { get; set; } = string.Empty;
    public List<BannerDetectionEventDto> Detections { get; set; } = new();
}