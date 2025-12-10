using AIReplyHelper.Models;
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
    /// Interaction logic for HistoryWindow.xaml
    /// </summary>
    public partial class HistoryWindow : Window
    {
        public event EventHandler<ReplyHistoryItem> ReplySelected;
        public event EventHandler ClearHistoryRequested;

        public HistoryWindow(List<ReplyHistoryItem> history)
        {
            InitializeComponent();

            if (history.Count == 0)
            {
                HistoryItemsControl.Visibility = Visibility.Collapsed;
                ClearHistoryButton.Visibility = Visibility.Collapsed;

                var emptyMessage = new System.Windows.Controls.TextBlock
                {
                    Text = "📭 No history yet.\n\nGenerate some replies to see them here!",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20)
                };

                ((System.Windows.Controls.Grid)Content).Children.Add(emptyMessage);
                System.Windows.Controls.Grid.SetRow(emptyMessage, 1);
            }
            else
            {
                HistoryItemsControl.ItemsSource = history;
            }
        }

        private void UseButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (System.Windows.Controls.Button)sender;
            var item = (ReplyHistoryItem)button.Tag;

            ReplySelected?.Invoke(this, item);
            Close();
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
