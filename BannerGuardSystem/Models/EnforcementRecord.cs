using System;
using System.Collections.Generic;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.Models;

public class EnforcementRecord
{
    public string EnforcementNo { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public string MapQuery { get; set; } = string.Empty;
    public string OcrText { get; set; } = string.Empty;
    public EnforcementViolationType ViolationType { get; set; }
    public EnforcementProcessingStatus Status { get; set; }
    public string PrimaryAction { get; set; } = "상세";
    public string SecondaryAction { get; set; } = string.Empty;

    public string EnforcementNoDisplay => EnforcementNo.StartsWith('#') ? EnforcementNo : $"#{EnforcementNo}";
    public string DetectedAtText => DetectedAt.ToString("yyyy-MM-dd HH:mm");
    public string BannerSummaryOneLine => BannerSimilarityHelper.ToOneLineSummary(OcrText);
    public string StatusDisplay => Status switch
    {
        EnforcementProcessingStatus.FineImposed => "처리완료",
        EnforcementProcessingStatus.Appeal => "검토중",
        EnforcementProcessingStatus.Received => "접수완료",
        EnforcementProcessingStatus.Delinquent => "미처리",
        EnforcementProcessingStatus.Urgent => "긴급",
        _ => Status.ToString()
    };
    public string ViolationTypeDisplay => ViolationType switch
    {
        EnforcementViolationType.IllegalBanner => "불법현수막 게시",
        EnforcementViolationType.IllegalFlyer => "불법 전단지 게시",
        _ => "위반"
    };
}