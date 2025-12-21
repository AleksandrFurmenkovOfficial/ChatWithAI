using System.Threading.Channels;

namespace ChatWithAI.Providers.Google
{
    internal sealed class GeminiStreamingResponse : IAiStreamingResponse
    {
        private string m_accumulatedText = string.Empty;

        private readonly Channel<string> m_textDeltas = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        private const int STATE_ACTIVE = 0;
        private const int STATE_COMPLETED = 1;
        private const int STATE_DISPOSED = 2;

        private int m_state = STATE_ACTIVE;

        private List<GeminiPart>? m_collectedParts;

        public void SetCollectedParts(List<GeminiPart> parts)
        {
            Volatile.Write(ref m_collectedParts, parts.ToList());
        }

        public IAsyncEnumerable<string> GetTextDeltasAsync(CancellationToken cancellationToken = default)
            => m_textDeltas.Reader.ReadAllAsync(cancellationToken);

        public List<ContentItem>? GetStructuredContent()
        {
            var collectedParts = Volatile.Read(ref m_collectedParts);
            var completeStreamedText = Volatile.Read(ref m_accumulatedText);

            if (collectedParts == null) return null;

            var result = new List<ContentItem>();
            bool textItemAdded = false;

            foreach (var part in collectedParts)
            {
                if (part.FunctionCall != null)
                {
                    var funcCallDict = new Dictionary<string, object>
                    {
                        ["function_call"] = new
                        {
                            name = part.FunctionCall.Name,
                            args = part.FunctionCall.Args
                        }
                    };

                    var item = new JsonObjectContentItem { JsonObject = funcCallDict };
                    if (!string.IsNullOrEmpty(part.ThoughtSignature))
                        item.Signature = part.ThoughtSignature;

                    result.Add(item);
                }
                else if (part.FunctionResponse != null)
                {
                    var funcRespDict = new Dictionary<string, object>
                    {
                        ["function_response"] = new
                        {
                            name = part.FunctionResponse.Name,
                            response = part.FunctionResponse.Response
                        }
                    };

                    result.Add(new JsonObjectContentItem { JsonObject = funcRespDict });
                }
                else if (part.InlineData != null)
                {
                    var mimeType = part.InlineData.MimeType ?? "application/octet-stream";
                    ContentItem item = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? new ImageContentItem
                        {
                            MimeType = mimeType,
                            ImageInBase64 = part.InlineData.Data
                        }
                        : mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                            ? new AudioContentItem
                            {
                                MimeType = mimeType,
                                AudioInBase64 = part.InlineData.Data
                            }
                            : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                                ? new VideoContentItem
                                {
                                    MimeType = mimeType,
                                    VideoInBase64 = part.InlineData.Data
                                }
                                : new DocumentContentItem
                                {
                                    MimeType = mimeType,
                                    DocumentInBase64 = part.InlineData.Data
                                };

                    if (!string.IsNullOrEmpty(part.ThoughtSignature))
                        item.Signature = part.ThoughtSignature;

                    result.Add(item);
                }
                else if (!string.IsNullOrEmpty(part.Text) && part.Thought != true)
                {
                    if (!textItemAdded && !string.IsNullOrEmpty(completeStreamedText))
                    {
                        var item = new TextContentItem { Text = completeStreamedText };
                        if (!string.IsNullOrEmpty(part.ThoughtSignature))
                            item.Signature = part.ThoughtSignature;

                        result.Add(item);
                        textItemAdded = true;
                    }
                    else
                    {
                        var item = new TextContentItem { Text = part.Text };
                        if (!string.IsNullOrEmpty(part.ThoughtSignature))
                            item.Signature = part.ThoughtSignature;

                        result.Add(item);
                    }
                }
            }

            if (!textItemAdded && !string.IsNullOrEmpty(completeStreamedText))
            {
                string? signature = null;
                for (int i = collectedParts.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(collectedParts[i].ThoughtSignature))
                    {
                        signature = collectedParts[i].ThoughtSignature;
                        break;
                    }
                }

                var item = new TextContentItem { Text = completeStreamedText };
                if (!string.IsNullOrEmpty(signature))
                    item.Signature = signature;

                result.Add(item);
            }

            return result;
        }

        public Task WriteTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Task.CompletedTask;

            while (true)
            {
                if (Volatile.Read(ref m_state) != STATE_ACTIVE)
                    return Task.CompletedTask;

                string currentSnapshot = Volatile.Read(ref m_accumulatedText);

                string newAccumulatedText;
                string delta;

                if (currentSnapshot.Length == 0)
                {
                    delta = text;
                    newAccumulatedText = text;
                }
                else if (text.StartsWith(currentSnapshot, StringComparison.Ordinal))
                {
                    delta = text.Substring(currentSnapshot.Length);
                    newAccumulatedText = text;
                }
                else if (currentSnapshot.StartsWith(text, StringComparison.Ordinal))
                {
                    return Task.CompletedTask;
                }
                else
                {
                    delta = text;
                    newAccumulatedText = currentSnapshot + text;
                }

                if (string.IsNullOrEmpty(delta))
                    return Task.CompletedTask;

                var originalValue = Interlocked.CompareExchange(
                    ref m_accumulatedText,
                    newAccumulatedText,
                    currentSnapshot);

                if (ReferenceEquals(originalValue, currentSnapshot))
                {
                    if (Volatile.Read(ref m_state) == STATE_ACTIVE)
                        m_textDeltas.Writer.TryWrite(delta);

                    return Task.CompletedTask;
                }
            }
        }

        public void Complete()
        {
            int previousState = Interlocked.CompareExchange(ref m_state, STATE_COMPLETED, STATE_ACTIVE);
            if (previousState == STATE_ACTIVE)
                m_textDeltas.Writer.TryComplete();
        }

        public ValueTask DisposeAsync()
        {
            int previousState = Interlocked.Exchange(ref m_state, STATE_DISPOSED);
            if (previousState == STATE_ACTIVE)
                m_textDeltas.Writer.TryComplete();

            return ValueTask.CompletedTask;
        }
    }
}
