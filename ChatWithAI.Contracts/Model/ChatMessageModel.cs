using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChatWithAI.Contracts.Model
{
    /// <summary>
    /// Pure domain model for a chat message.
    /// Contains only data relevant for AI conversation history.
    /// No UI-specific properties (IsSent, buttons, child messages for splitting).
    /// </summary>
    public sealed class ChatMessageModel
    {
        public ModelMessageId Id { get; set; }
        public long? OriginalMessageId { get; set; }
        public MessageRole Role { get; set; }
        public string Name { get; set; }
        public List<ContentItem> Content { get; set; }
        public DateTime CreatedAt { get; set; }

        public ChatMessageModel()
        {
            Id = ModelMessageId.New();
            Role = MessageRole.eRoleSystem;
            Name = string.Empty;
            Content = [];
            CreatedAt = DateTime.UtcNow;
        }

        public ChatMessageModel(List<ContentItem> content, MessageRole role, string name = "")
        {
            Id = ModelMessageId.New();
            Role = role;
            Name = name;
            Content = content;
            CreatedAt = DateTime.UtcNow;
        }

        public void AddTextContent(string text)
        {
            Content.Add(new TextContentItem { Text = text });
        }

        public void AddImageContent(Uri? imageUrl, string? imageInBase64)
        {
            Content.Add(new ImageContentItem
            {
                ImageUrl = imageUrl,
                ImageInBase64 = imageInBase64
            });
        }

        public string GetText()
        {
            var textParts = Content.OfType<TextContentItem>().Select(tp => tp.Text);
            return string.Join("\n\n", textParts);
        }

        public List<TextContentItem> GetTextContentItems()
        {
            return Content.OfType<TextContentItem>().ToList();
        }

        public List<ImageContentItem> GetImageContentItems()
        {
            return Content.OfType<ImageContentItem>().ToList();
        }

        public List<AudioContentItem> GetAudioContentItems()
        {
            return Content.OfType<AudioContentItem>().ToList();
        }

        public List<DocumentContentItem> GetDocumentContentItems()
        {
            return Content.OfType<DocumentContentItem>().ToList();
        }

        public List<VideoContentItem> GetVideoContentItems()
        {
            return Content.OfType<VideoContentItem>().ToList();
        }

        public bool HasImage()
        {
            return Content.OfType<ImageContentItem>().Any();
        }

        public bool HasAudio()
        {
            return Content.OfType<AudioContentItem>().Any();
        }

        public bool HasDocument()
        {
            return Content.OfType<DocumentContentItem>().Any();
        }

        public bool HasVideo()
        {
            return Content.OfType<VideoContentItem>().Any();
        }

        public bool IsEmpty()
        {
            if (Content.Count == 0)
            {
                return true;
            }

            var hasText = Content.OfType<TextContentItem>().Any(t => !string.IsNullOrEmpty(t.Text));
            var hasImage = Content.OfType<ImageContentItem>().Any(i => i.ImageUrl != null || !string.IsNullOrEmpty(i.ImageInBase64));
            var hasAudio = Content.OfType<AudioContentItem>().Any(a => a.AudioUrl != null || !string.IsNullOrEmpty(a.AudioInBase64));
            var hasDocument = Content.OfType<DocumentContentItem>().Any(d => d.DocumentUrl != null || !string.IsNullOrEmpty(d.DocumentInBase64));
            var hasVideo = Content.OfType<VideoContentItem>().Any(v => v.VideoUrl != null || !string.IsNullOrEmpty(v.VideoInBase64));

            return !hasText && !hasImage && !hasAudio && !hasDocument && !hasVideo;
        }

        public ChatMessageModel Clone()
        {
            var clonedContent = new List<ContentItem>();

            foreach (var item in Content)
            {
                if (item == null) continue;

                ContentItem? newItem = item switch
                {
                    TextContentItem t => new TextContentItem { Text = t.Text, Signature = t.Signature },
                    ImageContentItem i => i.CloneWithLoader(i.Loader),
                    JsonObjectContentItem j => new JsonObjectContentItem
                    {
                        JsonObject = CloneJsonObject(j.JsonObject),
                        Signature = j.Signature
                    },
                    AudioContentItem a => a.CloneWithLoader(a.Loader),
                    DocumentContentItem d => d.CloneWithLoader(d.Loader),
                    VideoContentItem v => v.CloneWithLoader(v.Loader),
                    _ => null
                };

                if (newItem != null)
                {
                    clonedContent.Add(newItem);
                }
            }

            return new ChatMessageModel
            {
                Id = Id,
                OriginalMessageId = OriginalMessageId,
                Role = Role,
                Name = Name,
                Content = clonedContent,
                CreatedAt = CreatedAt
            };
        }

        private static object? CloneJsonObject(object? jsonObject)
        {
            if (jsonObject == null) return null;

            if (jsonObject is Newtonsoft.Json.Linq.JToken token)
            {
                return token.DeepClone();
            }

            var serialized = JsonConvert.SerializeObject(jsonObject);
            return JsonConvert.DeserializeObject<object>(serialized);
        }

        // Static factory methods
        public static TextContentItem CreateText(string text)
        {
            return new TextContentItem { Text = text };
        }

        public static ImageContentItem CreateImage(Uri? uri, string? base64)
        {
            return new ImageContentItem
            {
                ImageUrl = uri,
                ImageInBase64 = base64
            };
        }

        public static AudioContentItem CreateAudio(Uri uri, string audioInBase64)
        {
            return new AudioContentItem
            {
                AudioUrl = uri,
                AudioInBase64 = audioInBase64
            };
        }

        public static JsonObjectContentItem CreateJsonObject(object jsonObject)
        {
            return new JsonObjectContentItem
            {
                JsonObject = jsonObject
            };
        }
    }
}
