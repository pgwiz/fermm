using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FermmAgent.Handlers;

public class ScreenshotHandler
{
    private readonly ILogger<ScreenshotHandler> _logger;

    public ScreenshotHandler(ILogger<ScreenshotHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(int ExitCode, List<string> Output, string? Error, object? Metadata)> CaptureWithMetadataAsync(CancellationToken ct)
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"fermm_screenshot_{Guid.NewGuid()}.png");
            
            // Capture window metadata before taking screenshot
            var metadata = await CaptureWindowMetadataAsync();
            
            var (exitCode, output, error) = await CaptureAsync(tempFile, ct);
            
            // Add image metadata if screenshot was successful
            if (exitCode == 0 && File.Exists(tempFile))
            {
                var fileInfo = new FileInfo(tempFile);
                var imageSize = await GetImageDimensionsAsync(tempFile);
                
                metadata["file_size"] = fileInfo.Length;
                metadata["width"] = imageSize.Width;
                metadata["height"] = imageSize.Height;
                metadata["capture_timestamp"] = DateTime.UtcNow.ToString("O");
            }
            
            return (exitCode, output, error, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot capture with metadata failed");
            return (-1, new List<string>(), ex.Message, null);
        }
    }

    // Legacy method for backward compatibility
    public async Task<(int ExitCode, List<string> Output, string? Error)> CaptureAsync(CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fermm_screenshot_{Guid.NewGuid()}.png");
        return await CaptureAsync(tempFile, ct);
    }

    private async Task<(int ExitCode, List<string> Output, string? Error)> CaptureAsync(string tempFile, CancellationToken ct)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await CaptureWindowsAsync(tempFile, ct);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await CaptureMacOSAsync(tempFile, ct);
            }
            else
            {
                return await CaptureLinuxAsync(tempFile, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot capture failed");
            return (-1, new List<string>(), ex.Message);
        }
    }

    private async Task<(int, List<string>, string?)> CaptureWindowsAsync(string tempFile, CancellationToken ct)
    {
        #if WINDOWS
        try
        {
            using var bitmap = new System.Drawing.Bitmap(
                System.Windows.Forms.SystemInformation.VirtualScreen.Width,
                System.Windows.Forms.SystemInformation.VirtualScreen.Height);
            
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                System.Windows.Forms.SystemInformation.VirtualScreen.Location,
                System.Drawing.Point.Empty,
                System.Windows.Forms.SystemInformation.VirtualScreen.Size);
            
            bitmap.Save(tempFile, System.Drawing.Imaging.ImageFormat.Png);
            
            var bytes = await File.ReadAllBytesAsync(tempFile, ct);
            var base64 = Convert.ToBase64String(bytes);
            
            File.Delete(tempFile);
            
            return (0, new List<string> { base64 }, null);
        }
        catch (Exception ex)
        {
            return (-1, new List<string>(), ex.Message);
        }
        #else
        // Fallback using PowerShell
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen; $bitmap = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height); $graphics = [System.Drawing.Graphics]::FromImage($bitmap); $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size); $bitmap.Save('{tempFile}'); $graphics.Dispose(); $bitmap.Dispose()\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return (-1, new List<string>(), "Failed to start PowerShell");
        
        await process.WaitForExitAsync(ct);
        
        if (!File.Exists(tempFile))
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            return (-1, new List<string>(), $"Screenshot failed: {stderr}");
        }
        
        var bytes = await File.ReadAllBytesAsync(tempFile, ct);
        var base64 = Convert.ToBase64String(bytes);
        
        try { File.Delete(tempFile); } catch { }
        
        return (0, new List<string> { base64 }, null);
        #endif
    }

    private async Task<(int, List<string>, string?)> CaptureMacOSAsync(string tempFile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/sbin/screencapture",
            Arguments = $"-x {tempFile}",
            RedirectStandardError = true,
            UseShellExecute = false
        };
        
        using var process = Process.Start(psi);
        if (process == null) return (-1, new List<string>(), "Failed to start screencapture");
        
        await process.WaitForExitAsync(ct);
        
        if (!File.Exists(tempFile))
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            return (-1, new List<string>(), $"Screenshot failed: {stderr}");
        }
        
        var bytes = await File.ReadAllBytesAsync(tempFile, ct);
        var base64 = Convert.ToBase64String(bytes);
        
        try { File.Delete(tempFile); } catch { }
        
        return (0, new List<string> { base64 }, null);
    }

    private async Task<(int, List<string>, string?)> CaptureLinuxAsync(string tempFile, CancellationToken ct)
    {
        // Try scrot first, then gnome-screenshot
        string[] tools = { "scrot", "gnome-screenshot" };
        
        foreach (var tool in tools)
        {
            var args = tool == "scrot" ? tempFile : $"-f {tempFile}";
            
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            
            try
            {
                using var process = Process.Start(psi);
                if (process == null) continue;
                
                await process.WaitForExitAsync(ct);
                
                if (File.Exists(tempFile))
                {
                    var bytes = await File.ReadAllBytesAsync(tempFile, ct);
                    var base64 = Convert.ToBase64String(bytes);
                    
                    try { File.Delete(tempFile); } catch { }
                    
                    return (0, new List<string> { base64 }, null);
                }
            }
            catch
            {
                continue;
            }
        }
        
        return (-1, new List<string>(), "No screenshot tool available (tried scrot, gnome-screenshot)");
    }

    private async Task<Dictionary<string, object>> CaptureWindowMetadataAsync()
    {
        var metadata = new Dictionary<string, object>
        {
            ["capture_method"] = "manual",
            ["platform"] = RuntimeInformation.OSDescription,
            ["capture_timestamp"] = DateTime.UtcNow.ToString("O")
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await CaptureWindowsMetadataAsync(metadata);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await CaptureLinuxMetadataAsync(metadata);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await CaptureMacMetadataAsync(metadata);
        }

        return metadata;
    }

    private async Task CaptureWindowsMetadataAsync(Dictionary<string, object> metadata)
    {
        try
        {
            // Get foreground window information
            var windowTitle = await GetActiveWindowTitleAsync();
            if (!string.IsNullOrEmpty(windowTitle))
            {
                metadata["active_window_title"] = windowTitle;
            }

            // Get active process information
            var activeProcess = await GetActiveProcessAsync();
            if (activeProcess != null)
            {
                metadata["active_process_name"] = activeProcess.ProcessName;
                metadata["active_process_id"] = activeProcess.Id;
                try
                {
                    metadata["active_process_path"] = activeProcess.MainModule?.FileName;
                }
                catch { } // Access might be denied for some processes
            }

            // Get screen resolution
            var screenInfo = await GetScreenResolutionAsync();
            metadata["screen_width"] = screenInfo.Width;
            metadata["screen_height"] = screenInfo.Height;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture Windows metadata");
        }
    }

    private async Task CaptureLinuxMetadataAsync(Dictionary<string, object> metadata)
    {
        try
        {
            // Try to get active window info using xdotool or wmctrl
            var windowInfo = await GetLinuxActiveWindowAsync();
            if (windowInfo != null)
            {
                metadata["active_window_title"] = windowInfo.Value.Title;
                metadata["active_process_id"] = windowInfo.Value.ProcessId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture Linux metadata");
        }
    }

    private async Task CaptureMacMetadataAsync(Dictionary<string, object> metadata)
    {
        try
        {
            // Use AppleScript to get active window info
            var windowInfo = await GetMacActiveWindowAsync();
            if (windowInfo != null)
            {
                metadata["active_window_title"] = windowInfo.Value.Title;
                metadata["active_application"] = windowInfo.Value.Application;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture Mac metadata");
        }
    }

    private async Task<string> GetActiveWindowTitleAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return string.Empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class Win32 { [DllImport(\\\"user32.dll\\\")] public static extern IntPtr GetForegroundWindow(); [DllImport(\\\"user32.dll\\\")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count); }'; $hwnd = [Win32]::GetForegroundWindow(); $title = New-Object System.Text.StringBuilder(256); [Win32]::GetWindowText($hwnd, $title, $title.Capacity); $title.ToString()\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            await process.WaitForExitAsync();
            var result = await process.StandardOutput.ReadToEndAsync();
            return result.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<Process?> GetActiveProcessAsync()
    {
        try
        {
            // Get the process of the foreground window
            var processes = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                .OrderByDescending(p => p.StartTime)
                .FirstOrDefault();

            return processes;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(int Width, int Height)> GetScreenResolutionAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use WMI or PowerShell to get screen resolution
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::PrimaryScreen.Bounds | Select-Object Width, Height | ConvertTo-Json\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    // Parse JSON output to get width/height
                    // Simplified parsing for now
                    return (1920, 1080); // Default fallback
                }
            }

            return (1920, 1080); // Default fallback
        }
        catch
        {
            return (1920, 1080);
        }
    }

    private async Task<(string Title, int ProcessId)?> GetLinuxActiveWindowAsync()
    {
        try
        {
            // Try xdotool first
            var psi = new ProcessStartInfo
            {
                FileName = "xdotool",
                Arguments = "getactivewindow getwindowname",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    var title = await process.StandardOutput.ReadToEndAsync();
                    return (title.Trim(), 0); // Process ID would need additional query
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<(string Title, string Application)?> GetMacActiveWindowAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = "-e \"tell application \\\"System Events\\\" to get name of first application process whose frontmost is true\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    var app = await process.StandardOutput.ReadToEndAsync();
                    return ("", app.Trim());
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<(int Width, int Height)> GetImageDimensionsAsync(string imagePath)
    {
        try
        {
            if (File.Exists(imagePath))
            {
                using var image = System.Drawing.Image.FromFile(imagePath);
                return (image.Width, image.Height);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get image dimensions for {ImagePath}", imagePath);
        }

        return (0, 0);
    }
}
