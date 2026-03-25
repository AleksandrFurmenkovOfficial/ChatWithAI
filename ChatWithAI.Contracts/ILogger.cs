using System;

namespace ChatWithAI.Contracts
{
    public interface ILogger
    {
        void LogInfoMessage(string message);

        void LogDebugMessage(string message);

        void LogErrorMessage(string message);

        void LogException(Exception e);
    }
}
