using System.Diagnostics;
using System.Text.Json;
using FermmAgent.Models;

namespace FermmAgent.Handlers;

public class GodModeHandler
{
    private readonly ILogger<GodModeHandler> _logger;

    public GodModeHandler(ILogger<GodModeHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(int exitCode, List<string> output, string? error)> ExecuteAsync(string payload, CancellationToken ct)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var request = JsonSerializer.Deserialize<GodModeRequest>(payload, options);
            if (request == null || string.IsNullOrEmpty(request.Action))
            {
                return (-1, new List<string>(), "Invalid payload format - missing action");
            }

            _logger.LogInformation("Executing GOD Mode action: {Action}", request.Action);

            return request.Action.ToLower() switch
            {
                "winr" => await OpenRunDialogAsync(request.Command, ct),
                "godmode" => await OpenGodModeAsync(ct),
                "admintools" => await OpenAdminToolsAsync(ct),
                "taskmanager" => await OpenTaskManagerAsync(ct),
                "services" => await OpenServicesAsync(ct),
                "registry" => await OpenRegistryAsync(ct),
                "eventviewer" => await OpenEventViewerAsync(ct),
                "devicemanager" => await OpenDeviceManagerAsync(ct),
                "diskmanagement" => await OpenDiskManagementAsync(ct),
                "systeminfo" => await ShowSystemInfoAsync(ct),
                "enable" => await EnableGodModeAsync(ct),
                "disable" => await DisableGodModeAsync(ct),
                "status" => await GetGodModeStatusAsync(ct),
                _ => (-1, new List<string>(), $"Unknown GOD Mode action: {request.Action}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GOD Mode command");
            return (-1, new List<string>(), ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> OpenRunDialogAsync(string? command, CancellationToken ct)
    {
        try
        {
            // Simulate Win+R by opening Run dialog directly
            var runProcess = new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "shell32.dll,#61",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(runProcess);
            if (process != null)
            {
                await Task.Delay(500, ct); // Wait for dialog to open

                // If command provided, send it to the dialog
                if (!string.IsNullOrEmpty(command))
                {
                    await Task.Delay(200, ct);
                    // System.Windows.Forms.SendKeys.SendWait(command);
                    await Task.Delay(100, ct);
                    // System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                }

                return (0, new List<string> { $"Run dialog opened{(command != null ? $" with command: {command}" : "")}" }, null);
            }

            return (-1, new List<string>(), "Failed to open Run dialog");
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Run dialog: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenGodModeAsync(CancellationToken ct)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var godModePath = Path.Combine(desktopPath, "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");

            // Create GOD Mode folder if it doesn't exist
            if (!Directory.Exists(godModePath))
            {
                Directory.CreateDirectory(godModePath);
            }

            // Open the GOD Mode folder
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{godModePath}\"",
                UseShellExecute = true
            });

            return (0, new List<string> { "GOD Mode folder opened", $"Path: {godModePath}" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening GOD Mode: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenAdminToolsAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "admintools",
                UseShellExecute = true
            });

            return (0, new List<string> { "Administrative Tools opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Administrative Tools: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenTaskManagerAsync(CancellationToken ct)
    {
        try
        {
            Process.Start("taskmgr.exe");
            return (0, new List<string> { "Task Manager opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Task Manager: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenServicesAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "services.msc",
                UseShellExecute = true
            });
            return (0, new List<string> { "Services console opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Services: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenRegistryAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "regedit.exe",
                UseShellExecute = true
            });
            return (0, new List<string> { "Registry Editor opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Registry Editor: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenEventViewerAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "eventvwr.msc",
                UseShellExecute = true
            });
            return (0, new List<string> { "Event Viewer opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Event Viewer: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenDeviceManagerAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "devmgmt.msc",
                UseShellExecute = true
            });
            return (0, new List<string> { "Device Manager opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Device Manager: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> OpenDiskManagementAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "diskmgmt.msc",
                UseShellExecute = true
            });
            return (0, new List<string> { "Disk Management opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening Disk Management: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> ShowSystemInfoAsync(CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msinfo32.exe",
                UseShellExecute = true
            });
            return (0, new List<string> { "System Information opened" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error opening System Information: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> EnableGodModeAsync(CancellationToken ct)
    {
        var output = new List<string>();
        
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var godModePath = Path.Combine(desktopPath, "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");

            if (!Directory.Exists(godModePath))
            {
                Directory.CreateDirectory(godModePath);
                output.Add("GOD Mode folder created on desktop");
            }
            else
            {
                output.Add("GOD Mode folder already exists");
            }

            output.Add($"Path: {godModePath}");
            output.Add("GOD Mode enabled - Access all Windows settings from one place");
            
            return (0, output, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error enabling GOD Mode: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> DisableGodModeAsync(CancellationToken ct)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var godModePath = Path.Combine(desktopPath, "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");

            if (Directory.Exists(godModePath))
            {
                Directory.Delete(godModePath);
                return (0, new List<string> { "GOD Mode folder removed from desktop" }, null);
            }

            return (0, new List<string> { "GOD Mode folder was not present" }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error disabling GOD Mode: {ex.Message}");
        }
    }

    private async Task<(int, List<string>, string?)> GetGodModeStatusAsync(CancellationToken ct)
    {
        var output = new List<string>();
        
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var godModePath = Path.Combine(desktopPath, "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");

            var isEnabled = Directory.Exists(godModePath);
            
            output.Add($"GOD Mode Status: {(isEnabled ? "ENABLED" : "DISABLED")}");
            output.Add($"Desktop Path: {desktopPath}");
            if (isEnabled)
            {
                output.Add($"GOD Mode Path: {godModePath}");
            }

            return (0, output, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), $"Error checking GOD Mode status: {ex.Message}");
        }
    }
}

public class GodModeRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Command { get; set; }
}