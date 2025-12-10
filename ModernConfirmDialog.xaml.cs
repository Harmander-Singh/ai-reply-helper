using System;
using System.Collections.Generic;
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
    /// Interaction logic for ModernConfirmDialog.xaml
    /// </summary>
    public partial class ModernConfirmDialog : Window
    {
        public ModernConfirmDialog(string title, string message, Window owner = null)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            Owner = owner;

            // Fade in animation
            Opacity = 0;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = System.TimeSpan.FromMilliseconds(200)
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
