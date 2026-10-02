using System;

namespace ConsoleApp1
{
    public sealed class LogEntry
    {
        public string Level { get; } // Тип
        public string Logger { get; }
        public string Message { get; } // Текст
        public string Signature { get; }

        public int Count; // Кол-во
        public string? FirstFile; // Файл где выводится
        public string? FirstTimestamp; // Время первого вхождения

        public LogEntry(string level, string logger, string message, string signature)
        {
            Level = level;
            Logger = logger;
            Message = message;
            Signature = signature;
        }

        public bool IsError => Level is "ERROR" or "FATAL";
    }
}