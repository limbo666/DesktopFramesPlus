using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Desktop_Frames.Layouts
{
    public sealed class LayoutRetention
    {
        public int Days { get; set; } = 90; // Zero means unlimited.
        public int Count { get; set; } = 0;
    }

    public sealed class LayoutEntry
    {
        public string FileName { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public bool Pinned { get; set; }
    }

    public sealed class LayoutStore
    {
        private const int MaxLayoutFileSize = 8 * 1024 * 1024;
        private const string TimestampFormat = "yyyyMMddHHmmssfffffff";
        private readonly string directory;
        private readonly object gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        public LayoutStore(string profileDirectory)
        {
            directory = Path.Combine(profileDirectory, "Layouts");
            Directory.CreateDirectory(directory);
        }

        private string PathFor(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file)
                throw new ArgumentException(Strings.LayoutInvalidName);
            return Path.Combine(directory, file);
        }

        private void Write<T>(string file, T value)
        {
            string target = PathFor(file);
            string temp = target + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(target))
                File.Replace(temp, target, target + ".previous", true);
            else
                File.Move(temp, target);
        }

        public SavedLayout? LoadIntended()
        {
            lock (gate)
            {
                if (!File.Exists(PathFor("intended.json")))
                    return null;
                try
                {
                    return Read("intended.json");
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is IOException)
                {
                    if (File.Exists(PathFor("intended.json.previous")))
                        return Read("intended.json.previous");
                    throw new InvalidOperationException(Strings.LayoutIntendedReadFailed, ex);
                }
            }
        }

        public void SaveIntended(SavedLayout layout)
        {
            layout.Validate();
            lock (gate) Write("intended.json", layout);
        }

        public SavedLayout Read(string file)
        {
            lock (gate)
            {
                var info = new FileInfo(PathFor(file));
                if (info.Length > MaxLayoutFileSize)
                    throw new InvalidOperationException(Strings.LayoutFileTooLarge);
                var result = JsonSerializer.Deserialize<SavedLayout>(File.ReadAllText(info.FullName)) ?? throw new InvalidOperationException(Strings.LayoutFileEmpty);
                result.Validate();
                return result;
            }
        }

        public string Snapshot(SavedLayout layout, string kind, string name)
            => CreateSnapshot(layout, kind, name, DateTime.UtcNow);

        private string CreateSnapshot(SavedLayout layout, string kind, string name, DateTime now)
        {
            if (!IsSnapshotKind(kind))
                throw new ArgumentException(Strings.LayoutInvalidKind);
            lock (gate)
            {
                var copy = layout.Copy();
                copy.Validate();
                copy.Name = name;
                copy.CreatedUtc = now;
                copy.UpdatedUtc = now;
                string timestamp = copy.CreatedUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
                string file = $"{timestamp}.{kind}.{Guid.NewGuid():N}.json";
                Write(file, copy);
                return file;
            }
        }

        public List<LayoutEntry> List()
        {
            lock (gate)
            {
                // Read filenames only. Snapshot contents are loaded for the current page/selection.
                var pins = Directory.EnumerateFiles(directory, "*.pin").Select(Path.GetFileName).ToHashSet();
                var entries = new List<LayoutEntry>();
                foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
                {
                    var entry = Parse(Path.GetFileName(path));
                    if (entry == null)
                        continue;
                    entry.Pinned = pins.Contains(entry.FileName + ".pin");
                    entries.Add(entry);
                }
                return entries.OrderByDescending(entry => entry.CreatedUtc).ToList();
            }
        }

        private static LayoutEntry? Parse(string? file)
        {
            var parts = (file ?? "").Split('.');
            if (parts.Length != 4 || !IsSnapshotKind(parts[1]) ||
                !DateTime.TryParseExact(parts[0], TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ||
                !Guid.TryParseExact(parts[2], "N", out _))
                return null;
            return new LayoutEntry { FileName = file!, Kind = parts[1], CreatedUtc = time };
        }

        private static bool IsSnapshotKind(string kind) => kind is "auto" or "manual" or "undo";

        public void Pin(string file, bool pin)
        {
            lock (gate)
            {
                if (Parse(file) == null || !File.Exists(PathFor(file))) throw new ArgumentException(Strings.LayoutSnapshotNotFound);
                if (pin) File.WriteAllText(PathFor(file + ".pin"), "Pinned");
                else File.Delete(PathFor(file + ".pin"));
            }
        }

        public void Delete(string file)
        {
            lock (gate)
            {
                if (Parse(file) == null) throw new ArgumentException(Strings.LayoutInvalidSnapshot);
                if (File.Exists(PathFor(file + ".pin"))) throw new InvalidOperationException(Strings.LayoutUnpinBeforeDelete);
                File.Delete(PathFor(file));
                File.Delete(PathFor(file + ".previous"));
            }
        }

        public LayoutRetention Retention()
        {
            lock (gate)
            {
                string path = PathFor("retention.json");
                if (!File.Exists(path))
                    return new LayoutRetention();
                return JsonSerializer.Deserialize<LayoutRetention>(File.ReadAllText(path)) ?? new LayoutRetention();
            }
        }

        public void SetRetention(LayoutRetention value)
        {
            if (value.Days < 0 || value.Count < 0) throw new ArgumentException(Strings.LayoutNegativeRetention);
            lock (gate) Write("retention.json", value);
        }

        public List<LayoutEntry> CleanupCandidates(LayoutRetention retention, DateTime now)
        {
            var eligible = List().Where(entry => entry.Kind != "manual" && !entry.Pinned).ToList();
            // The newest eligible snapshot is always kept, regardless of the limits.
            return eligible.Skip(1).Where((entry, index) =>
                (retention.Days > 0 && entry.CreatedUtc < now.AddDays(-retention.Days)) ||
                (retention.Count > 0 && index + 1 >= retention.Count)).ToList();
        }

        public void Cleanup()
        {
            lock (gate)
            {
                foreach (var entry in CleanupCandidates(Retention(), DateTime.UtcNow))
                    Delete(entry.FileName);
            }
        }

        public bool DailySnapshot(SavedLayout intended, DateTime now)
        {
            intended.Validate();
            now = now.ToUniversalTime();
            lock (gate)
            {
                var entries = List();
                // Today's unpinned snapshot is a rolling checkpoint. Historical,
                // manual and pinned snapshots are never rewritten.
                var today = entries.FirstOrDefault(x => x.Kind == "auto" && !x.Pinned &&
                    x.CreatedUtc.ToLocalTime().Date == now.ToLocalTime().Date);
                if (today != null)
                {
                    var previous = Read(today.FileName);
                    if (previous.Fingerprint() == intended.Fingerprint()) return false;
                    var updated = intended.Copy();
                    updated.CreatedUtc = previous.CreatedUtc;
                    updated.UpdatedUtc = now;
                    updated.Name = Strings.LayoutAutomatic;
                    Write(today.FileName, updated);
                    return true;
                }
                var latest = entries.FirstOrDefault(x => x.Kind == "auto");
                if (latest != null && Read(latest.FileName).Fingerprint() == intended.Fingerprint()) return false;
                var newest = entries.FirstOrDefault();
                if (newest != null && Read(newest.FileName).Fingerprint() == intended.Fingerprint()) return false;
                CreateSnapshot(intended, "auto", Strings.LayoutAutomatic, now);
                Cleanup();
                return true;
            }
        }
    }
}
