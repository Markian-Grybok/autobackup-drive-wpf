
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using static System.Formats.Asn1.AsnWriter;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Net;
using AppCopyDirecToDrive.services;
using AppCopyDirecToDrive.Services;
using Ookii.Dialogs.Wpf;
using System.Windows.Threading;
using System.Drawing;
using System.Windows.Forms; // Для NotifyIcon
using System.ComponentModel; // For CancelEventArgs

namespace AppCopyDirecToDrive
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DirectoryPathsManager _managerList = new();
        private List<string> _currentPathsList = new();

        private int timeIntervalSeconds = 0;
        private DispatcherTimer uploadTimer;
        private NotifyIcon notifyIcon;

        public MainWindow()
        {
            InitializeComponent();
            CheckExistingTokens();
            LoadPaths();
            
            InitializeTrayIcon();
            
            uploadTimer = new DispatcherTimer();
            uploadTimer.Tick += async (s, e) => await TimerElapsed();
            uploadTimer.IsEnabled = false; 
        }


        private void InitializeTrayIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "CopyDirecToDrive",
                Visible = true
            };

            // Контекстне меню
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Відновити", null, (s, e) => RestoreWindow());
            contextMenu.Items.Add("Вийти", null, (s, e) => ExitApplication());
            notifyIcon.ContextMenuStrip = contextMenu;

            // Подвійне клацання для відновлення
            notifyIcon.DoubleClick += (s, e) => RestoreWindow();

        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Hide();
            notifyIcon.ShowBalloonTip(1000, "CopyDirecToDrive", "Програма згорнута в системний трей", ToolTipIcon.Info);
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            uploadTimer.Stop();
            System.Windows.Application.Current.Shutdown();
        }


        #region ===((( Autorization )))===

        public void CheckExistingTokens()
        {
            var tokens = TokenManager.LoadTokens();
            if (tokens != null)
            {
                // Перевіряємо чи токен ще дійсний
                if (!string.IsNullOrEmpty(tokens.AccessToken))
                {
                    // Використовуємо існуючий токен
                    System.Windows.MessageBox.Show($"Використовуємо токен: {tokens.AccessToken}");
                }
                else if (!string.IsNullOrEmpty(tokens.RefreshToken))
                {
                    // Оновлюємо токен
                    RefreshTokenAsync(tokens.RefreshToken);
                }


                autorisation.Text = "Ви авторизовані";
            }
        }

        private async void RefreshTokenAsync(string refreshToken)
        {
            try
            {
                var newTokens = await GoogleOAuthService.RefreshTokenAsync(refreshToken);
                System.Windows.MessageBox.Show($"Використовуємо токен: {newTokens.AccessToken}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Помилка оновлення токена: {ex.Message}");
                TokenManager.ClearTokens();
            }
        }

        private async void Login_Button(object sender, RoutedEventArgs e)
        {
            // 1. Відкриваємо авторизацію у браузері
            GoogleOAuthService.OpenAuthInSystemBrowser();

            // 2. Чекаємо на код з callback URL
            var authCode = await GoogleOAuthService.ListenForAuthCode();

            // 3. Обмінюємо код на токени
            var tokens = await GoogleOAuthService.ExchangeCodeForToken(authCode);

            autorisation.Text = "Ви війшли";
            System.Windows.MessageBox.Show($"Авторизація успішна! Access Token: {tokens.AccessToken}\n Access Token:{tokens.RefreshToken}");
        }

        private void Logout_Button(object sender, RoutedEventArgs e)
        {
            TokenManager.ClearTokens();
            autorisation.Text = "Ви вийшли";
            System.Windows.MessageBox.Show("Ви вийшли з системи");
        }

        #endregion

        #region ===((( Drive )))===

        private void LoadPaths()
        {
            _currentPathsList = _managerList.LoadPaths();
            selectedDirectoriesList.ItemsSource = _currentPathsList;
        }


        private void DeleteDirectory_Button(object sender, RoutedEventArgs e)
        {
            if (selectedDirectoriesList.SelectedValue is null)
            {
                System.Windows.MessageBox.Show("Ви нічого не вибрали!");
                return;
            }
            
            string? selectedT = selectedDirectoriesList.SelectedValue.ToString();


            _currentPathsList.Remove(selectedT);
            _managerList.SavePaths(_currentPathsList);
            LoadPaths();
        }

        private void Browse_Button(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Виберіть директорію",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == true)
            {
                textPath.Text = dialog.SelectedPath;
            }
        }

        private void AddDirectory_Button(object sender, RoutedEventArgs e)
        {
            try
            {

                string? name = textPath.Text.Trim();


                if (_currentPathsList.Contains(name) || string.IsNullOrEmpty(name))
                {
                    throw new Exception("Така дорога вже є");
                }



                _currentPathsList.Add(name);
                _managerList.SavePaths(_currentPathsList);
                LoadPaths();
                textPath.Clear();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);
            }
            


        }
        #endregion


        private async void Start_Button(object sender, RoutedEventArgs e)
        {
            try
            {
                // Перевірка авторизації, часу та директорій
                var tokens = TokenManager.LoadTokens();
                if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
                {
                    System.Windows.MessageBox.Show("Ви не авторизовані! Будь ласка, увійдіть.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (timeIntervalSeconds <= 0)
                {
                    System.Windows.MessageBox.Show("Будь ласка, встановіть інтервал більше 0 секунд.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedDirectoriesList.Items.Count == 0)
                {
                    System.Windows.MessageBox.Show("Будь ласка, виберіть хоча б одну директорію.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                btnStop.IsEnabled = true; 
                btnStop.Focus();
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                info.Text = $"Відлік часу розпочато. Очікування {timeIntervalSeconds} секунд...";

                // Запустити таймер
                uploadTimer.Interval = TimeSpan.FromSeconds(timeIntervalSeconds);
                uploadTimer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Помилка при запуску: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                this.IsEnabled = true; // Увімкнути форму у разі помилки
                Mouse.OverrideCursor = null;
            }
        }

        private async Task TimerElapsed()
        {
            try
            {
                // Зупинити таймер
                uploadTimer.Stop();

                // Перевірити авторизацію
                var tokens = TokenManager.LoadTokens();
                if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
                {
                    System.Windows.MessageBox.Show("Ви не авторизовані! Будь ласка, увійдіть.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    this.IsEnabled = true; // Увімкнути форму
                    Mouse.OverrideCursor = null;
                    btnStart.IsEnabled = true; // Увімкнути кнопку Start
                    info.Text = "Таймер зупинено через відсутність авторизації.";
                    return;
                }

                
                this.IsEnabled = false; 
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                info.Text = "Завантаження файлів розпочато...";

                var driveService = GoogleOAuthService.CreateDriveService(tokens.AccessToken);
                var uploader = new GoogleDriveUploader(driveService);
                await uploader.UploadAllFromJsonAsync("directory_paths.json");

               
                var result = System.Windows.MessageBox.Show("Завантаження завершено успішно! Продовжити автоматичне завантаження?",
                    "Успіх", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                   
                    info.Text = $"Відлік часу розпочато. Очікування {timeIntervalSeconds} секунд...";
                    uploadTimer.Interval = TimeSpan.FromSeconds(timeIntervalSeconds);
                    uploadTimer.Start();
                    this.IsEnabled = false;
                    btnStop.IsEnabled = true;
                    btnStop.Focus();
                    Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                }
                else
                {
                    this.IsEnabled = true;
                    btnStart.IsEnabled = true;
                    Mouse.OverrideCursor = null;
                    info.Text = "Таймер зупинено. Натисніть Start для повторного запуску.";
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Помилка при автоматичному завантаженні: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                this.IsEnabled = true;
                btnStart.IsEnabled = true;
                Mouse.OverrideCursor = null;
                info.Text = "Таймер зупинено через помилку.";
            }
        }

        private void Stop_Button(object sender, RoutedEventArgs e)
        {
            // Перевірка авторизації, часу та директорій
            var tokens = TokenManager.LoadTokens();
            if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                System.Windows.MessageBox.Show("Ви не авторизовані! Будь ласка, увійдіть.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (timeIntervalSeconds <= 0)
            {
                System.Windows.MessageBox.Show("Будь ласка, встановіть інтервал більше 0 секунд.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selectedDirectoriesList.Items.Count == 0)
            {
                System.Windows.MessageBox.Show("Будь ласка, виберіть хоча б одну директорію.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            uploadTimer.Stop();
            this.IsEnabled = true;
            btnStart.IsEnabled = true;
            Mouse.OverrideCursor = null;
            info.Text = "Таймер зупинено. Натисніть Start для повторного запуску.";
        }

        private void SetInterval_Button(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(textTimeInterval.Text, out int interval) && interval > 0)
            {
                timeIntervalSeconds = interval;
                info.Text = $"Інтервал встановлено: {interval} секунд";
            }
            else
            {
                System.Windows.MessageBox.Show("Будь ласка, введіть коректне число секунд (більше 0).", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        
    }
}