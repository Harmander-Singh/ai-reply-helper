using AIReplyHelper.Command;
using AIReplyHelper.Common;
using AIReplyHelper.Models;
using AIReplyHelper.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AIReplyHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppSettings settings;
        private List<ReplyHistoryItem> replyHistory;
        private Stack<UndoState> undoStack = new Stack<UndoState>();
        private const int MaxHistoryItems = 20;
        private const int MaxUndoStates = 10;
        private const int MaxInputLength = 2000;
        private CancellationTokenSource cancellationTokenSource;
        private DispatcherTimer loadingTimer;
        private int loadingSeconds = 0;
        private bool isDarkMode = false;
        private const string AppVersion = "1.0.0";
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private string selectedModel = "gpt-4o-mini";
        private DispatcherTimer toastTimer;

        public event PropertyChangedEventHandler PropertyChanged;

        private ICommand _generateCommand;
        public ICommand GenerateCommand
        {
            get => _generateCommand;
            set
            {
                _generateCommand = value;
                OnPropertyChanged(nameof(GenerateCommand));
            }
        }

        private ICommand _copyCommand;
        public ICommand CopyCommand
        {
            get => _copyCommand;
            set
            {
                _copyCommand = value;
                OnPropertyChanged(nameof(CopyCommand));
            }
        }

        private ICommand _historyCommand;
        public ICommand HistoryCommand
        {
            get => _historyCommand;
            set
            {
                _historyCommand = value;
                OnPropertyChanged(nameof(HistoryCommand));
            }
        }

        private ICommand _settingsCommand;
        public ICommand SettingsCommand
        {
            get => _settingsCommand;
            set
            {
                _settingsCommand = value;
                OnPropertyChanged(nameof(SettingsCommand));
            }
        }

        private ICommand _undoCommand;
        public ICommand UndoCommand
        {
            get => _undoCommand;
            set
            {
                _undoCommand = value;
                OnPropertyChanged(nameof(UndoCommand));
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeCommands();
            LoadSettings();
            LoadHistory();
            InitializeLoadingTimer();
            _ = CheckForUpdatesAsync();
        }

        private void InitializeCommands()
        {
            GenerateCommand = new AsyncRelayCommand(async () => await GenerateReplyCommandAsync(), () => GenerateButton?.IsEnabled ?? true);
            CopyCommand = new RelayCommand(_ => CopyToClipboard(), _ => CopyButton?.IsEnabled ?? false);
            HistoryCommand = new RelayCommand(_ => ShowHistory());
            SettingsCommand = new RelayCommand(_ => ShowSettings());
            UndoCommand = new RelayCommand(_ => PerformUndo(), _ => UndoButton?.IsEnabled ?? false);
        }

        private void InitializeLoadingTimer()
        {
            loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            loadingTimer.Tick += LoadingTimer_Tick;

            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            toastTimer.Tick += ToastTimer_Tick;
        }

        private void ToastTimer_Tick(object sender, EventArgs e)
        {
            toastTimer.Stop();
            HideToast();
        }

        private void LoadingTimer_Tick(object sender, EventArgs e)
        {
            loadingSeconds++;
            LoadingTimeText.Text = $"{loadingSeconds}s";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowState();
        }

        private void RestoreWindowState()
        {
            if (settings.WindowLeft > 0 && settings.WindowLeft < SystemParameters.VirtualScreenWidth)
            {
                Left = settings.WindowLeft;
            }

            if (settings.WindowTop > 0 && settings.WindowTop < SystemParameters.VirtualScreenHeight)
            {
                Top = settings.WindowTop;
            }

            if (settings.WindowWidth > MinWidth && settings.WindowWidth < SystemParameters.VirtualScreenWidth)
            {
                Width = settings.WindowWidth;
            }

            if (settings.WindowHeight > MinHeight && settings.WindowHeight < SystemParameters.VirtualScreenHeight)
            {
                Height = settings.WindowHeight;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+G - Generate
            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (GenerateButton.IsEnabled)
                {
                    _ = GenerateReplyWithValidationAsync();
                }
            }
            // Ctrl+Shift+C - Copy
            else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                if (CopyButton.IsEnabled)
                {
                    CopyToClipboard();
                }
            }
            // Ctrl+H - History
            else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowHistory();
            }
            // Ctrl+, - Settings
            else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowSettings();
            }
            // Ctrl+Z - Undo
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (UndoButton.IsEnabled)
                {
                    PerformUndo();
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowState();
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            loadingTimer?.Stop();
        }

        private void SaveWindowState()
        {
            if (WindowState == WindowState.Normal)
            {
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
                SettingsManager.SaveSettings(settings);
            }
        }

        private void LoadSettings()
        {
            settings = SettingsManager.LoadSettings();
            if (settings.DefaultTone >= 0 && settings.DefaultTone < ToneComboBox.Items.Count)
            {
                ToneComboBox.SelectedIndex = settings.DefaultTone;
            }
        }

        private void LoadHistory()
        {
            replyHistory = HistoryManager.LoadHistory();
        }

        private async Task GenerateReplyCommandAsync()
        {
            if (GenerateButton.IsEnabled)
            {
                await GenerateReplyWithValidationAsync();
            }
        }

        private async Task GenerateReplyWithValidationAsync()
        {
            string inputMessage = InputTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(inputMessage))
            {
                ShowModernToast("Input Required", "Please enter a message to generate a reply.");
                InputTextBox.Focus();
                return;
            }

            if (inputMessage.Length < 10)
            {
                ShowModernToast("Input Too Short", "Please enter at least 10 characters for better results.");
                InputTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey) && !settings.OfflineMode)
            {
                //ShowModernToast("API Key Required", "OpenAI API key is not configured", ToastType.Info);
                var result = MessageBox.Show(
                    "OpenAI API key is not configured. Would you like to configure it now?",
                    "API Key Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ShowSettings();
                }
                return;
            }

            SaveUndoState();
            string selectedTone = GetSelectedTone();
            ShowLoading();

            try
            {
                cancellationTokenSource = new CancellationTokenSource();
                string generatedReply = await GenerateReplyAsync(inputMessage, selectedTone, cancellationTokenSource.Token);

                OutputTextBox.Text = generatedReply;
                CopyButton.IsEnabled = true;
                StatusTextBlock.Text = $"Reply generated in {loadingSeconds}s";

                AddToHistory(inputMessage, generatedReply, selectedTone);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Generation cancelled";
                OutputTextBox.Text = "";
            }
            catch (HttpRequestException ex)
            {
                StatusTextBlock.Text = "Network error";
                LogError(ex);
                ShowModernToast("Network Error", $"{GetFriendlyErrorMessage(ex)}\nPlease check your internet connection and try again.", ToastType.Error);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Generation failed";
                LogError(ex);
                ShowModernToast("Generation failed", $"Failed to generate reply:\n{GetFriendlyErrorMessage(ex)}", ToastType.Error);
            }
            finally
            {
                HideLoading();
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
            }
        }

        private void ShowLoading()
        {
            GenerateButton.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            LoadingOverlay.Visibility = Visibility.Visible;
            loadingSeconds = 0;
            LoadingTimeText.Text = "0s";
            loadingTimer.Start();

            var storyboard = (Storyboard)FindResource("LoadingAnimation");
            storyboard.Begin();
        }

        private void HideLoading()
        {
            GenerateButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            loadingTimer.Stop();

            var storyboard = (Storyboard)FindResource("LoadingAnimation");
            storyboard.Stop();
        }

        private string GetSelectedTone()
        {
            if (ToneComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string content = selectedItem.Content?.ToString() ?? "Polite";
                var parts = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[parts.Length - 1] : "Polite";
            }
            return "Polite";
        }

        private async Task<string> GenerateReplyAsync(string message, string tone, CancellationToken cancellationToken)
        {
            if (settings.OfflineMode)
            {
                await Task.Delay(2000, cancellationToken);
                return $"[Offline Mode - Demo Reply]\n\nThank you for your message. This is a sample reply generated in offline mode. Please configure your API key and disable offline mode in Settings to use the AI generation feature.\n\nOriginal tone requested: {tone}";
            }

            const int maxRetries = 3;
            int retryDelay = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallOpenAIApiAsync(message, tone, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        throw;
                    }

                    if (ex.Message.Contains("429") || ex.Message.Contains("Rate limit"))
                    {
                        StatusTextBlock.Text = $"Rate limited, retrying in {retryDelay / 1000}s... (Attempt {attempt}/{maxRetries})";
                        await Task.Delay(retryDelay, cancellationToken);
                        retryDelay *= 2;
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Retrying... (Attempt {attempt}/{maxRetries})";
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            throw new Exception("Failed after multiple retries");
        }

        private async Task<string> CallOpenAIApiAsync(string message, string tone, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = selectedModel, //"gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = $"You are a helpful assistant that generates {tone.ToLower()} replies to messages. Keep replies concise (under 150 words), appropriate, and natural. Generate only the reply text without any additional commentary or explanations." },
                    new { role = "user", content = $"Generate a {tone.ToLower()} reply to this message:\n\n{message}" }
                },
                max_tokens = 300,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    //ShowModernToast("Invalid API key", "Please check your OpenAI API key in Settings.", ToastType.Error);
                    throw new Exception("Invalid API key. Please check your OpenAI API key in Settings.");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    //ShowModernToast("Rate limit exceeded", "You exceeded your current quota, please check your plan and billing details.", ToastType.Error);
                    throw new Exception("You exceeded your current quota, please check your plan and billing details.");
                    //throw new Exception("Rate limit exceeded. Please wait a moment and try again.");
                }
                //ShowModernToast("API Error", $"({response.StatusCode}): {errorContent}", ToastType.Error);
                throw new Exception($"API Error ({response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseJson);

            string reply = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return reply?.Trim() ?? string.Empty;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
        }

        private void CopyToClipboard()
        {
            if (string.IsNullOrWhiteSpace(OutputTextBox.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(OutputTextBox.Text);
                StatusTextBlock.Text = "Copied to clipboard!";

                ShowModernToast("Copied!", "Reply copied to clipboard successfully.", ToastType.Success, 2);

                AnimateButtonSuccess(CopyButton);
            }
            catch (Exception ex)
            {
                LogError(ex);
                ShowModernToast("Copy Failed", "Failed to copy to clipboard. Please try again.", ToastType.Error);
            }
        }

        #region Toast

        private void ShowModernToast(string title, string message, ToastType type = ToastType.Info, int durationSeconds = 5)
        {
            Dispatcher.Invoke(() =>
            {
                // Set colors and icon based on type
                switch (type)
                {
                    case ToastType.Success:
                        ToastNotification.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                        //ToastIcon.Text = "✅";
                        break;
                    case ToastType.Warning:
                        ToastNotification.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
                        //ToastIcon.Text = "⚠️";
                        break;
                    case ToastType.Error:
                        ToastNotification.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                        //ToastIcon.Text = "❌";
                        break;
                    default: // Info
                        ToastNotification.Background = new SolidColorBrush(Color.FromRgb(79, 70, 229)); // Indigo
                        //ToastIcon.Text = "ℹ️";
                        break;
                }

                ToastTitle.Text = title;
                ToastMessage.Text = message;

                // Slide in animation
                ToastNotification.Visibility = Visibility.Visible;
                var slideIn = new ThicknessAnimation
                {
                    From = new Thickness(0, -100, 0, 0),
                    To = new Thickness(0, 0, 0, 15),
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToastNotification.BeginAnimation(MarginProperty, slideIn);

                // Auto-hide after duration
                toastTimer.Stop();
                toastTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
                toastTimer.Start();
            });
        }

        private void HideToast()
        {
            Dispatcher.Invoke(() =>
            {
                var slideOut = new ThicknessAnimation
                {
                    From = new Thickness(0, 0, 0, 15),
                    To = new Thickness(0, -100, 0, 0),
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                slideOut.Completed += (s, e) =>
                {
                    ToastNotification.Visibility = Visibility.Collapsed;
                };

                ToastNotification.BeginAnimation(MarginProperty, slideOut);
            });
        }

        private void ToastClose_Click(object sender, RoutedEventArgs e)
        {
            toastTimer.Stop();
            HideToast();
        }

        #endregion

        private void AnimateButtonSuccess(Button button)
        {
            var originalBg = button.Background;
            button.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, args) =>
            {
                button.Background = originalBg;
                timer.Stop();
            };
            timer.Start();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
        }

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHistory();
        }

        private void ShowHistory()
        {
            var historyWindow = new HistoryWindow(replyHistory) { Owner = this };

            historyWindow.ReplySelected += (s, item) =>
            {
                SaveUndoState();
                InputTextBox.Text = item.OriginalMessage;
                OutputTextBox.Text = item.GeneratedReply;
                CopyButton.IsEnabled = true;
                StatusTextBlock.Text = "Reply loaded from history";
            };

            historyWindow.ClearHistoryRequested += (s, args) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear all history? This cannot be undone.",
                    "Clear History",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    replyHistory.Clear();
                    HistoryManager.SaveHistory(replyHistory);
                    StatusTextBlock.Text = "History cleared";
                }
            };

            historyWindow.ShowDialog();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings();
        }

        private void ShowSettings()
        {
            var settingsWindow = new SettingsWindow(settings) { Owner = this };

            if (settingsWindow.ShowDialog() == true)
            {
                settings = settingsWindow.UpdatedSettings;
                SettingsManager.SaveSettings(settings);

                if (settings.DefaultTone >= 0 && settings.DefaultTone < ToneComboBox.Items.Count)
                {
                    ToneComboBox.SelectedIndex = settings.DefaultTone;
                }
            }
        }

        private void AddToHistory(string originalMessage, string generatedReply, string tone)
        {
            var historyItem = new ReplyHistoryItem
            {
                OriginalMessage = originalMessage,
                GeneratedReply = generatedReply,
                Tone = tone,
                Timestamp = DateTime.Now
            };

            replyHistory.Insert(0, historyItem);

            if (replyHistory.Count > MaxHistoryItems)
            {
                replyHistory.RemoveRange(MaxHistoryItems, replyHistory.Count - MaxHistoryItems);
            }

            HistoryManager.SaveHistory(replyHistory);
        }

        private void SaveUndoState()
        {
            var state = new UndoState
            {
                InputText = InputTextBox.Text ?? string.Empty,
                OutputText = OutputTextBox.Text ?? string.Empty,
                SelectedTone = ToneComboBox.SelectedIndex
            };

            undoStack.Push(state);

            if (undoStack.Count > MaxUndoStates)
            {
                var temp = new Stack<UndoState>(undoStack.Reverse().Take(MaxUndoStates).Reverse());
                undoStack = temp;
            }

            UndoButton.IsEnabled = true;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            PerformUndo();
        }

        private void PerformUndo()
        {
            if (undoStack.Count > 0)
            {
                var state = undoStack.Pop();
                InputTextBox.Text = state.InputText;
                OutputTextBox.Text = state.OutputText;
                ToneComboBox.SelectedIndex = state.SelectedTone;

                StatusTextBlock.Text = "Undo applied";
                UndoButton.IsEnabled = undoStack.Count > 0;
                CopyButton.IsEnabled = !string.IsNullOrWhiteSpace(OutputTextBox.Text);
            }
        }

        private void UpdateCostEstimate()
        {
            int charCount = InputTextBox.Text?.Length ?? 0;
            int estimatedTokens = Math.Max(1, charCount / 4);

            decimal pricePer1K = selectedModel switch
            {
                "gpt-4o-mini" => 0.00015m,
                "gpt-4o" => 0.00500m,
                "gpt-3.5-turbo" => 0.00050m,
                _ => 0.00050m // default fallback
            };

            // Include both input + output tokens (approximate)
            decimal estimatedCost = (estimatedTokens / 1000m) * pricePer1K * 2;

            TokenInfoText.Text =
                $"🧠 Model: {selectedModel} | 💰 Estimated: ${estimatedCost:F6} | 📊 {charCount} chars";
        }

        private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelSelector.SelectedItem is ComboBoxItem item)
            {
                selectedModel = item.Content.ToString();
            }

            UpdateCostEstimate();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCostEstimate();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (replyHistory.Count == 0)
            {
                ShowModernToast("Export History", "No history to export.", ToastType.Info);
                //MessageBox.Show("No history to export.", "Export History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"AIReplyHelper_History_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    string content = saveDialog.FilterIndex == 1
                        ? ExportAsJson()
                        : ExportAsText();

                    System.IO.File.WriteAllText(saveDialog.FileName, content);
                    StatusTextBlock.Text = "History exported successfully";
                    ShowModernToast("Export Successful", $"History exported to:\n{saveDialog.FileName}", ToastType.Success);
                    //MessageBox.Show($"History exported to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                ShowModernToast("Export Error", $"Failed to export history:\n{ex.Message}", ToastType.Error);
                //ShowError($"Failed to export history:\n{ex.Message}", "Export Error");
            }
        }

        private string ExportAsJson()
        {
            return JsonSerializer.Serialize(replyHistory, new JsonSerializerOptions { WriteIndented = true });
        }

        private string ExportAsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AI Reply Helper - Exported History");
            sb.AppendLine($"Export Date: {DateTime.Now}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            foreach (var item in replyHistory)
            {
                sb.AppendLine($"Date: {item.Timestamp}");
                sb.AppendLine($"Tone: {item.Tone}");
                sb.AppendLine($"Original: {item.OriginalMessage}");
                sb.AppendLine($"Reply: {item.GeneratedReply}");
                sb.AppendLine(new string('-', 50));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool showNoUpdateMessage = false)
        {
            try
            {
                await Task.Delay(1000);

                if (showNoUpdateMessage)
                {
                    ShowModernToast("No Updates Available", $"You are using the latest version ({AppVersion}).");

                    //MessageBox.Show(
                    //    $"You are using the latest version ({AppVersion}).",
                    //    "No Updates Available",
                    //    MessageBoxButton.OK,
                    //    MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                if (showNoUpdateMessage)
                {
                    ShowError($"Failed to check for updates:\n{ex.Message}", "Update Check Failed");
                }
            }
        }

        private string GetFriendlyErrorMessage(Exception ex)
        {
            string message = ex.Message;

            if (message.Contains("API key") || message.Contains("Unauthorized"))
            {
                return "Invalid API key. Please check your settings.";
            }
            if (message.Contains("429") || message.Contains("Rate limit"))
            {
                return "Too many requests. Please wait a moment and try again.";
            }

            if (message.Contains("timeout") || message.Contains("Timeout"))
            {
                return "Request timed out. Please check your internet connection.";
            }

            if (message.Contains("network") || message.Contains("Network"))
            {
                return "Network error. Please check your internet connection.";
            }

            return message;
        }

        private void ShowError(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowWarning(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void LogError(Exception ex)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AIReplyHelper",
                    "error.log");

                string directory = System.IO.Path.GetDirectoryName(logPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
                System.IO.File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Silent fail for logging errors
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateReplyWithValidationAsync();
        }

    }
}