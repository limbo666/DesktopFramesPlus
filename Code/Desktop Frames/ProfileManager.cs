using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Desktop_Frames.Localization;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages multi-profile support, directory paths, and ID-based navigation.
    /// Acts as the central authority for where files are stored.
    /// </summary>
    public static class ProfileManager
    {
        private static List<AutomationRule> _automationRules = new List<AutomationRule>();
        private static string _manualBaseProfile = "Default";

        public static List<AutomationRule> AutomationRules => _automationRules;
        public static string ManualBaseProfile => _manualBaseProfile;

        // NEW OPTION: Display OSD on switch
        public static bool DisplayProfileNameOnSwitch { get; set; } = false;

        public static void SetManualBaseProfile(string name) => _manualBaseProfile = name;

        private static string _appBaseDir;
        private static string _profilesRootDir;
        private static string _currentProfileName = "Default";

        // System Files
        private const string PROFILE_CONFIG_FILE = "ProfileOptions.json";
        private const string MASTER_OPTIONS_FILE = "MasterOptions.json";

        // Internal cache of profile metadata
        private static List<ProfileInfo> _profileCache = new List<ProfileInfo>();

        public class ProfileInfo
        {
            public string Name { get; set; }
            public int Id { get; set; }
        }

        // Profile Specific Files/Folders to Migrate
        private static readonly string[] FILES_TO_MOVE = { "frames.json", "fences.json", "options.json" };
        private static readonly string[] FOLDERS_TO_MOVE = { "Shortcuts", "Temp Shortcuts", "Last Fence Deleted", "CopiedItem", "Backups" };

        public static string CurrentProfileName => _currentProfileName;

        public static string CurrentProfileDir
        {
            get
            {
                return ProfileDirectory.GetRegisteredPath(_profilesRootDir, _currentProfileName);
            }
        }

        static ProfileManager()
        {
            _appBaseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            _profilesRootDir = Path.Combine(_appBaseDir, "Profiles");
        }

        public static void Initialize()
        {
            try
            {
                // 1. Check Migration
                // ====================================================================
                // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                // ====================================================================
                bool legacyDataExists = File.Exists(Path.Combine(_appBaseDir, "fences.json")) || File.Exists(Path.Combine(_appBaseDir, "frames.json"));
                bool profilesDirExists = Directory.Exists(_profilesRootDir);

                if (legacyDataExists && !profilesDirExists)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Legacy data detected. Migrating...");
                    MigrateLegacyData();
                }

                // 2. Ensure Root Exists
                if (!Directory.Exists(_profilesRootDir)) Directory.CreateDirectory(_profilesRootDir);

                // 3. Load & Sanitize
                LoadAndSanitizeConfig();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Profile System Initialized. Active: {_currentProfileName} (ID: {GetProfileId(_currentProfileName)})");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Profile Init Failed: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgProfileSystemError", ex.Message), Strings.DlgCriticalError);
            }
        }

        // --- CORE LOGIC: ID & JSON MANAGEMENT ---
        private static void LoadAndSanitizeConfig()
        {
            string configPath = Path.Combine(_appBaseDir, PROFILE_CONFIG_FILE);
            JObject config = new JObject();
            bool saveNeeded = false;

            // 1. Read existing JSON
            if (File.Exists(configPath))
            {
                try { config = JObject.Parse(File.ReadAllText(configPath)); }
                catch { saveNeeded = true; }
            }
            else saveNeeded = true;

            // 2. Get Physical Folders
            if (!ProfileDirectory.IsLocalDirectory(_profilesRootDir))
                throw new IOException("Profiles must be a physical directory.");
            var actualFolders = Directory.GetDirectories(_profilesRootDir)
                .Where(ProfileDirectory.IsLocalDirectory)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (actualFolders.Count == 0)
            {
                string defaultPath = Path.Combine(_profilesRootDir, "Default");
                Directory.CreateDirectory(defaultPath);
                ProfileDirectory.GetRegisteredPath(_profilesRootDir, "Default");
                actualFolders.Add("Default");
                File.WriteAllText(Path.Combine(_profilesRootDir, "Default", "frames.json"), "[]");
                File.WriteAllText(Path.Combine(_profilesRootDir, "Default", "options.json"), "{}");
            }

            // 3. Parse JSON Profiles
            _profileCache.Clear();
            var jsonProfiles = config["Profiles"] as JArray ?? new JArray();
            var usedIds = new HashSet<int>();

            foreach (var token in jsonProfiles)
            {
                if (token is not JObject) { saveNeeded = true; continue; }
                string name = token["Name"]?.ToString();
                int id = token["Id"]?.Value<int>() ?? -1;

                if (!string.IsNullOrEmpty(name) && actualFolders.Contains(name))
                {
                    if (id < 0 || usedIds.Contains(id)) { id = -1; saveNeeded = true; }
                    if (id >= 0) usedIds.Add(id);
                    _profileCache.Add(new ProfileInfo { Name = name, Id = id });
                }
                else saveNeeded = true;
            }

            // 4. Add Missing Folders
            foreach (string folder in actualFolders)
            {
                if (!_profileCache.Any(p => p.Name.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                {
                    _profileCache.Add(new ProfileInfo { Name = folder, Id = -1 });
                    saveNeeded = true;
                }
            }

            // 5. Assign IDs
            int nextId = 0;
            foreach (var profile in _profileCache.Where(p => p.Id == -1).OrderBy(p => p.Name))
            {
                while (usedIds.Contains(nextId)) nextId++;
                profile.Id = nextId;
                usedIds.Add(nextId);
                saveNeeded = true;
            }

            // 6. Set Active Profile
            string active = config["ActiveProfile"]?.ToString();
            if (string.IsNullOrEmpty(active) || !actualFolders.Contains(active))
            {
                active = _profileCache.OrderBy(p => p.Id).FirstOrDefault()?.Name ?? "Default";
                saveNeeded = true;
            }
            _currentProfileName = active;

            if (config["AutomationRules"] != null)
            {
                try { _automationRules = config["AutomationRules"].ToObject<List<AutomationRule>>() ?? new List<AutomationRule>(); }
                catch { _automationRules = new List<AutomationRule>(); }
            }
            _manualBaseProfile = _currentProfileName;

            // --- NEW: Sanitize Display Option ---
            if (config["DisplayProfileNameOnSwitch"] == null)
            {
                DisplayProfileNameOnSwitch = false; // Default off
                saveNeeded = true;
            }
            else
            {
                DisplayProfileNameOnSwitch = config["DisplayProfileNameOnSwitch"].Value<bool>();
            }

            if (saveNeeded) SaveConfigInternal();
        }

        public static void SaveConfigInternal()
        {
            try { SaveConfigOrThrow(); }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to save ProfileOptions: {ex.Message}");
            }
        }

        private static void SaveConfigOrThrow()
        {
            var config = new JObject
            {
                ["ActiveProfile"] = _currentProfileName,
                ["LastModified"] = DateTime.Now.ToString("o"),
                ["DisplayProfileNameOnSwitch"] = DisplayProfileNameOnSwitch,
                ["Profiles"] = JArray.FromObject(_profileCache.OrderBy(profile => profile.Id))
            };

            if (_automationRules.Count > 0)
                config["AutomationRules"] = JArray.FromObject(_automationRules);

            AtomicFile.WriteUtf8(Path.Combine(_appBaseDir, PROFILE_CONFIG_FILE), config.ToString(Formatting.Indented));
        }

        // --- PUBLIC API ---

        public static List<ProfileInfo> GetProfiles()
        {
            return _profileCache.OrderBy(p => p.Id).ToList();
        }

        public static int GetProfileId(string name)
        {
            var p = _profileCache.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return p?.Id ?? -1;
        }

        public static bool CreateProfile(string profileName)
        {
            try
            {
                var invalid = Path.GetInvalidFileNameChars();
                if (profileName.Any(c => invalid.Contains(c))) return false;

                string newProfilePath = Path.Combine(_profilesRootDir, profileName);
                if (Directory.Exists(newProfilePath)) return false;

                Directory.CreateDirectory(newProfilePath);
                Directory.CreateDirectory(Path.Combine(newProfilePath, "Shortcuts"));

                File.WriteAllText(Path.Combine(newProfilePath, "frames.json"), "[]");
                File.WriteAllText(Path.Combine(newProfilePath, "options.json"), "{}");

                LoadAndSanitizeConfig();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Created new profile: {profileName}");
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error creating profile: {ex.Message}");
                return false;
            }
        }


        // --- PHASE 4: PROFILE OPERATIONS (Rename, Delete, Reorder) ---
        public static bool RenameProfile(string oldName, string newName)
        {
            try
            {
                if (string.Equals(oldName, _currentProfileName, StringComparison.OrdinalIgnoreCase)) return false;

                var invalid = Path.GetInvalidFileNameChars();
                if (newName.Any(c => invalid.Contains(c))) return false;

                string oldDir = Path.Combine(_profilesRootDir, oldName);
                string newDir = Path.Combine(_profilesRootDir, newName);

                if (!Directory.Exists(oldDir)) return false;
                if (Directory.Exists(newDir)) return false;

                Directory.Move(oldDir, newDir);

                var pInfo = _profileCache.FirstOrDefault(p => p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                if (pInfo != null) pInfo.Name = newName;

                // Update Rules
                bool rulesChanged = false;
                foreach (var rule in _automationRules)
                {
                    if (string.Equals(rule.TargetProfile, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        rule.TargetProfile = newName;
                        rulesChanged = true;
                    }
                }

                if (string.Equals(_manualBaseProfile, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _manualBaseProfile = newName;
                }

                SaveConfigInternal();
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Rename failed: {ex.Message}");
                return false;
            }
        }

        public static bool DeleteProfile(string profileName)
        {
            try
            {
                if (string.Equals(profileName, _currentProfileName, StringComparison.OrdinalIgnoreCase)) return false;
                if (_profileCache.Count <= 1) return false;

                string targetDir = Path.Combine(_profilesRootDir, profileName);
                if (!Directory.Exists(targetDir)) return false;

                Directory.Delete(targetDir, true);

                var profile = _profileCache.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                {
                    _profileCache.Remove(profile);
                    SaveConfigInternal();
                }
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Delete failed: {ex.Message}");
                return false;
            }
        }


        public static void SwapProfileIds(string nameA, string nameB)
        {
            var pA = _profileCache.FirstOrDefault(p => p.Name.Equals(nameA, StringComparison.OrdinalIgnoreCase));
            var pB = _profileCache.FirstOrDefault(p => p.Name.Equals(nameB, StringComparison.OrdinalIgnoreCase));

            if (pA != null && pB != null)
            {
                int temp = pA.Id;
                pA.Id = pB.Id;
                pB.Id = temp;
                SaveConfigInternal();
            }
        }

        public static void SwitchToProfile(string profileName) => TrySwitchToProfile(profileName, showLoadError: true);

        private static bool _isSwitchingProfile;

        internal static bool TrySwitchToProfile(string profileName, bool showLoadError)
        {
            if (!Application.Current.Dispatcher.CheckAccess())
                return Application.Current.Dispatcher.Invoke(() => TrySwitchToProfile(profileName, showLoadError));
            if (_isSwitchingProfile || Framemanager.IsReloadingFrames) return false;

            var profile = _profileCache.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (profile == null) return false;
            if (string.Equals(_currentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase)) return true;

            _isSwitchingProfile = true;
            try
            {
                string targetDir = ProfileDirectory.GetRegisteredPath(_profilesRootDir, profile.Name);
                var prepared = FrameDataManager.PrepareProfileFrames(targetDir);
                string previousName = _currentProfileName;
                string previousDirectory = Directory.GetCurrentDirectory();

                CloseProfileWindows();
                var previousFrames = FrameDataManager.CaptureLoadedFrames();
                AutoOrganizeManager.Stop();
                try
                {
                    if (!Framemanager.TryReloadFrames(prepared, showLoadError: false, activateProfile: () =>
                    {
                        Directory.SetCurrentDirectory(targetDir);
                        _currentProfileName = profile.Name;
                    }))
                        throw new IOException(Strings.Get("MsgProfileReloadFailed"));

                    SaveConfigOrThrow();
                }
                catch
                {
                    _currentProfileName = previousName;
                    Directory.SetCurrentDirectory(previousDirectory);
                    FrameDataManager.UsePreparedFrames(previousFrames);
                    Framemanager.TryReloadFrames(previousFrames, showLoadError: false);
                    throw;
                }
                finally
                {
                    AutoOrganizeManager.Initialize();
                }

                try
                {
                    TrayManager.Instance?.UpdateTrayIcon();
                    TrayManager.Instance?.UpdateProfilesMenu();
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Profile tray update failed: {ex.Message}");
                }

                try
                {
                    string hookScript = Path.Combine(targetDir, "on_enter.bat");
                    if (File.Exists(hookScript) && (File.GetAttributes(hookScript) & FileAttributes.ReparsePoint) == 0)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = hookScript,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Profile hook failed: {ex.Message}");
                }

                if (DisplayProfileNameOnSwitch) ShowProfileSwitchOSD(profile.Name);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Could not switch to profile {profileName}: {ex.Message}");
                if (showLoadError)
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgFrameFileLoadFailed", ex.Message), Strings.DlgError);
                return false;
            }
            finally
            {
                _isSwitchingProfile = false;
            }
        }

        private static void CloseProfileWindows()
        {
            foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (window.GetType().Name is "TextFormatFormManager" or "SearchFormManager" or "IconPickerDialog"
                    or "EditShortcutWindow" or "CustomizeFrameFormManager")
                    window.Close();
            }
        }

        private static void ShowProfileSwitchOSD(string profileName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    Window osd = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        Topmost = true,
                        ShowInTaskbar = false,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Focusable = false,
                        IsHitTestVisible = false
                    };

                    // --- UNIFIED UI REPLACEMENT ---
                    // Replaced manual Border/TextBlock with the Centralized Factory
                    osd.Content = MessageBoxesManager.CreateUnifiedMessage($"Profile: {profileName}");
                    // -----------------------------

                    osd.Loaded += (s, e) =>
                    {
                        double screenWidth = SystemParameters.PrimaryScreenWidth;
                        double screenHeight = SystemParameters.PrimaryScreenHeight;
                        double windowWidth = osd.ActualWidth;
                        double windowHeight = osd.ActualHeight;

                        osd.Left = (screenWidth - windowWidth) / 2;
                        // Move up by 150px from center (Preserved your logic)
                        osd.Top = ((screenHeight - windowHeight) / 2) - 150;
                    };

                    osd.Show();

                    DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        osd.Close();
                    };
                    timer.Start();
                }
                catch { }
            });
        }
        //private static void ShowProfileSwitchOSD(string profileName)
        //{
        //    Application.Current.Dispatcher.Invoke(() =>
        //    {
        //        try
        //        {
        //            Window osd = new Window
        //            {
        //                WindowStyle = WindowStyle.None,
        //                AllowsTransparency = true,
        //                Background = Brushes.Transparent,
        //                Topmost = true,
        //                ShowInTaskbar = false,
        //                SizeToContent = SizeToContent.WidthAndHeight,
        //                // CHANGED: Use Manual location to offset slightly up
        //                WindowStartupLocation = WindowStartupLocation.Manual,
        //                Focusable = false,
        //                IsHitTestVisible = false
        //            };

        //            Border border = new Border
        //            {
        //                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
        //                CornerRadius = new CornerRadius(10),
        //                Padding = new Thickness(40, 20, 40, 20),
        //                Margin = new Thickness(20),
        //                Effect = new DropShadowEffect { BlurRadius = 15, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.5 }
        //            };

        //            TextBlock text = new TextBlock
        //            {
        //                Text = $"Profile: {profileName}",
        //                Foreground = Brushes.White,
        //                FontSize = 24,
        //                FontFamily = new FontFamily("Segoe UI"),
        //                FontWeight = FontWeights.Bold,
        //                TextAlignment = TextAlignment.Center
        //            };

        //            border.Child = text;
        //            osd.Content = border;

        //            // Calculate Position: Center Horizontally, but Higher Vertically
        //            osd.Loaded += (s, e) =>
        //            {
        //                double screenWidth = SystemParameters.PrimaryScreenWidth;
        //                double screenHeight = SystemParameters.PrimaryScreenHeight;
        //                double windowWidth = osd.ActualWidth;
        //                double windowHeight = osd.ActualHeight;

        //                osd.Left = (screenWidth - windowWidth) / 2;
        //                // Move up by 150px from center
        //                osd.Top = ((screenHeight - windowHeight) / 2) - 150;
        //            };

        //            osd.Show();

        //            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        //            timer.Tick += (s, e) =>
        //            {
        //                timer.Stop();
        //                osd.Close();
        //            };
        //            timer.Start();
        //        }
        //        catch { }
        //    });
        //}

        public static void SwitchToNextProfile()
        {
            NavigateProfile(1);
        }

        public static void SwitchToPreviousProfile()
        {
            NavigateProfile(-1);
        }

        public static void SwitchToProfileById(int id)
        {
            var target = _profileCache.FirstOrDefault(p => p.Id == id);
            if (target != null)
            {
                SwitchToProfile(target.Name);
            }
            else
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Hotkey ignored: No profile with ID {id}");
            }
        }

        private static void NavigateProfile(int direction)
        {
            var sorted = _profileCache.OrderBy(p => p.Id).ToList();
            if (sorted.Count <= 1) return;

            int currentIndex = sorted.FindIndex(p => p.Name.Equals(_currentProfileName, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = currentIndex + direction;

            if (newIndex >= sorted.Count) newIndex = 0;
            if (newIndex < 0) newIndex = sorted.Count - 1;

            SwitchToProfile(sorted[newIndex].Name);
        }

        private static void MigrateLegacyData()
        {
            try
            {
                string defaultProfileDir = Path.Combine(_profilesRootDir, "Default");
                Directory.CreateDirectory(defaultProfileDir);

                foreach (string file in FILES_TO_MOVE)
                {
                    string source = Path.Combine(_appBaseDir, file);
                    string dest = Path.Combine(defaultProfileDir, file);
                    if (File.Exists(source)) File.Move(source, dest);
                }

                foreach (string folder in FOLDERS_TO_MOVE)
                {
                    string source = Path.Combine(_appBaseDir, folder);
                    string dest = Path.Combine(defaultProfileDir, folder);
                    if (Directory.Exists(source)) Directory.Move(source, dest);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Migration failed: {ex.Message}. Manual intervention required.");
            }
        }

        public static string GetProfileFilePath(string filename)
        {
            return Path.Combine(CurrentProfileDir, filename);
        }

        public static string GetMasterOptionsJson()
        {
            string masterPath = Path.Combine(_appBaseDir, MASTER_OPTIONS_FILE);
            if (File.Exists(masterPath))
            {
                try { return File.ReadAllText(masterPath); } catch { }
            }
            return null;
        }
    }


    public class AutomationRule
    {
        public string ProcessName { get; set; }
        public string TargetProfile { get; set; }
        public int DelaySeconds { get; set; } = 0;
        public bool IsPersisted { get; set; } = false;
    }
}
