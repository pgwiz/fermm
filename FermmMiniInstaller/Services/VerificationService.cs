using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FermmMiniInstaller.Models;

namespace FermmMiniInstaller.Services
{
    public class VerificationService
    {
        private const string VERCEL_URL = "https://linkify-ten-sable.vercel.app/api/env";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private string GetConfigPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microlens",
                "config.json"
            );
        }

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            var updateInfo = new UpdateInfo { NeedsUpdate = false };

            try
            {
                // Query Vercel for UPDATE_DATE
                var response = await _httpClient.GetStringAsync(VERCEL_URL);
                var json = JsonDocument.Parse(response);

                if (!json.RootElement.TryGetProperty("UPDATE_DATE", out var dateElement))
                {
                    // If no UPDATE_DATE, assume we need update
                    updateInfo.NeedsUpdate = true;
                    return updateInfo;
                }

                string newDate = dateElement.GetString() ?? "";
                updateInfo.NewDate = newDate;

                // Check current installation
                string configPath = GetConfigPath();
                if (!File.Exists(configPath))
                {
                    // First installation
                    updateInfo.NeedsUpdate = true;
                    return updateInfo;
                }

                try
                {
                    var configJson = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (configJson.RootElement.TryGetProperty("install_date", out var currentDateElement))
                    {
                        string currentDate = currentDateElement.GetString() ?? "";
                        updateInfo.CurrentDate = currentDate;

                        // Compare dates (dd/mm/yy format)
                        if (CompareVersions(currentDate, newDate) < 0)
                        {
                            updateInfo.NeedsUpdate = true;
                        }
                    }
                    else
                    {
                        updateInfo.NeedsUpdate = true;
                    }
                }
                catch
                {
                    updateInfo.NeedsUpdate = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Verification error: {ex.Message}");
                updateInfo.NeedsUpdate = false; // Don't force update on error
            }

            return updateInfo;
        }

        private int CompareVersions(string currentDate, string newDate)
        {
            // Parse dd/mm/yy format
            try
            {
                if (DateTime.TryParseExact(currentDate, "dd/MM/yy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var current) &&
                    DateTime.TryParseExact(newDate, "dd/MM/yy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var newer))
                {
                    return current.CompareTo(newer);
                }
            }
            catch { }
            
            return 0;
        }
    }
}
