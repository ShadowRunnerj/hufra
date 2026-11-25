using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hufra.App.Views;
using Hufra.Core.Settings;
using Hufra.Core.Updates;
using Microsoft.Win32;

namespace Hufra.App
{
    public partial class MainWindow : Window
    {
        public const string CurrentVersion = "2.6.2";

        public MainWindow()
        {
            InitializeComponent();
            MainContent.Content = new HomeView();
            LoadProfileIntoUi();
            VersionText.Text = $"النسخة: {CurrentVersion}   |   اللغة: العربية";
            ProviderStatusText.Text = "تطوير: BASSAM DEV";
            Loaded += async (_, __) => await CheckForUpdateAsync();
        }

        private void SidebarDiscord_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://discord.gg/zcZeDZFwsP") { UseShellExecute = true });

        private UpdateInfo _pendingUpdate;

        public async System.Threading.Tasks.Task<bool> CheckForUpdateAsync()
        {
            _pendingUpdate = await UpdateChecker.CheckAsync(CurrentVersion);
            if (_pendingUpdate == null) return false;

            UpdateText.Text = $"🔔 تحديث جديد متوفر! ({_pendingUpdate.Version})";
            UpdateBanner.Visibility = Visibility.Visible;
            return true;
        }

        private async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate == null) return;
            UpdateButton.IsEnabled = false;
            try
            {
                var progress = new Progress<int>(p => UpdateText.Text = $"⏳ جاري تنزيل التحديث... {p}%");
                UpdateText.Text = "⏳ جاري تنزيل التحديث...";
                string installer = await UpdateChecker.DownloadAsync(_pendingUpdate, progress);

                UpdateText.Text = "✅ جاري التثبيت... سيعاد تشغيل حُفرة تلقائياً";
                // /SILENT: تثبيت بدون أسئلة، يغلق النسخة الحالية ويشغّل الجديدة بعد الانتهاء
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer,
                    "/SILENT /CLOSEAPPLICATIONS /NORESTART") { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateButton.IsEnabled = true;
                UpdateText.Text = "🔔 تحديث جديد متوفر!";
                MessageBox.Show($"تعذّر تنزيل التحديث: {ex.Message}", "حُفرة",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadProfileIntoUi()
        {
            var (name, avatarPath) = AppSettingsStore.GetProfile();

            ProfileNameBox.Text = string.IsNullOrWhiteSpace(name) ? "" : name;
            string initial = string.IsNullOrWhiteSpace(name) ? "؟" : name.Trim().Substring(0, 1);
            AvatarInitialText.Text = initial;
            PopupAvatarInitialText.Text = initial;

            if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(avatarPath);
                    bitmap.EndInit();

                    AvatarImage.Source = bitmap;
                    AvatarImage.Visibility = Visibility.Visible;
                    AvatarInitialText.Visibility = Visibility.Collapsed;

                    PopupAvatarImage.Source = bitmap;
                    PopupAvatarImage.Visibility = Visibility.Visible;
                    PopupAvatarInitialText.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    // الصورة تعذّر تحميلها (حُذفت أو تلفت) - نكتفي بالحرف الأول كما هو الوضع الافتراضي
                }
            }
        }

        private void AvatarButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHistoryList();
            ProfilePopup.IsOpen = true;
        }

        private void RefreshHistoryList()
        {
            var history = AppSettingsStore.GetHistory();
            HistoryListBox.Items.Clear();

            if (history.Count == 0)
            {
                HistoryListBox.Items.Add("ما ثبّتّ أي مود لسا");
                return;
            }

            foreach (var item in history)
            {
                HistoryListBox.Items.Add(
                    $"{item.GameName} — {item.TranslatedCount} نص ({item.EngineName})\n{item.Date:yyyy/MM/dd}");
            }
        }

        private void ChangeAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "اختر صورة الملف الشخصي",
                Filter = "صور|*.png;*.jpg;*.jpeg;*.bmp",
            };

            if (dialog.ShowDialog() != true) return;

            // ننسخ الصورة لمجلد بيانات البرنامج عشان تبقى موجودة حتى لو نقل المستخدم أو حذف الصورة الأصلية
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hufra");
            Directory.CreateDirectory(appDataDir);
            string destPath = Path.Combine(appDataDir, "avatar" + Path.GetExtension(dialog.FileName));

            try
            {
                File.Copy(dialog.FileName, destPath, overwrite: true);
                AppSettingsStore.SetProfile(ProfileNameBox.Text, destPath);
                LoadProfileIntoUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذّر حفظ الصورة: {ex.Message}", "حُفرة",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var (_, avatarPath) = AppSettingsStore.GetProfile();
            AppSettingsStore.SetProfile(ProfileNameBox.Text, avatarPath);
            LoadProfileIntoUi();
            ProfilePopup.IsOpen = false;
        }

        /// <summary>يستخدمها أي UserControl داخلي للتنقل لصفحة أخرى برمجياً (مثل زر "ابدأ تعريب لعبة")</summary>
        public void NavigateTo(string tag)
        {
            var target = tag switch
            {
                "Home" => NavHome,
                "Support" => NavSupport,
                "Discord" => NavDiscord,
                "Updates" => NavUpdates,
                "Settings" => NavSettings,
                _ => null,
            };
            if (target != null) target.IsChecked = true; // يشغّل Nav_Checked تلقائياً
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || MainContent == null) return;

            string tag = rb.Tag as string;

            if (PageTitleText != null)
            {
                PageTitleText.Text = tag switch
                {
                    "Home" => "الرئيسية",
                    "Support" => "الدعم والمعلومات",
                    "Discord" => "بوت الديسكورد",
                    "Updates" => "التحديثات والتنزيل",
                    "Settings" => "الإعدادات",
                    _ => PageTitleText.Text,
                };
            }

            MainContent.Content = tag switch
            {
                "Home" => new HomeView(),
                "Support" => new SupportView(),
                "Discord" => new DiscordBotView(),
                "Updates" => new UpdatesView(),
                "Settings" => new SettingsView(),
                _ => MainContent.Content,
            };
        }
    }
}
