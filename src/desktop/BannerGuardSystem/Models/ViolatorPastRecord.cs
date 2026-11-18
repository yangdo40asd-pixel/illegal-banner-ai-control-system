using System;

namespace BannerGuardSystem.Models;

public class ViolatorPastRecord
{
    public DateTime DetectedDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public string FineAmount { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    public string DetectedDateText => DetectedDate.ToString("yyyy-MM-dd");
}