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

        public DiscordBotView()
        {
            InitializeComponent();
            foreach (var (user, text) in Transcript) AddBubble(user, text, store: false);
            if (Transcript.Count == 0)
                AddBubble(false, "أهلاً! الصق توكن البوت واضغط اتصال، وبعدها اكتب لي اللي تبيه. أمثلة:\n• سوّ لي سيرفر متجر: رومات ترحيب وقوانين وإعلانات وشات عام وصوت\n• سوّ رتب: مالك، إدارة، مشرف، عضو بألوان مختلفة\n• سوّ روم خاص للإدارة فقط\n• خلّ روم الإعلانات للقراءة فقط", store: false);
            UpdateConnectionUi();
            PlanPanel.Visibility = _pending != null ? Visibility.Visible : Visibility.Collapsed;
            // اتصال تلقائي: التوكن مضمّن مع التطبيق (bot-token.txt) أو محفوظ من قبل
            if (!Connected && !_autoTried && (BundledToken().Length > 0 || !string.IsNullOrEmpty(AppSettingsStore.GetDiscordBotToken())))
            {
                _autoTried = true;
                Loaded += async (_, __) => await ConnectAsync();
            }
        }

        private bool Connected => _api != null && _guildId != null;

        // ---------- الاتصال ----------
        private void UpdateConnectionUi()
        {
            if (Connected)
            {
                ConnectStatus.Text = $"🟢 متصل: البوت «{_botName}» ← السيرفر «{_guildName}»";
                SetupPanel.Visibility = Visibility.Collapsed;
                ConnectButton.Visibility = Visibility.Collapsed;
                DisconnectButton.Visibility = Visibility.Visible;
                InviteButton.IsEnabled = true;
            }
            else
            {
                ConnectStatus.Text = "⚪ غير متصل — الصق توكن البوت واضغط اتصال";
                SetupPanel.Visibility = Visibility.Visible;
                ConnectButton.Visibility = Visibility.Visible;
                DisconnectButton.Visibility = Visibility.Collapsed;
                InviteButton.IsEnabled = _botId != null;
            }
        }

        private static string BundledToken()
        {
            try
            {
                string f = System.IO.Path.Combine(AppContext.BaseDirectory, "bot-token.txt");
                if (!System.IO.File.Exists(f)) return "";
                return System.IO.File.ReadAllLines(f).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 20 && !l.StartsWith("#")) ?? "";
            }
            catch { return ""; }
        }

        private static bool _autoTried;

        private void Connect_Click(object sender, RoutedEventArgs e) => _ = ConnectAsync();

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            string token = TokenBox.Password.Trim();
            if (token.Length == 0) token = BundledToken();
            if (token.Length == 0) token = AppSettingsStore.GetDiscordBotToken() ?? "";
            if (token.Length == 0)
            {
                ConnectStatus.Text = "⚠️ الصق توكن البوت.";
                return;
            }

            ConnectButton.IsEnabled = false;
            ConnectStatus.Text = "⏳ جاري الاتصال...";
            DiscordApi api = null;
            try
            {
                api = new DiscordApi(token);
                var me = await api.GetMeAsync();
                _botId = me.Id; _botName = me.Name;
                InviteButton.IsEnabled = true;
                var guilds = await api.GetMyGuildsAsync();
                if (guilds.Count == 0)
                {
                    ConnectStatus.Text = $"⚠️ التوكن صحيح (البوت «{_botName}») لكن البوت مو داخل أي سيرفر. اضغط «➕ إضافة البوت للسيرفر» ووافق، ثم اضغط اتصال مرة ثانية.";
                    api.Dispose();
                    return;
                }
                string guild = guilds[0].Id, name = guilds[0].Name;
                string saved = AppSettingsStore.GetDiscordGuildId();
                var keep = guilds.FirstOrDefault(g => g.Id == saved);
                if (keep.Id != null) { guild = keep.Id; name = keep.Name; }
                else if (guilds.Count > 1)
                {
                    var dlg = new GuildPickerWindow(guilds) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() != true) { api.Dispose(); ConnectStatus.Text = "⚪ أُلغي الاتصال."; return; }
                    guild = dlg.Selected.Id; name = dlg.Selected.Name;
                }
                _api?.Dispose();
                _api = api; _guildId = guild; _guildName = name;
                AppSettingsStore.SetDiscordBotToken(token);   // يُحفظ مشفّراً بحساب ويندوز
                AppSettingsStore.SetDiscordGuildId(guild);
                TokenBox.Clear();
                UpdateConnectionUi();
                AddBubble(false, $"تم الاتصال بسيرفر «{name}». وش تبي أسوي؟");
            }
            catch (DiscordApiException ex)
            {
                api?.Dispose();
                ConnectStatus.Text = "❌ " + ex.Message;
            }
            finally { ConnectButton.IsEnabled = true; }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _api?.Dispose(); _api = null; _guildId = null; _pending = null;
            AppSettingsStore.SetDiscordBotToken(null);
            PlanPanel.Visibility = Visibility.Collapsed;
            UpdateConnectionUi();
        }

        private void Portal_Click(object sender, RoutedEventArgs e) =>
            Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });

        private void Invite_Click(object sender, RoutedEventArgs e)
        {
            if (_botId == null) return;
            Process.Start(new ProcessStartInfo(DiscordApi.InviteUrl(_botId)) { UseShellExecute = true });
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
