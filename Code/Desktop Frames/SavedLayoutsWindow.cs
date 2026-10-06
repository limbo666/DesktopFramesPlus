using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Desktop_Frames.Layouts
{
    public sealed class SavedLayoutsWindow : Window
    {
        private static SavedLayoutsWindow? _instance;
        private readonly string _profile = ProfileManager.CurrentProfileDir;
        private readonly LayoutStore _store = LayoutManager.Store;

        private readonly ListBox _snapshotList = new() { BorderThickness = new Thickness(0), Margin = new Thickness(5) };
        private readonly LayoutPreviewCanvas _preview = new();
        private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
        private readonly TextBox _snapshotName = new() { Height = 25, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };
        private readonly TextBox _retentionDays = new() { Width = 60, Height = 25, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };
        private readonly TextBox _retentionCount = new() { Width = 60, Height = 25, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };

        private List<LayoutEntry> _entries = new();
        private SavedLayout? _selectedLayout;
        private LayoutEntry? _selectedEntry;
        private int _selectionVersion;
        private bool _busy;
        private bool _refreshPending;
        private bool _closed;

        private sealed record SnapshotRow(LayoutEntry Entry, SavedLayout? Layout);

        public static void ShowWindow(Window? owner = null)
        {
            if (!LayoutManager.Ready)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(LayoutManager.Status, Strings.LayoutSnapshots);
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
            // 1. Enforce Borderless Window Style
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // FIX: Using the solid color from ItemMoveDialog. This creates the thick visual frame!
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
            ResizeMode = ResizeMode.NoResize;

            Width = 980;
            Height = 740;

            // 2. DPI-Aware Mouse Screen Centering Setup
            WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += CenterOnActiveMonitor;

            Content = CreateMainContainer();

            _snapshotList.SelectionChanged += OnSelectionChanged;
            LayoutManager.SnapshotsChanged += OnSnapshotsChanged;
            Closed += OnClosed;
        }

        private void CenterOnActiveMonitor(object sender, RoutedEventArgs e)
        {
            // Get mouse position to determine the active screen
            var mousePos = System.Windows.Forms.Control.MousePosition;
            var screen = System.Windows.Forms.Screen.FromPoint(mousePos);

            // Apply DPI transformation matrix
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double screenX = screen.WorkingArea.Left / dpiX;
            double screenY = screen.WorkingArea.Top / dpiY;
            double screenWidth = screen.WorkingArea.Width / dpiX;
            double screenHeight = screen.WorkingArea.Height / dpiY;

            Left = screenX + (screenWidth - Width) / 2;
            Top = screenY + (screenHeight - Height) / 2;

            // Trigger initial load and fetch retention settings
            _ = RunAsync(async () =>
            {
                var settings = await Task.Run(_store.Retention);
                _retentionDays.Text = settings.Days.ToString();
                _retentionCount.Text = settings.Count.ToString();
                await RefreshAsync();
            });
        }

        private Border CreateMainContainer()
        {
            // Matched EXACTLY to ItemMoveDialog
            Border mainBorder = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(8), // Over the 248,249,250 window background, this is the visual frame
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 2,
                    BlurRadius = 8,
                    Opacity = 0.2
                }
            };

            var dock = new DockPanel();

            // Custom Title Bar
            dock.Children.Add(CreateTitleBar());

            // Bottom Action Bar
            dock.Children.Add(CreateFooter());

            // Two-Pane Content Area
            dock.Children.Add(CreateBrowser());

            mainBorder.Child = dock;
            return mainBorder;
        }

        private UIElement CreateTitleBar()
        {
            var titleBar = new Grid
            {
                Height = 45,
                Background = new SolidColorBrush(Color.FromRgb(85, 85, 85)) // Dark Gray matching Smart Desktop
            };
            DockPanel.SetDock(titleBar, Dock.Top);

            // Enable dragging
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 1) DragMove(); };

            var titleText = new TextBlock
            {
                Text = Strings.LayoutSnapshots,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            titleBar.Children.Add(titleText);

            var closeButton = new Button
            {
                Content = "✕",
                Width = 45,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) => Close();
            closeButton.MouseEnter += (s, e) => closeButton.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red hover
            closeButton.MouseLeave += (s, e) => closeButton.Background = Brushes.Transparent;

            titleBar.Children.Add(closeButton);
            return titleBar;
        }

        private UIElement CreateFooter()
        {
            var footer = new Border
            {
                Height = 65,
                Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            DockPanel.SetDock(footer, Dock.Bottom);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 15, 0) };

            var btnClose = CreateStyledButton(Strings.DlgClose ?? "Close", Color.FromRgb(108, 117, 125), 140);
            btnClose.Click += (s, e) => Close();

            btnPanel.Children.Add(btnClose);
            footer.Child = btnPanel;
            return footer;
        }

        private Grid CreateBrowser()
        {
            var grid = new Grid { Margin = new Thickness(15) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); // Left List Pane
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });  // Gap
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right Preview Pane

            // --- LEFT PANE (Snapshots) ---
            var leftStack = new StackPanel();

            var scroll = new ScrollViewer { Content = _snapshotList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var listGroup = CreateGroupBox(Strings.LayoutSnapshots, 410, scroll);
            leftStack.Children.Add(listGroup);

            // Left Pane Actions (Pin / Delete)
            var actionGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnPin = CreateStyledButton(Strings.LayoutPinToggle, Color.FromRgb(41, 74, 122), 150); // Navy
            btnPin.HorizontalAlignment = HorizontalAlignment.Left;
            btnPin.Click += async (s, e) => await RunAsync(TogglePinAsync);

            var btnDelete = CreateStyledButton(Strings.LayoutDelete, Color.FromRgb(220, 53, 69), 150); // Red
            btnDelete.HorizontalAlignment = HorizontalAlignment.Right;
            btnDelete.Click += async (s, e) => await RunAsync(DeleteSelectedAsync);

            Grid.SetColumn(btnPin, 0); actionGrid.Children.Add(btnPin);
            Grid.SetColumn(btnDelete, 1); actionGrid.Children.Add(btnDelete);
            leftStack.Children.Add(actionGrid);

            // Restore Button (Moved to Left Pane, full width)
            var btnRestore = CreateStyledButton(Strings.LayoutRestore, Color.FromRgb(34, 139, 34), double.NaN, 45); // Green, Stretch Width
            btnRestore.HorizontalAlignment = HorizontalAlignment.Stretch;
            btnRestore.Margin = new Thickness(0, 10, 0, 0);
            btnRestore.Click += async (s, e) => await RunAsync(RestoreSnapshotAsync);
            leftStack.Children.Add(btnRestore);

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // --- RIGHT PANE (Preview & Details/Save) ---
            var rightStack = new StackPanel();

            _preview.Margin = new Thickness(10);
            var previewGroup = CreateGroupBox(Strings.LayoutPreviewTitle ?? "Monitor Layout Preview", 350, _preview);
            rightStack.Children.Add(previewGroup);

            // Bottom Right Split Grid (Details vs Save)
            var rightBottomGrid = new Grid { Margin = new Thickness(0, 15, 0, 0) };
            rightBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Details column
            rightBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) }); // Gap
            rightBottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Save column

            // Column 0: Details
            _details.Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            var detailsGroup = CreateGroupBox(Strings.LayoutDetailsTitle ?? "Snapshot Details", 100, _details);
            Grid.SetColumn(detailsGroup, 0);
            rightBottomGrid.Children.Add(detailsGroup);

            // Column 2: Save New Snapshot Section
            var savePanel = new Grid { Margin = new Thickness(10) };
            savePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            savePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            _snapshotName.Text = Strings.LayoutMyLayout;
            _snapshotName.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_snapshotName, 0);
            savePanel.Children.Add(_snapshotName);

            var btnSave = CreateStyledButton(Strings.LayoutBtnSave ?? "Save", Color.FromRgb(34, 139, 34), 70, 30);
            btnSave.Margin = new Thickness(10, 0, 0, 0);
            btnSave.VerticalAlignment = VerticalAlignment.Center;
            btnSave.Click += async (s, e) => await RunAsync(SaveSnapshotAsync);
            Grid.SetColumn(btnSave, 1);
            savePanel.Children.Add(btnSave);

            var saveGroup = CreateGroupBox(Strings.LayoutSaveSnapshot, 100, savePanel); // Matches height of Details
            Grid.SetColumn(saveGroup, 2);
            rightBottomGrid.Children.Add(saveGroup);

            rightStack.Children.Add(rightBottomGrid);

            // Retention Settings Group (Spans the bottom of the right pane)
            var retentionPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(15, 10, 15, 10) };

            var valuesGrid = new Grid();
            valuesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Days
            valuesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Count

            var daysStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            daysStack.Children.Add(new TextBlock { Text = Strings.LayoutRetentionDays, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)) });
            daysStack.Children.Add(_retentionDays);
            Grid.SetColumn(daysStack, 0);
            valuesGrid.Children.Add(daysStack);

            var countStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            countStack.Children.Add(new TextBlock { Text = Strings.LayoutRetentionCount, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)) });
            countStack.Children.Add(_retentionCount);
            Grid.SetColumn(countStack, 1);
            valuesGrid.Children.Add(countStack);

            retentionPanel.Children.Add(valuesGrid);

            // Maroon Button placed below the values
            var btnApplyRetention = CreateStyledButton(Strings.LayoutApplyRetention, Color.FromRgb(128, 0, 0), 160, 30); // Maroon color
            btnApplyRetention.HorizontalAlignment = HorizontalAlignment.Right;
            btnApplyRetention.Margin = new Thickness(0, 12, 0, 0); // Space between textboxes and button
            btnApplyRetention.Click += async (s, e) => await RunAsync(ApplyRetentionAsync);
            retentionPanel.Children.Add(btnApplyRetention);

            // Increased height to 110 to accommodate the two stacked rows
            var retentionGroup = CreateGroupBox(Strings.LayoutCleanupSettings ?? "Cleanup Settings", 100, retentionPanel);
            retentionGroup.Margin = new Thickness(0, 15, 0, 0);
            rightStack.Children.Add(retentionGroup);

            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            return grid;
        }

        // --- UI HELPERS ---

        private static Grid CreateGroupBox(string title, double height, UIElement content)
        {
            var container = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Height = height,
                Background = Brushes.White,
                Child = content // Assign the passed content directly here
            };

            var label = new TextBlock
            {
                Text = " " + title + " ",
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, -10, 0, 0)
            };

            var grid = new Grid();
            grid.Children.Add(container);
            grid.Children.Add(label);
            return grid; // Return the fully assembled composition
        }

        private Button CreateStyledButton(string text, Color baseColor, double width = 120, double height = 45)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = height,
                Background = new SolidColorBrush(baseColor),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromRgb((byte)(Math.Min(255, baseColor.R + 30)), (byte)(Math.Min(255, baseColor.G + 30)), (byte)(Math.Min(255, baseColor.B + 30))));
            btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(baseColor);
            return btn;
        }

        // --- DATA & LOGIC HANDLING ---

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
            if (_closed || _profile != changedProfile || _profile != ProfileManager.CurrentProfileDir) return;
            if (_busy) { _refreshPending = true; return; }
            await RunAsync(RefreshAsync);
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_busy || _closed) return;
            _busy = true;
            try
            {
                do
                {
                    _refreshPending = false;
                    if (_profile != ProfileManager.CurrentProfileDir) throw new InvalidOperationException(Strings.LayoutProfileChanged);
                    await action();
                    action = RefreshAsync;
                }
                while (_refreshPending && !_closed);
            }
            catch (Exception ex)
            {
                if (!_closed) MessageBoxesManager.ShowOKOnlyMessageBoxForm(ex.Message, Strings.LayoutSnapshots);
            }
            finally
            {
                _busy = false;
            }
        }

        private bool Confirm(string text) =>
                 MessageBoxesManager.ShowCustomYesNoMessageBox(text, Strings.LayoutSnapshots);

        private async Task RefreshAsync()
        {
            string? selectedFile = _selectedEntry?.FileName;
            _entries = await Task.Run(_store.List);

            // Dropped pagination. Load top 150 items to keep UI snappy while showing everything relevant.
            var visible = _entries.Take(150).ToList();
            var rows = await Task.Run(() => visible.Select(ReadRow).ToList());

            if (_closed) return;
            if (_profile != ProfileManager.CurrentProfileDir) throw new InvalidOperationException(Strings.LayoutProfileChanged);

            _snapshotList.Items.Clear();
            foreach (var row in rows) _snapshotList.Items.Add(CreateListItem(row));

            _details.Text = _entries.Count == 0 ? Strings.LayoutNoSnapshots : Strings.LayoutSelectSnapshot;
            if (visible.Count > 0)
                _snapshotList.SelectedIndex = Math.Max(0, visible.FindIndex(entry => entry.FileName == selectedFile));
        }

        private SnapshotRow ReadRow(LayoutEntry entry)
        {
            try { return new SnapshotRow(entry, _store.Read(entry.FileName)); }
            catch { return new SnapshotRow(entry, null); }
        }

        private static ListBoxItem CreateListItem(SnapshotRow row)
        {
            string title = SnapshotTitle(row.Layout, row.Entry);
            if (row.Entry.Pinned) title = Strings.Get("LayoutPinnedTitle", title);
            DateTime saved = row.Layout?.UpdatedUtc ?? row.Layout?.CreatedUtc ?? row.Entry.CreatedUtc;

            var label = new TextBlock
            {
                Text = Strings.Get("LayoutListItem", title, saved.ToLocalTime(), SnapshotKind(row.Entry.Kind)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5),
                MaxWidth = 280
            };
            return new ListBoxItem { Content = label, Tag = row.Entry };
        }

        private static string SnapshotTitle(SavedLayout? layout, LayoutEntry entry)
        {
            if (layout == null) return Strings.LayoutUnreadable;
            if (entry.Kind != "auto") return layout.Name;
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
            if (entry == null) return;

            try
            {
                var layout = await Task.Run(() => _store.Read(entry.FileName));
                if (_closed || version != _selectionVersion) return;

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
            var missing = layout.Monitors.Where(monitor => LayoutResolver.Match(monitor, layout.Monitors, current) == null).Select(monitor => monitor.Name).ToList();
            if (missing.Count > 0) return Strings.Get("LayoutMissingMonitors", string.Join(", ", missing));
            return LayoutResolver.CanRestore(layout, current) ? Strings.LayoutMonitorsAvailable : Strings.LayoutMonitorMismatch;
        }

        private async Task SaveSnapshotAsync()
        {
            LayoutManager.SaveManual(_snapshotName.Text);
            await RefreshAsync();
        }

        private async Task UseCurrentArrangementAsync()
        {
            if (!Confirm(Strings.LayoutConfirmCurrent)) return;
            LayoutManager.UseCurrentArrangement();
            await RefreshAsync();
        }

        private async Task RestoreSnapshotAsync()
        {
            var layout = _selectedLayout;
            var entry = _selectedEntry;
            if (layout == null || entry == null || !Confirm(Strings.Get("LayoutConfirmRestore", SnapshotTitle(layout, entry)))) return;
            LayoutManager.Restore(layout);
            await RefreshAsync();
        }

        private async Task TogglePinAsync()
        {
            var entry = _selectedEntry;
            if (entry == null) return;
            await Task.Run(() => _store.Pin(entry.FileName, !entry.Pinned));
            await RefreshAsync();
        }

        private async Task DeleteSelectedAsync()
        {
            var entry = _selectedEntry;
            if (entry == null) return;
            if (entry.Pinned) throw new InvalidOperationException(Strings.LayoutUnpinBeforeDelete);
            if (!Confirm(Strings.LayoutConfirmDelete)) return;
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
            if (_closed || !Confirm(Strings.Get("LayoutConfirmRetention", candidates.Count))) return;

            await Task.Run(() =>
            {
                _store.SetRetention(settings);
                _store.Cleanup();
            });
            await RefreshAsync();
        }
    }
}