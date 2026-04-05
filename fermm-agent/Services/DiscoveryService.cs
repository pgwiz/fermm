using System.Text;
using System.Text.Json;
using FermmAgent.Models;

namespace FermmAgent.Services;

public class DiscoveryService
{
    private readonly ILogger<DiscoveryService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public DiscoveryService(ILogger<DiscoveryService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AgentConfig?> DiscoverAndRegisterAsync(CancellationToken ct = default)
    {
        try
        {
            // Try discovery endpoints in order
            var discoveryUrls = new[]
            {
                "http://localhost/api/devices/discover",
                "https://fermm.pgwiz.cloud/api/devices/discover",
                "http://127.0.0.1:8000/api/devices/discover"
            };

            foreach (var url in discoveryUrls)
            {
                try
                {
                    var config = await DiscoverServerAsync(url, ct);
                    if (config != null)
                    {
                        _logger.LogInformation("Successfully discovered server at {ServerUrl}", config.ServerUrl);
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to discover from {Url}", url);
                    continue;
                }
            }

            _logger.LogWarning("No discovery endpoints available");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery failed");
            return null;
        }
    }

    private async Task<AgentConfig?> DiscoverServerAsync(string discoveryUrl, CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);

        var response = await http.GetAsync(discoveryUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Discovery endpoint returned {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("server_url", out var serverUrlElem) ||
            !root.TryGetProperty("registration_token", out var tokenElem))
        {
            _logger.LogWarning("Invalid discovery response format");
            return null;
        }

        var serverUrl = serverUrlElem.GetString();
        var token = tokenElem.GetString();

        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        // Load existing device ID or generate new one
        var deviceId = LoadOrGenerateDeviceId();

        // Auto-register with the server
        var registered = await AutoRegisterAsync(serverUrl, deviceId, token, ct);
        if (!registered)
        {
            return null;
        }

        return new AgentConfig
        {
            ServerUrl = serverUrl,
            Token = token,
            DeviceId = deviceId,
            PollIntervalSeconds = 15,
            LogLevel = "Information"
        };
    }

    private async Task<bool> AutoRegisterAsync(string serverUrl, string deviceId, string token, CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var registerData = new
            {
                device_id = deviceId,
                hostname = Environment.MachineName,
                os = GetOsName(),
                arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            };

            var json = JsonSerializer.Serialize(registerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await http.PostAsync($"{serverUrl.TrimEnd('/')}/api/devices/register", content, ct);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Device auto-registered successfully");
                return true;
            }

            _logger.LogWarning("Auto-registration failed: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-registration error");
            return false;
        }
    }

    private string LoadOrGenerateDeviceId()
    {
        var idFile = Path.Combine(AppContext.BaseDirectory, ".device_id");
        
        if (File.Exists(idFile))
        {
            return File.ReadAllText(idFile).Trim();
        }

        var deviceId = Guid.NewGuid().ToString();
        try
        {
            File.WriteAllText(idFile, deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist device ID");
        }

        return deviceId;
    }

    private string GetOsName()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
            return "Windows";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Linux))
            return "Linux";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX))
            return "macOS";
        return "Unknown";
    }
}
