using System;
using System.Collections.Generic;

namespace BannerGuardSystem.Models;

public class EnforcementRecord
{
    public string EnforcementNo { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public string MapQuery { get; set; } = string.Empty;
    public EnforcementViolationType ViolationType { get; set; }
    public string ViolatorName { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public int FineAmountManwon { get; set; }
    public EnforcementProcessingStatus Status { get; set; }
    public string Assignee { get; set; } = string.Empty;
    public string PrimaryAction { get; set; } = "상세";
    public string SecondaryAction { get; set; } = string.Empty;
    public bool IsHighRisk { get; set; }
    public int PastViolationCount { get; set; }
    public int AccumulatedFineManwon { get; set; }
    public List<ViolatorPastRecord> PastRecords { get; set; } = [];

    public string EnforcementNoDisplay => EnforcementNo.StartsWith('#') ? EnforcementNo : $"#{EnforcementNo}";
    public string DetectedAtText => DetectedAt.ToString("yyyy-MM-dd HH:mm");
    public string ViolatorDisplay => string.IsNullOrEmpty(BusinessName)
        ? ViolatorName
        : $"{ViolatorName} (상호:{BusinessName})";
    public string FineAmountDisplay => $"{FineAmountManwon:N0}만원";
    public string StatusDisplay => Status switch
    {
        EnforcementProcessingStatus.FineImposed => "부과완료",
        EnforcementProcessingStatus.Appeal => "이의신청",
        EnforcementProcessingStatus.Received => "접수완료",
        EnforcementProcessingStatus.Delinquent => "체납",
        EnforcementProcessingStatus.Urgent => "긴급",
        _ => Status.ToString()
    };
    public string ViolationTypeDisplay => ViolationType switch
    {
        EnforcementViolationType.IllegalBanner => "불법현수막 게시",
        EnforcementViolationType.IllegalFlyer => "불법 전단지 게시",
        _ => "위반"
    };
    public string RiskSummary => IsHighRisk
        ? $"최근 3년 내 {PastViolationCount}회 적발 · 누적 과태료 {AccumulatedFineManwon:N0}만원"
        : string.Empty;
}