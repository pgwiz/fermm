using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FermmAgent.Handlers;

public class ShellHandler
{
    private readonly ILogger<ShellHandler> _logger;

    public ShellHandler(ILogger<ShellHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(int ExitCode, List<string> Output, string? Error)> ExecuteAsync(
        string command, 
        Func<string, Task>? onLine,
        CancellationToken ct)
    {
        var output = new List<string>();
        string? error = null;
        
        var (shell, args) = GetShellCommand(command);
        
        _logger.LogDebug("Executing: {Shell} {Args}", shell, args);
        
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = new Process { StartInfo = psi };
        
        process.OutputDataReceived += async (_, e) =>
        {
            if (e.Data == null) return;
            output.Add(e.Data);
            if (onLine != null)
            {
                try { await onLine(e.Data); } catch { }
            }
        };
        
        process.ErrorDataReceived += async (_, e) =>
        {
            if (e.Data == null) return;
            output.Add($"[stderr] {e.Data}");
            if (onLine != null)
            {
                try { await onLine($"[stderr] {e.Data}"); } catch { }
            }
        };
        
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync(ct);
            
            // Small delay to ensure all output is captured
            await Task.Delay(100, ct);
            
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell execution failed");
            return (-1, output, ex.Message);
        }
    }

    private static (string Shell, string Args) GetShellCommand(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", $"/c {command}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ("/bin/zsh", $"-c \"{command.Replace("\"", "\\\"")}\"");
        }
        else
        {
            return ("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"");
        }
    }
}
