using CASCEdit.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CASCHost
{
    public class Logger : ICASCLog, IDisposable
    {
        private const string LOG_FILE = "log.txt";
        private readonly ILogger _logger;
		private volatile bool _shouldRun = true;
        private ConcurrentQueue<string> logEntryQueue = new ConcurrentQueue<string>();
		private Timer timer;

        public Logger(ILogger logger)
        {
            _logger = logger;
            File.Delete(LOG_FILE);
			timer = new Timer(DoSave, null, 1000, Timeout.Infinite);
		}


		public void LogCritical(string message, params object[] args)
        {
            _logger.LogCritical(message, args);
            Enqueue("CRIT: " + string.Format(message, args));
        }

        public void LogDebug(string message, params object[] args)
        {
            _logger.LogDebug(message, args);
            Enqueue(string.Format(message, args));
        }

        public void LogError(string message, params object[] args)
        {
            _logger.LogError(message, args);
            Enqueue("ERR: " + string.Format(message, args));
        }

        public void LogInformation(string message, params object[] args)
        {
            _logger.LogInformation(message, args);
            Enqueue(string.Format(message, args));
        }

        public void LogConsole(string message, params object[] args)
        {
            _logger.LogInformation(message, args);
        }

        public void LogFile(string message, params object[] args)
        {
            Enqueue(string.Format(message, args));
        }

        public void LogWarning(string message, params object[] args)
        {
            _logger.LogWarning(message, args);
            Enqueue("WARN: " + string.Format(message, args));
        }


        private void Enqueue(string message)
        {
            message = $"[{DateTime.Now}] {message}";
            logEntryQueue.Enqueue(message);        
        }

		private void DoSave(object obj)
		{
			if (logEntryQueue.Count == 0 || !_shouldRun)
				return;

			_shouldRun = false;

			using (var sw = new StreamWriter(LOG_FILE, true, System.Text.Encoding.UTF8))
			{
				while (logEntryQueue.Count > 0)
					if (logEntryQueue.TryDequeue(out string message))
						sw.WriteLine(message);
			}

			_shouldRun = true;
		}


		public void Dispose()
		{
			timer.Dispose();
		}
	}
}
