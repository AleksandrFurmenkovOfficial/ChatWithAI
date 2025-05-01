using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;


namespace ChatWithAI.Contracts
{
    public enum ContentType
    {
        Text,
        Image
    }

    public class ContentItem
    {
        public ContentType Type { get; set; }
    }

    public class TextContentItem : ContentItem
    {
        public string? Text { get; set; }
    }

    public class JsonObjectContentItem : ContentItem
    {
        public object? JsonObject { get; set; }
    }

    public class ImageContentItem : ContentItem
    {
        public Uri? ImageUrl { get; set; }
        public string? ImageInBase64 { get; set; }
    }

    public class AudioContentItem : ContentItem
    {
        public Uri? AudioUrl { get; set; }
        public string? AudioInBase64 { get; set; }
    }

    public enum MessageRole
    {
        eRoleUnknown,
        eRoleSystem,
        eRoleAI,
        eRoleUser,
        eRoleFunction
    }

    public sealed class ChatMessage
    {
        public static readonly MessageId InternalMessageId = new("");

        public MessageId Id { get; set; }
        public bool IsSent { get; set; }
        public MessageRole Role { get; set; }
        public string Name { get; set; }
        public List<ContentItem> Content { get; set; }

        public ChatMessage(List<ContentItem> content,
            MessageRole role = MessageRole.eRoleSystem,
            string name = "")
        {
            Id = InternalMessageId;
            Role = role;
            Name = name;
            Content = content;
        }

        public ChatMessage()
        {
            Id = InternalMessageId;
            IsSent = false;
            Role = MessageRole.eRoleSystem;
            Name = "";
            Content = [];
        }

        public ChatMessage Clone()
        {
            var clonedContent = new List<ContentItem>();
            foreach (var item in Content)
            {
                if (item is TextContentItem textItem)
                {
                    clonedContent.Add(new TextContentItem { Text = (string)(textItem.Text!.Clone()) });
                }
                else if (item is ImageContentItem imageItem)
                {
                    clonedContent.Add(new ImageContentItem
                    {
                        ImageUrl = imageItem.ImageUrl == null ? null : new Uri(imageItem.ImageUrl.ToString()),
                        ImageInBase64 = imageItem.ImageInBase64 == null ? null : (string)imageItem.ImageInBase64.Clone()
                    });
                }
                else if (item is JsonObjectContentItem jsonObjectItem)
                {
                    clonedContent.Add(new JsonObjectContentItem
                    {
                        JsonObject = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(jsonObjectItem.JsonObject))!
                    });
                }
                else if (item is AudioContentItem audioContentItem)
                {
                    clonedContent.Add(new AudioContentItem
                    {
                        AudioUrl = audioContentItem.AudioUrl == null ? null : new Uri(audioContentItem.AudioUrl.ToString()),
                        AudioInBase64 = audioContentItem.AudioInBase64 == null ? null : (string)audioContentItem.AudioInBase64.Clone()
                    });
                }
            }

            return new ChatMessage(
                clonedContent,
                Role,
                Name)
            {
                IsSent = IsSent,
                Id = Id
            };
        }

        public void AddTextContent(string text)
        {
            Content.Add(new TextContentItem { Text = text });
        }

        public void AddImageContent(Uri imageUrl, string imageInBase64)
        {
            Content.Add(new ImageContentItem
            {
                ImageUrl = imageUrl,
                ImageInBase64 = imageInBase64
            });
        }

        public static TextContentItem CreateText(string text)
        {
            return new TextContentItem
            {
                Text = text
            };
        }

        public static JsonObjectContentItem CreateJsonObject(object jsonObject)
        {
            return new JsonObjectContentItem
            {
                JsonObject = jsonObject
            };
        }

        public static ImageContentItem CreateImage(Uri? uri, string? base64Webp)
        {
            return new ImageContentItem
            {
                ImageUrl = uri,
                ImageInBase64 = base64Webp
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

        public static List<TextContentItem> GetTextContentItem(ChatMessage message)
        {
            return message.Content.OfType<TextContentItem>().ToList();
        }

        public static void RemoveTextContent(ChatMessage message)
        {
            var toRemove = message.Content.OfType<TextContentItem>().ToList();
            foreach (var item in toRemove)
            {
                message.Content.Remove(item);
            }
        }

        public static string GetText(ChatMessage message)
        {
            var textParts = GetTextContentItem(message).Select(tp => tp.Text);
            return string.Join("\n", textParts);
        }

        public static List<ImageContentItem> GetImageContentItem(ChatMessage message)
        {
            return message.Content.OfType<ImageContentItem>().ToList();
        }

        public static List<AudioContentItem> GetAudioContentItem(ChatMessage message)
        {
            return message.Content.OfType<AudioContentItem>().ToList();
        }

        public static bool IsPhotoMessage(ChatMessage message)
        {
            return message.Content.OfType<ImageContentItem>().ToList().Count > 0;
        }
    }
}