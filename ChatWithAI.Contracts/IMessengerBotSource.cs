using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IMessengerBotSource
    {
        Task NewBotAsync();
        object? Bot();
    }
}