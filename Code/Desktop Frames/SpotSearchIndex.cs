using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames
{
    internal enum SpotSearchItemType
    {
        FileOrApp,
        Folder,
        Link
    }

    internal sealed record SpotSearchFrame(int Index, string? Title);
    internal sealed record SpotSearchTab(SpotSearchFrame Frame, int Index, string? Title);
    internal sealed record SpotSearchEntry(
        string Name, string Path, SpotSearchFrame Frame, SpotSearchTab? Tab, SpotSearchItemType Type);

    internal sealed class SpotSearchIndex
    {
        public IReadOnlyList<SpotSearchEntry> Entries { get; }

        public SpotSearchIndex(IEnumerable<object?> frameData)
        {
            var entries = new List<SpotSearchEntry>();
            int frameIndex = 0;
            foreach (object? data in frameData)
            {
                frameIndex++;
                var frame = data switch
                {
                    JObject json => json,
                    IDictionary<string, object?> fields => JObject.FromObject(fields),
                    _ => null
                };
                if (frame == null || ReadString(frame["ItemsType"]) != "Data") continue;

                var frameScope = new SpotSearchFrame(frameIndex, ReadString(frame["Title"]));
                bool foundTabItems = false;
                if (ReadFlag(frame["TabsEnabled"]) && frame["Tabs"] is JArray tabs)
                {
                    for (int index = 0; index < tabs.Count; index++)
                    {
                        if (tabs[index] is not JObject tab || tab["Items"] is not JArray tabItems) continue;

                        foundTabItems = true;
                        var tabScope = new SpotSearchTab(frameScope, index + 1, ReadString(tab["TabName"]));
                        AddItems(entries, tabItems, frameScope, tabScope);
                    }
                }

                // The main list mirrors the first tab when tabs are enabled.
                if (!foundTabItems && frame["Items"] is JArray items)
                    AddItems(entries, items, frameScope, null);
            }

            Entries = entries;
        }

        public IEnumerable<SpotSearchEntry> Find(
            string query, SpotSearchFrame? frame, SpotSearchTab? tab, SpotSearchItemType? type)
        {
            query = query.Trim();
            if (query.Length == 0 && frame == null && tab == null && type == null)
                return Enumerable.Empty<SpotSearchEntry>();

            return Entries.Where(entry =>
                (frame == null || entry.Frame == frame) &&
                (tab == null || entry.Tab == tab) &&
                (type == null || entry.Type == type) &&
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsWebAddress(string path) =>
            Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static void AddItems(List<SpotSearchEntry> entries, JArray items,
            SpotSearchFrame frame, SpotSearchTab? tab)
        {
            foreach (var item in items.OfType<JObject>())
            {
                string? path = ReadString(item["Filename"]);
                if (path == null || path.StartsWith("INTERNAL_BLANK_", StringComparison.Ordinal)) continue;

                string name = ReadString(item["DisplayName"]) ?? Path.GetFileNameWithoutExtension(GetFileName(path));
                var type = SpotSearchItemType.FileOrApp;
                if (ReadFlag(item["IsFolder"]))
                    type = SpotSearchItemType.Folder;
                else if (ReadFlag(item["IsLink"]) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || IsWebAddress(path))
                    type = SpotSearchItemType.Link;

                entries.Add(new SpotSearchEntry(name.Length > 0 ? name : path, path, frame, tab, type));
            }
        }

        private static string GetFileName(string path) =>
            Path.GetFileName(path.TrimEnd('\\', '/').Replace('\\', '/'));

        private static string? ReadString(JToken? token) =>
            token is JValue { Value: string value } && !string.IsNullOrWhiteSpace(value) ? value : null;

        private static bool ReadFlag(JToken? token) =>
            token is JValue && (bool.TryParse(token.ToString(), out bool flag)
                ? flag
                : token.Type == JTokenType.Integer && token.ToString() == "1");
    }
}
