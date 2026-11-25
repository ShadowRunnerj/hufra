using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Hufra.Core.Settings;

namespace Hufra.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // نمنع الإغلاق التلقائي مؤقتاً لحين ما تفتح النافذة الرئيسية فعلياً،
            // لأن اللحظة اللي تسكر فيها شاشة الترحيب (WelcomeWindow) ما فيها
            // أي نافذة مفتوحة بعد، وModeOnLastWindowClose الافتراضي كان يسكر
            // البرنامج بأكمله بهذي اللحظة قبل ما نوصل لفتح النافذة الرئيسية
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // نجبر الرندر البرمجي بدل الاعتماد على تعريف كرت الشاشة، لتفادي
            // مشاكل تجمّد معروفة مع بعض تعريفات كروت الشاشة القديمة/المدمجة.
            // التأثير على الأداء غير محسوس لواجهة بسيطة زي هذي.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // نظهر أي خطأ غير متوقع برسالة واضحة بدل ما يتجمد البرنامج بصمت
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    $"صار خطأ غير متوقع:\n\n{ex.Exception.Message}",
                    "حُفرة - خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // الإعداد الإجباري: دخول الديسكورد ثم مفتاح Gemini. لو أُغلقت النافذة بدون إكمال نخرج.
            if (!AppSettingsStore.GetDiscordJoined() || string.IsNullOrWhiteSpace(AppSettingsStore.GetGeminiApiKey()))
            {
                var onboarding = new OnboardingWindow();
                if (onboarding.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // أول مرة يفتح فيها البرنامج (ما فيه اسم محفوظ)، نطلب الاسم والصورة مرة وحدة
            var (name, _) = AppSettingsStore.GetProfile();
            if (string.IsNullOrWhiteSpace(name))
            {
                var welcome = new WelcomeWindow();
                welcome.ShowDialog();
            }

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
    }
}
