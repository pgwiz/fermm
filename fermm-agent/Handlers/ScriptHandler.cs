using System;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace FermmAgent.Handlers;

public class ScriptHandler
{
    private readonly ILogger<ScriptHandler> _logger;

    public ScriptHandler(ILogger<ScriptHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(int exitCode, List<string> output, string? error)> ExecuteAsync(string payload, CancellationToken ct)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var request = JsonSerializer.Deserialize<ScriptRequest>(payload, options);
            
            if (request == null || string.IsNullOrEmpty(request.Content))
            {
                return (-1, new List<string>(), "Invalid script payload");
            }

            _logger.LogInformation("Executing {ScriptType} script", request.ScriptType);
            
            return request.ScriptType?.ToLower() switch
            {
                "cmd" => await ExecuteCmd(request.Content, request.ScriptId, ct),
                "powershell" => await ExecutePowerShell(request.Content, request.ScriptId, ct),
                "bash" => await ExecuteBash(request.Content, request.ScriptId, ct),
                "sh" => await ExecuteShell(request.Content, request.ScriptId, ct),
                _ => (-1, new List<string>(), $"Unknown script type: {request.ScriptType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing script");
            return (-1, new List<string>(), ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> ExecuteCmd(string content, string? scriptId, CancellationToken ct)
    {
        var output = new List<string>();
        var logFile = $"script-logs/cmd_{scriptId ?? "unknown"}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        
        try
        {
            Directory.CreateDirectory("script-logs");
            
            var tempScript = Path.Combine(Path.GetTempPath(), $"fermm_script_{Guid.NewGuid()}.bat");
            await File.WriteAllTextAsync(tempScript, content, ct);
            
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{tempScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return (-1, new List<string>(), "Failed to start process");
            
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            
            process.WaitForExit();
            
            output.Add(stdout);
            if (!string.IsNullOrEmpty(stderr)) output.Add($"STDERR: {stderr}");
            
            // Save to log file
            await File.WriteAllLinesAsync(logFile, output, ct);
            output.Add($"Log saved to: {logFile}");
            
            File.Delete(tempScript);
            
            return (process.ExitCode, output, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string> { $"Error: {ex.Message}" }, ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> ExecutePowerShell(string content, string? scriptId, CancellationToken ct)
    {
        var output = new List<string>();
        var logFile = $"script-logs/powershell_{scriptId ?? "unknown"}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        
        try
        {
            Directory.CreateDirectory("script-logs");
            
            var tempScript = Path.Combine(Path.GetTempPath(), $"fermm_script_{Guid.NewGuid()}.ps1");
            await File.WriteAllTextAsync(tempScript, content, ct);
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return (-1, new List<string>(), "Failed to start process");
            
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            
            process.WaitForExit();
            
            output.Add(stdout);
            if (!string.IsNullOrEmpty(stderr)) output.Add($"STDERR: {stderr}");
            
            // Save to log file
            await File.WriteAllLinesAsync(logFile, output, ct);
            output.Add($"Log saved to: {logFile}");
            
            File.Delete(tempScript);
            
            return (process.ExitCode, output, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string> { $"Error: {ex.Message}" }, ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> ExecuteBash(string content, string? scriptId, CancellationToken ct)
    {
        var output = new List<string>();
        var logFile = $"script-logs/bash_{scriptId ?? "unknown"}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        
        try
        {
            Directory.CreateDirectory("script-logs");
            
            var tempScript = Path.Combine(Path.GetTempPath(), $"fermm_script_{Guid.NewGuid()}.sh");
            await File.WriteAllTextAsync(tempScript, content, ct);
            File.SetAttributes(tempScript, FileAttributes.Normal);
            
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{tempScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return (-1, new List<string>(), "Failed to start process");
            
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            
            process.WaitForExit();
            
            output.Add(stdout);
            if (!string.IsNullOrEmpty(stderr)) output.Add($"STDERR: {stderr}");
            
            // Save to log file
            await File.WriteAllLinesAsync(logFile, output, ct);
            output.Add($"Log saved to: {logFile}");
            
            File.Delete(tempScript);
            
            return (process.ExitCode, output, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string> { $"Error: {ex.Message}" }, ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> ExecuteShell(string content, string? scriptId, CancellationToken ct)
    {
        // Shell is same as bash on most Unix systems
        return await ExecuteBash(content, scriptId, ct);
    }
}

public class ScriptRequest
{
    public string? ScriptId { get; set; }
    public string? ScriptType { get; set; }
    public string? Content { get; set; }
}
