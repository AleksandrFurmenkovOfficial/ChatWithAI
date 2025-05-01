using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatFactory
    {
        Task<IChat> CreateChat(string chatId, string modeName, bool useExpiration);
    }
}