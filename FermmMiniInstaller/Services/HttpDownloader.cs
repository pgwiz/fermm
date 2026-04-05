using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FermmMiniInstaller.Models;

namespace FermmMiniInstaller.Services
{
    public class HttpDownloader
    {
        private const int CHUNK_SIZE = 5 * 1024 * 1024; // 5 MB
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        public event Action<DownloadProgress>? ProgressChanged;

        public async Task<string> DownloadAgentAsync(string url, DownloadProgress progress)
        {
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                $"fermm-agent-{Guid.NewGuid()}.exe"
            );

            try
            {
                // Get file size
                var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                var headResponse = await _httpClient.SendAsync(headRequest);
                
                if (!headResponse.IsSuccessStatusCode)
                    throw new Exception($"Failed to get file info: {headResponse.StatusCode}");

                long totalSize = headResponse.Content.Headers.ContentLength ?? 0;
                if (totalSize <= 0)
                    throw new Exception("Could not determine file size");

                progress.TotalBytes = totalSize;

                // Download in chunks
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    long downloaded = 0;
                    int chunkCount = (int)Math.Ceiling((double)totalSize / CHUNK_SIZE);

                    for (int i = 0; i < chunkCount; i++)
                    {
                        long rangeStart = i * CHUNK_SIZE;
                        long rangeEnd = Math.Min(rangeStart + CHUNK_SIZE - 1, totalSize - 1);

                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, rangeEnd);

                        var response = await _httpClient.SendAsync(request);
                        if (!response.IsSuccessStatusCode)
                            throw new Exception($"Chunk download failed: {response.StatusCode}");

                        var chunkData = await response.Content.ReadAsByteArrayAsync();
                        await fileStream.WriteAsync(chunkData, 0, chunkData.Length);

                        downloaded += chunkData.Length;
                        progress.BytesDownloaded = downloaded;
                        ProgressChanged?.Invoke(progress);
                    }
                }

                return tempPath;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw new Exception($"Download failed: {ex.Message}", ex);
            }
        }

        public static string CalculateSha256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLower();
            }
        }
    }
}
