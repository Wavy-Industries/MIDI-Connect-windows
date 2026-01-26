using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace MinimalWindowsApp.Infrastructure
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

        public static void Initialize()
        {
            lock (LockObj)
            {
                try
                {
                    _logWriter?.Dispose();

                    var tempDir = Path.Combine(Path.GetTempPath(), "MIDIConnect");
                    Directory.CreateDirectory(tempDir);

                    _logFilePath = Path.Combine(tempDir, "MIDIConnect.log");

                    _logWriter = new StreamWriter(_logFilePath, append: false, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };

                    _hasWarningOrError = false;
                    _sessionLog.Clear();

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

                Debug.WriteLine(logMessage);

#if DEBUG
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Info => ConsoleColor.Cyan,
                    LogLevel.Debug => ConsoleColor.Gray,
                    _ => ConsoleColor.White
                };
                Console.WriteLine(logMessage);
                Console.ForegroundColor = originalColor;
#endif

                try
                {
                    _logWriter?.WriteLine(logMessage);
                }
                catch
                {
                }

                _sessionLog.AppendLine(logMessage);

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
        }

        public static void Error(string message)
        {
            Write(LogLevel.Error, message);
        }

        public static void LogDebug(string message) => Write(LogLevel.Debug, message);

        public static void ErrorWithPopup(string message, Exception? exception = null)
        {
            var fullMessage = exception != null
                ? $"{message}: {exception.Message}\n{exception.StackTrace}"
                : message;

            Write(LogLevel.Error, fullMessage);

            ShowErrorPopup(message, exception);
        }

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

                var logLines = _sessionLog.ToString().Split('\n');
                var startIndex = Math.Max(0, logLines.Length - 50);
                for (int i = startIndex; i < logLines.Length; i++)
                {
                    body.AppendLine(logLines[i]);
                }

                var emailSubject = Uri.EscapeDataString($"MIDI Connect Error Report: {subject}");
                var emailBody = Uri.EscapeDataString(body.ToString());

                var mailtoUrl = $"mailto:hello@wavyindustries.com?subject={emailSubject}&body={emailBody}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = mailtoUrl,
                    UseShellExecute = true
                });

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

        public static string? GetLogFilePath() => _logFilePath;

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
