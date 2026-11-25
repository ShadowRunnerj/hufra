using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Hufra.Core.Settings;
using Microsoft.Win32;

namespace Hufra.App
{
    public partial class WelcomeWindow : Window
    {
        private string _chosenAvatarPath;

        public WelcomeWindow()
        {
            InitializeComponent();
        }

        private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "اختر صورتك",
                Filter = "صور|*.png;*.jpg;*.jpeg;*.bmp",
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(dialog.FileName);
                bitmap.EndInit();

                AvatarImagePreview.Source = bitmap;
                AvatarImagePreview.Visibility = Visibility.Visible;
                AvatarInitialText.Visibility = Visibility.Collapsed;
                _chosenAvatarPath = dialog.FileName;
            }
            catch
            {
                MessageBox.Show("تعذّر فتح الصورة، جرّب صورة ثانية.", "حُفرة",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            string name = string.IsNullOrWhiteSpace(NameBox.Text) ? "لاعب" : NameBox.Text.Trim();
            string savedAvatarPath = null;

            if (!string.IsNullOrEmpty(_chosenAvatarPath))
            {
                try
                {
                    string appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hufra");
                    Directory.CreateDirectory(appDataDir);
                    savedAvatarPath = Path.Combine(appDataDir, "avatar" + Path.GetExtension(_chosenAvatarPath));
                    File.Copy(_chosenAvatarPath, savedAvatarPath, overwrite: true);
                }
                catch
                {
                    savedAvatarPath = null; // ما قدرنا نحفظ الصورة، نكمل بدونها بدون ما نوقف المستخدم
                }
            }

            AppSettingsStore.SetProfile(name, savedAvatarPath);

            DialogResult = true;
            Close();
        }
    }
}
