using System.Text.Json;

namespace FermmAgent.Handlers;

public class FileHandler
{
    private readonly ILogger<FileHandler> _logger;

    public FileHandler(ILogger<FileHandler> logger)
    {
        _logger = logger;
    }

    public Task<(int ExitCode, List<string> Output, string? Error)> ListDirAsync(string path, CancellationToken ct)
    {
        try
        {
            path = Path.GetFullPath(path);
            
            if (!Directory.Exists(path))
            {
                return Task.FromResult((-1, new List<string>(), (string?)$"Directory not found: {path}"));
            }
            
            var dirInfo = new DirectoryInfo(path);
            
            var dirs = dirInfo.GetDirectories()
                .Select(d => new
                {
                    name = d.Name,
                    is_dir = true,
                    size = (long)0,
                    modified = d.LastWriteTimeUtc.ToString("o")
                });
            
            var files = dirInfo.GetFiles()
                .Select(f => new
                {
                    name = f.Name,
                    is_dir = false,
                    size = f.Length,
                    modified = f.LastWriteTimeUtc.ToString("o")
                });
            
            var entries = dirs.Concat(files).OrderBy(e => !e.is_dir).ThenBy(e => e.name).ToList();
            
            var result = new
            {
                path,
                entries
            };
            
            var json = JsonSerializer.Serialize(result);
            return Task.FromResult((0, new List<string> { json }, (string?)null));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult((-1, new List<string>(), (string?)$"Access denied: {path}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory {Path}", path);
            return Task.FromResult((-1, new List<string>(), (string?)ex.Message));
        }
    }

    public async Task<(int ExitCode, List<string> Output, string? Error)> ReadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            path = Path.GetFullPath(path);
            
            if (!File.Exists(path))
            {
                return (-1, new List<string>(), $"File not found: {path}");
            }
            
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var base64 = Convert.ToBase64String(bytes);
            
            var result = new
            {
                path,
                size = bytes.Length,
                content = base64
            };
            
            var json = JsonSerializer.Serialize(result);
            return (0, new List<string> { json }, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (-1, new List<string>(), $"Access denied: {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file {Path}", path);
            return (-1, new List<string>(), ex.Message);
        }
    }

    public async Task<(int ExitCode, List<string> Output, string? Error)> WriteFileAsync(string payload, CancellationToken ct)
    {
        try
        {
            // Payload format: "path|base64content"
            var parts = payload.Split('|', 2);
            if (parts.Length != 2)
            {
                return (-1, new List<string>(), "Invalid payload format. Expected: path|base64content");
            }
            
            var path = Path.GetFullPath(parts[0]);
            var bytes = Convert.FromBase64String(parts[1]);
            
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            await File.WriteAllBytesAsync(path, bytes, ct);
            
            _logger.LogInformation("Wrote {Bytes} bytes to {Path}", bytes.Length, path);
            return (0, new List<string> { $"Wrote {bytes.Length} bytes to {path}" }, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write file");
            return (-1, new List<string>(), ex.Message);
        }
    }
}
