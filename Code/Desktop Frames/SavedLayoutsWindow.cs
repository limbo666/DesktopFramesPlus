using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames.Layouts
{
    public sealed class SavedLayoutsWindow : Window
    {
        private const int PageSize = 20;
        private static SavedLayoutsWindow? _instance;
        private readonly string _profile = ProfileManager.CurrentProfileDir;
        private readonly LayoutStore _store = LayoutManager.Store;
        private readonly ListBox _snapshotList = new() { MinWidth = 260 };
        private readonly LayoutPreviewCanvas _preview = new();
        private readonly TextBlock _status = CreateParagraph();
        private readonly TextBlock _details = CreateParagraph();
        private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        private readonly TextBox _snapshotName = new() { Width = 240, Margin = new Thickness(0, 0, 8, 0), Text = Strings.LayoutMyLayout };
        private readonly TextBox _retentionDays = CreateNumberInput();
        private readonly TextBox _retentionCount = CreateNumberInput();
        private List<LayoutEntry> _entries = new();
        private SavedLayout? _selectedLayout;
        private LayoutEntry? _selectedEntry;
        private int _page;
        private int _selectionVersion;
        private bool _busy;
        private bool _refreshPending;
        private bool _closed;

        private sealed record SnapshotRow(LayoutEntry Entry, SavedLayout? Layout);

        public static void ShowWindow(Window? owner = null)
        {
            if (!LayoutManager.Ready)
            {
                MessageBox.Show(LayoutManager.Status, Strings.LayoutSnapshots);
                return;
            }
            if (_instance != null && _instance._profile != ProfileManager.CurrentProfileDir)
                _instance.Close();

            if (_instance == null)
            {
                _instance = new SavedLayoutsWindow { Owner = owner };
                _instance.Show();
            }
            else
            {
                _instance.Activate();
            }
        }

        public SavedLayoutsWindow()
        {
            Title = Strings.Get("LayoutWindowTitle", ProfileManager.CurrentProfileName);
            Width = 1040;
            Height = 810;
            MinWidth = 850;
            MinHeight = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            Content = CreateContent();
            _snapshotList.SelectionChanged += OnSelectionChanged;
            LayoutManager.SnapshotsChanged += OnSnapshotsChanged;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private DockPanel CreateContent()
        {
            var content = new DockPanel { Margin = new Thickness(20) };
            var header = CreateHeader();
            var footer = CreateRetentionControls();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            content.Children.Add(header);
            content.Children.Add(footer);
            content.Children.Add(CreateBrowser());
            return content;
        }

        private StackPanel CreateHeader()
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = Strings.LayoutSnapshots, FontSize = 25, FontWeight = FontWeights.SemiBold });
            _status.Text = LayoutManager.Status;
            header.Children.Add(_status);
            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            actions.Children.Add(_snapshotName);
            actions.Children.Add(CreateButton(Strings.LayoutSaveSnapshot, SaveSnapshotAsync));
            actions.Children.Add(CreateButton(Strings.LayoutUseCurrent, UseCurrentArrangementAsync));
            header.Children.Add(actions);
            return header;
        }

        private StackPanel CreateRetentionControls()
        {
            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            footer.Children.Add(CreateParagraph(Strings.LayoutAutomaticHelp));
            var limits = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            limits.Children.Add(CreateNumberSetting(Strings.LayoutRetentionDays, _retentionDays));
            limits.Children.Add(CreateNumberSetting(Strings.LayoutRetentionCount, _retentionCount));
            limits.Children.Add(CreateParagraph(Strings.LayoutNoLimit, muted: true));
            footer.Children.Add(limits);
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            actions.Children.Add(CreateButton(Strings.LayoutApplyRetention, ApplyRetentionAsync));
            actions.Children.Add(CreateButton(Strings.LayoutDeleteOlder, DeleteOlderAsync));
            footer.Children.Add(actions);
            footer.Children.Add(CreateParagraph(Strings.LayoutRetentionHelp, muted: true));
            return footer;
        }

        private Grid CreateBrowser()
        {
            var browser = new Grid();
            browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
            var navigation = new WrapPanel();
            navigation.Children.Add(CreateButton(Strings.LayoutPrevious, () => ChangePageAsync(-1)));
            navigation.Children.Add(_pageLabel);
            navigation.Children.Add(CreateButton(Strings.LayoutNext, () => ChangePageAsync(1)));
            DockPanel.SetDock(navigation, Dock.Bottom);
            left.Children.Add(navigation);
            left.Children.Add(_snapshotList);
            browser.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(_preview);
            right.Children.Add(_details);
            var actions = new WrapPanel();
            actions.Children.Add(CreateButton(Strings.LayoutRestore, RestoreSnapshotAsync));
            actions.Children.Add(CreateButton(Strings.LayoutPinToggle, TogglePinAsync));
            actions.Children.Add(CreateButton(Strings.LayoutDelete, DeleteSelectedAsync));
            right.Children.Add(actions);
            right.Children.Add(CreateParagraph(Strings.LayoutRestoreHelp, muted: true));
            var scroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(scroll, 1);
            browser.Children.Add(scroll);
            return browser;
        }

        private static TextBlock CreateParagraph(string text = "", bool muted = false) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted ? Brushes.DimGray : Brushes.Black,
            Margin = new Thickness(0, 10, 0, 10)
        };

        private static TextBox CreateNumberInput() => new()
        {
            Width = 64,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private static StackPanel CreateNumberSetting(string text, TextBox input)
        {
            var setting = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 8) };
            setting.Children.Add(new Label { Content = text, Target = input, VerticalAlignment = VerticalAlignment.Center });
            setting.Children.Add(input);
            return setting;
        }

        private Button CreateButton(string text, Func<Task> action)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 6, 6)
            };
            button.Click += async (_, _) => await RunAsync(action);
            return button;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                var settings = await Task.Run(_store.Retention);
                _retentionDays.Text = settings.Days.ToString();
                _retentionCount.Text = settings.Count.ToString();
                await RefreshAsync();
            });
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _closed = true;
            LayoutManager.SnapshotsChanged -= OnSnapshotsChanged;
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => await SelectAsync();

        private async void OnSnapshotsChanged(string changedProfile)
        {
            if (_closed || _profile != changedProfile || _profile != ProfileManager.CurrentProfileDir)
                return;
            if (_busy)
            {
                _refreshPending = true;
                return;
            }
            await RunAsync(RefreshAsync);
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_busy || _closed)
                return;
            _busy = true;
            try
            {
                do
                {
                    _refreshPending = false;
                    EnsureCurrentProfile();
                    await action();
                    action = RefreshAsync;
                }
                while (_refreshPending && !_closed);
                if (!_closed)
                    _status.Text = LayoutManager.Status;
            }
            catch (Exception ex)
            {
                if (!_closed)
                    MessageBox.Show(this, ex.Message, Strings.LayoutSnapshots, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        private void EnsureCurrentProfile()
        {
            if (_profile != ProfileManager.CurrentProfileDir)
                throw new InvalidOperationException(Strings.LayoutProfileChanged);
        }

        private bool Confirm(string text) =>
            MessageBox.Show(this, text, Strings.LayoutSnapshots, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

        private async Task RefreshAsync()
        {
            string? selectedFile = _selectedEntry?.FileName;
            _entries = await Task.Run(_store.List);
            _page = Math.Clamp(_page, 0, Math.Max(0, (_entries.Count - 1) / PageSize));
            var visible = _entries.Skip(_page * PageSize).Take(PageSize).ToList();
            var rows = await Task.Run(() => visible.Select(ReadRow).ToList());
            if (_closed)
                return;
            EnsureCurrentProfile();
            _snapshotList.Items.Clear();
            foreach (var row in rows)
                _snapshotList.Items.Add(CreateListItem(row));
            int pageCount = Math.Max(1, (_entries.Count + PageSize - 1) / PageSize);
            _pageLabel.Text = Strings.Get("LayoutPage", _page + 1, pageCount);
            _details.Text = _entries.Count == 0 ? Strings.LayoutNoSnapshots : Strings.LayoutSelectSnapshot;
            if (visible.Count > 0)
                _snapshotList.SelectedIndex = Math.Max(0, visible.FindIndex(entry => entry.FileName == selectedFile));
        }

        private SnapshotRow ReadRow(LayoutEntry entry)
        {
            try
            {
                return new SnapshotRow(entry, _store.Read(entry.FileName));
            }
            catch
            {
                // One damaged file must not prevent browsing other snapshots.
                return new SnapshotRow(entry, null);
            }
        }

        private static ListBoxItem CreateListItem(SnapshotRow row)
        {
            string title = SnapshotTitle(row.Layout, row.Entry);
            if (row.Entry.Pinned)
                title = Strings.Get("LayoutPinnedTitle", title);
            DateTime saved = row.Layout?.UpdatedUtc ?? row.Layout?.CreatedUtc ?? row.Entry.CreatedUtc;
            var label = new TextBlock
            {
                Text = Strings.Get("LayoutListItem", title, saved.ToLocalTime(), SnapshotKind(row.Entry.Kind)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5),
                MaxWidth = 255
            };
            return new ListBoxItem { Content = label, Tag = row.Entry };
        }

        private static string SnapshotTitle(SavedLayout? layout, LayoutEntry entry)
        {
            if (layout == null)
                return Strings.LayoutUnreadable;
            if (entry.Kind != "auto")
                return layout.Name; // Preserve names entered by the user.
            bool today = !entry.Pinned && entry.CreatedUtc.ToLocalTime().Date == DateTime.Today;
            return today ? Strings.LayoutAutomaticToday : Strings.LayoutAutomatic;
        }

        private static string SnapshotKind(string kind) => kind switch
        {
            "auto" => Strings.LayoutKindAutomatic,
            "manual" => Strings.LayoutKindManual,
            "undo" => Strings.LayoutKindSafety,
            _ => kind
        };

        private async Task SelectAsync()
        {
            int version = ++_selectionVersion;
            _selectedLayout = null;
            _selectedEntry = (_snapshotList.SelectedItem as ListBoxItem)?.Tag as LayoutEntry;
            _preview.ShowLayout(null);
            var entry = _selectedEntry;
            if (entry == null)
                return;
            try
            {
                var layout = await Task.Run(() => _store.Read(entry.FileName));
                if (_closed || version != _selectionVersion)
                    return;
                EnsureCurrentProfile();
                _selectedLayout = layout;
                _details.Text = Strings.Get("LayoutDetails", SnapshotTitle(layout, entry),
                    (layout.UpdatedUtc ?? layout.CreatedUtc).ToLocalTime(), layout.Frames.Count,
                    layout.Monitors.Count, MonitorStatus(layout));
                _preview.ShowLayout(layout);
            }
            catch (Exception ex)
            {
                if (!_closed && version == _selectionVersion)
                    _details.Text = Strings.Get("LayoutPreviewUnavailable", ex.Message);
            }
        }

        private static string MonitorStatus(SavedLayout layout)
        {
            var current = LayoutDisplays.Read();
            var missing = layout.Monitors
                .Where(monitor => LayoutResolver.Match(monitor, layout.Monitors, current) == null)
                .Select(monitor => monitor.Name)
                .ToList();
            if (missing.Count > 0)
                return Strings.Get("LayoutMissingMonitors", string.Join(", ", missing));
            return LayoutResolver.CanRestore(layout, current) ? Strings.LayoutMonitorsAvailable : Strings.LayoutMonitorMismatch;
        }

        private async Task SaveSnapshotAsync()
        {
            LayoutManager.SaveManual(_snapshotName.Text);
            _page = 0;
            await RefreshAsync();
        }

        private async Task UseCurrentArrangementAsync()
        {
            if (!Confirm(Strings.LayoutConfirmCurrent))
                return;
            LayoutManager.UseCurrentArrangement();
            await RefreshAsync();
        }

        private async Task ChangePageAsync(int direction)
        {
            int target = _page + direction;
            if (target < 0 || target * PageSize >= _entries.Count)
                return;
            _page = target;
            await RefreshAsync();
        }

        private async Task RestoreSnapshotAsync()
        {
            var layout = _selectedLayout;
            var entry = _selectedEntry;
            if (layout == null || entry == null || !Confirm(Strings.Get("LayoutConfirmRestore", SnapshotTitle(layout, entry))))
                return;
            LayoutManager.Restore(layout);
            await RefreshAsync();
        }

        private async Task TogglePinAsync()
        {
            var entry = _selectedEntry;
            if (entry == null)
                return;
            await Task.Run(() => _store.Pin(entry.FileName, !entry.Pinned));
            await RefreshAsync();
        }

        private async Task DeleteSelectedAsync()
        {
            var entry = _selectedEntry;
            if (entry == null)
                return;
            if (entry.Pinned)
                throw new InvalidOperationException(Strings.LayoutUnpinBeforeDelete);
            if (!Confirm(Strings.LayoutConfirmDelete))
                return;
            await Task.Run(() => _store.Delete(entry.FileName));
            await RefreshAsync();
        }

        private LayoutRetention ReadRetention()
        {
            if (!int.TryParse(_retentionDays.Text, out int days) || !int.TryParse(_retentionCount.Text, out int count) ||
                days < 0 || days > 36500 || count < 0 || count > 100000)
                throw new InvalidOperationException(Strings.LayoutInvalidRetention);
            return new LayoutRetention { Days = days, Count = count };
        }

        private async Task ApplyRetentionAsync()
        {
            var settings = ReadRetention();
            var candidates = await Task.Run(() => _store.CleanupCandidates(settings, DateTime.UtcNow));
            if (_closed || !Confirm(Strings.Get("LayoutConfirmRetention", candidates.Count)))
                return;
            await Task.Run(() =>
            {
                _store.SetRetention(settings);
                _store.Cleanup();
            });
            await RefreshAsync();
        }

        private async Task DeleteOlderAsync()
        {
            var settings = ReadRetention();
            if (settings.Days == 0)
                throw new InvalidOperationException(Strings.LayoutEnterAge);
            settings.Count = 0;
            var candidates = await Task.Run(() => _store.CleanupCandidates(settings, DateTime.UtcNow));
            if (_closed)
                return;
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, Strings.LayoutNoOldSnapshots, Strings.LayoutSnapshots);
                return;
            }
            if (!Confirm(Strings.Get("LayoutConfirmDeleteOlder", candidates.Count, settings.Days)))
                return;
            await Task.Run(() =>
            {
                foreach (var entry in candidates)
                    _store.Delete(entry.FileName);
            });
            await RefreshAsync();
        }
    }
}
