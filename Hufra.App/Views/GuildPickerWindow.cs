using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Hufra.App.Views
{
    /// <summary>نافذة صغيرة لاختيار السيرفر إذا البوت داخل أكثر من سيرفر.</summary>
    public class GuildPickerWindow : Window
    {
        private readonly ListBox _list = new ListBox { Margin = new Thickness(12), MinHeight = 120 };
        private readonly System.Collections.Generic.List<Hufra.Core.Discord.OwnedGuild> _guilds;
        public Hufra.Core.Discord.OwnedGuild Selected { get; private set; }

        public GuildPickerWindow(System.Collections.Generic.List<Hufra.Core.Discord.OwnedGuild> guilds)
        {
            _guilds = guilds;
            Title = "اختر السيرفر";
            Width = 360; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            foreach (var g in guilds) _list.Items.Add(g.BotIn ? g.Name : g.Name + "   (البوت غير مضاف)");
            _list.SelectedIndex = 0;
            var ok = new Button { Content = "اختيار", Margin = new Thickness(12, 0, 12, 12), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left, IsDefault = true };
            ok.Click += (s, e) => { if (_list.SelectedIndex >= 0) { Selected = _guilds[_list.SelectedIndex]; DialogResult = true; } };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "اختر السيرفر اللي تبي تديره (السيرفرات اللي تملكها):", Margin = new Thickness(12, 12, 12, 0) });
            panel.Children.Add(_list); panel.Children.Add(ok);
            Content = panel;
        }
    }
}
