using System;
using System.Collections.Generic;
using System.Text;

namespace CASCEdit.Logging
{
    public interface ICASCLog
    {
        void LogCritical(string message, params object[] args);
        void LogDebug(string message, params object[] args);
        void LogError(string message, params object[] args);
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
    }
}
