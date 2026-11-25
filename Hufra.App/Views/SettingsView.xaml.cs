using System.Windows;
using System.Windows.Controls;
using Hufra.Core.Settings;
using Hufra.Core.Translation;

namespace Hufra.App.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            UpdateStatusText.Text = $"الإصدار الحالي: {MainWindow.CurrentVersion}";
            ShowGeminiState();
        }

        private void ShowGeminiState()
        {
            string k = AppSettingsStore.GetGeminiApiKey();
            GeminiCurrentText.Text = string.IsNullOrWhiteSpace(k) ? "المفتاح: غير مضبوط" :
                "المفتاح الحالي: " + (k.Length > 8 ? k.Substring(0, 4) + "..." + k.Substring(k.Length - 4) : "****");
        }

        private void GeminiPortal_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/api-keys") { UseShellExecute = true });

        private async void GeminiSave_Click(object sender, RoutedEventArgs e)
        {
            string key = GeminiBox.Password.Trim();
            if (key.Length == 0) { GeminiStatusText.Text = "الصق المفتاح أولاً."; return; }
            GeminiStatusText.Text = "⏳ جاري التحقق...";
            try
            {
                var (model, status) = await GeminiModelResolver.ResolveAsync(key);
                if (model == null)
                {
                    GeminiStatusText.Text = status is 400 or 401 or 403
                        ? "❌ المفتاح غير صحيح. انسخه من aistudio.google.com/api-keys."
                        : "❌ تعذّر التحقق (رمز " + status + "). تأكد من الإنترنت.";
                    return;
                }
                AppSettingsStore.SetGeminiApiKey(key);
                GeminiBox.Clear();
                ShowGeminiState();
                GeminiStatusText.Text = "✅ تم الحفظ. النموذج: " + model;
            }
            catch (System.Exception ex) { GeminiStatusText.Text = "❌ " + ex.Message; }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusText.Text = $"الإصدار الحالي: {MainWindow.CurrentVersion}";
            if (Hufra.Core.Updates.UpdateChecker.GetRepo() == null)
            {
                UpdateStatusText.Text += "\nالتحديث التلقائي غير مفعّل (ملف update-repo.txt غير مضبوط).";
                return;
            }
            UpdateStatusText.Text += "\n⏳ جاري التحقق...";
            bool found = await ((MainWindow)Window.GetWindow(this)).CheckForUpdateAsync();
            UpdateStatusText.Text = $"الإصدار الحالي: {MainWindow.CurrentVersion}\n" +
                (found ? "🔔 يوجد تحديث جديد - اضغط «تحديث الآن» أعلى النافذة."
                    : Hufra.Core.Updates.UpdateChecker.LastProblem != null ? "⚠️ " + Hufra.Core.Updates.UpdateChecker.LastProblem : "✅ أنت على آخر إصدار.");
        }
    }
}
