using System;

namespace BannerGuardSystem.Services;

public class RtspVideoSource : IVideoSource
{
    private readonly string _rtspUrl;
    private readonly string _username;
    private readonly string _password;

    public RtspVideoSource(string rtspUrl, string username = "", string password = "")
    {
        _rtspUrl = rtspUrl;
        _username = username;
        _password = password;
    }

    public string GetMediaUri()
    {
        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            try
            {
                var uri = new Uri(_rtspUrl);
                return $"{uri.Scheme}://{_username}:{_password}@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
            }
            catch
            {
                return _rtspUrl;
            }
        }
        return _rtspUrl;
    }

    public bool IsRtsp => true;
    public string Label => _rtspUrl;
}
