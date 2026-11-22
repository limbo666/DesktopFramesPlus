using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages all frame data operations including JSON loading, saving, and basic migrations
    /// Extracted from Framemanager for better code organization and maintainability
    /// Handles core data persistence and simple validation/migration scenarios
    /// </summary>
    public static class FrameDataManager
    {
        #region Private Fields - Data Storage
        private static readonly object FrameFileLock = new();
        private static readonly JsonSerializerSettings FrameReadSettings = new() { DateParseHandling = DateParseHandling.None };
        private static List<dynamic> _frameData = new();
        private static string _jsonFilePath = "";
        private static bool _canSave;
        private static bool _hasFrameFile;

        // Key: ChildId, Value: List of ParentIds (Runtime 1-to-Many Accordion Snap Graph)
        public static Dictionary<string, List<string>> DockingMap = new Dictionary<string, List<string>>();
        #endregion

        #region Public Properties - Data Access
        /// <summary>
        /// Provides access to frame data collection
        /// Used by: Framemanager, TrayManager, CustomizeFrameForm, ItemMoveDialog
        /// Category: Data Access
        /// </summary>
        public static List<dynamic> FrameData
        {
            get => _frameData;
            set => _frameData = value;




        }

        /// <summary>
        /// Gets the JSON file path
        /// Used by: Framemanager, backup operations, debugging
        /// Category: File Management
        /// </summary>
        public static string JsonFilePath => _jsonFilePath;
        #endregion

        #region Initialization - Used by: Framemanager startup
        /// <summary>
        /// Initializes the data manager with JSON file path
        /// Used by: Framemanager during application startup
        /// Category: Initialization
        /// </summary>
        public static void Initialize()
        {
            lock (FrameFileLock)
            {
                _canSave = false;
                _jsonFilePath = ProfileManager.GetProfileFilePath("frames.json");
            }
        }
        #endregion

        #region Core Data Operations - Used by: Framemanager, TrayManager
        /// <summary>
        /// Main frame data loading method - moved from Framemanager.LoadFrameData
        /// Used by: Framemanager.LoadFrameData, application startup
        /// Category: Data Loading
        /// </summary>
        public static void LoadFrameData(TargetChecker targetChecker)
        {
            try
            {
                LoadFrameDataFromDisk();
                ApplySimpleMigrations();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Could not load frame data: {ex.Message}");
            }
        }

        internal static void LoadFrameDataFromDisk()
        {
            lock (FrameFileLock)
            {
                _canSave = false;
                _hasFrameFile = false;
                _frameData = new List<dynamic>();

                UsePreparedFrames(PrepareProfileFrames(Path.GetDirectoryName(_jsonFilePath)!));
            }
        }

        internal sealed record PreparedFrames(string Path, List<dynamic> Frames, bool HasFrameFile, bool CanSave = true);

        internal static PreparedFrames CaptureLoadedFrames()
        {
            lock (FrameFileLock)
                return new PreparedFrames(_jsonFilePath, _frameData, _hasFrameFile, _canSave);
        }

        internal static void UsePreparedFrames(PreparedFrames prepared)
        {
            lock (FrameFileLock)
            {
                _jsonFilePath = prepared.Path;
                _frameData = prepared.Frames;
                _hasFrameFile = prepared.HasFrameFile;
                _canSave = prepared.CanSave;
            }
        }

        internal static PreparedFrames PrepareProfileFrames(string profileDirectory)
        {
            string path = Path.Combine(profileDirectory, "frames.json");
            lock (FrameFileLock)
            {
                try
                {
                    return PrepareProfileFramesCore(profileDirectory, path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    if (string.Equals(path, _jsonFilePath, StringComparison.OrdinalIgnoreCase))
                        _canSave = false;
                    throw;
                }
            }
        }

        private static PreparedFrames PrepareProfileFramesCore(string profileDirectory, string path)
        {
            var frames = ReadFrameFile(path);
            if (frames == null)
            {
                string legacyPath = Path.Combine(profileDirectory, "fences.json");
                frames = ReadFrameFile(legacyPath);
                if (frames != null)
                {
                    // Create the new file before moving the original out of the migration path.
                    AtomicFile.WriteUtf8(path, JsonConvert.SerializeObject(frames, Formatting.Indented),
                        replaceExisting: false);
                    try
                    {
                        File.Move(legacyPath, legacyPath + ".migrated");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                            $"Migrated frame settings, but could not archive {legacyPath}: {ex.Message}");
                    }
                }
            }

            return new PreparedFrames(path, frames?.Cast<dynamic>().ToList() ?? new List<dynamic>(), frames != null);
        }

        private static List<JObject>? ReadFrameFile(string path)
        {
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            return ParseFrameData(content, path);
        }

        internal static List<JObject> ParseFrameData(string content, string source)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<JToken>(content, FrameReadSettings);
                var frames = data switch
                {
                    JObject frame => new List<JObject> { frame },
                    JArray array when array.All(item => item is JObject) => array.Cast<JObject>().ToList(),
                    _ => throw new JsonSerializationException("Expected a frame object or an array of frame objects.")
                };

                foreach (var frame in frames)
                    NormalizeLegacyKeys(frame);

                return frames;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{source}: {ex.Message}", ex);
            }
        }

        private static void NormalizeLegacyKeys(JObject frame)
        {
            ConsolidateFrameKey(frame, "FrameBorderColor", "FenceBorderColor", "frameBorderColor");
            ConsolidateFrameKey(frame, "FrameBorderThickness", "FenceBorderThickness", "frameBorderThickness");
        }

        private static void ConsolidateFrameKey(JObject frame, string key, params string[] legacyKeys)
        {
            foreach (string legacyKey in legacyKeys)
            {
                var legacyValue = frame[legacyKey];
                if (legacyValue == null) continue;

                // An existing zero is a valid border thickness.
                if (string.IsNullOrEmpty(frame[key]?.ToString()) && !string.IsNullOrEmpty(legacyValue.ToString()))
                    frame[key] = legacyValue;

                frame.Remove(legacyKey);
            }
        }

        /// <summary>
        /// Saves frame data to JSON with consistent formatting - moved from Framemanager.SaveFrameData
        /// Used by: Framemanager operations, frame updates, position changes
        /// Category: JSON Operations
        /// </summary>
        public static void SaveFrameData()
        {
            try
            {
                lock (FrameFileLock)
                {
                    if (!_canSave)
                        throw new InvalidOperationException(Localization.Strings.Get("MsgFrameFileSaveBlocked"));

                    var serializedData = new List<JObject>();
                    foreach (object frame in _frameData)
                    {
                        var frameDict = frame is IDictionary<string, object> dict
                            ? new Dictionary<string, object>(dict)
                            : ((JObject)frame).ToObject<IDictionary<string, object>>()!;
                        ApplyFormatConsistency(frameDict);

                        var serializedFrame = JObject.FromObject(frameDict);
                        NormalizeLegacyKeys(serializedFrame);
                        serializedData.Add(serializedFrame);
                    }

                    string formattedJson = JsonConvert.SerializeObject(serializedData, Formatting.Indented);
                    AtomicFile.WriteUtf8(_jsonFilePath, formattedJson, replaceExisting: _hasFrameFile);
                    _hasFrameFile = true;

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                        $"Saved frames.json with consistent formatting for {serializedData.Count} frames");
                }

                Layouts.LayoutManager.ConfigurationSaved();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                    $"Error saving frame data: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Frame Creation - Used by: TrayManager, Framemanager
        /// <summary>
        /// Creates new frame with proper defaults - moved from Framemanager.CreateNewFrame
        /// Category: Frame Creation
        /// </summary>
        public static dynamic CreateNewFrame(string title, string itemsType, double x = 20, double y = 20,
            string customColor = null, string customLaunchEffect = null)
        {
            try
            {
                // Generate appropriate frame name
                string frameName = (itemsType != "Portal") ?
                    CoreUtilities.GenerateRandomName() : title;

                // Create new frame object
                dynamic newFrame = new System.Dynamic.ExpandoObject();
                newFrame.Id = Guid.NewGuid().ToString();
                IDictionary<string, object> newframeDict = newFrame;

                // Set basic properties
                newframeDict["Title"] = frameName;
                newframeDict["X"] = x;
                newframeDict["Y"] = y;
                newframeDict["Width"] = 230;
                newframeDict["Height"] = 130;
                newframeDict["ItemsType"] = itemsType;

                // Set items based on frame type
                newframeDict["Items"] = itemsType == "Portal" ? "" : new JArray();

                // Apply default properties with simple validation
                ApplyFrameDefaults(newframeDict, customColor, customLaunchEffect);

                // Add to data collection and save
                _frameData.Add(newFrame);
                SaveFrameData();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Created new {itemsType} frame '{frameName}' with ID {newFrame.Id}");

                return newFrame;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error creating new frame: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Simple Migration and Validation - Internal Use



        /// <summary>
        /// Applies simple migrations and property defaults
        /// Used by: LoadFrameData during startup
        /// Category: Data Migration (Simple)
        /// </summary>
        private static void ApplySimpleMigrations()
        {
            bool jsonModified = false;

            for (int i = 0; i < _frameData.Count; i++)
            {
                try
                {
                    if (_frameData[i] is JObject frameObj)
                    {
                

                        // --- Run existing property/type validations ---
                        IDictionary<string, object> frameDict = frameObj.ToObject<IDictionary<string, object>>();
                        bool dictModified = false;

                        if (AddMissingBasicProperties(frameDict)) dictModified = true;
                        if (ValidateDataTypes(frameDict)) dictModified = true;

                        if (dictModified)
                        {
                            _frameData[i] = JObject.FromObject(frameDict);
                            jsonModified = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                        $"Error applying simple migration to frame: {ex.Message}");
                }
            }

            if (jsonModified)
            {
                SaveFrameData();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Applied simple migrations and saved updated frame data");
            }
        }

        /// <summary>
        /// Adds missing basic properties with defaults
        /// Used by: ApplySimpleMigrations
        /// Category: Property Validation
        /// </summary>
        private static bool AddMissingBasicProperties(IDictionary<string, object> frameDict)
        {
            bool modified = false;

            if (!frameDict.ContainsKey("Id") || string.IsNullOrEmpty(frameDict["Id"]?.ToString()))
            {
                frameDict["Id"] = Guid.NewGuid().ToString();
                modified = true;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"Added missing ID to frame '{(frameDict.ContainsKey("Title") ? frameDict["Title"] : "Unknown")}'");
            }

            if (!frameDict.ContainsKey("TabsEnabled")) { frameDict["TabsEnabled"] = "false"; modified = true; }
            if (!frameDict.ContainsKey("CurrentTab")) { frameDict["CurrentTab"] = 0; modified = true; }
            if (!frameDict.ContainsKey("Tabs")) { frameDict["Tabs"] = new JArray(); modified = true; }
            if (!frameDict.ContainsKey("IsHidden")) { frameDict["IsHidden"] = "false"; modified = true; }
            if (!frameDict.ContainsKey("IsRolled")) { frameDict["IsRolled"] = "false"; modified = true; }

            return modified;
        }

        /// <summary>
        /// Validates and fixes data types for consistency
        /// Used by: ApplySimpleMigrations
        /// Category: Data Validation
        /// </summary>
        private static bool ValidateDataTypes(IDictionary<string, object> frameDict)
        {
            bool modified = false;

            if (frameDict.ContainsKey("UnrolledHeight"))
            {
                if (!double.TryParse(frameDict["UnrolledHeight"]?.ToString(), out double unrolledHeight) || unrolledHeight <= 0)
                {
                    double defaultHeight = frameDict.ContainsKey("Height") ?
                        Convert.ToDouble(frameDict["Height"]) : 130;
                    frameDict["UnrolledHeight"] = defaultHeight.ToString();
                    modified = true;
                }
            }

            // ContainsKey guards: a frame without Width/Height threw KeyNotFoundException here,
            // which aborted the whole validation pass for that frame.
            if (!frameDict.ContainsKey("Width") || !double.TryParse(frameDict["Width"]?.ToString(), out double width) || width <= 0)
            {
                frameDict["Width"] = 230;
                modified = true;
            }

            if (!frameDict.ContainsKey("Height") || !double.TryParse(frameDict["Height"]?.ToString(), out double height) || height <= 0)
            {
                frameDict["Height"] = 130;
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Applies format consistency for JSON serialization
        /// Used by: SaveFrameData
        /// Category: Format Consistency
        /// </summary>
        private static void ApplyFormatConsistency(IDictionary<string, object> frameDict)
        {
            if (frameDict.ContainsKey("IsHidden"))
            {
                bool isHidden = false;
                if (frameDict["IsHidden"] is bool boolValue) isHidden = boolValue;
                else if (frameDict["IsHidden"] is string stringValue) isHidden = stringValue.ToLower() == "true";
                frameDict["IsHidden"] = isHidden.ToString().ToLower();
            }

            if (frameDict.ContainsKey("IsRolled"))
            {
                bool isRolled = false;
                if (frameDict["IsRolled"] is bool boolValue) isRolled = boolValue;
                else if (frameDict["IsRolled"] is string stringValue) isRolled = stringValue.ToLower() == "true";
                frameDict["IsRolled"] = isRolled.ToString().ToLower();
            }
        }

        /// <summary>
        /// Applies default properties to new frame
        /// Used by: CreateNewFrame
        /// Category: Frame Defaults
        /// </summary>
        private static void ApplyFrameDefaults(IDictionary<string, object> frameDict,
             string customColor, string customLaunchEffect)
        {
            frameDict["IsHidden"] = "false";
            frameDict["IsRolled"] = "false";
            frameDict["UnrolledHeight"] = frameDict["Height"].ToString();
            frameDict["TabsEnabled"] = "false";
            frameDict["CurrentTab"] = 0;
            frameDict["Tabs"] = new JArray();

            string itemsType = frameDict["ItemsType"]?.ToString();
            if (itemsType == "Note") NoteFramemanager.ApplyNoteDefaults(frameDict);

            if (!string.IsNullOrEmpty(customColor)) frameDict["CustomColor"] = customColor;
            if (!string.IsNullOrEmpty(customLaunchEffect)) frameDict["CustomLaunchEffect"] = customLaunchEffect;
        }
        #endregion

        #region Data Access Helpers - Used by: Various managers
        /// <summary>
        /// Finds frame by ID with null safety
        /// Used by: Framemanager, CustomizeFrameForm, various operations
        /// Category: Data Access
        /// </summary>
        public static dynamic FindFrameById(string frameId)
        {
            if (string.IsNullOrEmpty(frameId) || _frameData == null)
                return null;

            return _frameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
        }



        /// <summary>
        /// Mutates the master JSON frame array directly to guarantee persistence across restarts
        /// </summary>
        public static void UpdateDockedRelationships(string childId, List<string> parentIds)
        {
            try
            {
                if (string.IsNullOrEmpty(childId) || _frameData == null) return;

                if (parentIds != null && parentIds.Count > 0) DockingMap[childId] = parentIds;
                else DockingMap.Remove(childId);

                int index = _frameData.FindIndex(f => f.Id?.ToString() == childId);
                if (index >= 0)
                {
                    dynamic actualFrame = _frameData[index];
                    IDictionary<string, object> dict = actualFrame is IDictionary<string, object> d ?
                        d : ((JObject)actualFrame).ToObject<IDictionary<string, object>>();

                    if (parentIds != null && parentIds.Count > 0) dict["DockedParentId"] = string.Join(",", parentIds);
                    else if (dict.ContainsKey("DockedParentId")) dict.Remove("DockedParentId");

                    _frameData[index] = JObject.FromObject(dict);
                    SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Saved persistent multi-snap: '{childId}' -> '{string.Join(",", parentIds ?? new List<string>())}'");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error saving docked relationships: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans loaded JSON frame dictionaries, populates multi-parent DockingMap, and self-heals overlapping child coordinates
        /// </summary>
        public static void RebuildDockingMap()
        {
            try
            {
                DockingMap.Clear();
                if (_frameData == null) return;

                // 1. Rebuild runtime snap graph from persistent JSON attributes
                foreach (dynamic frame in _frameData)
                {
                    IDictionary<string, object> dict = frame is IDictionary<string, object> d ?
                        d : ((JObject)frame).ToObject<IDictionary<string, object>>();

                    if (dict.ContainsKey("Id") && dict.ContainsKey("DockedParentId") && dict["DockedParentId"] != null)
                    {
                        string cId = dict["Id"].ToString();
                        string pIdStr = dict["DockedParentId"].ToString();
                        if (!string.IsNullOrEmpty(cId) && !string.IsNullOrEmpty(pIdStr))
                        {
                            var pIds = pIdStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            if (pIds.Count > 0) DockingMap[cId] = pIds;
                        }
                    }
                }

                // 2. SELF-HEALING STARTUP ALIGNMENT FOR MULTI-PARENTS
                bool layoutHealed = false;
                foreach (var kvp in DockingMap)
                {
                    string childId = kvp.Key;
                    List<string> parentIds = kvp.Value;

                    int childIdx = _frameData.FindIndex(f => f.Id?.ToString() == childId);
                    if (childIdx >= 0 && parentIds.Count > 0)
                    {
                        dynamic childFrame = _frameData[childIdx];
                        double maxExpectedMinY = 0;

                        foreach (string pId in parentIds)
                        {
                            int parentIdx = _frameData.FindIndex(f => f.Id?.ToString() == pId);
                            if (parentIdx >= 0)
                            {
                                dynamic parentFrame = _frameData[parentIdx];
                                double parentY = Convert.ToDouble(parentFrame.Y?.ToString() ?? "0");

                                // --- FIX: State-Aware Height Calculation ---
                                bool isParentRolled = false;
                                try { isParentRolled = parentFrame.IsRolled?.ToString().ToLower() == "true"; } catch { }

                                double activeParentHeight = isParentRolled ? 28.0 : Convert.ToDouble(parentFrame.UnrolledHeight?.ToString() ?? "130");

                                // Anchor exactly 10px below the active bottom edge (matching runtime CascadeStack)
                                double expectedMinY = parentY + activeParentHeight + 10.0;

                                if (expectedMinY > maxExpectedMinY) maxExpectedMinY = expectedMinY;
                            }
                        }

                        double childY = Convert.ToDouble(childFrame.Y?.ToString() ?? "0");

                        // --- FIX: Strict Exact Alignment ---
                        // Math.Abs > 0.5 allows for tiny floating-point margins without endless saving,
                        // while ensuring frames are pulled UP to close gaps and pushed DOWN to clear overlaps.
                        if (maxExpectedMinY > 0 && Math.Abs(childY - maxExpectedMinY) > 0.5)
                        {
                            childFrame.Y = maxExpectedMinY;
                            layoutHealed = true;
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Self-Healed coordinate for multi-docked child '{childFrame.Title}' from Y={childY:F1} to Y={maxExpectedMinY:F1}");
                        }
                    }
                }

                if (layoutHealed)
                {
                    SaveFrameData();
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Restored {DockingMap.Count} persistent multi-snap links from JSON.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error in RebuildDockingMap: {ex.Message}");
            }
        }

        #endregion
    }
}
