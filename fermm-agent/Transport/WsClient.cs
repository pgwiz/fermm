using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FermmAgent.Models;

namespace FermmAgent.Transport;

public class WsClient : IDisposable
{
    private readonly AgentConfig _config;
    private readonly ILogger<WsClient> _logger;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _reconnectDelayMs = 1000;
    private const int MaxReconnectDelayMs = 60000;

    public event Func<AgentCommand, Task>? OnCommand;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public WsClient(AgentConfig config, ILogger<WsClient> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                
                var wsUrl = _config.ServerUrl.Replace("https://", "wss://").Replace("http://", "ws://");
                var uri = new Uri($"{wsUrl}/ws/agent/{_config.DeviceId}?token={Uri.EscapeDataString(_config.Token)}");
                
                _logger.LogInformation("Connecting to WebSocket at {Uri}", uri.GetLeftPart(UriPartial.Path));
                await _ws.ConnectAsync(uri, _cts.Token);
                
                _logger.LogInformation("WebSocket connected");
                _reconnectDelayMs = 1000; // Reset backoff on successful connect
                OnConnected?.Invoke();
                
                await ReceiveLoopAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket connection failed, retrying in {Delay}ms", _reconnectDelayMs);
                OnDisconnected?.Invoke();
                
                try
                {
                    await Task.Delay(_reconnectDelayMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                
                _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);
            }
            finally
            {
                _ws?.Dispose();
                _ws = null;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new List<byte>();
        
        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                // Receive without timeout - rely on WebSocket ping/pong
                var segment = new ArraySegment<byte>(buffer);
                var result = await _ws.ReceiveAsync(segment, ct);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed WebSocket connection");
                    break;
                }
                
                messageBuffer.AddRange(buffer.Take(result.Count));
                
                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear();
                    
                    try
                    {
                        var cmd = JsonSerializer.Deserialize<AgentCommand>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        
                        if (cmd != null && !string.IsNullOrEmpty(cmd.CommandId) && OnCommand != null)
                        {
                            _logger.LogInformation("📩 Command received: {CommandId}, Type: {Type}", cmd.CommandId, cmd.Type);
                            _ = Task.Run(async () =>
                            {
                                try { await OnCommand(cmd); }
                                catch (Exception ex) { _logger.LogError(ex, "Error handling command {CommandId}", cmd.CommandId); }
                            }, ct);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse command JSON");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket receive error");
                break;
            }
        }
        
        _logger.LogInformation("Receive loop ended, WebSocket state: {State}", _ws?.State);
    }

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send message, WebSocket not connected");
            return;
        }
        
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            _logger.LogInformation("📨 WebSocket message sent ({Length} bytes)", bytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send WebSocket message");
        }
    }

    public async Task SendResultAsync(CommandResult result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        await SendAsync(json, ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}
