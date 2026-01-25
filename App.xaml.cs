using System;
using MinimalWindowsApp;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;

namespace MinimalWindowsApp
{
    public partial class App : Application
    {
        private static TaskbarIcon? _notifyIcon;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Logger.Info("=== Application starting ===");
            
            try
            {
                // Create system tray icon
                _notifyIcon = new TaskbarIcon
                {
                    Icon = new Icon(SystemIcons.Application, 40, 40),
                    ToolTipText = "MIDI Toolbar"
                };

                // Create context menu
                var contextMenu = new ContextMenu();
                
                var openItem = new MenuItem { Header = "Open" };
                openItem.Click += (s, args) => ShowMainWindow();
                contextMenu.Items.Add(openItem);
                
                contextMenu.Items.Add(new Separator());
                
                var exitItem = new MenuItem { Header = "Exit" };
                exitItem.Click += (s, args) => Shutdown();
                contextMenu.Items.Add(exitItem);

                _notifyIcon.ContextMenu = contextMenu;
                _notifyIcon.TrayLeftMouseDown += (s, args) => ShowMainWindow();

                // Don't exit when the main window closes
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                
                // Show window on startup
                ShowMainWindow();
            }
            catch (Exception ex)
            {
                Logger.Error($"Startup error: {ex.Message}");
                MessageBox.Show($"Startup error: {ex.Message}","Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (s, e) => _mainWindow = null;
                _mainWindow.ShowNearTaskbar();
            }
            else
            {
                _mainWindow.ShowNearTaskbar();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
