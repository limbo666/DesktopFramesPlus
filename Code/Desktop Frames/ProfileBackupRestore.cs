using Desktop_Frames.Localization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames
{
    internal static class ProfileBackupRestore
    {
        private sealed record RestoreEntry(string Name, bool IsDirectory)
        {
            internal bool SavedOriginal { get; set; }
            internal bool Installed { get; set; }

            internal void Move(string from, string to)
            {
                if (IsDirectory) Directory.Move(from, to);
                else File.Move(from, to);
            }
        }

        internal static void Restore(string backupDirectory, string profileDirectory, bool restoreOptions,
            Action<FrameDataManager.PreparedFrames>? apply = null)
        {
            string framesFile = Path.Combine(backupDirectory, "frames.json");
            if (!Path.Exists(framesFile)) framesFile = Path.Combine(backupDirectory, "fences.json");
            var frames = FrameDataManager.ParseFrameData(File.ReadAllText(framesFile), framesFile);
            foreach (var frame in frames)
            {
                if (frame["ItemsType"]?.ToString() is not ("Data" or "Portal" or "Note" or "Plugin"))
                    throw new InvalidDataException("Unsupported frame type in backup.");
            }

            var options = restoreOptions
                ? SettingsManager.ReadOptions(Path.Combine(backupDirectory, "options.json"))
                    ?? throw new InvalidDataException("Missing or empty options.json in backup.")
                : null;

            string staging = Path.Combine(profileDirectory, $".restore-{Guid.NewGuid():N}");
            string incoming = Path.Combine(staging, "incoming");
            string previous = Path.Combine(staging, "previous");
            var entries = new List<RestoreEntry> { new("Shortcuts", true), new("frames.json", false) };
            if (options != null) entries.Add(new RestoreEntry("options.json", false));

            bool keepRecoveryFiles = false;
            try
            {
                Directory.CreateDirectory(incoming);
                Directory.CreateDirectory(previous);
                AtomicFile.WriteUtf8(Path.Combine(incoming, "frames.json"), JsonConvert.SerializeObject(frames, Formatting.Indented));
                if (options != null)
                    AtomicFile.WriteUtf8(Path.Combine(incoming, "options.json"), options.ToString(Formatting.Indented));
                CopyShortcuts(Path.Combine(backupDirectory, "Shortcuts"), Path.Combine(incoming, "Shortcuts"));

                foreach (var entry in entries)
                {
                    string live = Path.Combine(profileDirectory, entry.Name);
                    if (Path.Exists(live))
                    {
                        entry.Move(live, Path.Combine(previous, entry.Name));
                        entry.SavedOriginal = true;
                    }
                    entry.Move(Path.Combine(incoming, entry.Name), live);
                    entry.Installed = true;
                }

                apply?.Invoke(new FrameDataManager.PreparedFrames(Path.Combine(profileDirectory, "frames.json"),
                    frames.ConvertAll<dynamic>(frame => frame), HasFrameFile: true));
            }
            catch (Exception restoreError)
            {
                var errors = new List<Exception> { restoreError };
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    string live = Path.Combine(profileDirectory, entry.Name);
                    try
                    {
                        if (entry.Installed) entry.Move(live, Path.Combine(incoming, entry.Name));
                        if (entry.SavedOriginal) entry.Move(Path.Combine(previous, entry.Name), live);
                    }
                    catch (Exception rollbackError) { errors.Add(rollbackError); }
                }

                if (errors.Count > 1)
                {
                    keepRecoveryFiles = true;
                    throw new IOException(Strings.Get("MsgRestoreRecoveryDirectory", previous), new AggregateException(errors));
                }
                throw;
            }
            finally
            {
                if (!keepRecoveryFiles && Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ImportExport, $"Restore files retained at {staging}: {ex.Message}");
                    }
                }
            }
        }

        private static void CopyShortcuts(string source, string destination)
        {
            if (!ProfileDirectory.IsLocalDirectory(source))
                throw new IOException("The backup Shortcuts directory is missing or is a link.");

            Directory.CreateDirectory(destination);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"The backup contains a filesystem link: {entry.Name}");

                string target = Path.Combine(destination, entry.Name);
                if (entry is DirectoryInfo)
                    CopyShortcuts(entry.FullName, target);
                else
                    File.Copy(entry.FullName, target);
            }
        }
    }
}
