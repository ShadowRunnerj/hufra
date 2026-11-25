using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hufra.Core.Updates;

namespace Hufra.App.Views
{
    public partial class UpdatesView : UserControl
    {
        private List<UpdateInfo> _releases = new List<UpdateInfo>();
        private bool _busy;

        public UpdatesView()
        {
            InitializeComponent();
            CurrentText.Text = $"الإصدار المثبّت: {MainWindow.CurrentVersion}";
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async System.Threading.Tasks.Task LoadAsync()
        {
            StatusText.Text = "⏳ جاري جلب الإصدارات...";
            UpdateLatestButton.Visibility = Visibility.Collapsed;
            ReleasesPanel.Children.Clear();
            _releases = await UpdateChecker.ListAsync();

            if (_releases.Count == 0)
            {
                StatusText.Text = "⚠️ " + (UpdateChecker.LastProblem ?? "ما لقيت إصدارات.");
                return;
            }

            Version.TryParse(MainWindow.CurrentVersion, out var cur);
            var latest = _releases[0];
            if (cur != null && latest.Version > cur)
            {
                StatusText.Text = $"🔔 يوجد إصدار أحدث: {latest.Version}";
                UpdateLatestButton.Visibility = Visibility.Visible;
            }
            else StatusText.Text = "✅ أنت على آخر إصدار.";

            foreach (var r in _releases)
            {
                var row = new Border { Style = (Style)FindResource("CardStyle"), Margin = new Thickness(0, 0, 0, 8) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string tag = r == latest ? " (الأحدث)" : "";
                if (cur != null && r.Version == cur) tag += " — مثبّت";
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = "الإصدار " + r.Version + tag, FontWeight = FontWeights.Bold, FontSize = 14 });
                if (!string.IsNullOrWhiteSpace(r.Notes))
                    info.Children.Add(new TextBlock { Text = r.Notes.Trim(), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.8 });
                Grid.SetColumn(info, 0); grid.Children.Add(info);

                var btn = new Button
                {
                    Content = (cur != null && r.Version == cur) ? "إعادة تثبيت" : (cur != null && r.Version < cur ? "تثبيت (نسخة أقدم)" : "⬇ تنزيل وتثبيت"),
                    Style = (Style)FindResource("SecondaryButtonStyle"), Tag = r, VerticalAlignment = VerticalAlignment.Center,
                };
                btn.Click += Install_Click;
                Grid.SetColumn(btn, 1); grid.Children.Add(btn);
                row.Child = grid;
                ReleasesPanel.Children.Add(row);
            }
        }

        private void UpdateLatest_Click(object sender, RoutedEventArgs e)
        {
            if (_releases.Count > 0) _ = InstallAsync(_releases[0]);
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: UpdateInfo info }) _ = InstallAsync(info);
        }

        private async System.Threading.Tasks.Task InstallAsync(UpdateInfo info)
        {
            if (_busy) return;
            _busy = true;
            Progress.Value = 0; Progress.Visibility = Visibility.Visible;
            StatusText.Text = $"⏳ جاري تنزيل {info.Version}...";
            try
            {
                var p = new Progress<int>(v => { Progress.Value = v; StatusText.Text = $"⏳ جاري تنزيل {info.Version}... {v}%"; });
                string installer = await UpdateChecker.DownloadAsync(info, p);
                StatusText.Text = "✅ تم التنزيل — جاري التثبيت، سيعاد تشغيل حُفرة تلقائياً";
                Process.Start(new ProcessStartInfo(installer, "/SILENT /CLOSEAPPLICATIONS /NORESTART") { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ تعذّر التنزيل: " + ex.Message;
                Progress.Visibility = Visibility.Collapsed;
                _busy = false;
            }
        }
    }
}
