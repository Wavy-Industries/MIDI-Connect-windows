using System;
using System.Diagnostics;

namespace MinimalWindowsApp
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public static class Logger
    {
        private static readonly object LockObj = new();

        private static void Write(LogLevel level, string message, ConsoleColor color)
        {
            lock (LockObj)
            {
                var levelText = level.ToString().ToUpper();
                var logMessage = $"[{levelText}] {message}";
                
                Debug.WriteLine(logMessage);
                
                Console.ForegroundColor = color;
                Console.WriteLine(logMessage);
                Console.ResetColor();
            }
        }

        public static void Info(string message) => Write(LogLevel.Info, message, ConsoleColor.Cyan);
        public static void Warning(string message) => Write(LogLevel.Warning, message, ConsoleColor.Yellow);
        public static void Error(string message) => Write(LogLevel.Error, message, ConsoleColor.Red);
        public static void LogDebug(string message) => Write(LogLevel.Debug, message, ConsoleColor.Gray);
    }
}