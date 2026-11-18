namespace BannerGuardSystem.Services;

public interface IVideoSource
{
    string GetMediaUri();
    bool IsRtsp { get; }
    string Label { get; }
}
