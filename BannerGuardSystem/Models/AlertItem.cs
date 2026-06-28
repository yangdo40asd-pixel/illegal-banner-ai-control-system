using System;

namespace BannerGuardSystem.Models
{
    public enum AlertType { Info, Warning, Error, Success }

    public class AlertItem
    {
        public string Message { get; set; } = string.Empty;
        public AlertType Type { get; set; } = AlertType.Info;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
