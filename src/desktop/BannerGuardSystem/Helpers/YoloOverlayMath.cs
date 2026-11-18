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

    /// <summary>
    /// 두 바운딩 박스 간의 IoU(Intersection over Union)를 계산합니다 (0.0 ~ 1.0).
    /// </summary>
    public static double CalculateIoU(BBox a, BBox b)
    {
        double x1 = Math.Max(a.X, b.X);
        double y1 = Math.Max(a.Y, b.Y);
        double x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        double y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        double intersectionW = Math.Max(0, x2 - x1);
        double intersectionH = Math.Max(0, y2 - y1);
        double intersectionArea = intersectionW * intersectionH;

        if (intersectionArea <= 0)
            return 0.0;

        double areaA = a.Width * a.Height;
        double areaB = b.Width * b.Height;
        double unionArea = areaA + areaB - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0.0;
    }
}