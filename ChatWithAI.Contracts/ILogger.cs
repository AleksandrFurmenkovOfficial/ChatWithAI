using System;

namespace ChatWithAI.Contracts
{
    public interface ILogger
    {
        public void LogDebugMessage(string message);

        public void LogInfoMessage(string message);

        public void LogErrorMessage(string message);

        public void LogException(Exception e);
    }
}
