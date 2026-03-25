using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAiFunctionsManager
    {
        Task<AiFunctionResult> Execute(IAiAgent api, string functionName, string parameters, string userId, CancellationToken cancellationToken = default);
        Dictionary<string, string> ConvertParameters(string parameters);
        string Representation();
    }
}