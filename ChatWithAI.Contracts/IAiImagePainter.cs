using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAiImagePainter
    {
        Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default);
    }
}