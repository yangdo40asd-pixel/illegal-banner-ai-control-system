namespace BannerGuardSystem.Models;

public class AppSettings
{
    public string ApiBaseUrl { get; set; } = Helpers.RuntimePaths.Get("ApiBaseUrl", "http://localhost:8000");
    public string WsUrl { get; set; } = Helpers.RuntimePaths.Get("WsUrl", "ws://localhost:8000/ws/alerts");
    public int RecurrenceMonths { get; set; } = 3;
    public double TextSimilarityThreshold { get; set; } = 0.8; // 80% similarity
}
