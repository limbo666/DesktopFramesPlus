using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Desktop_Frames.Localization;
using Microsoft.VisualBasic.FileIO; // Required for sending to Recycle Bin

namespace Desktop_Frames
{
    public enum RuleConflictAction { Rename, Overwrite, Skip }

    public class OrganizeRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Extensions { get; set; } = "";
        public string NameContains { get; set; } = "";
        public string TargetFolderPath { get; set; }
        public RuleConflictAction ConflictAction { get; set; } = RuleConflictAction.Rename;
        public bool AutoCreateFrame { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 0;
        public DateTime? LastRun { get; set; } // NEW: Tracks last execution

        internal OrganizeRule Copy() => (OrganizeRule)MemberwiseClone();
    }

    public static class AutoOrganizeManager
    {
        private static readonly object MoveLock = new();
        private static FileSystemWatcher? _watcher;
        private static int _generation;
        private static string? _rulesFilePath;
        private static bool _canSaveRules;
        public static List<OrganizeRule> Rules { get; private set; } = new();
        private static readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        public static void Initialize()
        {
            LoadRules();
            if (SettingsManager.EnableAutoOrganize) Start();
        }

        public static void LoadRules()
        {
            lock (MoveLock)
            {
                ++_generation;
                string path = ProfileManager.GetProfileFilePath("auto_organize.json");
                _rulesFilePath = path;
                _canSaveRules = false;
                Rules = new List<OrganizeRule>();
                try
                {
                    string json = File.ReadAllText(path);
                    Rules = JsonConvert.DeserializeObject<List<OrganizeRule>>(json)
                        ?? throw new InvalidDataException("Expected a list of Smart Desktop rules.");
                    _canSaveRules = true;
                }
                catch (FileNotFoundException) { _canSaveRules = true; }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error loading organize rules: {ex.Message}");
                }
            }
        }

        internal static void SaveRules(IEnumerable<OrganizeRule> rules, string profileDirectory)
        {
            lock (MoveLock)
            {
                if (!string.Equals(_rulesFilePath, Path.Combine(profileDirectory, "auto_organize.json"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Strings.Get("MsgAutoOrgProfileChanged"));

                var editedRules = rules.Select(rule => rule.Copy()).ToList();
                WriteRules(editedRules);
                ++_generation;
                Rules = editedRules;
            }
        }

        private static void WriteRules(List<OrganizeRule> rules)
        {
            if (!_canSaveRules || _rulesFilePath == null ||
                !string.Equals(_rulesFilePath, ProfileManager.GetProfileFilePath("auto_organize.json"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Strings.Get("MsgAutoOrgRulesUnavailable"));

            AtomicFile.WriteUtf8(_rulesFilePath, JsonConvert.SerializeObject(rules, Formatting.Indented));
        }

        public static void Start()
        {
            if (_watcher != null) return;

            try
            {
                _watcher = new FileSystemWatcher(_desktopPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                // --- THE BROWSER SHIELD ---
                // Delay exactly 3 seconds before processing. This prevents browsers from panicking 
                // and deleting the file when we move it before they finish writing the Mark of the Web!
                _watcher.Created += QueueFile;
                _watcher.Renamed += QueueFile;
                _watcher.EnableRaisingEvents = true;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Organize engine started.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start Auto-Organize: {ex.Message}");
            }
        }

        private static void QueueFile(object sender, FileSystemEventArgs e)
        {
            int generation = Volatile.Read(ref _generation);
            if (!ReferenceEquals(sender, _watcher)) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                await ProcessFileAsync(e.FullPath, generation);
            });
        }

        public static void Stop()
        {
            lock (MoveLock)
            {
                ++_generation;
                var watcher = _watcher;
                _watcher = null;
                watcher?.Dispose();
            }
        }

        public static void ProcessDesktopNow(bool silent = false)
        {
            if (Rules.Count == 0) return;
            int generation = Volatile.Read(ref _generation);

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var file in AutoOrganizePlanner.GetDesktopFiles(_desktopPath))
                    {
                        if (generation != Volatile.Read(ref _generation)) return;
                        await ProcessFileAsync(file, generation);
                    }

                    if (!silent)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (generation == Volatile.Read(ref _generation))
                                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgDesktopOrganized, Strings.DlgSuccess);
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Auto-Organize scan failed: {ex.Message}");
                }
            });
        }

        internal static Task<IReadOnlyList<OrganizePlan>> PreviewDesktopAsync(IEnumerable<OrganizeRule> rules, CancellationToken cancellationToken)
        {
            var ruleSnapshot = rules.Where(rule => rule != null).Select(rule => rule.Copy()).ToArray();
            return Task.Run(() => AutoOrganizePlanner.Preview(_desktopPath, ruleSnapshot, cancellationToken), cancellationToken);
        }

        private static async Task ProcessFileAsync(string filePath, int generation)
        {
            try
            {
                OrganizeRule? rule;
                lock (MoveLock)
                {
                    if (!CanProcess(generation) || !File.Exists(filePath)) return;
                    rule = AutoOrganizePlanner.FindRule(filePath, AutoOrganizePlanner.GetActiveRules(Rules));
                }

                if (rule == null || !await WaitForFileUnlockAsync(filePath, 60000, generation)) return;

                OrganizePlan plan;
                lock (MoveLock)
                {
                    if (!CanProcess(generation) || !File.Exists(filePath)) return;

                    plan = AutoOrganizePlanner.PlanMove(filePath, rule);
                    if (!plan.WillMove || plan.DestinationPath == null) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(plan.DestinationPath)!);

                    if (plan.Action == OrganizeAction.Overwrite)
                        MoveReplacingFile(filePath, plan.DestinationPath);
                    else
                        File.Move(filePath, plan.DestinationPath);

                    rule.LastRun = DateTime.Now;
                    try { WriteRules(Rules); }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Could not save rule execution time: {ex.Message}");
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Auto-Organize moved {filePath} to {plan.DestinationPath}");
                if (SettingsManager.EnableAutoOrganizeNotifications || rule.AutoCreateFrame)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (generation != Volatile.Read(ref _generation)) return;
                        string targetFolder = Path.GetDirectoryName(plan.DestinationPath)!;
                        if (SettingsManager.EnableAutoOrganizeNotifications)
                            SmartToast.Show($"Rule: {rule.Name} Executed", $"Moved '{Path.GetFileName(filePath)}' to {new DirectoryInfo(targetFolder).Name}");
                        if (rule.AutoCreateFrame) CheckAndCreatePortalFrame(targetFolder);
                    });
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Auto-Organize failed to move {filePath}: {ex.Message}");
            }
        }

        private static bool CanProcess(int generation) =>
            generation == _generation && SettingsManager.EnableAutoOrganize && _canSaveRules &&
            string.Equals(_rulesFilePath, ProfileManager.GetProfileFilePath("auto_organize.json"), StringComparison.OrdinalIgnoreCase);

        private static void MoveReplacingFile(string source, string destination)
        {
            string backupDirectory = Path.Combine(Path.GetDirectoryName(destination)!, $".desktopframes-{Guid.NewGuid():N}");
            string backup = Path.Combine(backupDirectory, Path.GetFileName(destination));
            Directory.CreateDirectory(backupDirectory);
            try
            {
                File.Move(destination, backup);
                try
                {
                    File.Move(source, destination);
                }
                catch (Exception moveError)
                {
                    try { File.Move(backup, destination); }
                    catch (Exception rollbackError)
                    {
                        throw new IOException($"Could not restore the previous file. It is saved at {backup}.",
                            new AggregateException(moveError, rollbackError));
                    }
                    throw;
                }

                try { FileSystem.DeleteFile(backup, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Previous file was kept at {backup}: {ex.Message}");
                }
            }
            finally
            {
                try { Directory.Delete(backupDirectory); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static void CheckAndCreatePortalFrame(string targetFolder)
        {
            // Prevent duplicate frames: Check if a portal frame already points to this folder
            var existingFrames = Framemanager.GetFrameData();
            foreach (var f in existingFrames)
            {
                try
                {
                    string fType = null, fPath = null;
                    if (f is Newtonsoft.Json.Linq.JObject jObj)
                    {
                        fType = jObj["ItemsType"]?.ToString();
                        fPath = jObj["Path"]?.ToString(); // FIX: Changed from PortalFolderPath to Path
                    }
                    else
                    {
                        fType = f.GetType().GetProperty("ItemsType")?.GetValue(f)?.ToString() ?? f.ItemsType?.ToString();
                        fPath = f.GetType().GetProperty("Path")?.GetValue(f)?.ToString() ?? f.Path?.ToString(); // FIX
                    }

                    if (fType == "Portal" && string.Equals(fPath, targetFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // Frame already exists!
                    }
                }
                catch { }
            }

            // Command Framemanager to generate a new Portal Frame using the exact required schema
            dynamic newFrame = new Newtonsoft.Json.Linq.JObject();
            newFrame.Id = Guid.NewGuid().ToString();
            newFrame.Title = new DirectoryInfo(targetFolder).Name; // FIX: Changed from Name to Title
            newFrame.X = 100.0; // FIX: Changed from Left to X
            newFrame.Y = 100.0; // FIX: Changed from Top to Y
            newFrame.Width = 350.0;
            newFrame.Height = 250.0;
            newFrame.ItemsType = "Portal";
            newFrame.Path = targetFolder; // FIX: Changed from PortalFolderPath to Path
            newFrame.Items = ""; // Portal expects empty string for Items

            // FIX: Initialize ALL frame properties with defaults to match JSON structure exactly
            newFrame.IsLocked = "false";
            newFrame.IsHidden = "false";
            newFrame.CustomColor = null;
            newFrame.CustomLaunchEffect = null;
            newFrame.IsRolled = "false";
            newFrame.UnrolledHeight = 250.0;
            newFrame.TextColor = null;
            newFrame.BoldTitleText = "false";
            newFrame.TitleTextColor = null;
            newFrame.DisableTextShadow = "false";
            newFrame.IconSize = "Medium";
            newFrame.GrayscaleIcons = "false";
            newFrame.IconSpacing = 5;
            newFrame.TitleTextSize = "Medium";
            newFrame.FrameBorderColor = null;
            newFrame.FrameBorderThickness = 2;

            // TABS FEATURE requirements
            newFrame.TabsEnabled = "false";
            newFrame.CurrentTab = 0;
            newFrame.Tabs = new Newtonsoft.Json.Linq.JArray();

            // 1. Add to the central data manager
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                FrameDataManager.FrameData.Add(newFrame);

                // 2. Pass a TargetChecker instance to the creation engine
                Framemanager.CreateFrame(newFrame, new TargetChecker(1000));

                // 3. Save using the correct data manager method
                FrameDataManager.SaveFrameData();
            });

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Auto-Organize spawned a new Portal Frame for {targetFolder}");
        }

        private static async Task<bool> WaitForFileUnlockAsync(string filePath, int timeoutMs, int generation)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && generation == Volatile.Read(ref _generation))
            {
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        return true; // Lock acquired successfully, file is ready
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(500); // Wait half a second and try again
                }
            }
            return false; // Timed out
        }


        public static void Pause()
        {
            lock (MoveLock)
            {
                ++_generation;
                if (_watcher != null) _watcher.EnableRaisingEvents = false;
            }
        }

        public static void Resume()
        {
            if (!SettingsManager.EnableAutoOrganize) return;

            if (_watcher == null)
            {
                Start();
            }
            else
            {
                _watcher.EnableRaisingEvents = true;
            }

            // Silently sweep the desktop to catch any files that landed while we were paused!
            ProcessDesktopNow(true);
        }
    }
}