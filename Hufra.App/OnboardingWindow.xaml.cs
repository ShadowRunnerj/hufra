using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Hufra.Core.Settings;
using Hufra.Core.Translation;

namespace Hufra.App
{
    public partial class OnboardingWindow : Window
    {
        public const string DiscordUrl = "https://discord.gg/zcZeDZFwsP";
        public const string GeminiKeysUrl = "https://aistudio.google.com/api-keys";

        public OnboardingWindow()
        {
            InitializeComponent();
            // لو الديسكورد مكتمل من قبل (وناقص المفتاح فقط) نبدأ من الخطوة 2
            if (AppSettingsStore.GetDiscordJoined()) ShowStep2();
        }

        private static void Open(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            Open(DiscordUrl);
            JoinedCheck.IsEnabled = true; // لا يُفعّل التأكيد إلا بعد فتح رابط السيرفر
        }
        private void OpenGemini_Click(object sender, RoutedEventArgs e) => Open(GeminiKeysUrl);

        private void JoinedCheck_Changed(object sender, RoutedEventArgs e) =>
            Step1NextButton.IsEnabled = JoinedCheck.IsChecked == true;

        private void Step1Next_Click(object sender, RoutedEventArgs e)
        {
            AppSettingsStore.SetDiscordJoined(true);
            if (!string.IsNullOrWhiteSpace(AppSettingsStore.GetGeminiApiKey()))
            {
                DialogResult = true;
                Close();
                return;
            }
            ShowStep2();
        }

        private void ShowStep2()
        {
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Visible;
        }

        private async void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            string key = KeyBox.Text.Trim();
            if (key.Length < 20)
            {
                SetStatus("الصق المفتاح كامل أول.", false);
                return;
            }

            SaveKeyButton.IsEnabled = false;
            SetStatus("⏳ جاري التحقق من المفتاح...", null);

            string error = await TranslationService.ValidateGeminiKeyAsync(key);
            SaveKeyButton.IsEnabled = true;

            if (error != null)
            {
                SetStatus("❌ " + error, false);
                return;
            }

            AppSettingsStore.SetGeminiApiKey(key);
            DialogResult = true;
            Close();
        }

        private void SetStatus(string text, bool? ok)
        {
            KeyStatusText.Text = text;
            KeyStatusText.Foreground = (Brush)FindResource(
                ok == false ? "DangerBrush" : "BrownPrimaryBrush");
        }
    }
}
