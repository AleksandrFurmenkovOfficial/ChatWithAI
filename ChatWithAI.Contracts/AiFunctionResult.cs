using System;
using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public class AiFunctionResult(List<ContentItem> result)
    {
        public AiFunctionResult(string text) : this(new List<ContentItem>() { ChatMessage.CreateText(text) }) { }
        public AiFunctionResult(string text, Uri? imageUri, string? imageBase64 = null) : this(new List<ContentItem>() { ChatMessage.CreateText(text), ChatMessage.CreateImage(imageUri, imageBase64) }) { }
        public List<ContentItem> Content { get; } = result;
    }
}