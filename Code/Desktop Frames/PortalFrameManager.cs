using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IWshRuntimeLibrary;
using Newtonsoft.Json.Linq;
using Microsoft.VisualBasic;

namespace Desktop_Frames
{
    public class PortalFramemanager
    {
        // New field for the active filter
        private string _currentFilter = null;
        private int _sortMode = 0; // 0=Name, 1=Date Modified, 2=Type, 3=Size


        private readonly dynamic _frame;
        private readonly WrapPanel _wpcont;
        private readonly FileSystemWatcher _watcher;
        private string _targetFolderPath;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _debounceTimer;
        private int _navigationGeneration = 0; // Tracks active navigation to prevent thread collisions


        // --- FILTERING ENGINE START ---

        /// <summary>
        /// Updates the current filter and refreshes the visibility of all items.
        /// Publicly called by Framemanager when the user types in the filter bar.
        /// </summary>
        public void ApplyFilter(string filterText)
        {
            _currentFilter = filterText;
            _dispatcher.Invoke(() =>
            {
                foreach (StackPanel sp in _wpcont.Children.OfType<StackPanel>())
                {
                    if (sp.Tag != null)
                    {
                        // Safely retrieve path from anonymous type or object
                        string path = sp.Tag.GetType().GetProperty("FilePath")?.GetValue(sp.Tag)?.ToString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            sp.Visibility = ShouldShowItem(path) ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
            });
        }



        /// <summary>
        /// Determines if a file should be visible based on the current filter.
        /// Supports "Smart Match" if NoWildcardsOnPortalFilter is enabled.
        /// </summary>
        private bool ShouldShowItem(string filePath)
        {
            if (string.IsNullOrWhiteSpace(_currentFilter)) return true;

            string fileName = System.IO.Path.GetFileName(filePath);
            var terms = _currentFilter.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(t => t.Trim())
                                      .ToList();

            bool hasIncludeRules = terms.Any(t => !t.StartsWith(">"));
            bool matchesInclude = !hasIncludeRules;
            bool matchesExclude = false;

            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;

                string pattern = term;
                bool isExclude = false;

                // 1. Identify Exclusion
                if (pattern.StartsWith(">"))
                {
                    isExclude = true;
                    pattern = pattern.Substring(1); // Remove '>' prefix
                }

                // 2. Apply Smart Wildcards (Hidden Option)
                // Logic: If user wants "No Wildcards", we treat text as "Contains".
                // We only auto-wrap if the user hasn't typed wildcards themselves.
                if (SettingsManager.NoWildcardsOnPortalFilter)
                {
                    if (!pattern.Contains("*") && !pattern.Contains("?"))
                    {
                        pattern = "*" + pattern + "*";
                    }
                }

                // 3. Match
                if (isExclude)
                {
                    if (IsMatch(fileName, pattern))
                    {
                        matchesExclude = true;
                        break; // Hard fail
                    }
                }
                else
                {
                    if (IsMatch(fileName, pattern))
                    {
                        matchesInclude = true;
                    }
                }
            }

            return !matchesExclude && matchesInclude;
        }




        /// <summary>
        /// Simple glob matching (* and ?)
        /// </summary>
        private bool IsMatch(string text, string pattern)
        {
            // Use VB's Like operator or simple Regex. 
            // For a dependency-free C# solution, we convert glob to regex.
            try
            {
                string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                                      .Replace(@"\*", ".*")
                                      .Replace(@"\?", ".") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }
        // --- FILTERING ENGINE END ---


        // --- SORTING ENGINE START ---
        public string CycleSortMode()
        {
            _sortMode++;
            if (_sortMode > 3) _sortMode = 0;

            // Save state using the existing updater
            Framemanager.UpdateFrameProperty(_frame, "SortMode", _sortMode.ToString(), "Updated portal sort mode");

            string[] modeNames = { "Name", "Date Modified", "Type", "Size" };
            string activeMode = modeNames[_sortMode];

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Portal frame sorted by: {activeMode}");

            SortContents();

            return activeMode;
        }

        private void SortContents()
        {
            _dispatcher.Invoke(() =>
            {
                var children = _wpcont.Children.OfType<StackPanel>().ToList();
                if (children.Count == 0) return;

                _wpcont.Children.Clear();

                string GetPath(StackPanel sp)
                {
                    dynamic tag = sp.Tag;
                    return tag?.GetType().GetProperty("FilePath")?.GetValue(tag)?.ToString() ?? "";
                }

                bool IsFolder(StackPanel sp)
                {
                    dynamic tag = sp.Tag;
                    return tag != null && tag.GetType().GetProperty("IsFolder")?.GetValue(tag) as bool? == true;
                }

                IEnumerable<StackPanel> sorted;

                switch (_sortMode)
                {
                    case 1: // Date Modified (Newest first)
                        sorted = children.OrderByDescending(IsFolder)
                                         .ThenByDescending(sp => { try { return System.IO.File.GetLastWriteTime(GetPath(sp)); } catch { return DateTime.MinValue; } });
                        break;
                    case 2: // Type (A-Z)
                        sorted = children.OrderByDescending(IsFolder)
                                         .ThenBy(sp => System.IO.Path.GetExtension(GetPath(sp))?.ToLower() ?? "");
                        break;
                    case 3: // Size (Largest first)
                        sorted = children.OrderByDescending(IsFolder)
                                         .ThenByDescending(sp => { try { return IsFolder(sp) ? 0 : new System.IO.FileInfo(GetPath(sp)).Length; } catch { return 0; } });
                        break;
                    default: // 0 = Name (A-Z)
                        sorted = children.OrderByDescending(IsFolder)
                                         .ThenBy(sp => System.IO.Path.GetFileName(GetPath(sp))?.ToLower() ?? "");
                        break;
                }

                foreach (var sp in sorted)
                {
                    _wpcont.Children.Add(sp);
                }
            });
        }
        // --- SORTING ENGINE END ---


        public PortalFramemanager(dynamic frame, WrapPanel wpcont)
        {
            _frame = frame;
            _wpcont = wpcont;
            _dispatcher = _wpcont.Dispatcher;

            // Initialize debounce timer with longer interval for Excel temp files
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Increased for better stability
            };
            _debounceTimer.Tick += ProcessPendingEvents;

            // Extract folder path
            IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ? dict : ((JObject)frame).ToObject<IDictionary<string, object>>();
            _targetFolderPath = frameDict.ContainsKey("Path") ? frameDict["Path"]?.ToString() : null;

            // FIX: Load saved filter immediately on startup
            if (frameDict.ContainsKey("FilterString"))
            {
                _currentFilter = frameDict["FilterString"]?.ToString();
            }

            // NEW: Load saved sort mode
            if (frameDict.ContainsKey("SortMode"))
            {
                _sortMode = Convert.ToInt32(frameDict["SortMode"]?.ToString() ?? "0");
            }

            if (string.IsNullOrEmpty(_targetFolderPath))
            {
                throw new Exception("No folder path defined for Portal Frame. Please recreate the frame.");
            }

            if (!Directory.Exists(_targetFolderPath))
            {
                throw new Exception($"The folder '{_targetFolderPath}' does not exist. Please update the Portal Frame settings.");
            }

            _watcher = new FileSystemWatcher(_targetFolderPath)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
                InternalBufferSize = 65536 // --- BUG FIX: Maximize buffer to survive massive I/O operations ---
            };

            // --- BUG FIX: Simplified "State Reconciler" Trigger ---
            // The watcher is now just a "ping" to tell us something changed. 
            // We listen to the Error event to specifically catch buffer overflows!
            _watcher.Created += (s, e) => TriggerSync();
            _watcher.Deleted += (s, e) => TriggerSync();
            _watcher.Renamed += (s, e) => TriggerSync();
            _watcher.Error += (s, e) => TriggerSync();

            InitializeFrameContents();
            //  // --- TEST CODE START ---
            //  // Hardcode a filter to prove the engine works.
            //   // This simulates a user typing "*.txt" into the filter bar.
            //   ApplyFilter("*.txt");
            //  // --- TEST CODE END ---
        }

        private void TriggerSync(bool immediate = false)
        {
            _dispatcher.InvokeAsync(() =>
            {
                _debounceTimer.Stop();
                if (immediate)
                {
                    _ = RunReconcilerAsync();
                }
                else
                {
                    _debounceTimer.Start();
                }
            });
        }

        private void ProcessPendingEvents(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _ = RunReconcilerAsync();
        }

        private async System.Threading.Tasks.Task RunReconcilerAsync()
        {
            int myGeneration = ++_navigationGeneration;
            string targetPath = _targetFolderPath;

            try
            {
                if (!Directory.Exists(targetPath)) return;

                // 1. Read Disk & UI State (Background Thread)
                var diff = await System.Threading.Tasks.Task.Run(() =>
                {
                    // --- PERFORMANCE FIX: Use EnumerateFileSystemInfos to avoid N+1 disk I/O calls ---
                    // This fetches attributes instantly during the directory scan instead of hitting the disk for every single file.
                    var currentDiskFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var dirInfo = new DirectoryInfo(targetPath);
                        foreach (var fsi in dirInfo.EnumerateFileSystemInfos())
                        {
                            if (CoreUtilities.IsTemporaryFile(fsi.FullName)) continue;

                            // Attributes are pre-cached in 'fsi', ZERO extra disk I/O required!
                            if ((fsi.Attributes & FileAttributes.Hidden) == 0 &&
                                (fsi.Attributes & FileAttributes.System) == 0)
                            {
                                currentDiskFiles.Add(fsi.FullName);
                            }
                        }
                    }
                    catch { } // Handle access denied gracefully

                    List<string> currentUIFiles = new List<string>();
                    _dispatcher.Invoke(() =>
                    {
                        currentUIFiles = _wpcont.Children.OfType<StackPanel>()
                            .Select(sp => sp.Tag?.GetType().GetProperty("FilePath")?.GetValue(sp.Tag)?.ToString())
                            .Where(p => p != null).ToList();
                    });

                    var uiSet = currentUIFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    return new
                    {
                        ToRemove = currentUIFiles.Where(p => !currentDiskFiles.Contains(p)).ToList(),
                        ToAdd = currentDiskFiles.Where(p => !uiSet.Contains(p)).ToList()
                    };
                });

                // Abort if the user navigated away while we were scanning!
                if (myGeneration != _navigationGeneration) return;

                // 2. Remove old icons instantly
                if (diff.ToRemove.Count > 0)
                {
                    _dispatcher.Invoke(() =>
                    {
                        foreach (var path in diff.ToRemove) RemoveIcon(path);
                    });
                }

                if (myGeneration != _navigationGeneration) return;

                // 3. Add new icons (SMOOTH CHUNKING)
                // Instead of locking the UI thread to load 100 icons at once, we yield to the Background priority.
                // This keeps the app responsive during massive folder loads and avoids freezing.
                if (diff.ToAdd.Count > 0)
                {
                    foreach (var path in diff.ToAdd)
                    {
                        // Stop immediately if user navigated to another folder
                        if (myGeneration != _navigationGeneration) break;

                        await _dispatcher.InvokeAsync(() =>
                        {
                            AddIcon(path);
                        }, DispatcherPriority.Background);
                    }

                    if (myGeneration == _navigationGeneration)
                    {
                        _dispatcher.Invoke(() => SortContents());
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Portal Sync Error: {ex.Message}");
            }
        }

        private void AddIcon(string path)
        {



            // Enhanced filter during add to prevent duplicates
            FileAttributes attributes;
            bool isFolder = false;

            try
            {
                // --- PERFORMANCE FIX: 1 Disk Read instead of 4 ---
                // We grab attributes once. This immediately tells us if it exists, is a folder, and if it's hidden/system.
                attributes = System.IO.File.GetAttributes(path);
                isFolder = (attributes & FileAttributes.Directory) == FileAttributes.Directory;

                if (CoreUtilities.IsTemporaryFile(path)) return;
                if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden) return;
                if ((attributes & FileAttributes.System) == FileAttributes.System) return;

                // Check if icon already exists in UI (Safety Check)
                var existingPanel = _wpcont.Children.OfType<StackPanel>()
                    .FirstOrDefault(sp => sp.Tag != null &&
                                    sp.Tag.GetType().GetProperty("FilePath")?.GetValue(sp.Tag)?.ToString() == path);

                if (existingPanel != null) return;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Inaccessible or missing item {path}: {ex.Message}");
                return;
            }

            dynamic icon = new System.Dynamic.ExpandoObject();
            IDictionary<string, object> iconDict = icon;
            iconDict["Filename"] = path;
            iconDict["IsFolder"] = isFolder;





            // --- RESTORED: Network Path Detection ---
            iconDict["IsNetwork"] = Framemanager.IsNetworkPath(path);


            string displayName;

            try
            {
                // FIX: Handle Extensions based on Global Setting
                if (SettingsManager.ShowPortalExtensions && !isFolder)
                {
                    // Force display name WITH extension
                    displayName = Path.GetFileName(path);
                }
                else
                {
                    if (isFolder)
                    {
                        // Folders → keep full name even if they contain dots
                        displayName = Path.GetFileName(path);
                    }
                    else
                    {
                        // Files → strip extension (default behavior)
                        displayName = Path.GetFileNameWithoutExtension(path);
                    }
                }
            }
            catch
            {
                // Fallback: act like it's a file without extension
                displayName = Path.GetFileNameWithoutExtension(path);
            }

            iconDict["DisplayName"] = displayName;

            // --- FIX: ONE CALL ONLY ---
            // We use the new signature that passes '_frame' context.
            // This applies the custom settings (Size, Color, etc.) immediately.
            Framemanager.AddIcon(icon, _wpcont, _frame);

            // Now we grab the StackPanel that was just added to attach logic
            StackPanel sp = _wpcont.Children[_wpcont.Children.Count - 1] as StackPanel;
            if (sp != null)
            {
                // FIX: Apply filter immediately upon creation
                sp.Visibility = ShouldShowItem(path) ? Visibility.Visible : Visibility.Collapsed;

                Framemanager.ClickEventAdder(sp, path, Directory.Exists(path));

          
                // Create and attach context menu
                ContextMenu contextMenu = new ContextMenu();

                // 1. Copy Item (File Object)
                MenuItem copyFileItem = new MenuItem { Header = "Copy Item" };
                copyFileItem.Click += (s, e) =>
                {
                    try
                    {
                        // Add file to clipboard as a FileDropList (Standard Windows Copy)
                        var paths = new System.Collections.Specialized.StringCollection();
                        paths.Add(path);
                        Clipboard.SetFileDropList(paths);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Copied item to clipboard: {path}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error copying item: {ex.Message}");
                    }
                };
                contextMenu.Items.Add(copyFileItem);

                // 2. Cut Item (File Object with Move Effect)
                MenuItem cutFileItem = new MenuItem { Header = "Cut Item" };
                cutFileItem.Click += (s, e) =>
                {
                    try
                    {
                        var paths = new System.Collections.Specialized.StringCollection();
                        paths.Add(path);

                        // Create a DataObject to hold both the file list and the "Move" flag
                        DataObject data = new DataObject();
                        data.SetFileDropList(paths);

                        // Set "Preferred DropEffect" to Move (Byte value 2)
                        // This tells Windows Explorer to perform a MOVe operation on Paste
                        byte[] moveEffect = new byte[] { 2, 0, 0, 0 };
                        System.IO.MemoryStream stream = new System.IO.MemoryStream(moveEffect);
                        data.SetData("Preferred DropEffect", stream);

                        Clipboard.SetDataObject(data, true);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Cut item to clipboard: {path}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error cutting item: {ex.Message}");
                    }
                };
                contextMenu.Items.Add(cutFileItem);

                // 3. Rename item (Existing)
                MenuItem renameItem = new MenuItem { Header = "Rename item" };
                renameItem.Click += (s, e) => RenameItem(path, sp);
                contextMenu.Items.Add(renameItem);

                // 4. Delete item (Existing)
                MenuItem deleteItem = new MenuItem { Header = "Delete item" };
                deleteItem.Click += (s, e) => DeleteItem(path, sp);
                contextMenu.Items.Add(deleteItem);

                // 5. Separator
                contextMenu.Items.Add(new Separator());

                // 6. Copy path (Existing - Moved to bottom)
                MenuItem copyPathItem = new MenuItem { Header = "Copy path" };
                copyPathItem.Click += (s, e) => CopyPathOrTarget(path);
                contextMenu.Items.Add(copyPathItem);

                sp.ContextMenu = contextMenu;



            }
        }

        private void RenameItem(string currentPath, StackPanel sp)
        {
            try
            {
                string currentName = Path.GetFileNameWithoutExtension(currentPath);
                string extension = Path.GetExtension(currentPath);

                // Simple input dialog (you can replace with a proper dialog if you have one)
                string newName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter new name:",
                    "Rename Item",
                    currentName);

                if (string.IsNullOrEmpty(newName) || newName == currentName)
                    return;

                string newPath = Path.Combine(Path.GetDirectoryName(currentPath), newName + extension);

                // Check if target name already exists
                if (System.IO.File.Exists(newPath) || Directory.Exists(newPath))
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("A file or folder with that name already exists.", "Rename Error");
                    return;
                }

                // Perform the rename
                if (Directory.Exists(currentPath))
                {
                    Directory.Move(currentPath, newPath);
                }
                else if (System.IO.File.Exists(currentPath))
                {
                    System.IO.File.Move(currentPath, newPath);
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Renamed {currentPath} to {newPath}");

                // The FileSystemWatcher will automatically handle UI updates
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to rename {currentPath}: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Failed to rename item: {ex.Message}", "Rename Error");
            }
        }

        private void InitializeFrameContents()
        {
            _dispatcher.Invoke(() => _wpcont.Children.Clear());

            // --- NAVIGATION LAG FIX ---
            // Pass 'true' to completely bypass the FileWatcher's 500ms debounce timer.
            // This guarantees the folder begins loading instantly upon click.
            TriggerSync(immediate: true);

            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Requested immediate async initialization for {_targetFolderPath}");
        }

        private void CopyPathOrTarget(string path)
        {
            try
            {
                string pathToCopy;
                if (Path.GetExtension(path).ToLower() == ".lnk")
                {
                    // If it's a shortcut, get the target path
                    WshShell shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(path);
                    pathToCopy = shortcut.TargetPath;
                }
                else
                {
                    // Otherwise, copy the folder path (portal frame path)
                    pathToCopy = Path.GetDirectoryName(path); // Gets the parent directory
                }

                // Copy to clipboard
                Clipboard.SetText(pathToCopy);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Copied path to clipboard: {pathToCopy}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Failed to copy path for {path}: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Unable to copy path.", "Error");
            }
        }

        private void DeleteItem(string path, StackPanel sp)
        {
            bool UseRecycleBin = SettingsManager.UseRecycleBin;
            if (UseRecycleBin == true)
            {
                try
                {
                    // First, check if the item exists
                    if (!Directory.Exists(path) && !System.IO.File.Exists(path))
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Item not found for deletion: {path}");
                        return;
                    }

                    // Use SHFileOperation to move to recycle bin
                    SHFILEOPSTRUCT shf = new SHFILEOPSTRUCT();
                    shf.wFunc = FO_DELETE;
                    shf.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION;
                    shf.pFrom = path + '\0' + '\0'; // Double null-terminated string

                    int result = SHFileOperation(ref shf);

                    if (result != 0)
                    {
                        throw new Exception($"Failed to move to recycle bin (error code: {result})");
                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Moved to recycle bin: {path}");

                    // Remove the icon from the UI
                    _wpcont.Children.Remove(sp);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, ($"Removed icon for {path} from UI"));
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to move item {path} to recycle bin: {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Unable to move item to recycle bin.", "Error");
                }
            }
            else
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // Delete folder
                        Directory.Delete(path, true); // true for recursive deletion
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Deleted folder: {path}");
                    }
                    else if (System.IO.File.Exists(path))
                    {
                        // Delete file
                        System.IO.File.Delete(path);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Deleted file: {path}");
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Item not found for deletion: {path}");
                        return;
                    }

                    // Remove the icon from the UI
                    _wpcont.Children.Remove(sp);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Removed icon for {path} from UI");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Failed to delete item {path}: {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Unable to delete item.", "Error");
                }
            }
        }

        // Corrected Win32 API declarations
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation([In] ref SHFILEOPSTRUCT lpFileOp);

        const uint FO_DELETE = 0x0003;
        const ushort FOF_ALLOWUNDO = 0x0040;
        const ushort FOF_NOCONFIRMATION = 0x0010;

        private void RemoveIcon(string path)
        {
            var sp = _wpcont.Children.OfType<StackPanel>().FirstOrDefault(s =>
            {
                string p = s.Tag?.GetType().GetProperty("FilePath")?.GetValue(s.Tag)?.ToString();
                return string.Equals(p, path, StringComparison.OrdinalIgnoreCase);
            });

            if (sp != null)
            {
                _wpcont.Children.Remove(sp);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Successfully removed icon for {path}");
            }
            else
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Failed to find StackPanel for {path} in RemoveIcon");
            }
        }
        // TEST: Filter for only text files (REMOVE AFTER TEST)
        // ApplyFilter("*.txt");



        /// <summary>
        /// Safely switches the monitored folder without destroying the frame window.
        /// Used for the "Dive In" navigation feature.
        /// </summary>
        public void NavigateTo(string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath) || !Directory.Exists(newPath))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Cannot navigate to invalid path: {newPath}");
                    return;
                }

                // 1. Suspend Watcher to prevent event spam during switch
                bool wasEnable = _watcher.EnableRaisingEvents;
                _watcher.EnableRaisingEvents = false;

                // 2. Clear UI
                _dispatcher.Invoke(() => _wpcont.Children.Clear());

                // 3. Switch Target
                _targetFolderPath = newPath;
                _watcher.Path = newPath; // FileSystemWatcher supports dynamic path changing

                // 4. Reload Content
                InitializeFrameContents();

                // 5. Resume Watcher
                _watcher.EnableRaisingEvents = wasEnable;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Portal Frame navigated to: {newPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Navigation failed: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Could not navigate to folder.\n{ex.Message}", "Navigation Error");
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _debounceTimer?.Stop();
            _debounceTimer.Tick -= ProcessPendingEvents;
        }
    }
}