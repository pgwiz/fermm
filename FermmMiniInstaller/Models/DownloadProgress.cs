namespace FermmMiniInstaller.Models
{
    public class DownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public int PercentComplete => TotalBytes > 0 ? (int)((BytesDownloaded * 100) / TotalBytes) : 0;
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
    }
}
