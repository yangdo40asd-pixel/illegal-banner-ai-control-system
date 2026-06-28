using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using System.ComponentModel;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public class AlertService : INotifyPropertyChanged
{
    private readonly WebSocketService _wsService;
    private readonly DatabaseService _dbService;
    private readonly HashSet<string> _vlmCompletedKeys = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<BannerDetectionEventDto> AlertFeed { get; } = new();
    public ICollectionView AlertFeedView { get; }

    private int _todayDetectionCount = 0;
    public int TodayDetectionCount
    {
        get => _todayDetectionCount;
        set
        {
            if (_todayDetectionCount != value)
            {
                _todayDetectionCount = value;
                OnPropertyChanged();
            }
        }
    }

    private int _pendingCount = 0;
    public int PendingCount
    {
        get => _pendingCount;
        set
        {
            if (_pendingCount != value)
            {
                _pendingCount = value;
                OnPropertyChanged();
            }
        }
    }

    private int _completedCount = 0;
    public int CompletedCount
    {
        get => _completedCount;
        set
        {
            if (_completedCount != value)
            {
                _completedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public event Action? AlertsUpdated;
    public event Action<BannerDetectionEventDto>? VlmCompletedAlertReceived;
    /// <summary>VLM 긴급 → 단속 DB 등록·판정 변경 시 단속 현황 갱신용.</summary>
    public event Action? EnforcementListChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void NotifyEnforcementListChanged() => EnforcementListChanged?.Invoke();

    public AlertService(WebSocketService wsService, DatabaseService dbService)
    {
        _wsService = wsService;
        _dbService = dbService;
        
        AlertFeedView = CollectionViewSource.GetDefaultView(AlertFeed);
        
        _wsService.OnAlertReceived += OnAlertReceived;
        _wsService.OnChannelBatchReceived += OnChannelBatchReceived;
        _wsService.OnVlmImageBatchReceived += OnVlmImageBatchReceived;
    }

    private void OnChannelBatchReceived(ChannelDetectionBatchDto batch)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (batch.Detections == null || batch.Detections.Count == 0)
                return;

            var representative = batch.Detections
                .Where(d => d.HasBanner)
                .OrderByDescending(d => d.Confidence)
                .FirstOrDefault()
                ?? batch.Detections[0];

            ProcessCctvChannelAlert(representative, batch.SourceImage);
            AlertsUpdated?.Invoke();
        });
    }

    /// <summary>CCTV channel_batch — 채널당 로그 1건. LIVE 갱신 시 기존 항목만 업데이트.</summary>
    private void ProcessCctvChannelAlert(BannerDetectionEventDto alertDto, string? sourceImage)
    {
        var existing = AlertFeed.FirstOrDefault(x => x.CctvId == alertDto.CctvId);
        if (existing == null)
        {
            AlertFeed.Insert(0, alertDto);
            TodayDetectionCount++;
            PendingCount++;
            if (AlertFeed.Count > 100)
                AlertFeed.RemoveAt(AlertFeed.Count - 1);
            return;
        }

        existing.BoundingBox = alertDto.BoundingBox;
        existing.CroppedImageBase64 = alertDto.CroppedImageBase64;
        existing.FullImageBase64 = alertDto.FullImageBase64;
        existing.Confidence = alertDto.Confidence;
        existing.HasBanner = alertDto.HasBanner;
        existing.Timestamp = alertDto.Timestamp;
        if (sourceImage != null)
            existing.SourceImage = sourceImage;

        AlertFeedView.Refresh();
    }

    private void OnAlertReceived(BannerDetectionEventDto alertDto)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            ProcessAlertForFeed(alertDto, false, null);
            AlertsUpdated?.Invoke();
        });
    }

    private void OnVlmImageBatchReceived(ChannelDetectionBatchDto batch)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var vlmCompleted = batch.Detections.Where(d => d.IsVlmTriggered).ToList();
            if (vlmCompleted.Count == 0) return;

            BannerDetectionEventDto? lastProcessed = null;
            foreach (var alertDto in vlmCompleted)
            {
                alertDto.SourceImage = batch.SourceImage;
                ProcessAlertForFeed(alertDto, false, batch.SourceImage);
                lastProcessed = alertDto;
            }

            AlertsUpdated?.Invoke();
            if (lastProcessed != null)
                VlmCompletedAlertReceived?.Invoke(lastProcessed);
        });
    }

    private void ProcessAlertForFeed(BannerDetectionEventDto alertDto, bool fromSameBatch, string? sourceImage)
    {
        bool isVlmBySource = !string.IsNullOrEmpty(sourceImage);
        string incidentKey = isVlmBySource ? sourceImage! : alertDto.CctvId;

        bool SameFeedItem(BannerDetectionEventDto a) =>
            isVlmBySource
                ? string.Equals(a.SourceImage, sourceImage, StringComparison.OrdinalIgnoreCase)
                : (!string.IsNullOrEmpty(alertDto.Id) && a.Id == alertDto.Id)
                  || (a.CctvId == alertDto.CctvId && a.OcrText == alertDto.OcrText);

        var existing = AlertFeed.FirstOrDefault(SameFeedItem);
        bool wasVlmTriggered = existing?.IsVlmTriggered ?? false;
        bool isPreviewRefresh = existing != null && wasVlmTriggered && !alertDto.IsVlmTriggered;

        if (existing != null)
        {
            if (fromSameBatch || isPreviewRefresh)
            {
                existing.BoundingBox = alertDto.BoundingBox;
                existing.CroppedImageBase64 = alertDto.CroppedImageBase64;
                existing.FullImageBase64 = alertDto.FullImageBase64;
                existing.Confidence = alertDto.Confidence;
                existing.HasBanner = alertDto.HasBanner;
                existing.Timestamp = alertDto.Timestamp;
                if (isVlmBySource)
                    existing.SourceImage = sourceImage!;
            }
            else
            {
                existing.UpdateFrom(alertDto);
                if (isVlmBySource)
                    existing.SourceImage = sourceImage!;
            }

            AlertFeedView.Refresh();
        }
        else
        {
            AlertFeed.Insert(0, alertDto);
            TodayDetectionCount++;
            if (!alertDto.IsVlmTriggered)
                PendingCount++;
            if (AlertFeed.Count > 100)
                AlertFeed.RemoveAt(AlertFeed.Count - 1);
        }

        // VLM 검증 최초 완료(이미지당 1회) → 긴급이면 단속 현황 DB 등록
        if (alertDto.IsVlmTriggered && !wasVlmTriggered && _vlmCompletedKeys.Add(incidentKey))
        {
            CompletedCount++;
            if (PendingCount > 0)
                PendingCount--;

            var feedItem = AlertFeed.FirstOrDefault(SameFeedItem) ?? alertDto;
            if (_dbService.TryInsertUrgentEnforcement(feedItem))
                NotifyEnforcementListChanged();
        }
    }
}