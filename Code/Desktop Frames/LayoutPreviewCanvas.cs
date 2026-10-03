using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Desktop_Frames.Layouts
{
    /// <summary>Draws a layout diagram without accessing profiles, snapshots or the desktop.</summary>
    internal sealed class LayoutPreviewCanvas : Canvas
    {
        private SavedLayout? _layout;

        internal LayoutPreviewCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 29, 39));
            ClipToBounds = true;
            Height = 320;
            SizeChanged += (_, _) => DrawLayout();
        }

        internal void ShowLayout(SavedLayout? layout)
        {
            _layout = layout;
            DrawLayout();
        }

        private void DrawLayout()
        {
            Children.Clear();
            if (_layout == null || _layout.Monitors.Count == 0 || ActualWidth < 32)
                return;

            double left = _layout.Monitors.Min(monitor => monitor.Left);
            double top = _layout.Monitors.Min(monitor => monitor.Top);
            double width = _layout.Monitors.Max(monitor => monitor.Left + monitor.Width) - left;
            double height = _layout.Monitors.Max(monitor => monitor.Top + monitor.Height) - top;
            double scale = Math.Min((ActualWidth - 32) / width, (Height - 44) / height);
            double offsetX = (ActualWidth - width * scale) / 2;
            double offsetY = (Height - height * scale) / 2;
            foreach (var monitor in _layout.Monitors)
            {
                double x = offsetX + (monitor.Left - left) * scale;
                double y = offsetY + (monitor.Top - top) * scale;
                DrawMonitor(monitor, x, y, scale);
                foreach (var frame in _layout.Frames.Where(frame => frame.MonitorId == monitor.Id))
                    DrawFrame(frame, monitor, x, y, scale);
            }
        }

        private void DrawMonitor(LayoutMonitor monitor, double x, double y, double scale)
        {
            var outline = new Rectangle
            {
                Width = monitor.Width * scale,
                Height = monitor.Height * scale,
                Stroke = Brushes.SlateGray,
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromRgb(37, 47, 62))
            };
            Place(outline, x, y);
            var label = new TextBlock
            {
                Text = monitor.Name,
                Foreground = Brushes.White,
                FontSize = 11,
                MaxWidth = monitor.Width * scale,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Place(label, x + 3, y + monitor.Height * scale + 3);
        }

        private void DrawFrame(LayoutFrame frame, LayoutMonitor monitor, double x, double y, double scale)
        {
            double width = Math.Max(3, frame.Width * monitor.Scale * scale);
            double height = Math.Max(3, frame.Height * monitor.Scale * scale);
            var tile = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromArgb(210, 65, 130, 196)),
                BorderBrush = Brushes.LightSkyBlue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                ToolTip = frame.Title,
                ClipToBounds = true
            };
            if (width > 24 && height > 12)
            {
                tile.Child = new TextBlock
                {
                    Text = frame.Title,
                    FontSize = 9,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(2)
                };
            }
            Place(tile, x + frame.X * monitor.Width * scale, y + frame.Y * monitor.Height * scale);
        }

        private void Place(UIElement element, double x, double y)
        {
            SetLeft(element, x);
            SetTop(element, y);
            Children.Add(element);
        }
    }
}
