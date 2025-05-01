namespace ChatWithAI.Contracts
{
    public interface IMessengerBotSource
    {
        object NewBot();
        object Bot();
    }
}