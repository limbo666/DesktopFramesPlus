using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Desktop_Frames
{
    public class SearchFormManager : Window
    {
        private const int ResultLimit = 21;
        private static SearchFormManager? _instance;

        private readonly SpotSearchIndex _index;
        private readonly Dictionary<SpotSearchFrame, string> _frameLabels = new();
        private readonly Dictionary<SpotSearchTab, string> _tabLabels = new();
        private readonly Dictionary<(string Path, bool IsFolder), ImageSource> _icons = new();
        private readonly TextBox _searchBox;
        private readonly TextBlock _watermark;
        private readonly ComboBox _frameFilter;
        private readonly ComboBox _tabFilter;
        private readonly ComboBox _typeFilter;
        private readonly StackPanel _resultsPanel = new();
        private readonly ScrollViewer _scrollViewer;
        private readonly TextBlock _status;

        private List<SpotSearchEntry> _displayedResults = new();
        private bool _isClosing;
        private bool _updatingTabChoices;

        private sealed record FilterChoice<T>(string Label, T Value);

        public static void ToggleSearch()
        {
            if (_instance == null || !_instance.IsLoaded || _instance._isClosing)
            {
                _instance = new SearchFormManager();
                _instance.Show();
                _instance.Activate();
                _instance.FocusSearch();
            }
            else
            {
                _instance.SafeClose();
            }
        }

        public SearchFormManager()
        {
            _index = new SpotSearchIndex(Framemanager.GetFrameData());
            Title = Strings.SearchTitle;
            Width = Math.Min(600, SystemParameters.WorkArea.Width);
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            _watermark = new TextBlock
            {
                Text = Strings.SearchPlaceholder,
                FontSize = 18,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _searchBox = new TextBox
            {
                FontSize = 18,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black
            };
            AutomationProperties.SetName(_searchBox, Strings.SearchPlaceholder);

            var searchContainer = new Grid { Height = 44, Margin = new Thickness(12, 0, 12, 0) };
            searchContainer.Children.Add(_watermark);
            searchContainer.Children.Add(_searchBox);

            _frameFilter = new ComboBox();
            _tabFilter = new ComboBox();
            _typeFilter = new ComboBox();
            var filters = new UniformGrid { Columns = 3, Margin = new Thickness(8, 0, 8, 8) };
            filters.Children.Add(CreateFilterColumn(_frameFilter, "SpotSearchFilterFrame"));
            filters.Children.Add(CreateFilterColumn(_tabFilter, "SpotSearchFilterTab"));
            filters.Children.Add(CreateFilterColumn(_typeFilter, "SpotSearchFilterType"));

            _scrollViewer = new ScrollViewer
            {
                Content = _resultsPanel,
                MaxHeight = Math.Clamp((SystemParameters.WorkArea.Height - 168) / 2, 48, 396),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(8, 4, 8, 4),
                Visibility = Visibility.Collapsed
            };
            _status = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 7, 12, 7),
                Foreground = Brushes.DimGray
            };

            var content = new StackPanel();
            content.Children.Add(searchContainer);
            content.Children.Add(filters);
            content.Children.Add(new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
            });
            content.Children.Add(_scrollViewer);
            content.Children.Add(_status);
            Content = new Border
            {
                Child = content,
                Background = new SolidColorBrush(Color.FromRgb(252, 252, 252)),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                Effect = new DropShadowEffect { BlurRadius = 25, ShadowDepth = 10, Opacity = 0.4 }
            };

            InitializeFilters();
            RefreshResults();

            _searchBox.TextChanged += (_, _) => RefreshResults();
            _searchBox.PreviewKeyDown += OnSearchKeyDown;
            _frameFilter.SelectionChanged += (_, _) =>
            {
                UpdateTabChoices();
                RefreshResults();
            };
            _tabFilter.SelectionChanged += (_, _) =>
            {
                if (!_updatingTabChoices) RefreshResults();
            };
            _typeFilter.SelectionChanged += (_, _) => RefreshResults();
            PreviewKeyDown += OnWindowKeyDown;
            Deactivated += (_, _) => SafeClose();
        }

        public void FocusSearch() => _searchBox.Focus();

        private void SafeClose()
        {
            if (_isClosing) return;
            _isClosing = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _isClosing = true;
            if (_instance == this) _instance = null;
        }

        private static StackPanel CreateFilterColumn(ComboBox filter, string labelKey)
        {
            string label = Strings.Get(labelKey);
            filter.MinHeight = 28;
            filter.DisplayMemberPath = "Label";
            AutomationProperties.SetName(filter, label);

            var column = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            column.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 3)
            });
            column.Children.Add(filter);
            return column;
        }

        private void InitializeFilters()
        {
            var frames = _index.Entries.Select(entry => entry.Frame).Distinct();
            foreach (var group in frames.GroupBy(
                frame => frame.Title ?? Strings.Get("SpotSearchUnnamedFrame"), StringComparer.OrdinalIgnoreCase))
            {
                bool duplicate = group.Count() > 1;
                foreach (var frame in group)
                    _frameLabels[frame] = duplicate ? Strings.Get("SpotSearchNumberedLabel", group.Key, frame.Index) : group.Key;
            }

            var tabs = _index.Entries.Select(entry => entry.Tab).OfType<SpotSearchTab>().Distinct();
            foreach (var group in tabs.GroupBy(tab => (tab.Frame, Title: tab.Title ?? Strings.Get("SpotSearchUnnamedTab"))))
            {
                bool duplicate = group.Count() > 1;
                foreach (var tab in group)
                    _tabLabels[tab] = duplicate ? Strings.Get("SpotSearchNumberedLabel", group.Key.Title, tab.Index) : group.Key.Title;
            }

            var frameChoices = new List<FilterChoice<SpotSearchFrame?>>
            {
                new(Strings.Get("SpotSearchAllFrames"), null)
            };
            frameChoices.AddRange(_frameLabels.OrderBy(pair => pair.Key.Index)
                .Select(pair => new FilterChoice<SpotSearchFrame?>(pair.Value, pair.Key)));
            _frameFilter.ItemsSource = frameChoices;
            _frameFilter.SelectedIndex = 0;
            _frameFilter.IsEnabled = frameChoices.Count > 1;

            var typeChoices = new List<FilterChoice<SpotSearchItemType?>>
            {
                new(Strings.Get("SpotSearchAllTypes"), null)
            };
            typeChoices.AddRange(Enum.GetValues<SpotSearchItemType>()
                .Select(type => new FilterChoice<SpotSearchItemType?>(TypeLabel(type), type)));
            _typeFilter.ItemsSource = typeChoices;
            _typeFilter.SelectedIndex = 0;

            UpdateTabChoices();
        }

        private void UpdateTabChoices()
        {
            var frame = (_frameFilter.SelectedItem as FilterChoice<SpotSearchFrame?>)?.Value;
            var previousTab = (_tabFilter.SelectedItem as FilterChoice<SpotSearchTab?>)?.Value;
            var choices = new List<FilterChoice<SpotSearchTab?>>
            {
                new(Strings.Get("SpotSearchAllTabs"), null)
            };
            foreach (var pair in _tabLabels.OrderBy(pair => pair.Key.Frame.Index).ThenBy(pair => pair.Key.Index))
            {
                if (frame != null && pair.Key.Frame != frame) continue;
                string label = frame == null ? FormatLocation(pair.Key.Frame, pair.Key) : pair.Value;
                choices.Add(new FilterChoice<SpotSearchTab?>(label, pair.Key));
            }

            _updatingTabChoices = true;
            _tabFilter.ItemsSource = choices;
            _tabFilter.IsEnabled = choices.Count > 1;
            _tabFilter.SelectedItem = choices.FirstOrDefault(choice => choice.Value == previousTab) ?? choices[0];
            _updatingTabChoices = false;
        }

        private void RefreshResults()
        {
            string query = _searchBox.Text;
            _watermark.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var frame = (_frameFilter.SelectedItem as FilterChoice<SpotSearchFrame?>)?.Value;
            var tab = (_tabFilter.SelectedItem as FilterChoice<SpotSearchTab?>)?.Value;
            var type = (_typeFilter.SelectedItem as FilterChoice<SpotSearchItemType?>)?.Value;
            bool active = !string.IsNullOrWhiteSpace(query) || frame != null || tab != null || type != null;

            _displayedResults = _index.Find(query, frame, tab, type).Take(ResultLimit + 1).ToList();
            bool moreResults = _displayedResults.Count > ResultLimit;
            if (moreResults) _displayedResults.RemoveAt(ResultLimit);

            _resultsPanel.Children.Clear();
            foreach (var entry in _displayedResults)
                _resultsPanel.Children.Add(CreateResultButton(entry));
            _scrollViewer.ScrollToTop();
            _scrollViewer.Visibility = _displayedResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _status.Text = !active ? "Desktop Frames +"
                : _displayedResults.Count == 0 ? Strings.Get("SpotSearchNoResults")
                : moreResults ? Strings.Get("SpotSearchResultLimit", ResultLimit)
                : "";
            _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Button CreateResultButton(SpotSearchEntry entry)
        {
            string location = FormatLocation(entry.Frame, entry.Tab);
            string type = TypeLabel(entry.Type);
            bool isFolder = entry.Type == SpotSearchItemType.Folder;
            var key = (entry.Path, isFolder);
            if (!_icons.TryGetValue(key, out var icon))
            {
                icon = Utility.GetShellIcon(entry.Path, isFolder)
                    ?? new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                _icons[key] = icon;
            }

            var row = new DockPanel();
            var image = new Image { Source = icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);

            var category = new TextBlock
            {
                Text = type,
                Foreground = Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(category, Dock.Right);
            row.Children.Add(category);

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            labels.Children.Add(new TextBlock
            {
                Text = location,
                Foreground = Brushes.DimGray,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(labels);

            var button = new Button
            {
                Content = row,
                MinHeight = 46,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                ToolTip = $"{location}\n{entry.Path}"
            };
            AutomationProperties.SetName(button, $"{entry.Name}, {location}, {type}");
            button.Click += (_, _) => LaunchResult(entry);
            return button;
        }

        private string FormatLocation(SpotSearchFrame frame, SpotSearchTab? tab) =>
            tab == null ? _frameLabels[frame] : $"{_frameLabels[frame]} / {_tabLabels[tab]}";

        private static string TypeLabel(SpotSearchItemType type) => Strings.Get(type switch
        {
            SpotSearchItemType.Folder => "SpotSearchTypeFolder",
            SpotSearchItemType.Link => "SpotSearchTypeLink",
            _ => "SpotSearchTypeFileOrApp"
        });

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _frameFilter.IsDropDownOpen ||
                _tabFilter.IsDropDownOpen || _typeFilter.IsDropDownOpen) return;
            e.Handled = true;
            SafeClose();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _displayedResults.Count == 0) return;
            e.Handled = true;
            LaunchResult(_displayedResults[0]);
        }

        private void LaunchResult(SpotSearchEntry entry)
        {
            try
            {
                string finalPath = entry.Path;
                bool isWebAddress = SpotSearchIndex.IsWebAddress(finalPath);
                bool isFolder = entry.Type == SpotSearchItemType.Folder;
                bool isUncRoot = isFolder && Framemanager.IsUncRoot(finalPath);
                if (!isWebAddress && !isUncRoot && !File.Exists(finalPath) && !(isFolder && Directory.Exists(finalPath)))
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                        Strings.Get("MsgFileNotFoundInProfile", finalPath), Strings.DlgLaunchError);
                    return;
                }

                if (!isWebAddress && !isUncRoot) finalPath = Path.GetFullPath(finalPath);
                string extension = isWebAddress || isUncRoot ? "" : Path.GetExtension(finalPath);
                bool isShortcut = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                bool isStoreApp = isShortcut && Utility.IsStoreAppShortcut(finalPath);
                var startInfo = new ProcessStartInfo { UseShellExecute = true };

                if (isStoreApp || isFolder)
                {
                    startInfo.FileName = "explorer.exe";
                    startInfo.ArgumentList.Add(finalPath);
                }
                else
                {
                    startInfo.FileName = finalPath;
                    if (!isWebAddress && !isShortcut && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        startInfo.WorkingDirectory = Path.GetDirectoryName(finalPath) ?? "";
                }

                Process.Start(startInfo);
                SafeClose();
            }
            catch (Exception ex)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgLaunchFailed", ex.Message), Strings.DlgError);
            }
        }
    }
}
