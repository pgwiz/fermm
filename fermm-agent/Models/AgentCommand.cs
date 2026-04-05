using System.Text.Json.Serialization;

namespace FermmAgent.Models;

public record AgentCommand(
    [property: JsonPropertyName("command_id")] string CommandId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] string? Payload,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 30
);
