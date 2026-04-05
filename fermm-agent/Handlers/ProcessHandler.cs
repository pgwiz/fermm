using System.Diagnostics;
using System.Text.Json;

namespace FermmAgent.Handlers;

public class ProcessHandler
{
    private readonly ILogger<ProcessHandler> _logger;

    public ProcessHandler(ILogger<ProcessHandler> logger)
    {
        _logger = logger;
    }

    public Task<(int ExitCode, List<string> Output, string? Error)> ListAsync(CancellationToken ct)
    {
        try
        {
            var processes = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new
                        {
                            pid = p.Id,
                            name = p.ProcessName,
                            memoryMb = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                            status = p.Responding ? "Running" : "Not Responding"
                        };
                    }
                    catch
                    {
                        return new
                        {
                            pid = p.Id,
                            name = p.ProcessName,
                            memoryMb = 0.0,
                            status = "Unknown"
                        };
                    }
                })
                .OrderBy(p => p.name)
                .ToList();
            
            var json = JsonSerializer.Serialize(processes);
            return Task.FromResult((0, new List<string> { json }, (string?)null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list processes");
            return Task.FromResult((-1, new List<string>(), (string?)ex.Message));
        }
    }

    public Task<(int ExitCode, List<string> Output, string? Error)> KillAsync(int pid, CancellationToken ct)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            
            process.Kill(entireProcessTree: true);
            
            _logger.LogInformation("Killed process {Pid} ({Name})", pid, name);
            return Task.FromResult((0, new List<string> { $"Killed process {pid} ({name})" }, (string?)null));
        }
        catch (ArgumentException)
        {
            return Task.FromResult((-1, new List<string>(), (string?)$"Process {pid} not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill process {Pid}", pid);
            return Task.FromResult((-1, new List<string>(), (string?)ex.Message));
        }
    }
}
