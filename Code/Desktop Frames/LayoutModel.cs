using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Desktop_Frames.Layouts
{
    public sealed class LayoutMonitor
    {
        public string Id { get; set; } = "";
        public string HardwareId { get; set; } = "";
        public string Name { get; set; } = Strings.LayoutMonitor;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Scale { get; set; } = 1;
    }

    public sealed class LayoutFrame
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string MonitorId { get; set; } = "";
        // Fractions of the monitor work area, independent of desktop origin.
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class SavedLayout
    {
        private const int MaxMonitors = 64;
        private const int MaxFrames = 10000;
        public int Version { get; set; } = 1;
        public string Name { get; set; } = Strings.LayoutSavedName;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedUtc { get; set; }
        public List<LayoutMonitor> Monitors { get; set; } = new();
        public List<LayoutFrame> Frames { get; set; } = new();

        public SavedLayout Copy() => JsonSerializer.Deserialize<SavedLayout>(JsonSerializer.Serialize(this))!;

        public void Validate()
        {
            if (Version != 1 || Monitors == null || Frames == null ||
                Monitors.Count == 0 || Monitors.Count > MaxMonitors || Frames.Count > MaxFrames)
                throw new InvalidOperationException(Strings.LayoutInvalidFile);

            if (Monitors.Any(monitor => !ValidMonitor(monitor)))
                throw new InvalidOperationException(Strings.LayoutInvalidGeometry);

            var monitorIds = Monitors.Select(monitor => monitor.Id).ToHashSet();
            if (monitorIds.Count != Monitors.Count ||
                Frames.Any(frame => !ValidFrame(frame, monitorIds)) ||
                Frames.Select(frame => frame.Id).Distinct().Count() != Frames.Count)
                throw new InvalidOperationException(Strings.LayoutInvalidGeometry);
        }

        private static bool ValidMonitor(LayoutMonitor? monitor) =>
            monitor != null && !string.IsNullOrEmpty(monitor.Id) &&
            Finite(monitor.Left, monitor.Top, monitor.Width, monitor.Height, monitor.Scale) &&
            monitor.Width > 0 && monitor.Height > 0 && monitor.Scale > 0;

        private static bool ValidFrame(LayoutFrame? frame, HashSet<string> monitorIds) =>
            frame != null && !string.IsNullOrEmpty(frame.Id) &&
            Finite(frame.X, frame.Y, frame.Width, frame.Height) &&
            frame.Width > 0 && frame.Height > 0 && monitorIds.Contains(frame.MonitorId);

        private static bool Finite(params double[] values) => values.All(double.IsFinite);

        public string Fingerprint()
        {
            // Titles, timestamps and list order do not turn content edits into layout edits.
            var shape = new {
                Monitors = Monitors.OrderBy(m => m.Id).Select(m => new { m.Id, m.HardwareId, m.Left, m.Top, m.Width, m.Height, m.Scale }),
                Frames = Frames.OrderBy(f => f.Id).Select(f => new { f.Id, f.MonitorId, f.X, f.Y, f.Width, f.Height })
            };
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(shape))));
        }
    }

    public static class LayoutResolver
    {
        public static LayoutMonitor? Match(LayoutMonitor saved, IReadOnlyList<LayoutMonitor> expected, IReadOnlyList<LayoutMonitor> current)
        {
            var exact = current.Where(m => m.Id == saved.Id).ToList();
            if (exact.Count == 1) return exact[0];
            // EDID fallback is allowed only when unique on BOTH sides. Never guess between identical displays.
            if (string.IsNullOrEmpty(saved.HardwareId) || expected.Count(m => m.HardwareId == saved.HardwareId) != 1) return null;
            var hardware = current.Where(m => m.HardwareId == saved.HardwareId).ToList();
            return hardware.Count == 1 ? hardware[0] : null;
        }

        public static bool CanRestore(SavedLayout saved, IReadOnlyList<LayoutMonitor> current)
        {
            if (saved.Monitors.Count == 0 || saved.Monitors.Count != current.Count) return false;
            var matches = saved.Monitors.Select(m => Match(m, saved.Monitors, current)).ToList();
            if (matches.Any(m => m == null) || matches.Select(m => m!.Id).Distinct().Count() != matches.Count) return false;
            // Renumbering and changed desktop origins are fine. A temporary lower resolution is not.
            return saved.Monitors.Zip(matches, (a, b) => Math.Abs(a.Width - b!.Width) < 2 &&
                Math.Abs(a.Height - b.Height) < 2 && Math.Abs(a.Scale - b.Scale) < .01).All(x => x);
        }

        public static (double X, double Y, double Width, double Height) Resolve(LayoutFrame frame, LayoutMonitor monitor)
        {
            double width = Math.Min(frame.Width * monitor.Scale, monitor.Width);
            double height = Math.Min(frame.Height * monitor.Scale, monitor.Height);
            return (monitor.Left + Math.Clamp(frame.X * monitor.Width, 0, monitor.Width - width),
                monitor.Top + Math.Clamp(frame.Y * monitor.Height, 0, monitor.Height - height), width, height);
        }
    }
}
