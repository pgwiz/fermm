using System.Net.Http.Json;
using System.Text.Json;
using FermmAgent.Handlers;
using FermmAgent.Models;
using FermmAgent.Transport;

namespace FermmAgent;

public class AgentService : BackgroundService
{
    private readonly ILogger<AgentService> _logger;
    private readonly AgentConfig _config;
    private readonly CommandDispatcher _dispatcher;
    private readonly WsClient _wsClient;
    private readonly PollClient _pollClient;
    private readonly IHttpClientFactory _httpFactory;
    
    private bool _useWebSocket = true;
    private Task? _wsTask;

    public AgentService(
        ILogger<AgentService> logger,
        AgentConfig config,
        CommandDispatcher dispatcher,
        WsClient wsClient,
        PollClient pollClient,
        IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _config = config;
        _dispatcher = dispatcher;
        _wsClient = wsClient;
        _pollClient = pollClient;
        _httpFactory = httpFactory;
        
        // Wire up events
        _wsClient.OnCommand += HandleCommandAsync;
        _wsClient.OnConnected += () => { _useWebSocket = true; _logger.LogInformation("Switched to WebSocket mode"); };
        _wsClient.OnDisconnected += () => { _useWebSocket = false; _logger.LogInformation("Switched to polling mode"); };
        
        _pollClient.OnCommand += HandleCommandAsync;
        
        _dispatcher.OnResult += async result =>
        {
            if (_useWebSocket && _wsClient.IsConnected)
            {
                await _wsClient.SendResultAsync(result, CancellationToken.None);
            }
            else
            {
                await _pollClient.PostResultAsync(result, CancellationToken.None);
            }
        };
        
        _dispatcher.OnStreamLine += async (cmdId, line) =>
        {
            if (_wsClient.IsConnected)
            {
                var streamMsg = JsonSerializer.Serialize(new { type = "stream", commandId = cmdId, line });
                await _wsClient.SendAsync(streamMsg, CancellationToken.None);
            }
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FERMM Agent starting...");
        _logger.LogInformation("Device ID: {DeviceId}", _config.DeviceId);
        _logger.LogInformation("Server URL: {ServerUrl}", _config.ServerUrl);
        
        try
        {
            _config.Validate();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Configuration error");
            return;
        }
        
        // Register device with server
        await RegisterDeviceAsync(stoppingToken);
        
        // Start WebSocket in background
        _wsTask = Task.Run(() => _wsClient.ConnectAsync(stoppingToken), stoppingToken);
        
        // Run polling as fallback when WS is not connected
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_useWebSocket || !_wsClient.IsConnected)
            {
                try
                {
                    await _pollClient.PollLoopAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            else
            {
                // WS is connected, just wait and check periodically
                try
                {
                    await Task.Delay(1000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        _logger.LogInformation("FERMM Agent stopping...");
    }

    private async Task RegisterDeviceAsync(CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient("FermmAgent");
            
            // Step 1: Get discovery info and registration token
            _logger.LogInformation("📡 Discovering server...");
            var discoverResponse = await http.GetAsync("api/devices/discover", ct);
            if (!discoverResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discovery failed: {Status}", discoverResponse.StatusCode);
                return;
            }
            
            var discoverContent = await discoverResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(discoverContent);
            var registrationToken = doc.RootElement.GetProperty("registration_token").GetString();
            
            if (string.IsNullOrEmpty(registrationToken))
            {
                _logger.LogWarning("No registration token in discovery response");
                return;
            }
            
            _logger.LogInformation("✓ Got registration token");
            
            // Step 2: Register device with the token
            var payload = new
            {
                device_id = _config.DeviceId,
                hostname = Environment.MachineName,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            };
            
            var registerRequest = new HttpRequestMessage(HttpMethod.Post, "api/devices/register")
            {
                Content = JsonContent.Create(payload)
            };
            registerRequest.Headers.Add("Authorization", $"Bearer {registrationToken}");
            
            var registerResponse = await http.SendAsync(registerRequest, ct);
            
            if (registerResponse.IsSuccessStatusCode)
            {
                _config.Token = registrationToken;
                _logger.LogInformation("✓ Device registered successfully");
            }
            else
            {
                var body = await registerResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Device registration failed with status {Status}: {Body}", registerResponse.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register device (will retry on next poll)");
        }
    }

    private async Task HandleCommandAsync(AgentCommand cmd)
    {
        _logger.LogInformation("🔵 [COMMAND RECEIVED] ID: {CommandId}, Type: {Type}", cmd.CommandId, cmd.Type);
        
        try
        {
            var result = await _dispatcher.HandleCommandAsync(cmd, CancellationToken.None);
            _logger.LogInformation("🟢 [COMMAND COMPLETED] Exit Code: {ExitCode}, Output: {OutputLines} lines",
                result.ExitCode, result.Output?.Count ?? 0);
            
            // Send result back to server
            if (_useWebSocket && _wsClient.IsConnected)
            {
                _logger.LogInformation("📤 Sending result via WebSocket");
                await _wsClient.SendResultAsync(result, CancellationToken.None);
            }
            else
            {
                _logger.LogInformation("📤 Sending result via HTTP");
                await _pollClient.PostResultAsync(result, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ERROR] Failed to handle command");
        }
    }
}
