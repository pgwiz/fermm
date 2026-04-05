namespace FermmAgent.Models;

public record CommandResult(
    string CommandId,
    string DeviceId,
    string Type,
    int ExitCode,
    List<string> Output,
    string? Error,
    long DurationMs,
    DateTime Timestamp
);
