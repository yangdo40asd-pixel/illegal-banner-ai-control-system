using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BannerGuardSystem.Helpers;

namespace BannerGuardSystem.Services;

internal static class ImageSimilarityHelper
{
    private const int HashWidth = 9;
    private const int HashHeight = 8;

    public static int ComparePercent(string? base64A, string? base64B)
    {
        var hashA = ComputeDHash(base64A);
        var hashB = ComputeDHash(base64B);
        if (hashA == null || hashB == null)
            return 0;

        var distance = HammingDistance(hashA.Value, hashB.Value);
        return (int)Math.Round((64 - distance) * 100.0 / 64.0);
    }

    private static ulong? ComputeDHash(string? base64)
    {
        var bitmap = ImageDimensionHelper.LoadBitmap(base64);
        if (bitmap == null)
            return null;

        try
        {
            var scaled = new TransformedBitmap(
                bitmap,
                new ScaleTransform(
                    HashWidth / (double)bitmap.PixelWidth,
                    HashHeight / (double)bitmap.PixelHeight));

            var gray = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
            gray.Freeze();

            var pixels = new byte[HashWidth * HashHeight];
            gray.CopyPixels(pixels, HashWidth, 0);

            ulong hash = 0;
            var bit = 0;
            for (var y = 0; y < HashHeight; y++)
            {
                for (var x = 0; x < HashWidth - 1; x++)
                {
                    if (pixels[y * HashWidth + x] < pixels[y * HashWidth + x + 1])
                        hash |= 1UL << bit;
                    bit++;
                }
            }

            return hash;
        }
        catch
        {
            return null;
        }
    }

    private static int HammingDistance(ulong a, ulong b)
    {
        var xor = a ^ b;
        var count = 0;
        while (xor != 0)
        {
            count += (int)(xor & 1);
            xor >>= 1;
        }
        return count;
    }
}