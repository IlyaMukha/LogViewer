using System;

namespace ConsoleApp1
{
    /// <summary>
    /// Результат анализа: сообщение (ошибка), количество вхождений, уровень.
    /// </summary>
    public sealed class LogEntry
    {
        public string Level { get; }
        public string Logger { get; }
        public string Message { get; }   // сама ошибка
        public string Signature { get; }

        public int Count;                // количество вхождений
        public string? FirstFile;
        public string? FirstTimestamp;

        public LogEntry(string level, string logger, string message, string signature)
        {
            Level = level;
            Logger = logger;
            Message = message;
            Signature = signature;
        }

        public bool IsError => Level is "ERROR" or "FATAL";

        public override string ToString()
            => $"[{Count,5}] {Level,-5} {Logger} - {Message}";
    }
}