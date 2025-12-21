using Telegram.Bot;

namespace ChatWithAI.Core
{
    public sealed class TelegramBotSource(string telegramBotKey, IHttpClientFactory httpClientFactory) : IMessengerBotSource, IDisposable
    {
        private readonly string _telegramBotKey = telegramBotKey;
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

        private readonly TimeSpan _resetInterval = TimeSpan.FromSeconds(25);
        private readonly SemaphoreSlim _sync = new(1, 1);

        private Task<ITelegramBotClient>? _botTask;
        private DateTime _lastResetTime = DateTime.MinValue;
        private bool _disposed;

        public async Task NewBotAsync()
        {
            await _sync.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;

                var needNew =
                    _botTask is null ||
                    now - _lastResetTime >= _resetInterval ||
                    _botTask.IsFaulted ||
                    _botTask.IsCanceled;

                if (needNew)
                {
                    _lastResetTime = now;
                    _botTask = CreateBotAsync();
                    await _botTask!.ConfigureAwait(false);
                }
            }
            finally
            {
                _sync.Release();
            }
        }

        public object? Bot()
        {
            if (_botTask is { IsCompletedSuccessfully: true } task)
                return task.Result;
            return null;
        }

        private async Task<ITelegramBotClient> CreateBotAsync()
        {
            var httpClient = _httpClientFactory.CreateClient("telegram_bot_client");
            var options = new TelegramBotClientOptions(_telegramBotKey);
            var bot = new TelegramBotClient(options, httpClient);
            await InitializeBotNetworkAsync(bot).ConfigureAwait(false);
            return bot;
        }

        private static async Task InitializeBotNetworkAsync(ITelegramBotClient client)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

                await client.DeleteWebhook(cancellationToken: cts.Token).ConfigureAwait(false);
                await client.DropPendingUpdates(cancellationToken: cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to init bot: {ex}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _sync.Dispose();
            _disposed = true;
        }
    }
}
