using System;
using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public class AiFunctionResult(List<ContentItem> result)
    {
        public AiFunctionResult(string text) : this(new List<ContentItem>() { new TextContentItem { Text = text } }) { }
        public AiFunctionResult(string text, Uri? imageUri, string? imageBase64 = null) : this(new List<ContentItem>() { new TextContentItem { Text = text }, new ImageContentItem { ImageUrl = imageUri, ImageInBase64 = imageBase64 } }) { }
        public List<ContentItem> Content { get; } = result;
    }
}