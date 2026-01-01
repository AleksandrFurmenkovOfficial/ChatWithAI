using ChatWithAI.Contracts.Configs;
using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.ChatCommands;
using ChatWithAI.Core.ChatMessageActions;
using ChatWithAI.Core.StateMachine;
using ChatWithAI.Core.ViewModel;
using System.Text;
using MessageId = ChatWithAI.Contracts.MessageId;

namespace ChatWithAI.Core
{
    public sealed partial class Chat(
        AppConfig config,
        string chatId,
        IAiAgentFactory aiAgentFactory,
        IMessenger messenger,
        ILogger logger,
        ChatCache cache,
        bool useExpiration) : IChatInternal, IDisposable
    {
        private static readonly CompositeFormat StartWarningFormat = CompositeFormat.Parse(Strings.StartWarning);

        private IAiAgent? aiAgent;
        private ChatMode? chatMode;
        internal IMessenger Messenger => messenger;
        internal ILogger Logger => logger;
        public ChatMode GetMode() => chatMode ?? throw new ArgumentNullException(nameof(chatMode));
        private string GetStateCacheKey() => $"{Id}_state";
        public string Id { get; } = chatId;

        
        public void Dispose()
        {
            if (aiAgent is IDisposable disposableAgent)
            {
                disposableAgent.Dispose();
            }
        }

        
        public Task SetModeAsync(ChatMode mode)
        {
            chatMode = mode;
            var old = aiAgent;
            if (useExpiration && !mode.UseImage) mode.UseFlash = true; // tmp, force flash for non premium
            aiAgent = aiAgentFactory.CreateAiAgent(mode.AiName, mode.AiSettings, mode.UseFunctions, mode.UseImage, mode.UseFlash);
            if (old is IDisposable disposableOld) disposableOld.Dispose();
            return Task.CompletedTask;
        }

        #region State Management

        private ChatState GetOrCreateState()
        {
            var cacheData = cache.Get<ChatState>(GetStateCacheKey());
            if (cacheData == null)
            {
                cacheData = new ChatState(Id, messenger.MaxTextMessageLen(), messenger.MaxPhotoMessageLen());
                SaveState(cacheData);
            }
            return cacheData;
        }

        private void SaveState(ChatState state)
        {
            var expiration = TimeSpan.MaxValue;
            if (useExpiration)
            {
                expiration = TimeSpan.FromMinutes(config.ChatCacheAliveInMinutes);
            }
            cache.Set(GetStateCacheKey(), state, expiration);
        }

        private void ClearCache()
        {
            cache.Remove(GetStateCacheKey());
        }

        #endregion

        #region Public API (IChatInternal Implementation)

        public Task AddUserMessagesToChatHistoryAsync(List<ChatMessageModel> messages, bool forceOldTurn = false)
        {
            var state = GetOrCreateState();
            state.History.AddUserMessages(messages, forceOldTurn);
            SaveState(state);
            return Task.CompletedTask;
        }

        
        public async Task OnEnterWaitingForFirstMessageAsync()
        {
            await ResetInternal().ConfigureAwait(false);
            string modeNameFull = GetMode().AiName.Split("_")[1];
            var mode = modeNameFull.Replace("Mode", "");
            var note = mode != SetCommonMode.StaticName ? "\n" + Strings.ReturnNote : "";
            var msg = string.Format(CultureInfo.InvariantCulture, StartWarningFormat, mode, note);
            await messenger.SendTextMessage(Id, new MessengerMessageDTO { TextContent = msg }).ConfigureAwait(false);
        }

        
        public async Task OnEnterErrorAsync()
        {
            var errorMessage = new ChatMessageModel
            {
                Role = MessageRole.eRoleAI,
                Name = chatMode?.AiName ?? Strings.DefaultName,
                Content = [ChatMessageModel.CreateText(Strings.ErrorTryAgain)]
            };

            var state = GetOrCreateState();
            state.History.AddAssistantMessage(errorMessage);
            SaveState(state);

            // Create UI message and send
            var uiMessage = state.UIState.CreateInitialUIMessage(errorMessage, [RetryAction.Id]);
            uiMessage.TextContent = Strings.ErrorTryAgain;

            await SendUIMessageToMessenger(uiMessage).ConfigureAwait(false);
            SaveState(state);
        }

        
        public async Task OnExitErrorAsync()
        {
            var state = GetOrCreateState();
            var errorMessage = state.History.GetLastAssistantMessage();
            if (errorMessage != null)
            {
                state.History.RemoveMessageFromLastTurn(errorMessage);
                SaveState(state);

                await DeleteUIMessagesForModel(errorMessage.Id).ConfigureAwait(false);
            }
        }

        
        public async Task<ChatOperationResult> InitiateResponseAsync(CancellationToken ct)
        {
            logger.LogDebugMessage($"Chat {Id}: InitiateResponseAsync");

            await RemoveLatestButtonsFromUIInternal().ConfigureAwait(false);
            return await DoResponseToLastMessageInternal(ct).ConfigureAwait(false);
        }

        
        public async Task<ChatOperationResult> ContinueResponseAsync(CancellationToken ct)
        {
            logger.LogDebugMessage($"Chat {Id}: ContinueResponseAsync");

            var prevLastMessage = GetLastResponseModelMessage();
            var systemMessagePleaseContinue = new ChatMessageModel([ChatMessageModel.CreateText(Strings.Continue)], MessageRole.eRoleUser, Strings.RoleSystem);

            var state = GetOrCreateState();
            state.History.AddUserMessages([systemMessagePleaseContinue], forceAddToLastTurn: true);
            SaveState(state);

            await RemoveLatestButtonsFromUIInternal().ConfigureAwait(false);

            var result = await DoResponseToLastMessageInternal(ct,
                onCleanup: async () =>
                {
                    // If regenerate command was cancelled we should return buttons on cancel
                    if (prevLastMessage != null)
                    {
                        await UpdateUIMessageWithButtons(prevLastMessage.Id, [ContinueAction.Id, RegenerateAction.Id]).ConfigureAwait(false);
                    }

                    var state = GetOrCreateState();
                    state.History.RemoveMessageFromLastTurn(systemMessagePleaseContinue);
                    SaveState(state);

                }).ConfigureAwait(false);

            return result;
        }

        
        public async Task<ChatOperationResult> RegenerateResponseAsync(CancellationToken ct)
        {
            logger.LogDebugMessage($"Chat {Id}: RegenerateResponseAsync");

            await RemoveAllResponseFromLastTurnAndClearUIInternal().ConfigureAwait(false);
            return await DoResponseToLastMessageInternal(ct).ConfigureAwait(false);
        }

        #endregion

        #region Internal Operations

        private async Task ResetInternal()
        {
            await RemoveLatestButtonsFromUIInternal().ConfigureAwait(false);
            ClearCache();
        }

        private async Task RemoveAllResponseFromLastTurnAndClearUIInternal()
        {
            var state = GetOrCreateState();
            var removedMessages = state.History.RemoveAllAssistantMessagesFromLastTurn();
            SaveState(state);

            foreach (var modelMsg in removedMessages)
            {
                await DeleteUIMessagesForModel(modelMsg.Id).ConfigureAwait(false);
            }
        }

        private async Task<UIMessageViewModel?> RemoveLatestButtonsFromUIInternal()
        {
            var state = GetOrCreateState();
            var messageWithButtons = state.UIState.ClearActiveButtons();
            if (messageWithButtons != null && messageWithButtons.IsSent)
            {
                await UpdateUIMessageInMessenger(messageWithButtons, null).ConfigureAwait(false);
            }
            SaveState(state);
            return messageWithButtons;
        }

        private ChatMessageModel? GetLastResponseModelMessage()
        {
            return GetOrCreateState().History.GetLastAssistantMessage();
        }

        private async Task<ChatMessageModel> CreateAndSendResponseTargetMessage()
        {
            var responseMessage = new ChatMessageModel
            {
                Role = MessageRole.eRoleAI,
                Name = chatMode?.AiName ?? Strings.DefaultName,
                Content = [ChatMessageModel.CreateText(string.Empty)]
            };

            var state = GetOrCreateState();
            state.History.AddAssistantMessage(responseMessage);
            SaveState(state);

            // Create initial UI message with placeholder
            var uiMessage = state.UIState.CreateInitialUIMessage(responseMessage, [CancelAction.Id]);
            uiMessage.TextContent = Strings.InitAnswerTemplate;

            await SendUIMessageToMessenger(uiMessage).ConfigureAwait(false);
            SaveState(state);

            // Clear content after send (actual content will come from stream)
            uiMessage.TextContent = string.Empty;

            return responseMessage;
        }

        private async Task<ChatOperationResult> DoResponseToLastMessageInternal(CancellationToken ct, Func<Task>? onCleanup = null)
        {
            if (aiAgent == null)
                throw new InvalidOperationException("AI agent is not initialized");

            var allMessagesSnapshot = GetOrCreateState().History.GetAllMessagesForAI();
            allMessagesSnapshot = FilterVideoContentIfNeeded(allMessagesSnapshot);
            ChatMessageModel? targetMsg = null;

            try
            {
                targetMsg = await CreateAndSendResponseTargetMessage().ConfigureAwait(false);
                var responseStream = await aiAgent.GetResponseStreamAsync(Id, allMessagesSnapshot, ct).ConfigureAwait(false);
                var streamingContext = new StreamingContext(responseStream, ct);
                return ChatOperationResult.Success(streamingContext);
            }
            catch (OperationCanceledException)
            {
                await OnCancelDoResponseToLastMessageInternal(targetMsg, onCleanup).ConfigureAwait(false);
                return ChatOperationResult.Failure(ChatTrigger.UserCancel);
            }
            catch (Exception ex)
            {
                await OnErrorDoResponseToLastMessageInternal(targetMsg, onCleanup, ex).ConfigureAwait(false);
                return ChatOperationResult.Failure(ChatTrigger.AIResponseError);
            }
        }

        private async Task OnErrorDoResponseToLastMessageInternal(ChatMessageModel? targetMsg, Func<Task>? onCleanup, Exception ex)
        {
            logger.LogErrorMessage($"Chat {Id}: Error getting response stream: {ex.Message}");
            await CleanupAfterExceptionInDoResponseToLastMessageInternal(targetMsg, onCleanup).ConfigureAwait(false);
        }

        private async Task OnCancelDoResponseToLastMessageInternal(ChatMessageModel? targetMsg, Func<Task>? onCleanup)
        {
            logger.LogDebugMessage($"Chat {Id}: Response request was cancelled, cleaning up");
            await CleanupAfterExceptionInDoResponseToLastMessageInternal(targetMsg, onCleanup).ConfigureAwait(false);
        }

        private async Task CleanupAfterExceptionInDoResponseToLastMessageInternal(ChatMessageModel? targetMsg, Func<Task>? onCleanup)
        {
            if (targetMsg != null)
            {
                await RemoveSpecificResponseFromLastTurnAndClearUIInternal(targetMsg).ConfigureAwait(false);
            }
            else
            {
                await RemoveAllResponseFromLastTurnAndClearUIInternal().ConfigureAwait(false);
            }

            if (onCleanup != null)
                await onCleanup().ConfigureAwait(false);
        }

        private async Task RemoveSpecificResponseFromLastTurnAndClearUIInternal(ChatMessageModel targetMsg)
        {
            var state = GetOrCreateState();
            var removed = state.History.RemoveMessageFromLastTurn(targetMsg);
            SaveState(state);

            if (removed)
            {
                await DeleteUIMessagesForModel(targetMsg.Id).ConfigureAwait(false);
            }
        }

#pragma warning disable CA1822 // Mark members as static
        private List<ChatMessageModel> FilterVideoContentIfNeeded(List<ChatMessageModel> messages)
#pragma warning restore CA1822 // Mark members as static
        {
            return messages;
            //
            //if (chatMode?.UseFlash == true)
            //{
            //    return messages;
            //}
            //
            //return messages
            //    .Select(m =>
            //    {
            //        if (!m.HasVideo() && !m.HasDocument())
            //        {
            //            return m;
            //        }
            //
            //        var clone = m.Clone();
            //        
            //        clone.Content.RemoveAll(c => c is DocumentContentItem); // VideoContentItem or 
            //
            //        if (clone.IsEmpty())
            //        {
            //            clone.AddTextContent(Strings.DocumentNotSupported);
            //        }
            //        else
            //        {
            //            clone.GetTextContentItems().Last().Text += "\nAdditional note: " + Strings.DocumentNotSupported + "\n";
            //        }
            //
            //        return clone;
            //    })
            //    .ToList();
        }

        #endregion

        #region UI Operations (ViewModel Layer)

        private async Task SendUIMessageToMessenger(UIMessageViewModel uiMessage)
        {
            var dto = new MessengerMessageDTO
            {
                Role = uiMessage.Role,
                Name = uiMessage.Name,
                TextContent = uiMessage.TextContent,
                MediaContent = [.. uiMessage.MediaContent]
            };

            var actions = uiMessage.ActiveButtons;

            string messengerMsgId;
            if (uiMessage.HasImage)
            {
                messengerMsgId = await messenger.SendPhotoMessage(Id, dto, actions).ConfigureAwait(false);
            }
            else
            {
                messengerMsgId = await messenger.SendTextMessage(Id, dto, actions).ConfigureAwait(false);
            }

            ChatUIState.MarkAsSent(uiMessage, new MessageId(messengerMsgId));

            var state = GetOrCreateState();
            state.History.UpdateMessageOriginalId(uiMessage.ParentModelId, Helpers.MessageIdToInt(new MessageId(messengerMsgId)));
            SaveState(state);
        }

        private async Task UpdateUIMessageInMessenger(UIMessageViewModel uiMessage, IEnumerable<ActionId>? newActions)
        {
            if (!uiMessage.IsSent || uiMessage.IsDeleted) return;

            MessengerEditResult result;
            if (uiMessage.HasImage)
            {
                result = await messenger.EditPhotoMessage(Id, uiMessage.MessengerMessageId, uiMessage.TextContent, newActions).ConfigureAwait(false);
            }
            else
            {
                result = await messenger.EditTextMessage(Id, uiMessage.MessengerMessageId, uiMessage.TextContent, newActions).ConfigureAwait(false);
            }

            if (result == MessengerEditResult.MessageDeleted)
            {
                uiMessage.IsDeleted = true;
                SaveState(GetOrCreateState());
            }
        }

        private async Task DeleteUIMessagesForModel(ModelMessageId modelId)
        {
            var state = GetOrCreateState();
            var uiMessages = state.UIState.RemoveUIMessages(modelId);
            SaveState(state);

            // Delete in reverse order (last first)
            for (int i = uiMessages.Count - 1; i >= 0; i--)
            {
                var uiMsg = uiMessages[i];
                if (uiMsg.IsSent)
                {
                    await messenger.DeleteMessage(Id, uiMsg.MessengerMessageId).ConfigureAwait(false);
                }
            }
        }

        private async Task UpdateUIMessageWithButtons(ModelMessageId modelId, List<ActionId> buttons)
        {
            var state = GetOrCreateState();
            var lastUI = state.UIState.GetLastUIMessage(modelId);
            if (lastUI != null)
            {
                state.UIState.SetActiveButtons(lastUI, buttons);
                await UpdateUIMessageInMessenger(lastUI, buttons).ConfigureAwait(false);
                SaveState(state);
            }
        }

        #endregion
    }
}
