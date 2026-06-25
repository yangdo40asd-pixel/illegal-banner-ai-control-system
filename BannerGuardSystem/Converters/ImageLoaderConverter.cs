using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.Converters;

public class ImageLoaderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;

        string input = value.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(input)) return null;

        try
        {
            // Check if it's base64 data (embedded crop snap)
            if (input.StartsWith("data:image") || IsBase64String(input))
            {
                string base64Str = input;
                if (base64Str.Contains(","))
                {
                    base64Str = base64Str.Split(',')[1];
                }
                
                // Clean up any potential whitespace/newlines in the base64 string
                base64Str = base64Str.Trim().Replace("\r", "").Replace("\n", "");
                
                try
                {
                    byte[] binaryData = System.Convert.FromBase64String(base64Str);
                    // Do not wrap MemoryStream in a using block to avoid race conditions 
                    // where WPF accesses the stream after disposal.
                    var stream = new MemoryStream(binaryData);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = stream;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze(); // Crucial to prevent thread affinity locks in WPF
                    
                    BackendManager.Log($"[ImageLoaderConverter] Successfully loaded crop image from Base64 (binary size: {binaryData.Length} bytes)");
                    return bi;
                }
                catch (Exception ex)
                {
                    BackendManager.Log($"[ImageLoaderConverter-Err] Base64 decoding/Bitmap creation failed: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
            // Check if it's a local file path
            else
            {
                string resolvedPath = input;
                if (!File.Exists(resolvedPath))
                {
                    resolvedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, input);
                }

                if (File.Exists(resolvedPath))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    string absolutePath = Path.GetFullPath(resolvedPath);
                    bi.UriSource = new Uri(absolutePath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                else
                {
                    BackendManager.Log($"[ImageLoaderConverter-Warn] Local file path not found: {resolvedPath}");
                }
            }
        }
        catch (Exception ex)
        {
            BackendManager.Log($"[ImageLoaderConverter-Err] Conversion failed for input (length: {input.Length}): {ex.Message}");
        }

        return null;
    }

    private bool IsBase64String(string base64)
    {
        base64 = base64.Trim();
        if (base64.Length < 20) return false;
        return (base64.Length % 4 == 0) && System.Text.RegularExpressions.Regex.IsMatch(base64, @"^[a-zA-Z0-9\+/]*={0,3}$");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
