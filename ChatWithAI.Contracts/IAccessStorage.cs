using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAccessStorage
    {
        Task<string> GetAllowedUsers(CancellationToken cancellationToken = default);
        Task<string> GetPremiumUsers(CancellationToken cancellationToken = default);
    }
}
