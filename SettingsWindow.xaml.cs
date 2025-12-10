using AIReplyHelper.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AIReplyHelper
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public AppSettings UpdatedSettings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();

            // Load current settings
            ApiKeyTextBox.Text = currentSettings.ApiKey;
            DefaultToneComboBox.SelectedIndex = currentSettings.DefaultTone;
            OfflineModeCheckBox.IsChecked = currentSettings.OfflineMode;

            UpdatedSettings = currentSettings;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatedSettings = new AppSettings
            {
                ApiKey = ApiKeyTextBox.Text.Trim(),
                DefaultTone = DefaultToneComboBox.SelectedIndex,
                OfflineMode = OfflineModeCheckBox.IsChecked ?? false
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApiKeyLink_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://platform.openai.com/api-keys",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
