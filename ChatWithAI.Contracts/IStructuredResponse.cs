using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public interface IStructuredResponse
    {
        List<ContentItem>? GetStructuredContent();
    }
}
