namespace ChatWithAI.Core
{
    public sealed class ChatModeLoader(IModeStorage modeStorage) : IChatModeLoader
    {
        public async Task<ChatMode> GetChatMode(string modeName, CancellationToken cancellationToken = default)
        {
            var systemMessage = await modeStorage.GetContent(modeName, cancellationToken).ConfigureAwait(false);
            systemMessage +=
                "\n\nThe following are the only tags currently supported in the Telegram chat where you are communicating with a World and Users:\n" +
                "<b>bold</b> - use to emphasize, <strong>bold</strong>\n" +
                "<i>italic</i>, <em>italic</em>\n" +
                "<s>strikethrough</s>, <strike>strikethrough</strike>, <del>strikethrough</del>\n" +
                "<a href=\"http://www.example.com/\">inline URL</a>\n" +
                "<pre><code class=\"language-python\">pre-formatted fixed-width code block written in the Python programming language</code></pre>\n" +
                "Please pay attention, these are ALL the tags that are supported! No <br>, <ul>, <p>, <li>, <image>, <span>, or anything else!";

            return new ChatMode
            {
                AiName = $"Vivy_{modeName}",
                AiSettings = systemMessage
            };
        }
    }
}