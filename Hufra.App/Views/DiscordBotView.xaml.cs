using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hufra.Core.Discord;
using Hufra.Core.Settings;

namespace Hufra.App.Views
{
    public partial class DiscordBotView : UserControl
    {
        // الحالة تبقى حتى لو انتقل المستخدم لصفحة ثانية
        private static DiscordApi _api;
        private static string _botId, _botName, _guildId, _guildName;
        private static readonly List<(string Role, string Text)> History = new();
        private static DiscordPlan _pending;
        private static readonly List<(bool User, string Text)> Transcript = new();
        private CancellationTokenSource _cts;
        private bool _busy;

        private static string _session, _clientId;
        private static bool _autoTried;
        private static CancellationTokenSource _waitCts;

        public DiscordBotView()
        {
            InitializeComponent();
            foreach (var (user, text) in Transcript) AddBubble(user, text, store: false);
            if (Transcript.Count == 0)
                AddBubble(false, "أهلاً! سجّل دخولك بديسكورد من الأعلى، واختر سيرفرك، وبعدها اكتب لي اللي تبيه. أمثلة:\n• سوّ لي سيرفر متجر: رومات ترحيب وقوانين وإعلانات وشات عام وصوت\n• سوّ رتب: مالك، إدارة، مشرف، عضو\n• سوّ رومات: شات عام، صور، أخبار\n• احذف روم اسم", store: false);
            UpdateConnectionUi();
            PlanPanel.Visibility = _pending != null ? Visibility.Visible : Visibility.Collapsed;
            // دخول تلقائي بالجلسة المحفوظة (صالحة 8 ساعات)
            if (!Connected && !_autoTried && !string.IsNullOrEmpty(AppSettingsStore.GetDiscordBotToken()) && DiscordLogin.BackendUrl() != null)
            {
                _autoTried = true;
                Loaded += async (_, __) => await ConnectAsync(fresh: false);
            }
        }

        private bool Connected => _api != null && _guildId != null;

        // ---------- الاتصال ----------
        private void UpdateConnectionUi()
        {
            if (Connected)
            {
                ConnectStatus.Text = $"🟢 متصل بسيرفر «{_guildName}»";
                SetupPanel.Visibility = Visibility.Collapsed;
                ConnectButton.Visibility = Visibility.Collapsed;
                DisconnectButton.Visibility = Visibility.Visible;
                ChangeGuildButton.Visibility = Visibility.Visible;
            }
            else
            {
                if (ConnectStatus.Text == "" || ConnectStatus.Text.StartsWith("🟢")) ConnectStatus.Text = "⚪ غير متصل — سجّل دخولك بديسكورد";
                SetupPanel.Visibility = Visibility.Visible;
                ConnectButton.Visibility = Visibility.Visible;
                DisconnectButton.Visibility = Visibility.Collapsed;
                ChangeGuildButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e) => _ = ConnectAsync(fresh: true);

        private async System.Threading.Tasks.Task ConnectAsync(bool fresh)
        {
            string backend = DiscordLogin.BackendUrl();
            if (backend == null)
            {
                ConnectStatus.Text = "⚠️ خادم حُفرة الوسيط غير مضبوط (ملف backend.txt). راجع المطوّر.";
                return;
            }

            ConnectButton.IsEnabled = false;
            try
            {
                LoginResult login = null;
                string saved = AppSettingsStore.GetDiscordBotToken();
                if (!fresh && !string.IsNullOrEmpty(saved))
                {
                    ConnectStatus.Text = "⏳ جاري استعادة الجلسة...";
                    try { login = await DiscordLogin.RefreshAsync(backend, saved); } catch (DiscordApiException) { login = null; }
                }
                if (login == null)
                {
                    ConnectStatus.Text = "⏳ افتح المتصفح ووافق على تسجيل الدخول بديسكورد...";
                    login = await DiscordLogin.LoginAsync(backend, DiscordLogin.ClientIdFallback);
                }
                _session = login.Session; _clientId = login.ClientId;
                AppSettingsStore.SetDiscordBotToken(_session);   // هنا يُحفظ «جلسة» المستخدم (مشفّرة بـ DPAPI) وليس توكن البوت

                await ChooseGuildAsync(login, backend, pickAgain: fresh);
            }
            catch (DiscordApiException ex) { ConnectStatus.Text = "❌ " + ex.Message; }
            finally { ConnectButton.IsEnabled = true; }
        }

        private async System.Threading.Tasks.Task ChooseGuildAsync(LoginResult login, string backend, bool pickAgain)
        {
            if (login.Guilds.Count == 0)
            {
                ConnectStatus.Text = $"⚠️ أهلاً {login.UserName}، ما لقيت سيرفر تملكه في حسابك. أنشئ سيرفر في ديسكورد أو سجّل بحساب مالك السيرفر.";
                return;
            }

            OwnedGuild guild;
            string savedId = AppSettingsStore.GetDiscordGuildId();
            var keep = login.Guilds.FirstOrDefault(g => g.Id == savedId);
            if (login.Guilds.Count == 1) guild = login.Guilds[0];
            else if (keep != null && !pickAgain) guild = keep;
            else
            {
                var dlg = new GuildPickerWindow(login.Guilds) { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() != true) { ConnectStatus.Text = "⚪ ما اخترت سيرفر."; return; }
                guild = dlg.Selected;
            }

            if (!guild.BotIn)
            {
                ConnectStatus.Text = $"➕ أضف البوت لسيرفر «{guild.Name}» من الصفحة اللي انفتحت (وافق على كل الصلاحيات)… أنتظرك.";
                Process.Start(new ProcessStartInfo(DiscordLogin.InviteUrl(login.ClientId, guild.Id)) { UseShellExecute = true });
                _waitCts?.Cancel(); _waitCts = new CancellationTokenSource();
                var ct = _waitCts.Token;
                for (int i = 0; i < 60 && !ct.IsCancellationRequested; i++)
                {
                    await System.Threading.Tasks.Task.Delay(3000);
                    try
                    {
                        var r = await DiscordLogin.RefreshAsync(backend, _session);
                        if (r.Guilds.Any(g => g.Id == guild.Id && g.BotIn)) { guild = guild with { BotIn = true }; break; }
                    }
                    catch (DiscordApiException) { }
                }
                if (!guild.BotIn) { ConnectStatus.Text = "⚠️ ما انضاف البوت. أضفه ثم اضغط «تسجيل الدخول» مرة ثانية."; return; }
            }

            var api = new DiscordApi(backend, _session);
            try
            {
                var me = await api.GetMeAsync();
                _botId = me.Id; _botName = me.Name;
                await api.GetGuildNameAsync(guild.Id); // يتأكد أن البوت يشوف السيرفر
            }
            catch { api.Dispose(); throw; }

            _api?.Dispose();
            _api = api; _guildId = guild.Id; _guildName = guild.Name;
            AppSettingsStore.SetDiscordGuildId(guild.Id);
            UpdateConnectionUi();
            AddBubble(false, $"أهلاً {login.UserName}! اتصلت بسيرفر «{guild.Name}». وش تبي أسوي؟");
        }

        private void ChangeGuild_Click(object sender, RoutedEventArgs e) => _ = ChangeGuildAsync();

        private async System.Threading.Tasks.Task ChangeGuildAsync()
        {
            string backend = DiscordLogin.BackendUrl();
            if (backend == null || string.IsNullOrEmpty(_session)) return;
            try
            {
                var login = await DiscordLogin.RefreshAsync(backend, _session);
                _pending = null; PlanPanel.Visibility = Visibility.Collapsed;
                await ChooseGuildAsync(login, backend, pickAgain: true);
            }
            catch (DiscordApiException ex) { ConnectStatus.Text = "❌ " + ex.Message; }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _waitCts?.Cancel();
            _api?.Dispose(); _api = null; _guildId = null; _pending = null; _session = null;
            AppSettingsStore.SetDiscordBotToken(null);
            ConnectStatus.Text = "";
            PlanPanel.Visibility = Visibility.Collapsed;
            UpdateConnectionUi();
        }

        // ---------- الشات ----------
        private void AddBubble(bool user, string text, bool store = true)
        {
            if (store) Transcript.Add((user, text));
            var bubble = new Border
            {
                Background = user ? (Brush)FindResource("BrownPrimaryBrush") : new SolidColorBrush(Color.FromRgb(0xF7, 0xF0, 0xE6)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(user ? 60 : 0, 4, user ? 0 : 60, 4),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = user ? Brushes.White : (Brush)FindResource("BrownDarkestBrush"),
                },
            };
            ChatPanel.Children.Add(bubble);
            ChatScroll.ScrollToEnd();
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter = إرسال، Shift+Enter = سطر جديد
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                Send_Click(sender, null);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SendButton.IsEnabled = !busy;
            ExecuteButton.IsEnabled = !busy;
            SendButton.Content = busy ? "⏳..." : "إرسال ➤";
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text.Trim();
            if (text.Length == 0 || _busy) return;
            if (!Connected) { AddBubble(false, "اربط البوت بالسيرفر أولاً (الإطار العلوي)."); return; }

            InputBox.Clear();
            AddBubble(true, text);
            History.Add(("user", text));
            _pending = null;
            PlanPanel.Visibility = Visibility.Collapsed;
            SetBusy(true);
            _cts = new CancellationTokenSource();
            try
            {
                var roles = await _api.GetRolesAsync(_guildId, _cts.Token);
                var channels = await _api.GetChannelsAsync(_guildId, _cts.Token);
                DiscordPlan plan;
                if (string.IsNullOrWhiteSpace(AppSettingsStore.GetGeminiApiKey()))
                    plan = LocalPlanner.Plan(text, roles.Select(r => r.Name), channels.Select(c => c.Name));
                else
                {
                    try { plan = await DiscordPlanner.PlanAsync(DiscordPlanner.DescribeStructure(roles, channels), History, _cts.Token); }
                    catch (InvalidOperationException gex)
                    {
                        // Gemini مشغول/غير متاح: نكمل بالمخطِّط المحلي بدل ما نوقف
                        plan = LocalPlanner.Plan(text, roles.Select(r => r.Name), channels.Select(c => c.Name));
                        AddBubble(false, "⚠️ " + gex.Message + " — جرّبت أفهم طلبك محلياً بدون Gemini.");
                    }
                }

                if (plan.Actions.Count == 0 && !string.IsNullOrWhiteSpace(AppSettingsStore.GetGeminiApiKey()))
                {
                    var alt = LocalPlanner.Plan(text, roles.Select(r => r.Name), channels.Select(c => c.Name));
                    if (alt.Actions.Count > 0) plan = alt;
                }
                string reply = plan.Message ?? "";
                if (plan.Actions.Count == 0)
                {
                    AddBubble(false, string.IsNullOrWhiteSpace(reply) ? "ما فهمت طلبك، وضّح أكثر؟" : reply);
                    History.Add(("model", reply));
                }
                else
                {
                    string preview = plan.Preview();
                    var warnings = plan.Warnings();
                    string msg = reply + "\n\nالخطة (" + plan.Actions.Count + " إجراء):\n" + preview +
                                 (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings) : "") +
                                 "\n\nما نُفّذ شيء بعد. اضغط «✅ تنفيذ الخطة» أو عدّل طلبك.";
                    AddBubble(false, msg);
                    History.Add(("model", reply + " [خطة: " + plan.Actions.Count + " إجراء، لم تُنفَّذ بعد]"));
                    _pending = plan;
                    PlanPanel.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (DiscordApiException ex) { AddBubble(false, "❌ " + ex.Message); }
            catch (Exception ex) { AddBubble(false, "❌ " + ex.Message); }
            finally { SetBusy(false); }
        }

        private void CancelPlan_Click(object sender, RoutedEventArgs e)
        {
            _pending = null;
            PlanPanel.Visibility = Visibility.Collapsed;
            AddBubble(false, "تم إلغاء الخطة. ما تغيّر شيء في السيرفر.");
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (_pending == null || !Connected || _busy) return;
            var plan = _pending;

            if (plan.HasDestructive)
            {
                var dels = string.Join("\n", plan.Actions.Where(a => a.IsDestructive).Select(a => a.Describe()));
                var r = MessageBox.Show("الخطة فيها حذف لا يمكن التراجع عنه:\n\n" + dels + "\n\nمتأكد؟", "تأكيد الحذف",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (r != MessageBoxResult.Yes) return;
            }

            _pending = null;
            PlanPanel.Visibility = Visibility.Collapsed;
            SetBusy(true);
            AddBubble(false, "⏳ جاري التنفيذ...");
            _cts = new CancellationTokenSource();
            try
            {
                var exec = new DiscordExecutor(_api, _guildId, _botId);
                var lines = new List<string>();
                var result = await exec.ExecuteAsync(plan, l => lines.Add(l), _cts.Token);
                string summary = $"انتهى التنفيذ: نجح {result.Succeeded}، فشل {result.Failed}.";
                AddBubble(false, summary + "\n\n" + string.Join("\n", lines) +
                    (result.Failed > 0 ? "\n\nلو الفشل بسبب الصلاحيات: ارفع رتبة البوت فوق الرتب المطلوبة من إعدادات السيرفر." : ""));
                History.Add(("model", "[نُفّذت الخطة: نجح " + result.Succeeded + "، فشل " + result.Failed + "]"));
            }
            catch (OperationCanceledException) { AddBubble(false, "أُوقف التنفيذ."); }
            catch (Exception ex) { AddBubble(false, "❌ " + ex.Message); }
            finally { SetBusy(false); }
        }
    }
}
