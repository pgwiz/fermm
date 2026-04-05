using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FermmAgent.Models;
using FermmAgent.Services;

namespace FermmAgent.Handlers;

public class OverlayHandler
{
    private readonly OverlayService _overlayService;
    private readonly ILogger<OverlayHandler> _logger;
    private readonly AgentConfig _config;

    public OverlayHandler(OverlayService overlayService, AgentConfig config, ILogger<OverlayHandler> logger)
    {
        _overlayService = overlayService;
        _config = config;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(AgentCommand cmd, CancellationToken ct)
    {
        try
        {
            var payload = cmd.Payload ?? "{}";
            var options = JsonDocument.Parse(payload);
            var root = options.RootElement;

            var action = root.TryGetProperty("action", out var actionElem) 
                ? actionElem.GetString() 
                : null;

            return action switch
            {
                "spawn" => await HandleSpawnAsync(root, cmd, ct),
                "close" => await HandleCloseAsync(cmd, ct),
                "send_message" => await HandleSendMessageAsync(root, cmd, ct),
                _ => new CommandResult(
                    cmd.CommandId,
                    _config.DeviceId,
                    "overlay",
                    -1,
                    new List<string>(),
                    $"Unknown overlay action: {action}",
                    0,
                    DateTime.UtcNow
                )
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling overlay command");
            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                -1,
                new List<string>(),
                ex.Message,
                0,
                DateTime.UtcNow
            );
        }
    }

    private async Task<CommandResult> HandleSpawnAsync(JsonElement root, AgentCommand cmd, CancellationToken ct)
    {
        try
        {
            var config = root.TryGetProperty("config", out var cfgElem) 
                ? cfgElem.GetRawText() 
                : null;

            await _overlayService.SpawnOverlayAsync(config, ct);

            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                0,
                new List<string> { "Overlay spawned successfully" },
                null,
                0,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn overlay");
            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                -1,
                new List<string>(),
                ex.Message,
                0,
                DateTime.UtcNow
            );
        }
    }

    private async Task<CommandResult> HandleCloseAsync(AgentCommand cmd, CancellationToken ct)
    {
        try
        {
            await _overlayService.CloseOverlayAsync();

            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                0,
                new List<string> { "Overlay closed successfully" },
                null,
                0,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close overlay");
            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                -1,
                new List<string>(),
                ex.Message,
                0,
                DateTime.UtcNow
            );
        }
    }

    private async Task<CommandResult> HandleSendMessageAsync(JsonElement root, AgentCommand cmd, CancellationToken ct)
    {
        try
        {
            var message = root.TryGetProperty("message", out var msgElem)
                ? msgElem.GetString()
                : null;

            if (string.IsNullOrEmpty(message))
                return new CommandResult(
                    cmd.CommandId,
                    _config.DeviceId,
                    "overlay",
                    -1,
                    new List<string>(),
                    "Message cannot be empty",
                    0,
                    DateTime.UtcNow
                );

            await _overlayService.SendMessageToOverlayAsync(message);

            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                0,
                new List<string> { "Message sent to overlay" },
                null,
                0,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to overlay");
            return new CommandResult(
                cmd.CommandId,
                _config.DeviceId,
                "overlay",
                -1,
                new List<string>(),
                ex.Message,
                0,
                DateTime.UtcNow
            );
        }
    }
}
