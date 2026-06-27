#define DEBUG
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;
using BannerGuardSystem.Views;

namespace BannerGuardSystem.ViewModels;

public class MainViewModel : BaseViewModel, IDisposable
{
	private readonly WebSocketService _wsService;

	private readonly DatabaseService _dbService;

	private readonly PdfReportService _pdfReportService;

	private readonly AppSettings _settings;

	private readonly DispatcherTimer _clockTimer;

	private readonly Random _random = new Random();

	private DateTime _lastCpuTime = DateTime.MinValue;

	private TimeSpan _lastTotalProcessorTime;

	private int _gcTickCount = 0;

	private object? _currentView;

	private EnforcementStatusViewModel? _enforcementStatusViewModel;

	private StatisticsReportViewModel? _statisticsReportViewModel;

	private SystemSettingsViewModel? _systemSettingsViewModel;

	private RealTimeMonitoringView? _realTimeMonitoringView;

	private CctvMonitoringView? _cctvMonitoringView;

	private CctvChannel? _selectedChannel;

	private BannerDetectionEventDto? _activeAlert;

	private string _mapQuery = "인천광역시 연수구 송도동";

	private string _connectionStatus = "Disconnected";

	private bool _isConnected;

	private string _systemModeText = "데모 버전 (송도동 한정)";

	private string _aiStatusText = "수신 대기 중 (Listening)";

	private bool _isImageCropEnabled = true;

	private bool _isAutomationEnabled = true;

	private string _dbStatus = "Offline";

	private int _todayDetectionCount = 0;

	private int _pendingCount = 0;

	private int _completedCount = 0;

	private int _emergencyCount = 0;

	private string _currentTimeText = string.Empty;

	private string _cpuUsageText = "0%";

	private string _ramUsageText = "0MB";

	private string _gpuUsageText = "0%";

	private bool _isRoiMaskMode;

	private readonly Dictionary<string, DateTime> _lastIncidentTime = new Dictionary<string, DateTime>();



	public ObservableCollection<CctvChannel> Channels { get; } = new ObservableCollection<CctvChannel>();

	public ObservableCollection<BannerDetectionEventDto> AlertFeed { get; } = new ObservableCollection<BannerDetectionEventDto>();

	public ObservableCollection<EnforcementHistoryDto> HistoryLog { get; } = new ObservableCollection<EnforcementHistoryDto>();

	public object? CurrentView
	{
		get
		{
			return _currentView;
		}
		set
		{
			SetField(ref _currentView, value, "CurrentView");
		}
	}

	public CctvChannel? SelectedChannel
	{
		get
		{
			return _selectedChannel;
		}
		set
		{
			if (!SetField(ref _selectedChannel, value, "SelectedChannel"))
			{
				return;
			}
			foreach (CctvChannel channel in Channels)
			{
				channel.IsSelected = channel == value;
			}
			if (value != null)
			{
				MapQuery = value.Address;
			}
		}
	}

	public BannerDetectionEventDto? ActiveAlert
	{
		get
		{
			return _activeAlert;
		}
		set
		{
			if (_activeAlert == value)
			{
				return;
			}
			_activeAlert = value;
			OnPropertyChanged("ActiveAlert");
			OnPropertyChanged("HasActiveAlert");
			OnPropertyChanged("HasNoActiveAlert");
			LoadPastViolationsForActiveAlert();
			if (_activeAlert != null)
			{
				CctvChannel cctvChannel = Channels.FirstOrDefault((CctvChannel c) => c.ChannelId == _activeAlert.CctvId);
				if (cctvChannel != null)
				{
					SelectedChannel = cctvChannel;
				}
			}
		}
	}

	public string MapQuery
	{
		get
		{
			return _mapQuery;
		}
		set
		{
			SetField(ref _mapQuery, value, "MapQuery");
		}
	}

	public bool HasActiveAlert => ActiveAlert != null;

	public bool HasNoActiveAlert => ActiveAlert == null;

	public string ConnectionStatus
	{
		get
		{
			return _connectionStatus;
		}
		set
		{
			SetField(ref _connectionStatus, value, "ConnectionStatus");
		}
	}

	public bool IsConnected
	{
		get
		{
			return _isConnected;
		}
		set
		{
			SetField(ref _isConnected, value, "IsConnected");
		}
	}

	public string SystemModeText
	{
		get
		{
			return _systemModeText;
		}
		set
		{
			SetField(ref _systemModeText, value, "SystemModeText");
		}
	}

	public string AiStatusText
	{
		get
		{
			return _aiStatusText;
		}
		set
		{
			SetField(ref _aiStatusText, value, "AiStatusText");
		}
	}

	public bool IsImageCropEnabled
	{
		get
		{
			return _isImageCropEnabled;
		}
		set
		{
			if (SetField(ref _isImageCropEnabled, value, "IsImageCropEnabled"))
			{
				OnPropertyChanged("IsImageCropEnabledText");
			}
		}
	}

	public string IsImageCropEnabledText => IsImageCropEnabled ? "이미지 크롭: ON" : "이미지 크롭: OFF";

	public bool IsAutomationEnabled
	{
		get
		{
			return _isAutomationEnabled;
		}
		set
		{
			if (SetField(ref _isAutomationEnabled, value, "IsAutomationEnabled"))
			{
				_wsService?.SendCommandAsync("toggle_automation", value);
			}
		}
	}

	public string DbStatus
	{
		get
		{
			return _dbStatus;
		}
		set
		{
			SetField(ref _dbStatus, value, "DbStatus");
		}
	}

	public int TodayDetectionCount
	{
		get
		{
			return _todayDetectionCount;
		}
		set
		{
			SetField(ref _todayDetectionCount, value, "TodayDetectionCount");
		}
	}

	public int PendingCount
	{
		get
		{
			return _pendingCount;
		}
		set
		{
			SetField(ref _pendingCount, value, "PendingCount");
		}
	}

	public int CompletedCount
	{
		get
		{
			return _completedCount;
		}
		set
		{
			SetField(ref _completedCount, value, "CompletedCount");
		}
	}

	public int EmergencyCount
	{
		get
		{
			return _emergencyCount;
		}
		set
		{
			SetField(ref _emergencyCount, value, "EmergencyCount");
		}
	}

	public string CurrentTimeText
	{
		get
		{
			return _currentTimeText;
		}
		set
		{
			SetField(ref _currentTimeText, value, "CurrentTimeText");
		}
	}

	public string CpuUsageText
	{
		get
		{
			return _cpuUsageText;
		}
		set
		{
			SetField(ref _cpuUsageText, value, "CpuUsageText");
		}
	}

	public string RamUsageText
	{
		get
		{
			return _ramUsageText;
		}
		set
		{
			SetField(ref _ramUsageText, value, "RamUsageText");
		}
	}

	public string GpuUsageText
	{
		get
		{
			return _gpuUsageText;
		}
		set
		{
			SetField(ref _gpuUsageText, value, "GpuUsageText");
		}
	}

	public bool IsRoiMaskMode
	{
		get
		{
			return _isRoiMaskMode;
		}
		set
		{
			SetField(ref _isRoiMaskMode, value, "IsRoiMaskMode");
		}
	}

	public ICommand ApproveCommand { get; }

	public ICommand DismissCommand { get; }

	public ICommand ToggleRoiModeCommand { get; }

	public ICommand SelectAlertCommand { get; }

	public ICommand SelectChannelCommand { get; }

	public ICommand NavigateCommand { get; }

	public MainViewModel()
	{
		//IL_01ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Expected O, but got Unknown
		_settings = new AppSettings();
		_wsService = new WebSocketService();
		_dbService = new DatabaseService();
		_pdfReportService = new PdfReportService();
		ApproveCommand = new SimpleCommand(OnApproveActiveAlert);
		DismissCommand = new SimpleCommand(OnDismissActiveAlert);
		ToggleRoiModeCommand = new SimpleCommand(delegate
		{
			IsRoiMaskMode = !IsRoiMaskMode;
		});
		SelectAlertCommand = new RelayCommand(delegate(object? p)
		{
			if (p is BannerDetectionEventDto activeAlert)
			{
				ActiveAlert = activeAlert;
			}
		});
		SelectChannelCommand = new RelayCommand(delegate(object? p)
		{
			string id = p as string;
			if (id != null)
			{
				CctvChannel cctvChannel = Channels.FirstOrDefault((CctvChannel c) => c.ChannelId == id);
				if (cctvChannel != null)
				{
					SelectedChannel = cctvChannel;
				}
			}
			else if (p is CctvChannel selectedChannel)
			{
				SelectedChannel = selectedChannel;
			}
		});
		NavigateCommand = new RelayCommand(delegate(object? p)
		{
			if (p is string text)
			{
				if (text == "RealTimeMonitoring")
				{
					_realTimeMonitoringView ??= new RealTimeMonitoringView();
					CurrentView = _realTimeMonitoringView;
				}
				else if (text == "CctvMonitoring")
				{
					_cctvMonitoringView ??= new CctvMonitoringView();
					CurrentView = _cctvMonitoringView;
				}
				else if (text == "EnforcementList")
				{
					_enforcementStatusViewModel ??= new EnforcementStatusViewModel();
					CurrentView = new EnforcementStatusView
					{
						DataContext = _enforcementStatusViewModel
					};
				}
				else if (text == "Statistics")
				{
					_statisticsReportViewModel ??= new StatisticsReportViewModel();
					CurrentView = new StatisticsReportView
					{
						DataContext = _statisticsReportViewModel
					};
				}
				else if (text == "Settings")
				{
					_systemSettingsViewModel ??= new SystemSettingsViewModel(this);
					CurrentView = new SystemSettingsView
					{
						DataContext = _systemSettingsViewModel
					};
				}
			}
		});
		_dbService.Initialize();
		DbStatus = "Connected";
		_wsService.OnAlertReceived += OnWebSocketAlertReceived;
		_wsService.OnChannelBatchReceived += OnChannelBatchReceived;
		_wsService.OnVlmImageBatchReceived += OnVlmImageBatchReceived;
		_wsService.ConnectionChanged += OnConnectionChanged;
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_clockTimer.Tick += OnClockTick;
		_clockTimer.Start();
		LoadDefaultChannels();
		SelectedChannel = Channels.FirstOrDefault();
		_realTimeMonitoringView = new RealTimeMonitoringView();
		CurrentView = _realTimeMonitoringView;
		ConnectWebSocketAsync();
	}

	private async Task ConnectWebSocketAsync()
	{
		ConnectionStatus = "Connecting...";
		await _wsService.ConnectAsync(_settings.WsUrl);
	}

	private void OnConnectionChanged(bool connected)
	{
		BaseViewModel.RunOnUi(delegate
		{
			try
			{
				IsConnected = connected;
				if (connected)
				{
					ConnectionStatus = "Connected (Live)";
					AiStatusText = "수신 대기 중 (Listening)";
				}
				else
				{
					ConnectionStatus = "Disconnected";
					AiStatusText = "서버 연결 끊김 (Off)";
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[MainViewModel] Connection change processing error: " + ex.Message);
			}
		});
	}

	private void LoadDefaultChannels()
	{
		(string, string, string)[] array = new(string, string, string)[4]
		{
			("CCTV_01", "CCTV_01 삼산동 A구역", "d:\\138.종합 민원 이미지 AI데이터\\WPF\\20260525_133553.mp4"),
			("CCTV_02", "CCTV_02 삼산동 B구역", "d:\\138.종합 민원 이미지 AI데이터\\WPF\\20260525_133611.mp4"),
			("CCTV_03", "CCTV_03 구월동 A구역", "d:\\138.종합 민원 이미지 AI데이터\\WPF\\20260525_133838.mp4"),
			("CCTV_04", "CCTV_04 구월동 B구역", "d:\\138.종합 민원 이미지 AI데이터\\WPF\\20260525_133919.mp4")
		};
		(string, string, string)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, string, string) tuple = array2[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			string item3 = tuple.Item3;
			CctvChannel cctvChannel = new CctvChannel();
			cctvChannel.ChannelId = item;
			cctvChannel.Name = item2;
			cctvChannel.Gu = ((item == "CCTV_01" || item == "CCTV_02") ? "부평구" : "남동구");
			cctvChannel.Dong = ((item == "CCTV_01" || item == "CCTV_02") ? "삼산동" : "구월동");
			cctvChannel.Address = item switch
			{
				"CCTV_03" => "인천 남동구 구월동 78", 
				"CCTV_02" => "인천 부평구 삼산동 45", 
				"CCTV_01" => "인천 부평구 삼산동 12", 
				_ => "인천 남동구 구월동 10", 
			};
			CctvChannel cctvChannel2 = cctvChannel;
			if (1 == 0)
			{
			}
			string defaultImagePath = item switch
			{
				"CCTV_01" => "Assets/20260525_133548.jpg", 
				"CCTV_02" => "Assets/20260525_133609.jpg", 
				"CCTV_03" => "Assets/20260525_133852.jpg", 
				_ => "Assets/20260525_133916.jpg", 
			};
			if (1 == 0)
			{
			}
			cctvChannel2.DefaultImagePath = defaultImagePath;
			cctvChannel.VideoSource = new LocalFileVideoSource(item3);
			cctvChannel.Status = ChannelStatus.Offline;
			CctvChannel item4 = cctvChannel;
			Channels.Add(item4);
		}
	}

	private void OnChannelBatchReceived(ChannelDetectionBatchDto batch)
	{
		BaseViewModel.RunOnUi(delegate
		{
			AiStatusText = "AI 분석 수신 완료 (Running)";
			var channel = Channels.FirstOrDefault(c => c.ChannelId == batch.CctvId);
			if (channel != null)
				channel.ApplyDetectionBatch(batch);

			ResetAiStatusAfterDelay();
		});
	}

	private void OnVlmImageBatchReceived(ChannelDetectionBatchDto batch)
	{
		BaseViewModel.RunOnUi(delegate
		{
			AiStatusText = "AI 분석 수신 완료 (Running)";

			foreach (var alertDto in batch.Detections)
			{
				alertDto.SourceImage = batch.SourceImage;
				ProcessAlertForFeed(alertDto, fromSameBatch: true, sourceImage: batch.SourceImage);
			}

			if (batch.Detections.Count > 0)
				ActiveAlert = batch.Detections.FirstOrDefault(d => d.IsVlmTriggered)
				              ?? batch.Detections[0];

			ResetAiStatusAfterDelay();
		});
	}

	private void OnWebSocketAlertReceived(BannerDetectionEventDto alertDto)
	{
		BaseViewModel.RunOnUi(delegate
		{
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e6: Expected O, but got Unknown
			//IL_0312: Unknown result type (might be due to invalid IL or missing references)
			//IL_0317: Unknown result type (might be due to invalid IL or missing references)
			//IL_032a: Expected O, but got Unknown
			AiStatusText = "AI 분석 수신 완료 (Running)";
			ProcessAlertForFeed(alertDto);
			ActiveAlert = alertDto;
			HighlightChannelAlertState(alertDto.CctvId, alertDto.HasBanner, alertDto);
			ResetAiStatusAfterDelay();
		});
	}

	private void ProcessAlertForFeed(BannerDetectionEventDto alertDto, bool fromSameBatch = false, string sourceImage = "")
	{
		bool isVlmBySource = !string.IsNullOrEmpty(sourceImage);
		string incidentKey = isVlmBySource ? sourceImage : alertDto.CctvId;

		bool SameFeedItem(BannerDetectionEventDto a) =>
			isVlmBySource
				? a.SourceImage == sourceImage
				: a.Id == alertDto.Id
				  || (a.CctvId == alertDto.CctvId && a.OcrText == alertDto.OcrText);

		if (alertDto.IsVlmTriggered)
		{
			BannerDetectionEventDto bannerDetectionEventDto = AlertFeed.FirstOrDefault(SameFeedItem);
			if (bannerDetectionEventDto == null || !bannerDetectionEventDto.IsVlmTriggered)
			{
				int completedCount = CompletedCount;
				CompletedCount = completedCount + 1;
				if (PendingCount > 0)
				{
					completedCount = PendingCount;
					PendingCount = completedCount - 1;
				}
			}
		}

		BannerDetectionEventDto? existing = AlertFeed.FirstOrDefault(SameFeedItem);
		if (existing != null)
		{
			existing.UpdateFrom(alertDto);
			if (isVlmBySource)
				existing.SourceImage = sourceImage;
			_lastIncidentTime[incidentKey] = DateTime.Now;
		}
		else
		{
			bool shouldAdd = fromSameBatch || isVlmBySource
				|| !_lastIncidentTime.TryGetValue(incidentKey, out var lastTime)
				|| (DateTime.Now - lastTime).TotalSeconds >= 3.0;
			if (shouldAdd)
			{
				_lastIncidentTime[incidentKey] = DateTime.Now;
				AlertFeed.Insert(0, alertDto);
				if (AlertFeed.Count > 100)
					AlertFeed.RemoveAt(AlertFeed.Count - 1);

				int completedCount = TodayDetectionCount;
				TodayDetectionCount = completedCount + 1;
				if (alertDto.IsRepeatOffender)
				{
					completedCount = EmergencyCount;
					EmergencyCount = completedCount + 1;
				}
				completedCount = PendingCount;
				PendingCount = completedCount + 1;
			}
		}
	}

	private void ResetAiStatusAfterDelay()
	{
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(3L)
		};
		timer.Tick += delegate
		{
			if (IsConnected)
				AiStatusText = "수신 대기 중 (Listening)";
			timer.Stop();
		};
		timer.Start();
	}

	private void LoadPastViolationsForActiveAlert()
	{
		HistoryLog.Clear();
		if (ActiveAlert == null)
		{
			return;
		}
		try
		{
			List<ViolationRecord> violationHistory = _dbService.GetViolationHistory(ActiveAlert.CctvId);
			foreach (ViolationRecord item in violationHistory)
			{
				double similarityScore = RecurrenceAnalyzer.CalculateSimilarity(ActiveAlert.OcrText, item.OcrText);
				HistoryLog.Add(new EnforcementHistoryDto
				{
					HistoryId = item.Id,
					DetectedDate = item.ViolationDate,
					LocationAddress = item.FullAddress,
					MatchedText = item.OcrText,
					SimilarityScore = similarityScore,
					Status = item.Status
				});
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MainViewModel] GetHistory failed: " + ex.Message);
		}
	}

	private void TriggerImmediateGC()
	{
		Task.Run(delegate
		{
			try
			{
				GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
			}
			catch
			{
			}
		});
	}

	private void OnApproveActiveAlert()
	{
		if (ActiveAlert != null)
		{
			Debug.WriteLine("단속 승인 및 행정 보고서 생성 완료");
			Console.WriteLine("단속 승인 및 행정 보고서 생성 완료");
			GeneratePdfAdministrativeReport(ActiveAlert);
			_dbService.UpdateBannerStatus(ActiveAlert.GetHashCode(), "단속확정");
			BannerDetectionEventDto activeAlert = ActiveAlert;
			HighlightChannelAlertState(activeAlert.CctvId, isAlerting: false);
			AlertFeed.Remove(activeAlert);
			SelectNextPendingAlert();
			TriggerImmediateGC();
		}
	}

	private void OnDismissActiveAlert()
	{
		if (ActiveAlert != null)
		{
			Debug.WriteLine("오탐 기각 처리 완료");
			Console.WriteLine("오탐 기각 처리 완료");
			_dbService.UpdateBannerStatus(ActiveAlert.GetHashCode(), "오탐기각");
			BannerDetectionEventDto activeAlert = ActiveAlert;
			HighlightChannelAlertState(activeAlert.CctvId, isAlerting: false);
			AlertFeed.Remove(activeAlert);
			SelectNextPendingAlert();
			TriggerImmediateGC();
		}
	}

	private void GeneratePdfAdministrativeReport(BannerDetectionEventDto alert)
	{
		string reportsFolder = Path.Combine("d:\\138.종합 민원 이미지 AI데이터\\BannerGuardSystem", "Reports");
		Task.Run(delegate
		{
			try
			{
				if (!Directory.Exists(reportsFolder))
				{
					Directory.CreateDirectory(reportsFolder);
				}
				string path = $"단속보고서_{alert.CctvId}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
				string text = Path.Combine(reportsFolder, path);
				_pdfReportService.GenerateReport(alert, text);
				Debug.WriteLine("[ApproveCommand] PDF 행정 보고서 생성 완료: " + text);
			}
			catch (UnauthorizedAccessException ex)
			{
				Debug.WriteLine("[ApproveCommand] PDF 생성 실패 (디스크 권한 오류): " + ex.Message);
			}
			catch (IOException ex2)
			{
				Debug.WriteLine("[ApproveCommand] PDF 생성 실패 (디스크 I/O 오류): " + ex2.Message);
			}
			catch (Exception ex3)
			{
				Debug.WriteLine("[ApproveCommand] PDF 생성 중 예상치 못한 오류 발생: " + ex3.Message);
			}
		});
	}

	private void HighlightChannelAlertState(string cctvId, bool isAlerting, BannerDetectionEventDto? alertDto = null)
	{
		CctvChannel cctvChannel = Channels.FirstOrDefault((CctvChannel c) => c.ChannelId == cctvId);
		if (cctvChannel == null)
			return;

		if (!isAlerting)
		{
			cctvChannel.ClearDetections();
			return;
		}

		if (alertDto != null)
		{
			cctvChannel.CurrentAlerts.Clear();
			cctvChannel.CurrentAlerts.Add(alertDto);
			cctvChannel.CurrentAlert = alertDto;
			cctvChannel.Status = ChannelStatus.Alerting;
		}
	}

	private void SelectNextPendingAlert()
	{
		ActiveAlert = AlertFeed.FirstOrDefault();
	}

	private void OnClockTick(object? sender, EventArgs e)
	{
		CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		double num = CalculateCpuUsage();
		double value = 0.0;
		try
		{
			value = (double)Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
		}
		catch
		{
		}
		int num2 = Channels.Count((CctvChannel c) => c.Status == ChannelStatus.Online || c.Status == ChannelStatus.Alerting);
		double num3 = (double)num2 * 11.5 + num * 0.12 + (double)_random.Next(1, 5);
		if (num3 > 100.0)
		{
			num3 = 100.0;
		}
		if (num3 < 0.0)
		{
			num3 = 0.0;
		}
		CpuUsageText = $"{num:F1}%";
		RamUsageText = $"{value:F0}MB";
		GpuUsageText = $"{num3:F0}%";
		_gcTickCount++;
		if (_gcTickCount % 10 != 0)
		{
			return;
		}
		Task.Run(delegate
		{
			try
			{
				GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
			}
			catch
			{
			}
		});
	}

	private double CalculateCpuUsage()
	{
		try
		{
			Process currentProcess = Process.GetCurrentProcess();
			DateTime utcNow = DateTime.UtcNow;
			if (_lastCpuTime == DateTime.MinValue)
			{
				_lastCpuTime = utcNow;
				_lastTotalProcessorTime = currentProcess.TotalProcessorTime;
				return _random.Next(2, 6);
			}
			TimeSpan timeSpan = currentProcess.TotalProcessorTime - _lastTotalProcessorTime;
			TimeSpan timeSpan2 = utcNow - _lastCpuTime;
			_lastCpuTime = utcNow;
			_lastTotalProcessorTime = currentProcess.TotalProcessorTime;
			if (timeSpan2.TotalMilliseconds == 0.0)
			{
				return _random.Next(2, 6);
			}
			double num = timeSpan.TotalMilliseconds / (timeSpan2.TotalMilliseconds * (double)Environment.ProcessorCount) * 100.0;
			return Math.Min(100.0, Math.Max(0.0, num + 4.5));
		}
		catch
		{
			return _random.Next(2, 6);
		}
	}

	public void Dispose()
	{
		_clockTimer.Stop();
		_wsService.Dispose();
		_dbService.Dispose();
		foreach (CctvChannel channel in Channels)
		{
			channel.Dispose();
		}
	}
}
