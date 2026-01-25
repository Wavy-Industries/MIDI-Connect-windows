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
        public static MainViewModel? ViewModel { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Initialize logger (creates fresh temp log file)
            Logger.Initialize();
            Logger.Info("Application starting");
            
            try
            {
                // Create system tray icon
                _notifyIcon = new TaskbarIcon
                {
                    Icon = new Icon("icon.ico"),
                    ToolTipText = "MIDI Connect"
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
                ViewModel = _mainWindow.DataContext as MainViewModel;
                _mainWindow.Closed += (s, e) => 
                {
                    _mainWindow = null;
                    ViewModel = null;
                };
                _mainWindow.ShowNearTaskbar();
            }
            else
            {
                _mainWindow.ShowNearTaskbar();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("Application exiting");
            
            // Check if there were any errors during the session
            Logger.CheckAndPromptForErrors();
            
            // Cleanup
            _notifyIcon?.Dispose();
            Logger.Shutdown();
            
            base.OnExit(e);
        }
    }
}
