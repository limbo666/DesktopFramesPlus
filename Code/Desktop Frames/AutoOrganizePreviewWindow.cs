using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Desktop_Frames
{
    internal sealed class AutoOrganizePreviewWindow : Window
    {
        internal AutoOrganizePreviewWindow(Window owner, IEnumerable<OrganizeRule> rules)
        {
            Owner = owner;
            Title = Strings.Get("AutoOrgPreviewTitle");
            Width = 880;
            Height = 480;
            MinWidth = 520;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var status = new TextBlock
            {
                Text = Strings.Get("AutoOrgPreviewLoading"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                status, System.Windows.Automation.AutomationLiveSetting.Polite);

            var table = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };
            System.Windows.Automation.AutomationProperties.SetName(table, Title);
            table.Columns.Add(PreviewColumn(Strings.ColName, nameof(PreviewRow.FileName), 1.2));
            table.Columns.Add(PreviewColumn(Strings.Get("AutoOrgPreviewRule"), nameof(PreviewRow.RuleName), 1));
            table.Columns.Add(PreviewColumn(Strings.Get("AutoOrgPreviewDestination"), nameof(PreviewRow.DestinationPath), 2));
            table.Columns.Add(PreviewColumn(Strings.Get("AutoOrgPreviewAction"), nameof(PreviewRow.Action), 1.4));

            var closeButton = new Button
            {
                Content = Strings.BtnClose,
                IsCancel = true,
                MinWidth = 96,
                Padding = new Thickness(15, 5, 15, 5),
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var panel = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(status, Dock.Top);
            DockPanel.SetDock(closeButton, Dock.Bottom);
            panel.Children.Add(status);
            panel.Children.Add(closeButton);
            panel.Children.Add(table);
            Content = panel;

            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            Closed += (s, e) =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            };
            ContentRendered += async (s, e) =>
            {
                try
                {
                    var plans = await AutoOrganizeManager.PreviewDesktopAsync(rules, token);
                    if (token.IsCancellationRequested) return;

                    table.ItemsSource = plans.Select(plan => new PreviewRow(
                        Path.GetFileName(plan.SourcePath), plan.Rule.Name, plan.DestinationPath,
                        Strings.Get("AutoOrgPreview" + plan.Action))).ToList();

                    status.Text = plans.Count == 0
                        ? Strings.Get("AutoOrgPreviewEmpty")
                        : Strings.Get("AutoOrgPreviewSummary", plans.Count, plans.Count(plan => plan.WillMove));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        status.Text = Strings.Get("AutoOrgPreviewFailed", ex.Message);
                }
            };
        }

        private static DataGridTextColumn PreviewColumn(string header, string property, double width)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));

            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star),
                ElementStyle = style
            };
        }

        private sealed record PreviewRow(string FileName, string RuleName, string? DestinationPath, string Action);
    }
}
