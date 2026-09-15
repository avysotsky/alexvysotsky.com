using static CoreDTO.Logger.Enums;
using CoreDTO.Tools;
using CoreDTO.Logger.Telegram;
using System;
using System.IO;

namespace CoreDTO.Logger
{
    public static class Tools
    {
        private static object debugLock = new();
        public static void Debug(string logMessage, string prefix, LocalDateTime localTimeFunc, StreamWriter logger)
        {
            lock (debugLock)
            {
                Console.WriteLine($"{localTimeFunc():yyyy.MM.dd HH:mm:ss.fff} [{prefix}], {logMessage}");
                logger?.WriteLine($"{localTimeFunc():yyyy.MM.dd HH:mm:ss.fff} [{prefix}], {logMessage}");
                logger?.Flush();
            }
        }

        public static void DebugQueue(string logMessage, string prefix, LocalDateTime localTimeFunc, StringLogger logger)
        {
            Console.WriteLine($"{localTimeFunc():yyyy.MM.dd HH:mm:ss.fff} [{prefix}], {logMessage}");
            logger?.OnNext($"{localTimeFunc():yyyy.MM.dd HH:mm:ss.fff} [{prefix}], {logMessage}");
        }

        public static LogAction GetDebugFunction(string subsystem, LocalDateTime localTimeFunc,  StreamWriter logger, LogLevel logLevel)
        {
            return (s, l) =>
            {
                if (l <= logLevel)
                    Debug(s, subsystem, localTimeFunc, logger);
            };
        }

        public static LogAction GetDebugFunctionWithTelegram(LogAction action, TelegramClient client, string errorPattern)
        {
            return (s, l) =>
            {
                if (s.Contains(errorPattern))
                {
                    client.SendErrorMessage($"{DateTime.UtcNow:yyyyMMdd hh.mm} {errorPattern}");
                }
                action(s, l);
            };
        }

        public static LogAction GetDebugQueueFunction(string subsystem, LocalDateTime localTimeFunc, StringLogger logger, LogLevel logLevel)
        {
            return (s, l) =>
            {
                if (l <= logLevel)
                    DebugQueue(s, subsystem, localTimeFunc, logger);
            };
        }
    }
}
