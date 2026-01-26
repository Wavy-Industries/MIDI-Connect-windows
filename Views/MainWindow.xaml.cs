using System;
using System.Windows;
using System.Windows.Input;
using MinimalWindowsApp.Infrastructure;
using MinimalWindowsApp.Services;
using MinimalWindowsApp.ViewModels;

namespace MinimalWindowsApp.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                _viewModel = new MainViewModel();
                DataContext = _viewModel;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Window initialization error: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            Hide();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBoxItem item && item.DataContext is DeviceSession device)
            {
                Logger.Info($"Click detected: {device.Name}");
                var isConnected = _viewModel?.ConnectedDevices.Contains(device) ?? false;
                Logger.Info($"Device is connected: {isConnected}");
                if (isConnected)
                {
                    Logger.Info($"Executing disconnect for {device.Name}");
                    _viewModel?.DisconnectCommand.Execute(device);
                }
                else
                {
                    Logger.Info($"Executing connect for {device.Name}");
                    _viewModel?.ConnectCommand.Execute(device);
                }
                e.Handled = true;
            }
        }

        public void ShowNearTaskbar()
        {
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 10;
            Top = workingArea.Bottom - Height - 40;
            Show();
            Activate();
        }
    }
}
