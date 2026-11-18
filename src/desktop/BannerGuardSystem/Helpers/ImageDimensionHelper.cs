using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace BannerGuardSystem.Helpers;

public static class ImageDimensionHelper
{
    public static bool TryDecodeFromBase64(string? imageData, out double width, out double height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(imageData))
            return false;

        try
        {
            var bytes = ExtractBytes(imageData);
            if (bytes == null) return false;

            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;

            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static BitmapSource? LoadBitmap(string? imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData))
            return null;

        try
        {
            if (imageData.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) || IsBase64(imageData))
            {
                var bytes = ExtractBytes(imageData);
                if (bytes == null) return null;

                var stream = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = stream;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }

            var path = imageData;
            if (!File.Exists(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imageData);

            if (!File.Exists(path))
                return null;

            var uri = new BitmapImage();
            uri.BeginInit();
            uri.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            uri.CacheOption = BitmapCacheOption.OnLoad;
            uri.EndInit();
            uri.Freeze();
            return uri;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ExtractBytes(string imageData)
    {
        var payload = imageData;
        if (imageData.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = imageData.IndexOf(',');
            if (comma < 0) return null;
            payload = imageData[(comma + 1)..];
        }

        return Convert.FromBase64String(payload.Trim().Replace("\r", "").Replace("\n", ""));
    }

    private static bool IsBase64(string value)
    {
        value = value.Trim();
        return value.Length >= 20 && value.Length % 4 == 0;
    }
}