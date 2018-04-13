using System;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit.Logging
{
    public class ConsoleLogger : ICASCLog
    {
		public void LogAndThrow(LogType type, string message, params object[] args)
		{
			switch(type)
			{
				case LogType.Critical:
					LogCritical(message, args);
					break;
				case LogType.Debug:
					LogDebug(message, args);
					break;
				case LogType.Error:
					LogError(message, args);
					break;
				case LogType.Information:
					LogInformation(message, args);
					break;
				default:
					LogWarning(message, args);
					break;
			}

			throw new Exception(string.Format(message, args));
		}

		public void LogCritical(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogDebug(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogError(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogInformation(string message, params object[] args) => Console.WriteLine(message, args);

        public void LogWarning(string message, params object[] args) => Console.WriteLine(message, args);
    }
}
