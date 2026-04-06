using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;

namespace FermmAgent.Handlers
{
    public class FilePullHandler
    {
        private readonly HttpClient _httpClient;
        private const int CHUNK_SIZE = 256 * 1024; // 256KB chunks

        public FilePullHandler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(int exitCode, List<string> output, string? error)> ExecuteAsync(
            string payload,
            CancellationToken ct)
        {
            try
            {
                var request = JsonSerializer.Deserialize<FilePullRequest>(
                    payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (request?.FilePath == null)
                    return (1, new List<string>(), "Missing file_path");

                return await PullFileAsync(request, ct);
            }
            catch (Exception ex)
            {
                return (1, new List<string>(), ex.Message);
            }
        }

        private async Task<(int, List<string>, string?)> PullFileAsync(
            FilePullRequest request,
            CancellationToken ct)
        {
            var output = new List<string>();

            try
            {
                // Validate file exists
                if (!File.Exists(request.FilePath))
                    return (1, output, $"File not found: {request.FilePath}");

                var fileInfo = new FileInfo(request.FilePath);
                output.Add($"✓ Found file: {request.FilePath} ({fileInfo.Length} bytes)");

                // Calculate total chunks
                int totalChunks = (int)((fileInfo.Length + CHUNK_SIZE - 1) / CHUNK_SIZE);
                output.Add($"✓ Will split into {totalChunks} chunks of {CHUNK_SIZE} bytes");

                // Start upload session
                var sessionResponse = await StartUploadSession(request, fileInfo, ct);
                if (sessionResponse == null)
                    return (1, output, "Failed to start upload session");

                string sessionId = sessionResponse;
                output.Add($"✓ Started session: {sessionId}");

                // Upload chunks
                int chunksUploaded = 0;
                using (var fs = File.OpenRead(request.FilePath))
                {
                    var buffer = new byte[CHUNK_SIZE];
                    int bytesRead;
                    int chunkIndex = 0;

                    while ((bytesRead = fs.Read(buffer, 0, CHUNK_SIZE)) > 0 && !ct.IsCancellationRequested)
                    {
                        // Calculate hash of this chunk
                        using (var sha = SHA256.Create())
                        {
                            var hash = sha.ComputeHash(buffer, 0, bytesRead);
                            var chunkHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                            // Upload chunk with retry logic
                            int retries = 3;
                            while (retries > 0)
                            {
                                try
                                {
                                    var uploaded = await UploadChunk(
                                        sessionId,
                                        chunkIndex,
                                        chunkHash,
                                        buffer,
                                        bytesRead,
                                        ct
                                    );

                                    if (uploaded)
                                    {
                                        chunksUploaded++;
                                        output.Add($"✓ Chunk {chunkIndex + 1}/{totalChunks} ({bytesRead} bytes)");
                                        break;
                                    }

                                    retries--;
                                    if (retries > 0)
                                        await Task.Delay(1000, ct);
                                }
                                catch (Exception)
                                {
                                    retries--;
                                    if (retries == 0)
                                        throw;
                                    await Task.Delay(1000, ct);
                                }
                            }
                        }

                        chunkIndex++;
                    }
                }

                output.Add($"✓ Uploaded {chunksUploaded}/{totalChunks} chunks");

                // Complete upload and get verification
                var completeResult = await CompleteUpload(request, sessionId, ct);
                if (!completeResult)
                    return (1, output, "Failed to complete upload");

                output.Add($"✓ Upload completed successfully");
                output.Add($"✓ Download URL: /api/files/chunks/completed/{sessionId}/{Path.GetFileName(request.FilePath)}");

                // Cleanup local file if requested
                if (request.DeleteAfterUpload)
                {
                    try
                    {
                        File.Delete(request.FilePath);
                        output.Add($"✓ Deleted local file: {request.FilePath}");
                    }
                    catch (Exception ex)
                    {
                        output.Add($"⚠ Could not delete local file: {ex.Message}");
                    }
                }

                return (0, output, null);
            }
            catch (Exception ex)
            {
                return (1, output, ex.Message);
            }
        }

        private async Task<string?> StartUploadSession(
            FilePullRequest request,
            FileInfo fileInfo,
            CancellationToken ct)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        device_id = Environment.GetEnvironmentVariable("DEVICE_ID") ?? "unknown",
                        filepath = request.FilePath,
                        filename = Path.GetFileName(request.FilePath),
                        file_size = fileInfo.Length
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync(
                    "/api/files/chunks/start",
                    content,
                    ct
                );

                if (!response.IsSuccessStatusCode)
                    return null;

                var responseData = await response.Content.ReadAsStringAsync(ct);
                using var jsonDoc = JsonDocument.Parse(responseData);
                var sessionId = jsonDoc.RootElement.GetProperty("session_id").GetString();
                return sessionId;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> UploadChunk(
            string sessionId,
            int chunkIndex,
            string chunkHash,
            byte[] buffer,
            int length,
            CancellationToken ct)
        {
            try
            {
                using (var form = new MultipartFormDataContent())
                {
                    form.Add(new ByteArrayContent(buffer, 0, length), "file", $"chunk_{chunkIndex}");
                    form.Add(new StringContent(chunkIndex.ToString()), "chunk_index");
                    form.Add(new StringContent(chunkHash), "chunk_hash");

                    var response = await _httpClient.PostAsync(
                        $"/api/files/chunks/{sessionId}/upload",
                        form,
                        ct
                    );

                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CompleteUpload(
            FilePullRequest request,
            string sessionId,
            CancellationToken ct)
        {
            try
            {
                // Calculate file hash
                string fileHash = "";
                if (File.Exists(request.FilePath))
                {
                    using (var sha = SHA256.Create())
                    using (var fs = File.OpenRead(request.FilePath))
                    {
                        var hash = sha.ComputeHash(fs);
                        fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }

                var content = new StringContent(
                    JsonSerializer.Serialize(new { file_hash = fileHash }),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync(
                    $"/api/files/chunks/{sessionId}/complete",
                    content,
                    ct
                );

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    public class FilePullRequest
    {
        public string FilePath { get; set; } = "";
        public bool DeleteAfterUpload { get; set; } = false;
    }
}
