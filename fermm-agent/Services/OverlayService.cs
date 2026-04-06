using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FermmAgent.Models;

namespace FermmAgent.Services;

public class OverlayService
{
    private readonly AgentConfig _config;
    private readonly ILogger<OverlayService> _logger;
    
    private Process? _overlayProcess;
    private NamedPipeServerStream? _pipeServer;
    private StreamWriter? _pipeWriter;
    private CancellationTokenSource? _pipeCts;

    public event Func<string, Task>? OnOverlayMessage;

    public OverlayService(AgentConfig config, ILogger<OverlayService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SpawnOverlayAsync(string? config = null, CancellationToken ct = default)
    {
        try
        {
            if (_overlayProcess?.HasExited == false)
            {
                _logger.LogWarning("Overlay process already running");
                return;
            }

            _logger.LogInformation("🎨 Spawning overlay...");

            // Use the same executable with --overlay flag
            var agentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            
            if (string.IsNullOrEmpty(agentExe) || !File.Exists(agentExe))
            {
                _logger.LogError("Cannot find agent executable path");
                return;
            }

            // Start the overlay process
            var psi = new ProcessStartInfo
            {
                FileName = agentExe,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                CreateNoWindow = false,
                Arguments = $"--overlay --device-id {_config.DeviceId}"
            };

            _overlayProcess = Process.Start(psi);
            if (_overlayProcess == null)
            {
                _logger.LogError("Failed to start overlay process");
                return;
            }

            _logger.LogInformation("✅ Overlay spawned with PID {PID}", _overlayProcess.Id);

            // Initialize IPC pipe for messaging
            _ = Task.Run(() => ListenToPipeAsync(ct), ct);

            // Monitor overlay process
            _ = Task.Run(async () => await MonitorOverlayProcessAsync(ct), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to spawn overlay");
        }
    }

    public async Task CloseOverlayAsync()
    {
        try
        {
            if (_overlayProcess?.HasExited == false)
            {
                _logger.LogInformation("🛑 Closing overlay...");
                
                try
                {
                    _overlayProcess.Kill();
                    await Task.Delay(500);
                }
                catch
                {
                    // Process already exited
                }

                _overlayProcess?.Dispose();
                _overlayProcess = null;
                
                _pipeWriter?.Dispose();
                _pipeServer?.Dispose();
                _pipeCts?.Cancel();
                
                _logger.LogInformation("✅ Overlay closed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to close overlay");
        }
    }

    public async Task SendMessageToOverlayAsync(string message)
    {
        try
        {
            if (_overlayProcess?.HasExited != false)
            {
                _logger.LogWarning("Overlay not running, cannot send message");
                return;
            }

            if (_pipeWriter == null)
            {
                _logger.LogWarning("Pipe not connected, cannot send message");
                return;
            }

            await _pipeWriter.WriteLineAsync(message);
            await _pipeWriter.FlushAsync();

            _logger.LogDebug("📤 Message sent to overlay: {Message}", message[..50]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send message to overlay");
        }
    }

    private async Task ListenToPipeAsync(CancellationToken ct)
    {
        try
        {
            var pipeName = $"fermm_overlay_{_config.DeviceId}";
            _pipeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _pipeServer = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Message
            );

            _logger.LogInformation("⏳ Waiting for overlay connection on pipe {PipeName}...", pipeName);

            // Wait for overlay to connect
            await _pipeServer.WaitForConnectionAsync(_pipeCts.Token);

            _logger.LogInformation("✅ Overlay connected to pipe");

            using var reader = new StreamReader(_pipeServer, Encoding.UTF8);
            _pipeWriter = new StreamWriter(_pipeServer, Encoding.UTF8) { AutoFlush = true };

            // Read messages from overlay
            while (!_pipeCts.Token.IsCancellationRequested && _overlayProcess?.HasExited == false)
            {
                try
                {
                    var message = await reader.ReadLineAsync(_pipeCts.Token);
                    
                    if (string.IsNullOrEmpty(message))
                        continue;

                    _logger.LogDebug("📥 Message from overlay: {Message}", message[..Math.Min(50, message.Length)]);

                    // Trigger event for message handling
                    if (OnOverlayMessage != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await OnOverlayMessage(message); }
                            catch (Exception ex) { _logger.LogError(ex, "Error handling overlay message"); }
                        }, _pipeCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Pipe communication error");
        }
        finally
        {
            _pipeWriter?.Dispose();
            _pipeServer?.Disconnect();
            _pipeServer?.Dispose();
        }
    }

    private async Task MonitorOverlayProcessAsync(CancellationToken ct)
    {
        try
        {
            if (_overlayProcess == null)
                return;

            await _overlayProcess.WaitForExitAsync(ct);
            _logger.LogInformation("⚠️ Overlay process exited");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring overlay process");
        }
    }

    public bool IsRunning => _overlayProcess?.HasExited == false;

    public void Dispose()
    {
        CloseOverlayAsync().Wait();
        _pipeCts?.Dispose();
    }
}
