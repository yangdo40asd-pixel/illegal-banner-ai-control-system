using System.IO;

namespace BannerGuardSystem.Services;

public class LocalFileVideoSource : IVideoSource
{
    private readonly string _filePath;

    public LocalFileVideoSource(string filePath)
    {
        _filePath = filePath;
    }

    public string GetMediaUri() => _filePath;
    public bool IsRtsp => false;
    public string Label => Path.GetFileName(_filePath);
}
