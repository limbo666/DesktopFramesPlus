using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Desktop_Frames
{
    internal sealed class SharedFramePackage
    {
        private const long MaxArchiveBytes = 256L * 1024 * 1024;
        private const long MaxFrameBytes = 16L * 1024 * 1024;
        private const long MaxShortcutBytes = 8L * 1024 * 1024;
        private const long MaxAllShortcutBytes = 128L * 1024 * 1024;

        private readonly Dictionary<JObject, ZipArchiveEntry> _shortcuts = new();

        private SharedFramePackage(JObject frame) => Frame = frame;

        internal JObject Frame { get; }
        internal int SkippedItems { get; private set; }
        internal int ShortcutCount => _shortcuts.Values.Distinct().Count();

        internal static void CheckArchiveSize(string path)
        {
            if (new FileInfo(path).Length > MaxArchiveBytes)
                throw new InvalidDataException("Frame archive is too large.");
        }

        internal static SharedFramePackage Read(ZipArchive archive)
        {
            if (archive.Entries.Count > 8192) throw new InvalidDataException("Too many entries in frame archive.");

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/')) continue;
                if (!name.Equals("frame.json", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("fence.json", StringComparison.OrdinalIgnoreCase) &&
                    (!name.StartsWith("Shortcuts/", StringComparison.OrdinalIgnoreCase) || !IsShortcutName(name))) continue;

                if (!name.Split('/').All(IsSafeFileName) || !entries.TryAdd(name, entry))
                    throw new InvalidDataException("Invalid or duplicate file name in frame archive.");
            }

            if (!entries.TryGetValue("frame.json", out var metadata) && !entries.TryGetValue("fence.json", out metadata))
                throw new InvalidDataException("Frame archive is missing its metadata.");

            using var content = new MemoryStream();
            CopyEntry(metadata, content, MaxFrameBytes);
            content.Position = 0;
            using var reader = new StreamReader(content);
            var frames = FrameDataManager.ParseFrameData(reader.ReadToEnd(), metadata.FullName);
            if (frames.Count != 1 || frames[0]["ItemsType"]?.ToString() is not ("Data" or "Portal" or "Note" or "Plugin"))
                throw new InvalidDataException("Expected one supported frame in the archive.");

            var package = new SharedFramePackage(frames[0]);
            string? frameType = package.Frame["ItemsType"]?.ToString();
            if (frameType == "Data")
                package.MapShortcuts(entries);
            else if (frameType == "Portal")
                package.Frame.Remove("Path");
            package.Frame["Id"] = Guid.NewGuid().ToString();
            return package;
        }

        private void MapShortcuts(Dictionary<string, ZipArchiveEntry> entries)
        {
            var itemLists = new List<JArray>();
            if (Frame["Items"] is JArray items) itemLists.Add(items);
            if (Frame["Tabs"] is JArray tabs)
                itemLists.AddRange(tabs.OfType<JObject>().Select(tab => tab["Items"]).OfType<JArray>());
            var pathsByName = itemLists.SelectMany(list => list.OfType<JObject>())
                .Where(item => item["Filename"]?.Type == JTokenType.String)
                .GroupBy(item => GetFileName((string)item["Filename"]!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group
                    .Select(item => ((string)item["Filename"]!).Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var list in itemLists)
            {
                foreach (var token in list.ToList())
                {
                    if (token is JObject item && item["Filename"]?.Type == JTokenType.String)
                    {
                        string path = ((string)item["Filename"]!).Replace('\\', '/');
                        if (path.StartsWith("INTERNAL_BLANK_", StringComparison.Ordinal) && IsSafeFileName(path))
                            continue;

                        string name = GetFileName(path);
                        string flatPath = "Shortcuts/" + name;
                        string[] originalPaths = pathsByName[name];
                        // Older exports flattened nested names. Keep a flat match only when it is unambiguous.
                        bool canMatchFlat = originalPaths.Length == 1 || originalPaths.All(entries.ContainsKey);
                        if (IsShortcutName(name) && IsSafeFileName(name) &&
                            (entries.TryGetValue(path, out var entry) &&
                                (!path.Equals(flatPath, StringComparison.OrdinalIgnoreCase) || canMatchFlat) ||
                                originalPaths.Length == 1 && entries.TryGetValue(flatPath, out entry)))
                        {
                            item.Remove("AlwaysRunAsAdmin");
                            item.Remove("AlwaysRunAsDifferentUser");
                            _shortcuts.Add(item, entry);
                            continue;
                        }
                    }
                    token.Remove();
                    SkippedItems++;
                }
            }

            if (_shortcuts.Values.Distinct().Any(entry => entry.Length > MaxShortcutBytes) ||
                _shortcuts.Values.Distinct().Sum(entry => entry.Length) > MaxAllShortcutBytes)
                throw new InvalidDataException("Shortcut data in frame archive is too large.");
        }

        internal void InstallShortcuts(string profileDirectory, List<string> createdFiles)
        {
            if (_shortcuts.Count == 0) return;

            string directory = Path.Combine(profileDirectory, "Shortcuts");
            Directory.CreateDirectory(directory);
            if (!ProfileDirectory.IsLocalDirectory(directory))
                throw new IOException("Shortcuts must be a physical directory inside the profile.");

            var installedNames = new Dictionary<ZipArchiveEntry, string>();
            foreach (var pair in _shortcuts)
            {
                if (!installedNames.TryGetValue(pair.Value, out string? name))
                {
                    string original = GetFileName(pair.Value.FullName.Replace('\\', '/'));
                    name = original;
                    string target = Path.Combine(directory, name);
                    for (int number = 1; Path.Exists(target); number++)
                    {
                        name = $"{Path.GetFileNameWithoutExtension(original)} ({number}){Path.GetExtension(original)}";
                        target = Path.Combine(directory, name);
                    }

                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        createdFiles.Add(target);
                        CopyEntry(pair.Value, output, MaxShortcutBytes);
                    }
                    installedNames.Add(pair.Value, name);
                }
                pair.Key["Filename"] = Path.Combine("Shortcuts", name);
            }
        }

        private static void CopyEntry(ZipArchiveEntry entry, Stream output, long limit)
        {
            if (entry.Length < 0 || entry.Length > limit) throw new InvalidDataException("Frame archive entry is too large.");
            using var input = entry.Open();
            byte[] buffer = new byte[8192];
            long remaining = entry.Length;
            int read;
            while ((read = input.Read(buffer)) != 0)
            {
                remaining -= read;
                if (remaining < 0) throw new InvalidDataException("Invalid frame archive entry size.");
                output.Write(buffer, 0, read);
            }
            if (remaining != 0) throw new InvalidDataException("Incomplete frame archive entry.");
        }

        private static string GetFileName(string path) => path[(path.LastIndexOf('/') + 1)..];

        internal static bool IsShortcutName(string name) =>
            name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

        private static bool IsSafeFileName(string name)
        {
            if (name.Length == 0 || name.EndsWith('.') || name.EndsWith(' ') ||
                name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl)) return false;

            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
                !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]));
        }
    }
}
