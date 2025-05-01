using System;
using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public sealed class AiFunctionErrorResult : AiFunctionResult
    {
        public AiFunctionErrorResult(List<ContentItem> result) : base(result) { }
        public AiFunctionErrorResult(string text) : base(text) { }
        public AiFunctionErrorResult(string text, Uri uri) : base(text, uri) { }
    }
}