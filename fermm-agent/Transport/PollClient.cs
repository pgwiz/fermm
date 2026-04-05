using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FermmAgent.Models;

namespace FermmAgent.Transport;

public class PollClient
{
    private readonly AgentConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PollClient> _logger;
    private int _pollIntervalMs;
    private const int MaxPollIntervalMs = 60000;

    public event Func<AgentCommand, Task>? OnCommand;

    public PollClient(AgentConfig config, IHttpClientFactory httpFactory, ILogger<PollClient> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
        _pollIntervalMs = config.PollIntervalSeconds * 1000;
    }
    
    private HttpClient CreateClient()
    {
        var http = _httpFactory.CreateClient("FermmAgent");
        // Ensure base address is set from config
        if (http.BaseAddress == null && !string.IsNullOrEmpty(_config.ServerUrl))
        {
            http.BaseAddress = new Uri(_config.ServerUrl.TrimEnd('/') + "/");
        }
        http.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _config.Token);
        return http;
    }

    public async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting HTTP polling fallback");
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollPendingCommandsAsync(ct);
                _pollIntervalMs = _config.PollIntervalSeconds * 1000; // Reset on success
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Poll failed, next attempt in {Interval}ms", _pollIntervalMs);
                _pollIntervalMs = Math.Min(_pollIntervalMs * 2, MaxPollIntervalMs);
            }
            
            try
            {
                await Task.Delay(_pollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollPendingCommandsAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        var response = await http.GetAsync($"api/devices/{_config.DeviceId}/pending", ct);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poll request failed with status {Status}", response.StatusCode);
            return;
        }
        
        var json = await response.Content.ReadAsStringAsync(ct);
        var commands = JsonSerializer.Deserialize<List<AgentCommand>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (commands == null || commands.Count == 0)
            return;
        
        _logger.LogInformation("Received {Count} pending commands", commands.Count);
        
        foreach (var cmd in commands)
        {
            if (OnCommand != null)
            {
                try
                {
                    await OnCommand(cmd);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling command {CommandId}", cmd.CommandId);
                }
            }
        }
    }

    public async Task PostResultAsync(CommandResult result, CancellationToken ct)
    {
        using var http = CreateClient();
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"api/devices/{_config.DeviceId}/results", content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to post result for command {CommandId}, status {Status}", 
                result.CommandId, response.StatusCode);
        }
    }
}
