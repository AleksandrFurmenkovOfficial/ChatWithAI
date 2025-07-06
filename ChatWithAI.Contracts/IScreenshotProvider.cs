using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IScreenshotProvider
    {
        Task<byte[]> CaptureScreenAsync(CancellationToken cancellationToken = default);
    }
}