using Microsoft.Extensions.Logging;

namespace ChatWithAI.Core
{
    public class Logger : Contracts.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public Logger(ILogger<Logger> logger) // TODO: how does it works?
        {
            _logger = logger;
        }

        public void LogDebugMessage(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public void LogInfoMessage(string message)
        {
            Log(LogLevel.Information, message);
        }

        public void LogErrorMessage(string message)
        {
            Log(LogLevel.Error, message);
        }

        public void LogException(Exception e)
        {
            _logger.Log(
                LogLevel.Error,
                new EventId(0, "Exception"),
                e,
                e,
                (ex, exception) => ex.ToString());
        }

        private void Log(LogLevel level, string message)
        {
            _logger.Log(
                level,
                new EventId(0, level.ToString()),
                message,
                null,
                (state, _) => state);
        }
    }
}