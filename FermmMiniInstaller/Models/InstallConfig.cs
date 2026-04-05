namespace FermmMiniInstaller.Models
{
    public class InstallConfig
    {
        public string Version { get; set; } = "";
        public DateTime InstallDate { get; set; }
        public string InstallPath { get; set; } = "";
        public string HostUrl { get; set; } = "";
        public string HostId { get; set; } = "";
    }

    public class UpdateInfo
    {
        public bool NeedsUpdate { get; set; }
        public string NewDate { get; set; } = "";
        public string CurrentDate { get; set; } = "";
    }
}
