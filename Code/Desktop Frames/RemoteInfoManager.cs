using Desktop_Frames.Localization;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;

namespace Desktop_Frames
{
    public static class RemoteInfoManager
    {
        private static readonly Uri ManifestUri = new("https://raw.githubusercontent.com/limbo666/DesktopFramesPlus/refs/heads/main/ngdfcs/getversion.json");
        private const string ReleasesUrl = "https://github.com/limbo666/DesktopFramesPlus/releases";

        private static bool _hasChecked = false;

        public static void Initialize()
        {
            if (_hasChecked) return;
            _hasChecked = true;

            Task.Run(async () =>
            {
                Uri? developerManifest = null;
#if DEBUG
                developerManifest = ReadDeveloperManifestUri();
#endif
                await Task.Delay(developerManifest == null ? 25000 : 3000);
                await PerformCheck(developerManifest ?? ManifestUri);
            });
        }

        internal static Uri? GetHttpsUri(string? value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrEmpty(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri;
            }

            return null;
        }

#if DEBUG
        private static Uri? ReadDeveloperManifestUri()
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "developer_repo.txt");
            if (!System.IO.File.Exists(path)) return null;

            try
            {
                var uri = GetHttpsUri(System.IO.File.ReadAllText(path).Trim());
                if (uri == null)
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, "RemoteInfoManager: developer_repo.txt must contain an HTTPS URL without credentials.");
                return uri;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"RemoteInfoManager: Could not read developer_repo.txt: {ex.Message}");
                return null;
            }
        }
#endif

        private static async Task PerformCheck(Uri manifestUri)
        {
            try
            {
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopFrames/2.7");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var requestUri = new UriBuilder(manifestUri)
                    {
                        Query = (manifestUri.Query.Length == 0 ? "" : manifestUri.Query[1..] + "&") +
                            $"t={DateTime.UtcNow.Ticks}"
                    };

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"RemoteInfoManager: Downloading manifest from {requestUri.Host}");

                    string json = await client.GetStringAsync(requestUri.Uri);

                    if (string.IsNullOrWhiteSpace(json)) return;

                    var manifest = JsonConvert.DeserializeObject<RemoteManifest>(json);
                    if (manifest == null) return;

                    // The manifest only supplies data for notifications.
                    ProcessMeta(manifest.Meta);

                    if (manifest.Announcements != null && manifest.Announcements.Count > 0)
                    {
                        foreach (var msg in manifest.Announcements)
                        {
                            if (ShouldShowMessage(msg))
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    // SHOW THE NOTIFICATION WINDOW
                                    NotificationFormManager.Show(msg);
                                });
                                break; // Show only one announcement per session
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail silently on network errors
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"RemoteInfoManager Error: {ex.Message}");
            }
        }

        private static void ProcessMeta(RemoteMeta meta)
        {
            if (meta == null) return;

            // VERSION CHECK (4-Part Support: Major.Minor.Build.Revision)
            Version currentVer = Assembly.GetEntryAssembly().GetName().Version;

            if (Version.TryParse(meta.LatestVersion, out Version remoteVer))
            {
                // This comparison checks version
                if (remoteVer > currentVer)
                {
                    // Construct a fake "Announcement" for the update so we can reuse the UI
                    var updateMsg = new RemoteAnnouncement
                    {
                        Id = $"UPDATE_{remoteVer}", // Unique ID ensures they see it once/max count
                        Title = Strings.UpdateAvailable,
                        Body = $"A new version ({remoteVer}) is available.\nYou are currently using {currentVer}.",
                        Type = meta.CriticalUpdate ? "Alert" : "Info",
                        Link = GetHttpsUri(meta.DownloadUrl)?.AbsoluteUri ?? ReleasesUrl,
                        CanUserDismiss = !meta.CriticalUpdate,
                        MaxDisplayCount = meta.CriticalUpdate ? 50 : 3 // Nag more for critical
                    };

                    if (ShouldShowMessage(updateMsg))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            NotificationFormManager.Show(updateMsg);
                        });
                    }
                }
            }
        }

        private static bool ShouldShowMessage(RemoteAnnouncement msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.Id)) return false;

            // 1. Check Dismissal
            if (RegistryHelper.IsMessageDismissed(msg.Id)) return false;

            // 2. Check Max Count
            int currentCount = RegistryHelper.GetMessageDisplayCount(msg.Id);
            if (currentCount >= msg.MaxDisplayCount) return false;

            // 3. Check Version Targeting
            Version currentVer = Assembly.GetEntryAssembly().GetName().Version;

            if (Version.TryParse(msg.TargetVersionMin, out Version minVer) && currentVer < minVer) return false;
            if (Version.TryParse(msg.TargetVersionMax, out Version maxVer) && currentVer > maxVer) return false;

            return true;
        }
    }
}