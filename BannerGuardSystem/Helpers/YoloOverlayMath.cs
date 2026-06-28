using BannerGuardSystem.Models;

namespace BannerGuardSystem.Helpers;

public static class YoloOverlayMath
{
    public const double DefaultImageWidth = 4032;
    public const double DefaultImageHeight = 2268;
    public const double DefaultVideoWidth = 1920;
    public const double DefaultVideoHeight = 1080;

    /// <summary>
    /// Uniform(letterbox) — VLC VideoView 기본 비율 유지와 동일. CCTV 2x2 셀에 맞춤.
    /// </summary>
    public static (double Left, double Top, double Width, double Height) MapToContainer(
        BBox bbox,
        double imageWidth,
        double imageHeight,
        double containerWidth,
        double containerHeight)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return (0, 0, 0, 0);

        double scale = Math.Min(containerWidth / imageWidth, containerHeight / imageHeight);
        double displayW = imageWidth * scale;
        double displayH = imageHeight * scale;
        double offsetX = (containerWidth - displayW) / 2.0;
        double offsetY = (containerHeight - displayH) / 2.0;

        return (
            offsetX + bbox.X * scale,
            offsetY + bbox.Y * scale,
            bbox.Width * scale,
            bbox.Height * scale);
    }

    public static (double DisplayWidth, double DisplayHeight, double OffsetX, double OffsetY) GetUniformLayout(
        double imageWidth,
        double imageHeight,
        double containerWidth,
        double containerHeight)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return (0, 0, 0, 0);

        double scale = Math.Min(containerWidth / imageWidth, containerHeight / imageHeight);
        double displayW = imageWidth * scale;
        double displayH = imageHeight * scale;
        return (displayW, displayH, (containerWidth - displayW) / 2.0, (containerHeight - displayH) / 2.0);
    }

    public static bool IsValid(BBox bbox) => bbox.Width > 0 && bbox.Height > 0;
}