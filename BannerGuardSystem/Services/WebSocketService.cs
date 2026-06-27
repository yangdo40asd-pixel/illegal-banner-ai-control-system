using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public class WebSocketService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _isConnected;
    private string? _wsUrl;
    private bool _isDisposing;
    private int _retryDelayMs = 1000;
    private const int MaxRetryDelayMs = 30000;

    public event Action<BannerDetectionEventDto>? OnAlertReceived;
    public event Action<ChannelDetectionBatchDto>? OnChannelBatchReceived;
    public event Action<ChannelDetectionBatchDto>? OnVlmImageBatchReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(string wsUrl)
    {
        _wsUrl = wsUrl;
        _isDisposing = false;
        _retryDelayMs = 1000;
        await DoConnectAsync();
    }

    private async Task DoConnectAsync()
    {
        if (_isDisposing) return;

        // Cancel previous task token if any
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        try
        {
            BackendManager.Log($"[WebSocket] Connecting to {_wsUrl}...");
            await _ws.ConnectAsync(new Uri(_wsUrl!), _cts.Token);
            
            _isConnected = true;
            _retryDelayMs = 1000; // Reset retry interval on successful connection
            ConnectionChanged?.Invoke(true);
            BackendManager.Log($"[WebSocket] Connected successfully to {_wsUrl}");
            
            // Run listening loop on background thread pool
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(false);
            BackendManager.Log($"[WebSocket-Err] Connect failed: {ex.Message}");
            _ = ScheduleReconnectAsync();
        }
    }

    private async Task ScheduleReconnectAsync()
    {
        if (_isDisposing) return;

        BackendManager.Log($"[WebSocket] Reconnecting in {_retryDelayMs}ms...");
        await Task.Delay(_retryDelayMs);
        
        // Exponential backoff
        _retryDelayMs = Math.Min(_retryDelayMs * 2, MaxRetryDelayMs);
        await DoConnectAsync();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[2 * 1024 * 1024]; // 2MB — 리사이즈된 FullImageBase64 청크 수신용
        var sb = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult result;
                sb.Clear();

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    BackendManager.Log("[WebSocket] Connection closed by remote host");
                    break;
                }

                var json = sb.ToString();
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var token = JObject.Parse(json);
                        var messageType = token["MessageType"]?.ToString();

                        if (messageType == "channel_batch")
                        {
                            var batch = token.ToObject<ChannelDetectionBatchDto>();
                            if (batch != null && batch.Detections != null)
                            {
                                BackendManager.Log($"[WebSocket] Channel batch: CCTV={batch.CctvId}, Count={batch.Detections.Count}, Frame={batch.FrameWidth}x{batch.FrameHeight}");
                                OnChannelBatchReceived?.Invoke(batch);
                            }
                        }
                        else if (messageType == "vlm_image_batch")
                        {
                            var batch = token.ToObject<ChannelDetectionBatchDto>();
                            if (batch != null && batch.Detections != null)
                            {
                                BackendManager.Log($"[WebSocket] VLM image batch: Source={batch.SourceImage}, CCTV={batch.CctvId}, Count={batch.Detections.Count}, Frame={batch.FrameWidth}x{batch.FrameHeight}");
                                OnVlmImageBatchReceived?.Invoke(batch);
                            }
                        }
                        else
                        {
                            var alertDto = token.ToObject<BannerDetectionEventDto>();
                            if (alertDto != null)
                            {
                                BackendManager.Log($"[WebSocket] Received alert event: ID={alertDto.Id}, CCTV={alertDto.CctvId}, HasBanner={alertDto.HasBanner}");
                                OnAlertReceived?.Invoke(alertDto);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        BackendManager.Log($"[WebSocket-Err] Deserialization failed: {ex.Message}. Raw JSON length: {json.Length}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                BackendManager.Log($"[WebSocket-Err] Error during receive: {ex.Message}");
                break;
            }
        }

        _isConnected = false;
        ConnectionChanged?.Invoke(false);

        // Schedule reconnection if disconnected unexpectedly
        if (!ct.IsCancellationRequested && !_isDisposing)
        {
            System.Diagnostics.Debug.WriteLine("[WebSocket] Connection lost. Reconnecting...");
            _ = ScheduleReconnectAsync();
        }
    }

    public async Task DisconnectAsync()
    {
        _isDisposing = true;
        _cts?.Cancel();
        
        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebSocket] Close failed: {ex.Message}");
            }
        }
        
        _isConnected = false;
        ConnectionChanged?.Invoke(false);
    }

    public async Task SendCommandAsync(string command, object value)
    {
        if (command == "human_verdict" && value != null)
        {
            await SendJsonAsync(value);
            return;
        }

        await SendJsonAsync(new { command, value });
    }

    public async Task SendJsonAsync(object payload)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            return;

        try
        {
            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            BackendManager.Log($"[WebSocket-Err] Send failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _isDisposing = true;
        _cts?.Cancel();
        _ws?.Dispose();
    }
}
