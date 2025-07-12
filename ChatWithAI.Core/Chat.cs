using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.ChatMessageActions;
using System.Diagnostics;

namespace ChatWithAI.Core
{
    public sealed class Chat(AppConfig config, string chatId, IAiAgentFactory aiAgentFactory, IMessenger messenger, ILogger logger, ChatCache cache, bool useExpiration) : IChat
    {
        private const int MessageUpdateStepInCharsCount = 84;

        private IAiAgent? aiAgent;
        private ChatMode? chatMode;

        private List<ChatTurn> GetTurns()
        {
            var cacheData = cache.Get<List<ChatTurn>>(Id);
            if (cacheData == null || cacheData.Count == 0)
                return [];

            return cacheData;
        }

        private void SetTurns(List<ChatTurn> turns)
        {
            if (turns.Count == 0)
            {
                cache.Remove(Id);
                return;
            }

            var expiration = TimeSpan.MaxValue;
            if (useExpiration)
            {
                expiration = TimeSpan.FromMinutes(config.ChatCacheAliveInMinutes);
            }

            cache.Set(Id, turns, expiration);
        }

        public string Id { get; } = chatId;

        public async Task SendSomethingGoesWrong(CancellationToken cancellationToken)
        {
            var message = new ChatMessage([ChatMessage.CreateText(Strings.SomethingGoesWrong)]);
            var messageId = await messenger.SendTextMessage(Id, message, [RetryAction.Id], cancellationToken).ConfigureAwait(false);
            message.Id = new MessageId(messageId);

            AddAnswerMessage(message);
        }

        public Task SendSystemMessage(string content, CancellationToken cancellationToken = default)
        {
            return messenger.SendTextMessage(Id, new ChatMessage { Content = [ChatMessage.CreateText(content)] }, null, cancellationToken);
        }

        public void AddMessages(List<ChatMessage> messages)
        {
            ResetLastMessageButtons().GetAwaiter().GetResult();
            AddNewUsersMessages(messages);
        }

        public async Task DoResponseToLastMessage(CancellationToken cancellationToken)
        {
            await ResetLastMessageButtons().ConfigureAwait(false);
            await SendResponseTargetMessage(cancellationToken).ConfigureAwait(false);
            await DoStreamResponseToLastMessage(cancellationToken).ConfigureAwait(false);
        }

        public async Task Reset(CancellationToken cancellationToken)
        {
            await ResetLastMessageButtons().ConfigureAwait(false);
            Clear();
        }

        private void Clear()
        {
            var turns = GetTurns();
            turns.Clear();
            SetTurns(turns);
        }

        public async Task RegenerateLastResponse(CancellationToken cancellationToken)
        {
            await RemoveResponse(default).ConfigureAwait(false);
            await SendResponseTargetMessage(default).ConfigureAwait(false);
            await DoStreamResponseToLastMessage(cancellationToken).ConfigureAwait(false);
        }

        public async Task ContinueLastResponse(CancellationToken cancellationToken)
        {
            AddMessages([new ChatMessage([ChatMessage.CreateText(Strings.Continue)],
                MessageRole.eRoleUser, Strings.RoleSystem)]);
            await DoResponseToLastMessage(cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveResponse(CancellationToken cancellationToken)
        {
            var turns = GetTurns();
            if (turns.Count == 0)
                return;

            var lastTurn = turns.Last();
            if (lastTurn.Count == 0)
                return;

            foreach (var message in lastTurn.Where(message => message.IsSent))
            {
                try
                {
                    await messenger.DeleteMessage(Id, message.Id, cancellationToken).ConfigureAwait(false);
                    message.IsSent = false;
                }
                catch (Exception e)
                {
                    logger?.LogException(e);
                }
            }

            lastTurn.RemoveRange(1, lastTurn.Count - 1);
            SetTurns(turns);
        }

        private async Task ResetLastMessageButtons()
        {
            var turns = GetTurns();
            if (turns.Count == 0)
                return;

            try
            {
                ChatMessage? lastMessage = null;
                ChatTurn? turnOfLastMessage = null;

                foreach (var turn in Enumerable.Reverse(turns))
                {
                    var lastSentMessageInTurn = turn.LastOrDefault(m => m.IsSent);
                    if (lastSentMessageInTurn != null)
                    {
                        lastMessage = lastSentMessageInTurn;
                        turnOfLastMessage = turn;
                        break;
                    }
                }

                if (lastMessage == null)
                    return;

                if (!lastMessage.IsActive)
                    return;

                var lastMessageContent = lastMessage.Content;
                bool isEmpty = lastMessageContent == null
                    || lastMessageContent.Count == 0
                    || (string.IsNullOrEmpty(lastMessageContent.OfType<TextContentItem>().FirstOrDefault()?.Text)
                       && string.IsNullOrEmpty(lastMessageContent.OfType<ImageContentItem>().FirstOrDefault()?.ImageInBase64));

                if (isEmpty && turnOfLastMessage != null)
                {
                    turnOfLastMessage.Remove(lastMessage);
                    if (turnOfLastMessage.Count == 0)
                    {
                        turns.Remove(turnOfLastMessage);
                        SetTurns(turns);
                    }
                    await messenger.DeleteMessage(Id, lastMessage.Id, default).ConfigureAwait(false);
                }
                else
                {
                    await UpdateMessage(lastMessage, lastMessageContent!, null, default).ConfigureAwait(false);
                    lastMessage.IsActive = false;
                }
            }
            catch (Exception e)
            {
                logger?.LogException(e);
            }
        }

        private async Task UpdateMessage(ChatMessage message, List<ContentItem> newContent,
            IEnumerable<ActionId>? newActions = null, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("UpdateMessage");
            try
            {
                var newText = newContent.OfType<TextContentItem>().FirstOrDefault()?.Text ?? Strings.InitAnswerTemplate;
                if (ChatMessage.IsPhotoMessage(message))
                {
                    Debug.WriteLine("IsPhotoMessage newText=" + newText);
                    await messenger.EditPhotoMessage(Id, message.Id, newText, newActions, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Debug.WriteLine("EditTextMessage newText=" + newText);
                    await messenger.EditTextMessage(Id, message.Id, newText, newActions, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                logger?.LogException(e);
            }
        }

        private void AddNewUsersMessages(List<ChatMessage> newMessagesFromUser)
        {
            var turns = GetTurns();
            turns.Add([.. newMessagesFromUser]);
            SetTurns(turns);
        }

        private void AddAnswerMessage(ChatMessage responseTargetMessage)
        {
            var turns = GetTurns();
            turns.Last().Add(responseTargetMessage);
            SetTurns(turns);
        }

        private async Task DoStreamResponseToLastMessage(CancellationToken cancellationToken = default)
        {
            var turns = GetTurns();
            var messagesState = turns.SelectMany(subList => subList.Select(item => item.Clone()).ToList()).ToList();
            Func<ResponseStreamChunk, Task<bool>> resultGetter = (chunk) => ProcessResponseStreamChunkAsync(chunk, cancellationToken);
            await aiAgent!.GetResponse(Id, messagesState, resultGetter, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> ProcessResponseStreamChunkAsync(
            ResponseStreamChunk contentDelta,
            CancellationToken cancellationToken = default)
        {
            var turns = GetTurns();
            if (cancellationToken.IsCancellationRequested || turns == null || turns.Count == 0 || turns.Last() == null || turns.Last().Count == 0)
                return true;

            var sentMessages = turns.Last().Where(m => m.IsSent);
            if (!sentMessages.Any())
                return true;

            try
            {
                var responseTargetMessage = turns.Last().Where(m => m.IsSent).Last();

                bool isDeltaHaveOnlyTextContent = contentDelta.Messages == null || contentDelta.Messages.Count == 0;
                var isFinalMessageUpdate = contentDelta is LastResponseStreamChunk || cancellationToken.IsCancellationRequested;
                if (isDeltaHaveOnlyTextContent || isFinalMessageUpdate)
                {
                    var textContent = ChatMessage.GetTextContentItem(responseTargetMessage)[0];
                    textContent.Text += contentDelta.TextDelta ?? "";

                    string extraDelta = "";
                    CutMessageContent(responseTargetMessage, ref extraDelta);

                    if ((textContent.Text.Length % MessageUpdateStepInCharsCount) != 1 || isFinalMessageUpdate)
                    {
                        await UpdateTargetMessage(responseTargetMessage, isFinalMessageUpdate, default).ConfigureAwait(false);
                    }

                    if (!string.IsNullOrEmpty(extraDelta))
                    {
                        await SendResponseTargetMessage(default).ConfigureAwait(false);

                        // We need to get turns again as SendResponseTargetMessage may have updated it
                        turns = GetTurns();
                        var newResponseTargetMessage = turns.Last().Where(m => m.IsSent).Last();
                        TextContentItem? textContentNew = newResponseTargetMessage.Content.OfType<TextContentItem>().FirstOrDefault();
                        if (textContentNew != null)
                        {
                            textContentNew.Text = extraDelta;
                        }

                        await UpdateTargetMessage(newResponseTargetMessage, isFinalMessageUpdate, default).ConfigureAwait(false);

                        // Reset buttons on previous
                        await UpdateMessage(responseTargetMessage, responseTargetMessage.Content, null, default).ConfigureAwait(false);
                    }

                    SetTurns(turns);
                    return isFinalMessageUpdate;
                }

                return await ProcessFunctionResult(responseTargetMessage, contentDelta, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger?.LogException(e);
                return true;
            }
        }

        private void CutMessageContent(ChatMessage responseTargetMessage, ref string extraDelta)
        {
            extraDelta = "";
            var textContent = responseTargetMessage.Content.OfType<TextContentItem>().FirstOrDefault();
            if (string.IsNullOrEmpty(textContent?.Text))
                return;

            string fullContentCopy = (string)textContent.Text.Clone();
            bool hasMedia = ChatMessage.IsPhotoMessage(responseTargetMessage);
            switch (hasMedia)
            {
                case true when fullContentCopy.Length > messenger.MaxPhotoMessageLen():
                    textContent.Text = fullContentCopy[..messenger.MaxPhotoMessageLen()];
                    extraDelta = fullContentCopy[messenger.MaxPhotoMessageLen()..];
                    break;
                case false when fullContentCopy.Length > messenger.MaxTextMessageLen():
                    textContent.Text = fullContentCopy[..messenger.MaxTextMessageLen()];
                    extraDelta = fullContentCopy[messenger.MaxTextMessageLen()..];
                    break;
            }
        }

        private async Task<bool> ProcessFunctionResult(
            ChatMessage responseTargetMessage,
            ResponseStreamChunk contentDelta,
            CancellationToken cancellationToken = default)
        {
            var functionCallMessage = contentDelta.Messages.First();
            AddAnswerMessage(functionCallMessage);

            var functionResultMessage = contentDelta.Messages.Last();
            AddAnswerMessage(functionResultMessage);

            bool imageMessage = ChatMessage.IsPhotoMessage(functionResultMessage);
            if (imageMessage)
            {
                var turns = GetTurns();
                var textContent = ChatMessage.GetTextContentItem(responseTargetMessage)[0];
                textContent.Text = (textContent.Text == Strings.InitAnswerTemplate ? "" : textContent.Text) ?? "";
                var previousTextContent = textContent.Text;

                await messenger.DeleteMessage(Id, responseTargetMessage.Id, default)
                    .ConfigureAwait(false);

                List<ContentItem> newContent = [ChatMessage.CreateText(previousTextContent)];

                var functionResultMessages = ChatMessage.GetImageContentItem(functionResultMessage);

                foreach (var image in functionResultMessages)
                {
                    newContent.Add(image);
                }

                var responseTargetMessageNew = new ChatMessage()
                {
                    Name = aiAgent?.AiName ?? Strings.DefaultName,
                    Role = MessageRole.eRoleAI,
                    Content = newContent
                };

                responseTargetMessageNew.Id = new MessageId(await messenger.SendPhotoMessage(Id, responseTargetMessageNew, [StopAction.Id], cancellationToken).ConfigureAwait(false));

                AddAnswerMessage(responseTargetMessageNew);
                responseTargetMessage = responseTargetMessageNew;

                SetTurns(turns);
            }

            await DoStreamResponseToLastMessage(cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task UpdateTargetMessage(ChatMessage responseTargetMessage, bool finalUpdate,
            CancellationToken cancellationToken = default)
        {
            bool hasContent = ChatMessage.GetTextContentItem(responseTargetMessage)[0].Text?.Length > 0;
            var newContent = hasContent ? responseTargetMessage.Content : [ChatMessage.CreateText(Strings.InitAnswerTemplate)];
            List<ActionId> actionsByContent = hasContent ? [ContinueAction.Id, RegenerateAction.Id] : [RetryAction.Id];
            List<ActionId> actions = finalUpdate ? actionsByContent : [StopAction.Id];

            // Fix for google models
            if (finalUpdate)
            {
                var textContent = newContent.OfType<TextContentItem>().FirstOrDefault();
                if (textContent != null && textContent.Text != null)
                {
                    var lastTagToRemove = "Here is the original image:";
                    if (textContent.Text.EndsWith(lastTagToRemove, StringComparison.InvariantCulture))
                    {
                        textContent.Text = textContent.Text[..^lastTagToRemove.Length];
                    }
                }
            }

            await UpdateMessage(responseTargetMessage, newContent, actions, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ChatMessage> SendResponseTargetMessage(CancellationToken cancellationToken)
        {
            var responseTargetMessage = new ChatMessage
            {
                Role = MessageRole.eRoleAI,
                Name = chatMode?.AiName ?? Strings.DefaultName,
                Content = [ChatMessage.CreateText(Strings.InitAnswerTemplate)]
            };

            responseTargetMessage.Id = new MessageId(await messenger
                .SendTextMessage(Id, responseTargetMessage, [CancelAction.Id], cancellationToken)
                .ConfigureAwait(false));

            responseTargetMessage.Content = [ChatMessage.CreateText("")];

            AddAnswerMessage(responseTargetMessage);
            return responseTargetMessage;
        }

        public void SetMode(ChatMode mode)
        {
            chatMode = mode;
            RecreateAiAgent();
        }

        public void RecreateAiAgent()
        {
            var old = aiAgent;
            aiAgent = aiAgentFactory.CreateAiAgent(chatMode!.AiName, chatMode!.AiSettings, true);
            if (old is IDisposable disposableOld) disposableOld.Dispose();
        }

        public ChatMode GetMode()
        {
            if (chatMode == null)
                throw new ArgumentNullException(nameof(chatMode));

            return chatMode;
        }
    }
}