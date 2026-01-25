using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace MinimalWindowsApp
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public static class Logger
    {
        private static readonly object LockObj = new();
        private static string? _logFilePath;
        private static StreamWriter? _logWriter;
        private static bool _hasWarningOrError;
        private static readonly StringBuilder _sessionLog = new();
        
        public static string? LogFilePath => _logFilePath;
        public static bool HasWarningOrError => _hasWarningOrError;

        /// <summary>
        /// Initialize the logger. Creates a new temp log file, erasing any previous session.
        /// </summary>
        public static void Initialize()
        {
            lock (LockObj)
            {
                try
                {
                    // Close existing writer if any
                    _logWriter?.Dispose();
                    
                    // Create temp log file path
                    var tempDir = Path.Combine(Path.GetTempPath(), "MIDIConnect");
                    Directory.CreateDirectory(tempDir);
                    
                    _logFilePath = Path.Combine(tempDir, "MIDIConnect.log");
                    
                    // Erase previous log file by creating new one (overwrite)
                    _logWriter = new StreamWriter(_logFilePath, append: false, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    
                    _hasWarningOrError = false;
                    _sessionLog.Clear();
                    
                    // Write header
                    var header = $"=== MIDI Connect Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
                    _logWriter.WriteLine(header);
                    _logWriter.WriteLine($"Version: {GetVersionString()}");
                    _logWriter.WriteLine($"OS: {Environment.OSVersion}");
                    _logWriter.WriteLine($"Machine: {Environment.MachineName}");
                    _logWriter.WriteLine(new string('=', header.Length));
                    _logWriter.WriteLine();
                    
                    _sessionLog.AppendLine(header);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Shutdown the logger and cleanup resources.
        /// </summary>
        public static void Shutdown()
        {
            lock (LockObj)
            {
                try
                {
                    _logWriter?.WriteLine();
                    _logWriter?.WriteLine($"=== Log ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _logWriter?.Dispose();
                    _logWriter = null;
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private static string GetVersionString()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        private static void Write(LogLevel level, string message)
        {
            lock (LockObj)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var levelText = level.ToString().ToUpper().PadRight(7);
                var logMessage = $"[{timestamp}] [{levelText}] {message}";
                
                // Write to debug output
                Debug.WriteLine(logMessage);
                
                // Write to file
                try
                {
                    _logWriter?.WriteLine(logMessage);
                }
                catch
                {
                    // Ignore file write errors
                }
                
                // Track in session log
                _sessionLog.AppendLine(logMessage);
                
                // Track if we have warnings or errors
                if (level == LogLevel.Warning || level == LogLevel.Error)
                {
                    _hasWarningOrError = true;
                }
            }
        }

        public static void Info(string message) => Write(LogLevel.Info, message);
        
        public static void Warning(string message)
        {
            Write(LogLevel.Warning, message);
            // Don't show popup for warnings immediately - let them accumulate
        }
        
        public static void Error(string message)
        {
            Write(LogLevel.Error, message);
        }
        
        public static void LogDebug(string message) => Write(LogLevel.Debug, message);

        /// <summary>
        /// Log an error and show a popup with option to send email with log file.
        /// </summary>
        public static void ErrorWithPopup(string message, Exception? exception = null)
        {
            var fullMessage = exception != null 
                ? $"{message}: {exception.Message}\n{exception.StackTrace}" 
                : message;
            
            Write(LogLevel.Error, fullMessage);
            
            ShowErrorPopup(message, exception);
        }

        /// <summary>
        /// Show error popup with option to send log via email.
        /// </summary>
        public static void ShowErrorPopup(string message, Exception? exception = null)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var errorDetails = exception != null
                    ? $"{message}\n\nError: {exception.Message}"
                    : message;

                var result = MessageBox.Show(
                    $"{errorDetails}\n\nWould you like to send an error report to Wavy Industries support?",
                    "MIDI Connect - Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    SendErrorEmail(message, exception);
                }
            });
        }

        /// <summary>
        /// Check if there were any warnings/errors and optionally prompt user.
        /// Call this on app exit or at appropriate times.
        /// </summary>
        public static void CheckAndPromptForErrors()
        {
            if (!_hasWarningOrError) return;
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var result = MessageBox.Show(
                    "Some warnings or errors occurred during this session.\n\n" +
                    "Would you like to send the log file to Wavy Industries support?",
                    "MIDI Connect - Session Report",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SendErrorEmail("Session log with warnings/errors", null);
                }
            });
        }

        /// <summary>
        /// Opens default email client with log file attached.
        /// </summary>
        private static void SendErrorEmail(string subject, Exception? exception)
        {
            try
            {
                var body = new StringBuilder();
                body.AppendLine("Please describe what you were doing when this error occurred:");
                body.AppendLine();
                body.AppendLine("---");
                body.AppendLine();
                body.AppendLine($"Error: {subject}");
                
                if (exception != null)
                {
                    body.AppendLine($"Exception: {exception.Message}");
                    body.AppendLine($"Type: {exception.GetType().FullName}");
                }
                
                body.AppendLine();
                body.AppendLine($"App Version: {GetVersionString()}");
                body.AppendLine($"OS: {Environment.OSVersion}");
                body.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                body.AppendLine();
                body.AppendLine("--- LOG FILE ---");
                body.AppendLine($"(Please attach the log file from: {_logFilePath})");
                body.AppendLine();
                
                // Add recent log entries to email body (last 50 lines)
                var logLines = _sessionLog.ToString().Split('\n');
                var startIndex = Math.Max(0, logLines.Length - 50);
                for (int i = startIndex; i < logLines.Length; i++)
                {
                    body.AppendLine(logLines[i]);
                }

                var emailSubject = Uri.EscapeDataString($"MIDI Connect Error Report: {subject}");
                var emailBody = Uri.EscapeDataString(body.ToString());
                
                // Open default email client
                var mailtoUrl = $"mailto:hello@wavyindustries.com?subject={emailSubject}&body={emailBody}";
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = mailtoUrl,
                    UseShellExecute = true
                });
                
                // Also show the log file location
                if (_logFilePath != null && File.Exists(_logFilePath))
                {
                    var openLogResult = MessageBox.Show(
                        $"Your email client should now open.\n\n" +
                        $"Please attach the log file:\n{_logFilePath}\n\n" +
                        $"Would you like to open the log file location?",
                        "MIDI Connect - Send Report",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (openLogResult == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{_logFilePath}\"",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open email client: {ex.Message}");
                
                MessageBox.Show(
                    $"Could not open email client.\n\n" +
                    $"Please manually send the log file to:\nhello@wavyindustries.com\n\n" +
                    $"Log file location:\n{_logFilePath}",
                    "MIDI Connect - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Get the full path to the current log file.
        /// </summary>
        public static string? GetLogFilePath() => _logFilePath;

        /// <summary>
        /// Open the log file in the default text editor.
        /// </summary>
        public static void OpenLogFile()
        {
            if (_logFilePath != null && File.Exists(_logFilePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _logFilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open log file: {ex.Message}");
                }
            }
        }
    }
}
