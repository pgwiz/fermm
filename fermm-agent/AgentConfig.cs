namespace FermmAgent;

public class AgentConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 15;
    public string LogLevel { get; set; } = "Information";
    
    public static AgentConfig LoadFromEnvironment()
    {
        var config = new AgentConfig
        {
            ServerUrl = Environment.GetEnvironmentVariable("FERMM_SERVER_URL") ?? "",
            Token = Environment.GetEnvironmentVariable("FERMM_TOKEN") ?? "",
            DeviceId = Environment.GetEnvironmentVariable("FERMM_DEVICE_ID") ?? "",
            LogLevel = Environment.GetEnvironmentVariable("FERMM_LOG_LEVEL") ?? "Information"
        };
        
        if (int.TryParse(Environment.GetEnvironmentVariable("FERMM_POLL_INTERVAL_SECONDS"), out var interval))
        {
            config.PollIntervalSeconds = interval;
        }
        
        // Generate device ID if not set
        if (string.IsNullOrEmpty(config.DeviceId))
        {
            var idFile = Path.Combine(AppContext.BaseDirectory, ".device_id");
            if (File.Exists(idFile))
            {
                config.DeviceId = File.ReadAllText(idFile).Trim();
            }
            else
            {
                config.DeviceId = Guid.NewGuid().ToString();
                try { File.WriteAllText(idFile, config.DeviceId); } catch { }
            }
        }
        
        return config;
    }
    
    public void Validate()
    {
        if (string.IsNullOrEmpty(ServerUrl))
            throw new InvalidOperationException("FERMM_SERVER_URL environment variable is required");
        // Token can be empty - it will be set during auto-registration
    }
}
