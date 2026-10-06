using Desktop_Frames.Localization;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Desktop_Frames.Layouts
{
    public static class LayoutManager
    {
        private static SavedLayout? intended;
        private static LayoutStore? store;
        private static string profile = "";
        private static List<LayoutMonitor> displays = new();
        private static readonly HashSet<Window> editing = new();
        private static readonly Dictionary<Window, string> editTopology = new();
        private static readonly TimeSpan SnapshotDelay = TimeSpan.FromSeconds(5);
        private static DispatcherTimer? displayTimer;
        private static DispatcherTimer? dailyTimer;
        private static DispatcherTimer? snapshotTimer;
        private static DateTime snapshotDueUtc = DateTime.MinValue;
        private static bool subscribed, loading, applying;
        private static volatile bool paused = true;
        internal static Func<List<LayoutMonitor>> ReadDisplays = LayoutDisplays.Read;
        private static bool configurationPending;
        private static int stableSamples;
        private static string lastTopology = "";
        private static Task background = Task.CompletedTask;
        private static DateTime lastCleanupDay = DateTime.MinValue;
        private static string statusKey = "LayoutNotLoaded";
        private static object[] statusArguments = Array.Empty<object>();
        public static string Status => Strings.Get(statusKey, statusArguments);
        public static bool Ready => intended != null && store != null;
        public static bool Paused => paused;
        public static LayoutStore Store => store ?? throw new InvalidOperationException(Status);
        public static SavedLayout Intended => intended?.Copy() ?? throw new InvalidOperationException(Status);
        public static event Action<string>? SnapshotsChanged;

        private static string Topology(List<LayoutMonitor> monitors) => new SavedLayout { Monitors = monitors }.Fingerprint();
        private static IEnumerable<NonActivatingWindow> Windows => Application.Current.Windows.OfType<NonActivatingWindow>();

        public static void BeginLoad()
        {
            FlushPendingSnapshot();
            snapshotTimer?.Stop();
            snapshotDueUtc = DateTime.MinValue;
            loading = true;
            editing.Clear();
            editTopology.Clear();
            displayTimer?.Stop();
            paused = true;
        }

        public static void Initialize()
        {
            try
            {
                string currentProfile = ProfileManager.CurrentProfileDir;
                if (store == null || profile != currentProfile)
                {
                    profile = currentProfile;
                    store = new LayoutStore(profile);
                    intended = null;
                    intended = store.LoadIntended();
                    lastCleanupDay = DateTime.MinValue;
                }
                displays = ReadDisplays();
                if (intended == null)
                {
                    // Seed from persisted positions, before Windows or auto-reposition can move the windows.
                    intended = CaptureData(displays);
                    store.SaveIntended(intended);
                    store.Snapshot(intended, "manual", Strings.LayoutInitial);
                }
                paused = !LayoutResolver.CanRestore(intended, displays);
                if (!paused) ApplyData(intended, displays);
                SetStatus(paused ? "LayoutWaiting" : "LayoutProtected");
                if (!subscribed)
                {
                    SystemEvents.DisplaySettingsChanging += OnDisplayChange;
                    SystemEvents.DisplaySettingsChanged += OnDisplayChange;
                    Application.Current.Exit += (_, _) =>
                    {
                        snapshotTimer?.Stop();
                        dailyTimer?.Stop();
                        displayTimer?.Stop();
                        FlushPendingSnapshot();
                        SystemEvents.DisplaySettingsChanging -= OnDisplayChange;
                        SystemEvents.DisplaySettingsChanged -= OnDisplayChange;
                    };
                    subscribed = true;
                }
                dailyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                dailyTimer.Tick -= DailyTick;
                dailyTimer.Tick += DailyTick;
                dailyTimer.Start();
            }
            catch (Exception ex) { paused = true; Report(ex); }
        }

        public static void CompleteLoad()
        {
            loading = false;
            if (!Ready) return;
            if (!paused) ApplyWindows();
            StartDisplayCheck();
        }

        private static SavedLayout CaptureData(List<LayoutMonitor> monitors)
        {
            var layout = new SavedLayout { Monitors = monitors };
            foreach (dynamic raw in FrameDataManager.FrameData ?? new List<dynamic>())
            {
                var data = JObject.FromObject((object)raw);
                double scale = monitors[0].Scale;
                double x = Number(data, "X", 20) * scale, y = Number(data, "Y", 20) * scale;
                var monitor = LayoutDisplays.Nearest(monitors, x, y);
                layout.Frames.Add(new LayoutFrame { Id = data.Value<string>("Id") ?? "", Title = data.Value<string>("Title") ?? Strings.LayoutFrame,
                    MonitorId = monitor.Id, X = (x - monitor.Left) / monitor.Width, Y = (y - monitor.Top) / monitor.Height,
                    Width = Number(data, "Width", 230), Height = Number(data, "UnrolledHeight", Number(data, "Height", 130)) });
            }
            return layout;
        }

        private static double Number(JObject data, string key, double fallback) =>
            double.TryParse(data[key]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : fallback;

        public static bool IsEditing(Window window) => !paused && !loading && !applying && editing.Contains(window);

        public static void BeginEdit(Window window)
        {
            if (!Ready || loading || applying || paused) return;
            try
            {
                var current = ReadDisplays();
                if (!LayoutResolver.CanRestore(intended!, current)) { Pause(); return; }
                displays = current;
                editing.Add(window);
                editTopology[window] = Topology(current);
            }
            catch (Exception ex) { Pause(); Report(ex); }
        }

        public static void EndEdit(Window window)
        {
            bool wasEditing = editing.Remove(window);
            editTopology.Remove(window, out var started);
            if (!wasEditing || !Ready || paused || loading || applying) return;
            try
            {
                var current = ReadDisplays();
                if (Topology(current) != started || !LayoutResolver.CanRestore(intended!, current)) { Pause(); return; }
                var frame = intended!.Frames.FirstOrDefault(f => f.Id == window.Tag?.ToString());
                if (frame == null) return;
                CaptureWindow(frame, window, current);
                // Use current physical identities/origins after a driver re-enumeration.
                RebindMonitors(current);
                store!.SaveIntended(intended);
                QueueSnapshot();
            }
            catch (Exception ex) { Report(ex); }
        }

        private static void CaptureWindow(LayoutFrame frame, Window window, List<LayoutMonitor> current)
        {
            if (!LayoutDisplays.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect))
                throw new InvalidOperationException(Strings.LayoutReadPositionFailed);
            var monitor = LayoutDisplays.Nearest(current, rect.Left, rect.Top);
            frame.MonitorId = monitor.Id;
            frame.X = (rect.Left - monitor.Left) / monitor.Width;
            frame.Y = (rect.Top - monitor.Top) / monitor.Height;
            frame.Width = window.Width;
            // Roll-up is a visual state; retain the intended expanded height.
            var raw = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frame.Id);
            var data = raw == null ? new JObject() : JObject.FromObject((object)raw);
            bool rolled = data["IsRolled"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true || window.Height <= 33;
            frame.Height = rolled ? Number(data, "UnrolledHeight", frame.Height) : window.Height;
        }

        private static void RebindMonitors(List<LayoutMonitor> current)
        {
            foreach (var old in intended!.Monitors)
            {
                var match = LayoutResolver.Match(old, intended.Monitors, current);
                if (match != null) foreach (var frame in intended.Frames.Where(f => f.MonitorId == old.Id)) frame.MonitorId = match.Id;
            }
            intended.Monitors = current;
            displays = current;
        }

        // Called after configuration writes. Only membership/title changes are observed here, never positions.
        public static void ConfigurationSaved()
        {
            if (!Ready || loading || applying || paused || editing.Count > 0 || configurationPending) return;
            configurationPending = true;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                configurationPending = false;
                SyncMembership();
            }));
        }

        private static void SyncMembership()
        {
            if (!Ready || loading || applying || paused || editing.Count > 0) return;
            try
            {
                // Most configuration writes only edit icons or notes. Avoid querying displays for those.
                var ids = FrameDataManager.FrameData.Select(f => (string)f.Id.ToString()).ToHashSet();
                if (ids.SetEquals(intended!.Frames.Select(f => f.Id)))
                {
                    bool renamed = false;
                    foreach (dynamic raw in FrameDataManager.FrameData)
                    {
                        var saved = intended.Frames.First(f => f.Id == (string)raw.Id.ToString());
                        string title = raw.Title?.ToString() ?? Strings.LayoutFrame;
                        if (saved.Title != title) { saved.Title = title; renamed = true; }
                    }
                    if (renamed) store!.SaveIntended(intended);
                    return;
                }
                var current = ReadDisplays();
                if (!LayoutResolver.CanRestore(intended!, current)) { Pause(); return; }
                var data = CaptureData(current);
                bool changed = false;
                foreach (var frame in data.Frames)
                {
                    var known = intended!.Frames.FirstOrDefault(f => f.Id == frame.Id);
                    if (known != null) known.Title = frame.Title;
                    else { intended!.Frames.Add(frame); changed = true; }
                }
                changed |= intended!.Frames.RemoveAll(f => !data.Frames.Any(d => d.Id == f.Id)) > 0;
                if (changed)
                {
                    RebindMonitors(current);
                    store!.SaveIntended(intended);
                    QueueSnapshot();
                }
            }
            catch (Exception ex) { Report(ex); }
        }

        private static void ApplyData(SavedLayout layout, List<LayoutMonitor> current)
        {
            foreach (dynamic raw in FrameDataManager.FrameData ?? new List<dynamic>())
            {
                string? id = raw.Id?.ToString();
                var saved = layout.Frames.FirstOrDefault(f => f.Id == id);
                if (saved == null) continue;
                var original = layout.Monitors.First(m => m.Id == saved.MonitorId);
                var target = LayoutResolver.Match(original, layout.Monitors, current);
                if (target == null) continue;
                var rect = LayoutResolver.Resolve(saved, target);
                raw.X = rect.X / target.Scale;
                raw.Y = rect.Y / target.Scale;
                raw.Width = saved.Width;
                raw.UnrolledHeight = saved.Height;
                raw.Height = raw.IsRolled?.ToString().ToLowerInvariant() == "true" ? 28 : saved.Height;
            }
        }

        private static void ApplyWindows()
        {
            if (!Ready || loading) return;
            applying = true;
            try
            {
                ApplyData(intended!, displays);

                foreach (var window in Windows.ToList())
                {
                    var frame = intended!.Frames.FirstOrDefault(f => f.Id == window.Tag?.ToString());
                    if (frame == null) continue;
                    var target = LayoutResolver.Match(intended.Monitors.First(m => m.Id == frame.MonitorId), intended.Monitors, displays);
                    if (target == null) continue;
                    var rect = LayoutResolver.Resolve(frame, target);
                    var raw = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == frame.Id);
                    bool rolled = raw?.IsRolled?.ToString().ToLowerInvariant() == "true";

                    // --- Runtime state ownership ---
                    //
                    // Auto-roll (owned by Framemanager): a frame whose ID is in
                    //   Framemanager._autoRolledFrames is physically short and
                    //   has a collapsed WrapPanel. We must not grow it here, or
                    //   it would sit expanded but empty until a mouse-over.
                    //   That state is intentionally never written to the JSON
                    //   "IsRolled" flag (which carries manual roll intent).
                    //
                    // Docking (owned by SnapManager.CascadeStack): a docked
                    //   child's Y is derived from its parents' bottom edges,
                    //   not stored independently. The layout snapshot's Y for
                    //   such a child is only valid if every parent is in the
                    //   same roll state it had at capture time. When that
                    //   isn't the case — e.g. a parent was captured while
                    //   auto-rolled and is now unrolled — the snapshot Y is
                    //   the rolled Y and applying it would drop the child
                    //   inside the parent's body. RepositionDockedChildren()
                    //   below is the authority for these frames.
                    bool autoRolled = Framemanager.IsFrameAutoRolled(frame.Id);

                    bool parentCurrentlyRolled = false;
                    if (FrameDataManager.DockingMap.TryGetValue(frame.Id, out var parentIds))
                    {
                        foreach (var pid in parentIds)
                        {
                            var pRaw = FrameDataManager.FrameData.FirstOrDefault(f => f.Id?.ToString() == pid);
                            bool pJsonRolled = pRaw?.IsRolled?.ToString().ToLowerInvariant() == "true";
                            if (pJsonRolled || Framemanager.IsFrameAutoRolled(pid))
                            {
                                parentCurrentlyRolled = true;
                                break;
                            }
                        }
                    }

                    window.Width = rect.Width / target.Scale;
                    if (!autoRolled)
                    {
                        window.Height = rolled ? 28 : rect.Height / target.Scale;
                    }

                    // Horizontal position (Left/Width) is always safe: it is
                    // orthogonal to both auto-roll and vertical docking.
                    window.Left = rect.X / target.Scale;

                    // Vertical position: for docked children whose parents are
                    // currently rolled, keep the runtime Y — the post-pass
                    // below will place it correctly. For everything else, use
                    // the snapshot Y.
                    if (!parentCurrentlyRolled)
                    {
                        window.Top = rect.Y / target.Scale;
                    }

                    // Native SetWindowPos is kept because it forces the OS
                    // rect atomically, avoiding a transient frame where the
                    // WPF property has changed but the OS window has not.
                    if (!LayoutDisplays.SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                        (int)Math.Round(rect.X), (int)Math.Round(window.Top * target.Scale),
                        (int)Math.Round(rect.Width), (int)Math.Round(window.Height * target.Scale), 0x0014))
                        throw new InvalidOperationException(Strings.LayoutRestorePositionFailed);
                }

                // Post-pass: enforce the docking cascade's rule for every
                // docked child. This is the single point that guarantees
                // children sit exactly 10px below the lowest bottom edge among
                // their parents — the same rule SnapManager.CascadeStack
                // enforces at runtime. Without this, a stale snapshot Y for a
                // child whose parents' roll state has since changed would
                // leave the frame overlapping its parents.
                RepositionDockedChildren();

                FrameDataManager.SaveFrameData();
            }
            finally { applying = false; }
        }

        /// <summary>
        /// Repositions every frame that appears in FrameDataManager.DockingMap
        /// so its top edge sits exactly 10px below the lowest bottom edge
        /// among its parents. Mirrors SnapManager.CascadeStack's rule and
        /// runs to a fixed point so a child of a moved child also moves.
        ///
        /// This is the authoritative Y-setter for docked frames after a
        /// layout application. It is intentionally independent of the layout
        /// snapshot: the snapshot may have been captured while parents were
        /// auto-rolled, in which case its Y for the child is the rolled Y and
        /// would place the child inside the parent's body.
        /// </summary>
        internal static void RepositionDockedChildren()
        {
            if (FrameDataManager.DockingMap.Count == 0) return;
            var windows = Application.Current.Windows.OfType<NonActivatingWindow>().ToList();

            // Fixed-point iteration. The docking graph is a shallow DAG in
            // practice; a handful of passes suffice. The cap prevents a
            // pathological cycle from spinning here.
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                foreach (var kvp in FrameDataManager.DockingMap)
                {
                    string childId = kvp.Key;
                    var parentIds = kvp.Value;
                    if (parentIds == null || parentIds.Count == 0) continue;

                    var childWindow = windows.FirstOrDefault(w => w.Tag?.ToString() == childId);
                    if (childWindow == null) continue;

                    var activeParents = windows.Where(w => parentIds.Contains(w.Tag?.ToString())).ToList();
                    if (activeParents.Count == 0) continue;

                    double maxParentBottom = activeParents.Max(p => p.Top + p.Height);
                    double targetTop = maxParentBottom + 10.0;

                    if (Math.Abs(childWindow.Top - targetTop) > 0.5)
                    {
                        childWindow.Top = targetTop;
                        moved = true;
                    }
                }
                if (!moved) break;
            }
        }
        private static void OnDisplayChange(object? sender, EventArgs e)
        {
            // Set immediately, even when the UI dispatcher is busy in a native move/size loop.
            paused = true;
            Application.Current?.Dispatcher.BeginInvoke(new Action(Pause));
        }

        private static void Pause()
        {
            paused = true;
            editing.Clear();
            editTopology.Clear();
            SetStatus("LayoutDisplayChanged");
            StartDisplayCheck();
        }

        private static void StartDisplayCheck()
        {
            stableSamples = 0;
            lastTopology = "";
            if (displayTimer == null)
            {
                displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                displayTimer.Tick += (_, _) => CheckDisplays();
            }
            displayTimer.Start();
        }

        private static void CheckDisplays()
        {
            if (!Ready || loading || editing.Count > 0) return;
            try
            {
                var current = ReadDisplays();
                string topology = Topology(current);
                stableSamples = topology == lastTopology ? stableSamples + 1 : 0;
                lastTopology = topology;
                if (stableSamples < 2 || !LayoutResolver.CanRestore(intended!, current)) { paused = true; return; }
                displays = current;
                ApplyWindows();
                paused = false;
                SyncMembership();
                SetStatus("LayoutRecovered");
                displayTimer!.Stop();
            }
            catch (Exception ex) { paused = true; Report(ex); }
        }

        private static void QueueSnapshot()
        {
            snapshotDueUtc = DateTime.UtcNow + SnapshotDelay;
            if (snapshotTimer == null)
            {
                snapshotTimer = new DispatcherTimer { Interval = SnapshotDelay };
                snapshotTimer.Tick += DailyTick;
            }
            snapshotTimer.Stop();
            snapshotTimer.Interval = SnapshotDelay;
            snapshotTimer.Start();
        }

        private static void FlushPendingSnapshot()
        {
            if (!Ready) return;
            try
            {
                // A pending background write must finish before the final checkpoint,
                // otherwise it could replace the latest edits with an older copy.
                try { background.GetAwaiter().GetResult(); } catch (Exception ex) { Report(ex); }
                Store.DailySnapshot(Intended, DateTime.UtcNow);
            }
            catch (Exception ex) { Report(ex); }
        }

        private static void DailyTick(object? sender, EventArgs e)
        {
            if (!Ready || loading || paused || editing.Count > 0 || !background.IsCompleted) return;
            var remaining = snapshotDueUtc - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                // Dispatcher timers may fire a fraction early. Wait only the remaining
                // time instead of accidentally adding another full five seconds.
                if (ReferenceEquals(sender, snapshotTimer) && snapshotTimer != null)
                    snapshotTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, remaining.TotalMilliseconds));
                return;
            }
            snapshotTimer?.Stop();
            var snapshot = intended!.Copy();
            var destination = store!; // Capture profile; a profile switch must not redirect queued writes.
            var destinationProfile = profile;
            bool cleanup = lastCleanupDay != DateTime.Today;
            lastCleanupDay = DateTime.Today;
            var operation = Task.Run(() =>
            {
                bool updated = destination.DailySnapshot(snapshot, DateTime.UtcNow);
                if (cleanup) destination.Cleanup();
                return updated;
            });
            background = operation;
            _ = operation.ContinueWith(t =>
            {
                var error = t.Exception?.GetBaseException();
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error != null) { Report(error); return; }
                    if (!t.Result) return;
                    if (profile == destinationProfile && intended?.Fingerprint() == snapshot.Fingerprint())
                        SetStatus("LayoutSnapshotUpdated");
                    SnapshotsChanged?.Invoke(destinationProfile);
                }));
            });
        }

        public static void SaveManual(string name)
        {
            SyncMembership();
            Store.Snapshot(Intended, "manual", string.IsNullOrWhiteSpace(name) ? Strings.LayoutSavedName : name.Trim());
        }

        public static void UseCurrentArrangement()
        {
            var current = ReadDisplays();
            var replacement = CaptureData(current);
            foreach (var window in Windows)
            {
                var frame = replacement.Frames.FirstOrDefault(f => f.Id == window.Tag?.ToString());
                if (frame != null) CaptureWindow(frame, window, current);
            }
            replacement.Validate();
            if (Ready) Store.Snapshot(Intended, "undo", Strings.LayoutBeforeAdopting);
            Store.SaveIntended(replacement);
            intended = replacement;
            displays = current;
            paused = false;
            displayTimer?.Stop();
            ApplyWindows();
            QueueSnapshot();
            SetStatus("LayoutAdopted");
        }

        public static int Restore(SavedLayout selected)
        {
            selected.Validate();
            var current = ReadDisplays();
            if (!LayoutResolver.CanRestore(selected, current))
                throw new InvalidOperationException(Strings.LayoutRestoreMonitorRequired);
            var replacement = Intended;
            // Preserve current frames/contents; a layout restore never recreates deleted frames.
            int restored = 0;
            foreach (var frame in replacement.Frames.ToList())
            {
                var match = selected.Frames.FirstOrDefault(f => f.Id == frame.Id);
                if (match == null) continue;
                replacement.Frames[replacement.Frames.IndexOf(frame)] = match;
                restored++;
            }
            // Normalize all assignments to the current device identities before combining layouts.
            foreach (var frame in replacement.Frames)
            {
                var origin = selected.Frames.Any(f => f.Id == frame.Id) ? selected : intended!;
                var monitor = LayoutResolver.Match(origin.Monitors.First(m => m.Id == frame.MonitorId), origin.Monitors, current);
                if (monitor == null) throw new InvalidOperationException(Strings.LayoutRestoreMissingMonitor);
                frame.MonitorId = monitor.Id;
            }
            replacement.Monitors = current;
            replacement.Validate();
            Store.Snapshot(Intended, "undo", Strings.Get("LayoutBeforeRestore", selected.Name));
            Store.SaveIntended(replacement);
            intended = replacement;
            displays = current;
            paused = false;
            displayTimer?.Stop();
            ApplyWindows();
            QueueSnapshot();
            SetStatus("LayoutRestored", restored);
            return restored;
        }

        private static void SetStatus(string key, params object[] arguments)
        {
            // Resolve when displayed so switching the app language also updates status text.
            statusKey = key;
            statusArguments = arguments;
        }

        private static void Report(Exception ex)
        {
            SetStatus("LayoutProtectionError", ex.Message);
            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"Layout protection: {ex}");
        }
    }
}
