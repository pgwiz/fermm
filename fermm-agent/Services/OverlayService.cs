using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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

            // Prefer launching in the active user session (service-safe)
            if (!TryLaunchOverlayInUserSession(agentExe))
            {
                // Fallback: direct Process.Start (interactive mode)
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
            }

            _logger.LogInformation("✅ Overlay spawned with PID {PID}", _overlayProcess?.Id);

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

    private bool TryLaunchOverlayInUserSession(string agentExe)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr envBlock = IntPtr.Zero;
        PROCESS_INFORMATION pi = default;

        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                _logger.LogWarning("No active user session found for overlay");
                return false;
            }

            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                _logger.LogWarning("WTSQueryUserToken failed: {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            if (!DuplicateTokenEx(
                    userToken,
                    TOKEN_ALL_ACCESS,
                    IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary,
                    out primaryToken))
            {
                _logger.LogWarning("DuplicateTokenEx failed: {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            if (!CreateEnvironmentBlock(out envBlock, primaryToken, false))
            {
                _logger.LogWarning("CreateEnvironmentBlock failed: {Error}", Marshal.GetLastWin32Error());
                envBlock = IntPtr.Zero;
            }

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default"
            };

            var commandLine = $"\"{agentExe}\" --overlay --device-id {_config.DeviceId}";
            var creationFlags = envBlock != IntPtr.Zero ? CREATE_UNICODE_ENVIRONMENT : 0;

            if (!CreateProcessAsUser(
                    primaryToken,
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    envBlock,
                    Path.GetDirectoryName(agentExe),
                    ref si,
                    out pi))
            {
                _logger.LogWarning("CreateProcessAsUser failed: {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            _overlayProcess = Process.GetProcessById((int)pi.dwProcessId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch overlay in user session");
            return false;
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public bool IsRunning => _overlayProcess?.HasExited == false;

    public void Dispose()
    {
        CloseOverlayAsync().Wait();
        _pipeCts?.Dispose();
    }
}
