using BannerGuardSystem.Models;

namespace BannerGuardSystem.Helpers;

public static class YoloOverlayMath
{
    public const double DefaultImageWidth = 4032;
    public const double DefaultImageHeight = 2268;
    public const double DefaultVideoWidth = 1920;
    public const double DefaultVideoHeight = 1080;

    public static (double Left, double Top, double Width, double Height) MapToContainer(
        BBox bbox,
        double imageWidth,
        double imageHeight,
        double containerWidth,
        double containerHeight)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return (0, 0, 0, 0);

        // Uniform(레터박스) — MediaElement Stretch=Uniform 과 동일
        double scale = Math.Min(containerWidth / imageWidth, containerHeight / imageHeight);
        double renderedW = imageWidth * scale;
        double renderedH = imageHeight * scale;
        double offsetX = (containerWidth - renderedW) / 2;
        double offsetY = (containerHeight - renderedH) / 2;

        return (
            bbox.X * scale + offsetX,
            bbox.Y * scale + offsetY,
            bbox.Width * scale,
            bbox.Height * scale);
    }

    public static bool IsValid(BBox bbox) => bbox.Width > 0 && bbox.Height > 0;
}