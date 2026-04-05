using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FermmAgent.Handlers;

public class SysInfoHandler
{
    private readonly ILogger<SysInfoHandler> _logger;

    public SysInfoHandler(ILogger<SysInfoHandler> logger)
    {
        _logger = logger;
    }

    public Task<(int ExitCode, List<string> Output, string? Error)> CollectAsync(CancellationToken ct)
    {
        try
        {
            var info = new
            {
                hostname = Environment.MachineName,
                os = RuntimeInformation.OSDescription,
                arch = RuntimeInformation.ProcessArchitecture.ToString(),
                dotnetVersion = RuntimeInformation.FrameworkDescription,
                uptime = GetUptime(),
                memory = GetMemoryInfo(),
                disks = GetDiskInfo(),
                network = GetNetworkInfo()
            };
            
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            return Task.FromResult((0, new List<string> { json }, (string?)null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect system info");
            return Task.FromResult((-1, new List<string>(), (string?)ex.Message));
        }
    }

    private static object? GetUptime()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return new
            {
                days = uptime.Days,
                hours = uptime.Hours,
                minutes = uptime.Minutes,
                totalHours = Math.Round(uptime.TotalHours, 1)
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? GetMemoryInfo()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return new
            {
                totalMb = Math.Round(gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 0),
                processUsedMb = Math.Round(Environment.WorkingSet / 1024.0 / 1024.0, 1)
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<object> GetDiskInfo()
    {
        var disks = new List<object>();
        
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                disks.Add(new
                {
                    name = drive.Name,
                    type = drive.DriveType.ToString(),
                    totalGb = Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 1),
                    freeGb = Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1),
                    usedPercent = Math.Round((1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100, 1)
                });
            }
        }
        catch { }
        
        return disks;
    }

    private static List<object> GetNetworkInfo()
    {
        var networks = new List<object>();
        
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var props = iface.GetIPProperties();
                var addresses = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ||
                               a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();
                
                if (addresses.Count > 0)
                {
                    networks.Add(new
                    {
                        name = iface.Name,
                        type = iface.NetworkInterfaceType.ToString(),
                        addresses
                    });
                }
            }
        }
        catch { }
        
        return networks;
    }
}
