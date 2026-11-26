using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Hufra.App.Views
{
    public partial class SupportView : UserControl
    {
        public SupportView()
        {
            InitializeComponent();
            VersionInfoText.Text = $"الإصدار {MainWindow.CurrentVersion}";
        }

        private void Discord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/dMvxKhB8SX") { UseShellExecute = true });
        }
    }
}
