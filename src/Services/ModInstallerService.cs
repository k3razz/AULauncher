using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace AULauncher.Services
{
    public class ModInstallerService
    {
        public readonly SettingsService _settingsService;
        private readonly string _amongUsPath;

        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public ModInstallerService(SettingsService settingsService, string amongUsPath)
        {
            _settingsService = settingsService;
            _amongUsPath = amongUsPath;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AULauncher");
        }

        public async Task InstallModAsync(Action<string, double> onProgress)
        {
            string tempPath = Path.GetTempPath();

            string repoBase = "https://github.com/CallOfCreator/NewMod/releases/latest/download/";

            bool isMicrosoftStore = _amongUsPath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)
                                 || _amongUsPath.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

            if (isMicrosoftStore)
            {
                onProgress?.Invoke("Microsoft Store version may not support mods", 0);
            }

            string[] modZips = isMicrosoftStore ? new[] { "NewMod-MS.zip" } : new[] { "NewMod.zip" };
            bool installedZip = false;

            string tempZip = Path.Combine(tempPath, "NewMod_temp.zip");
            string tempExtractDir = Path.Combine(tempPath, "NewModExtract");

            foreach (var zipName in modZips)
            {
                string url = repoBase + zipName;

                try
                {
                    onProgress?.Invoke("Downloading " + zipName, 0);

                    await DownloadFileAsync(url, tempZip,
                        progress => onProgress?.Invoke("Downloading " + zipName, progress));

                    if (Directory.Exists(tempExtractDir))
                    {
                        try { Directory.Delete(tempExtractDir, true); } catch { }
                    }

                    ZipFile.ExtractToDirectory(tempZip, tempExtractDir, true);

                    string sourceDir = Path.Combine(tempExtractDir, "NewMod");

                    if (Directory.Exists(sourceDir))
                    {
                        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(sourceDir, filePath);
                            var destFile = Path.Combine(_amongUsPath, relativePath);

                            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                            File.Copy(filePath, destFile, true);
                        }
                    }

                    onProgress?.Invoke("Extracted " + zipName, 100);
                    installedZip = true;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to install {zipName}: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);

                    if (Directory.Exists(tempExtractDir))
                    {
                        try { Directory.Delete(tempExtractDir, true); } catch { }
                    }
                }
            }

            if (!installedZip)
            {
                onProgress?.Invoke("No installation package found", 0);
            }

            onProgress?.Invoke("Install complete", 100);
        }

        public bool IsModInstalled()
        {
            // Минимальная проверка — просто наличие папки
            string bepinExPath = Path.Combine(_amongUsPath, "BepInEx");
            return Directory.Exists(bepinExPath);
        }

        // ✅ ВОТ ЭТО ВАЖНО — вернули метод
        public async Task DownloadFileAsyncWrapper(string url, string destination, Action<double> onProgress = null)
        {
            await DownloadFileAsync(url, destination, onProgress);
        }

        private async Task DownloadFileAsync(string url, string destination, Action<double> onProgress)
        {
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    null,
                    response.StatusCode);

            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            long downloaded = 0;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloaded += bytesRead;

                if (onProgress != null && totalBytes > 0)
                {
                    double progress = (double)downloaded / totalBytes * 100.0;
                    onProgress(progress);
                }
            }
        }
    }
}