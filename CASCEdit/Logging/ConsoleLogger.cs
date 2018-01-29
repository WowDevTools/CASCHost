using System;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit.Logging
{
    public class ConsoleLogger : ICASCLog
    {
        public void LogCritical(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogDebug(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogError(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogInformation(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogWarning(string message, params object[] args) => Console.WriteLine(message, args);
    }
}
