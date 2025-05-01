using RxTelegram.Bot;


namespace ChatWithAI.Core
{
    public sealed class TelegramBotSource(string telegramBotKey) : IMessengerBotSource
    {
        private ITelegramBot? bot;

        public object Bot() => bot ?? throw new ArgumentNullException(nameof(bot));

        public object NewBot()
        {
            var newBot = new TelegramBot(telegramBotKey);
            _ = Interlocked.CompareExchange(ref bot, newBot, bot);
            return newBot;
        }
    }
}