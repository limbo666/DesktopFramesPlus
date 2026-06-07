using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // Main frame data collection - moved from Framemanager
        private static List<dynamic> _frameData;

        // JSON file path - moved from Framemanager  
        private static string _jsonFilePath;
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
        public static string JsonFilePath
        {
            get => _jsonFilePath;
            set => _jsonFilePath = value;
        }
        #endregion

        #region Initialization - Used by: Framemanager startup
        /// <summary>
        /// Initializes the data manager with JSON file path
        /// Used by: Framemanager during application startup
        /// Category: Initialization
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // ====================================================================
                // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                // Self-Healing Logic: Checks for legacy fences.json and instantly 
                // renames it to frames.json upon profile load.
                // ====================================================================
                string legacyPath = ProfileManager.GetProfileFilePath("fences.json");
                string newPath = ProfileManager.GetProfileFilePath("frames.json");

                if (!File.Exists(newPath) && File.Exists(legacyPath))
                {
                    try
                    {
                        File.Move(legacyPath, newPath);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Migrated fences.json to frames.json");
                    }
                    catch (Exception moveEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Rename failed (locked file?): {moveEx.Message}");
                        newPath = legacyPath; // Fallback to old file if rename is blocked by OS
                    }
                }

                _jsonFilePath = newPath;

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"FrameDataManager initialized with path: {_jsonFilePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error initializing FrameDataManager: {ex.Message}");
                throw;
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
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "FrameDataManager: Starting frame data load");

                // Initialize frame data list
                _frameData = new List<dynamic>();

                // Check if JSON file exists
                if (!File.Exists(_jsonFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                        "JSON file not found. Starting with empty frame configuration.");
                    return;
                }

                // Load and parse JSON data
                if (!LoadFrameDataFromJson())
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        "Failed to load JSON data. Starting with empty configuration.");
                    return;
                }

                // Apply simple migrations and validation
                ApplySimpleMigrations();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Successfully loaded {_frameData?.Count ?? 0} frames from JSON");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Critical error in LoadFrameData: {ex.Message}");
                _frameData = new List<dynamic>(); // Fallback to empty list
            }
        }

        /// <summary>
        /// JSON file parsing and loading - moved from Framemanager.LoadFrameDataFromJson
        /// Used by: LoadFrameData method
        /// Category: JSON Operations
        /// </summary>
        private static bool LoadFrameDataFromJson()
        {
            try
            {
                string jsonContent = File.ReadAllText(_jsonFilePath);

                // Check if the file is empty or contains only whitespace
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        "JSON file is empty or contains only whitespace. Using default frame configuration.");
                    return false;
                }

                // First, try to parse as a list of frames
                try
                {
                    _frameData = JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        $"Successfully loaded {_frameData?.Count ?? 0} frames from JSON array.");
                    return _frameData != null;
                }
                catch (JsonException)
                {
                    // If that fails, try to parse as a single frames object
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        "Failed to parse as array, trying single object format.");

                    dynamic singleFrame = JsonConvert.DeserializeObject(jsonContent);
                    _frameData = new List<dynamic> { singleFrame };
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        "Successfully loaded single frame from JSON object.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error loading frame data from JSON: {ex.Message}");
                return false;
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
                var serializedData = new List<JObject>();

                foreach (dynamic frame in _frameData)
                {
                    IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ?
                        dict : ((JObject)frame).ToObject<IDictionary<string, object>>();

                    // ====================================================================
                    // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                    // The Ultimate Interceptor: Forces old or accidentally generated 
                    // "Fence" keys into official "Frame" keys RIGHT BEFORE saving to disk.
                    // ====================================================================
                    void ConsolidateKey(string officialKey, string[] legacyKeys)
                    {
                        object rescuedValue = null;

                        // Extract valid data from old keys and delete them permanently
                        foreach (string oldKey in legacyKeys)
                        {
                            if (frameDict.ContainsKey(oldKey))
                            {
                                if (frameDict[oldKey] != null && frameDict[oldKey].ToString() != "0" && frameDict[oldKey].ToString() != "")
                                {
                                    rescuedValue = frameDict[oldKey];
                                }
                                frameDict.Remove(oldKey); // Vacuum it out
                            }
                        }

                        // Apply rescued data to the official key if it doesn't already have a valid setting
                        bool hasValidOfficial = frameDict.ContainsKey(officialKey) && frameDict[officialKey] != null && frameDict[officialKey].ToString() != "0" && frameDict[officialKey].ToString() != "";

                        if (!hasValidOfficial && rescuedValue != null)
                        {
                            frameDict[officialKey] = rescuedValue;
                        }
                    }

                    ConsolidateKey("FrameBorderColor", new[] { "FrameBorderColor", "FrameBorderColor" });
                    ConsolidateKey("FrameBorderThickness", new[] { "FrameBorderThickness", "FrameBorderThickness" });

                    // Apply simple format consistency
                    ApplyFormatConsistency(frameDict);

                    serializedData.Add(JObject.FromObject(frameDict));
                }

                string formattedJson = JsonConvert.SerializeObject(serializedData, Formatting.Indented);
                File.WriteAllText(_jsonFilePath, formattedJson);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                    $"Saved frames.json with consistent formatting for {serializedData.Count} frames");
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

        // ====================================================================
        // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
        // Executes at Load Time. Directly manipulates the JObject in memory
        // to consolidate messy/legacy keys and permanently delete them.
        // ====================================================================
        private static bool MigrateLegacyKeys(JObject frameObj)
        {
            bool modified = false;

            void ConsolidateKey(string officialKey, string[] legacyKeys)
            {
                JToken rescuedValue = null;

                // 1. Vacuum up the legacy keys
                foreach (string oldKey in legacyKeys)
                {
                    if (frameObj.ContainsKey(oldKey))
                    {
                        var val = frameObj[oldKey];
                        // Rescue valid data before deleting the key
                        if (val != null && val.Type != JTokenType.Null && val.ToString() != "" && val.ToString() != "0")
                        {
                            rescuedValue = val;
                        }

                        // Permanently remove the legacy key from memory
                        frameObj.Remove(oldKey);
                        modified = true;
                    }
                }

                // 2. Check if the official key already has valid data
                bool hasValidOfficial = frameObj.ContainsKey(officialKey) &&
                                        frameObj[officialKey] != null &&
                                        frameObj[officialKey].Type != JTokenType.Null &&
                                        frameObj[officialKey].ToString() != "" &&
                                        frameObj[officialKey].ToString() != "0";

                // 3. If the official key is missing or empty, apply the rescued data
                if (!hasValidOfficial && rescuedValue != null)
                {
                    frameObj[officialKey] = rescuedValue;
                    modified = true;
                }
            }

            // Consolidate Border properties
            ConsolidateKey("FrameBorderColor", new[] { "FenceBorderColor", "FrameBorderColor" });
            ConsolidateKey("FrameBorderThickness", new[] { "FenceBorderThickness", "FrameBorderThickness" });

            return modified;
        }

        /// <summary>
        /// Applies simple migrations and property defaults
        /// Used by: LoadFrameData during startup
        /// Category: Data Migration (Simple)
        /// </summary>
        private static void ApplySimpleMigrations()
        {
            bool jsonModified = false;

            // Use a standard for-loop so we can safely modify items in the list
            for (int i = 0; i < _frameData.Count; i++)
            {
                try
                {
                    // Ensure we are working directly with JObject to modify it correctly
                    if (_frameData[i] is JObject frameObj)
                    {
                        // --- 1. Run the Legacy Scrubber directly on the JObject ---
                        if (MigrateLegacyKeys(frameObj))
                            jsonModified = true;

                        // --- 2. Run existing property/type validations ---
                        IDictionary<string, object> frameDict = frameObj.ToObject<IDictionary<string, object>>();
                        bool dictModified = false;

                        if (AddMissingBasicProperties(frameDict)) dictModified = true;
                        if (ValidateDataTypes(frameDict)) dictModified = true;

                        // --- CRITICAL ARCHITECTURAL FIX ---
                        // Because ToObject creates a copy, if the validation dictionary was modified, 
                        // we MUST save it back to _frameData. Otherwise, changes are thrown away!
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

            // Save if any modifications were made (this permanently writes the clean JObjects to disk)
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

            // Add missing ID
            if (!frameDict.ContainsKey("Id") || string.IsNullOrEmpty(frameDict["Id"]?.ToString()))
            {
                frameDict["Id"] = Guid.NewGuid().ToString();
                modified = true;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"Added missing ID to frame '{(frameDict.ContainsKey("Title") ? frameDict["Title"] : "Unknown")}'");
            }

            // Add basic tab properties if missing
            if (!frameDict.ContainsKey("TabsEnabled"))
            {
                frameDict["TabsEnabled"] = "false";
                modified = true;
            }

            if (!frameDict.ContainsKey("CurrentTab"))
            {
                frameDict["CurrentTab"] = 0;
                modified = true;
            }

            if (!frameDict.ContainsKey("Tabs"))
            {
                frameDict["Tabs"] = new JArray();
                modified = true;
            }

            // Add basic state properties
            if (!frameDict.ContainsKey("IsHidden"))
            {
                frameDict["IsHidden"] = "false";
                modified = true;
            }

            if (!frameDict.ContainsKey("IsRolled"))
            {
                frameDict["IsRolled"] = "false";
                modified = true;
            }

            return modified;
        }


        /// <summary>
        /// ATOMIC SANITIZER:
        /// Scans every Frame JObject, migrates values from legacy keys to official keys, 
        /// and permanently destroys the legacy keys.
        /// </summary>
        private static void SanitizeFrameData(JObject frameObj)
        {
            // Define the migration map: Official Key -> Possible Legacy Keys
            var migrationMap = new Dictionary<string, string[]>
            {
                { "FrameBorderColor",     new[] { "FenceBorderColor", "frameBorderColor" } },
                { "FrameBorderThickness", new[] { "FenceBorderThickness", "frameBorderThickness" } }
            };

            foreach (var mapping in migrationMap)
            {
                string officialKey = mapping.Key;
                string[] legacyKeys = mapping.Value;

                JToken rescuedValue = null;

                // 1. Find and Rescue valid data from legacy keys
                foreach (string oldKey in legacyKeys)
                {
                    if (frameObj.ContainsKey(oldKey) && frameObj[oldKey].Type != JTokenType.Null)
                    {
                        var val = frameObj[oldKey];
                        // Only rescue if the value is meaningful (not 0, null, or empty)
                        if (val.ToString() != "0" && val.ToString() != "")
                        {
                            rescuedValue = val;
                        }
                    }
                }

                // 2. Remove ALL legacy keys from the object
                foreach (string oldKey in legacyKeys)
                {
                    frameObj.Remove(oldKey);
                }

                // 3. Set the official key if we rescued a value
                if (rescuedValue != null)
                {
                    frameObj[officialKey] = rescuedValue;
                }
            }
        }



        /// <summary>
        /// Validates and fixes data types for consistency
        /// Used by: ApplySimpleMigrations
        /// Category: Data Validation
        /// </summary>
        private static bool ValidateDataTypes(IDictionary<string, object> frameDict)
        {
            bool modified = false;

            // Ensure UnrolledHeight is a valid number
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

            // Ensure Width and Height are valid
            if (!double.TryParse(frameDict["Width"]?.ToString(), out double width) || width <= 0)
            {
                frameDict["Width"] = 230;
                modified = true;
            }

            if (!double.TryParse(frameDict["Height"]?.ToString(), out double height) || height <= 0)
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
            // Convert IsHidden to string format
            if (frameDict.ContainsKey("IsHidden"))
            {
                bool isHidden = false;
                if (frameDict["IsHidden"] is bool boolValue)
                    isHidden = boolValue;
                else if (frameDict["IsHidden"] is string stringValue)
                    isHidden = stringValue.ToLower() == "true";

                frameDict["IsHidden"] = isHidden.ToString().ToLower();
            }

            // Convert IsRolled to string format
            if (frameDict.ContainsKey("IsRolled"))
            {
                bool isRolled = false;
                if (frameDict["IsRolled"] is bool boolValue)
                    isRolled = boolValue;
                else if (frameDict["IsRolled"] is string stringValue)
                    isRolled = stringValue.ToLower() == "true";

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
            // Basic state defaults
            frameDict["IsHidden"] = "false";
            frameDict["IsRolled"] = "false";
            frameDict["UnrolledHeight"] = frameDict["Height"].ToString();

            // Tab defaults
            frameDict["TabsEnabled"] = "false";
            frameDict["CurrentTab"] = 0;
            frameDict["Tabs"] = new JArray();

            // Apply frame type specific defaults
            string itemsType = frameDict["ItemsType"]?.ToString();
            if (itemsType == "Note")
            {
                NoteFramemanager.ApplyNoteDefaults(frameDict);
            }

            // Custom properties
            if (!string.IsNullOrEmpty(customColor))
                frameDict["CustomColor"] = customColor;

            if (!string.IsNullOrEmpty(customLaunchEffect))
                frameDict["CustomLaunchEffect"] = customLaunchEffect;
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


        #endregion
    }
}