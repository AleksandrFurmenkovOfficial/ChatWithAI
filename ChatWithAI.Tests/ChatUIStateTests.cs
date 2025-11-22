using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.ViewModel;

namespace ChatWithAI.Tests;

public class ChatUIStateTests
{
    private readonly ChatUIState _uiState;

    public ChatUIStateTests()
    {
        _uiState = new ChatUIState("test-chat", 4096, 1024);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var state = new ChatUIState("chat123", 2000, 500);

        Assert.Equal("chat123", state.ChatId);
        Assert.Equal(2000, state.MaxTextMessageLen);
    }

    #endregion

    #region CreateInitialUIMessage Tests

    [Fact]
    public void CreateInitialUIMessage_CreatesMessageWithCorrectProperties()
    {
        var modelMessage = new ChatMessageModel
        {
            Role = MessageRole.eRoleAI,
            Name = "Assistant"
        };

        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);

        Assert.Equal(modelMessage.Id, uiMessage.ParentModelId);
        Assert.Equal(0, uiMessage.SegmentIndex);
        Assert.Equal(MessageRole.eRoleAI, uiMessage.Role);
        Assert.Equal("Assistant", uiMessage.Name);
        Assert.False(uiMessage.IsSent);
    }

    [Fact]
    public void CreateInitialUIMessage_WithButtons_SetsActiveButtons()
    {
        var modelMessage = new ChatMessageModel();
        var buttons = new List<ActionId> { new ActionId("Button1"), new ActionId("Button2") };

        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage, buttons);

        Assert.NotNull(uiMessage.ActiveButtons);
        Assert.Equal(2, uiMessage.ActiveButtons.Count);
    }

    [Fact]
    public void CreateInitialUIMessage_WithEmptyButtons_DoesNotSetActiveButtons()
    {
        var modelMessage = new ChatMessageModel();
        var buttons = new List<ActionId>();

        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage, buttons);

        Assert.Null(uiMessage.ActiveButtons);
    }

    [Fact]
    public void CreateInitialUIMessage_StoresInDictionary()
    {
        var modelMessage = new ChatMessageModel();

        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);
        var retrieved = _uiState.GetUIMessages(modelMessage.Id);

        Assert.Single(retrieved);
        Assert.Same(uiMessage, retrieved[0]);
    }

    #endregion

    #region MarkAsSent Tests

    [Fact]
    public void MarkAsSent_SetsIsSentAndMessageId()
    {
        var uiMessage = new UIMessageViewModel();
        var messageId = new MessageId("12345");

        ChatUIState.MarkAsSent(uiMessage, messageId);

        Assert.True(uiMessage.IsSent);
        Assert.Equal(messageId, uiMessage.MessengerMessageId);
    }

    #endregion

    #region CreateNextSegment Tests

    [Fact]
    public void CreateNextSegment_IncrementsSegmentIndex()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);

        var secondSegment = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        Assert.Equal(1, secondSegment.SegmentIndex);
    }

    [Fact]
    public void CreateNextSegment_MultipleSegments_CorrectIndexes()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);

        var segment2 = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");
        var segment3 = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");
        var segment4 = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        Assert.Equal(1, segment2.SegmentIndex);
        Assert.Equal(2, segment3.SegmentIndex);
        Assert.Equal(3, segment4.SegmentIndex);
    }

    [Fact]
    public void CreateNextSegment_WithButtons_SetsActiveButtons()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);
        var buttons = new List<ActionId> { new ActionId("Btn") };

        var segment = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant", buttons);

        Assert.NotNull(segment.ActiveButtons);
    }

    #endregion

    #region GetUIMessages Tests

    [Fact]
    public void GetUIMessages_WithNonExistentId_ReturnsEmptyList()
    {
        var result = _uiState.GetUIMessages(new ModelMessageId());

        Assert.Empty(result);
    }

    [Fact]
    public void GetUIMessages_ReturnsAllSegments()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);
        _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");
        _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        var messages = _uiState.GetUIMessages(modelMessage.Id);

        Assert.Equal(3, messages.Count);
    }

    #endregion

    #region GetLastUIMessage Tests

    [Fact]
    public void GetLastUIMessage_ReturnsLastSegment()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);
        var lastSegment = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        var result = _uiState.GetLastUIMessage(modelMessage.Id);

        Assert.Same(lastSegment, result);
    }

    [Fact]
    public void GetLastUIMessage_WithNonExistentId_ReturnsNull()
    {
        var result = _uiState.GetLastUIMessage(new ModelMessageId());

        Assert.Null(result);
    }

    #endregion

    #region SetActiveButtons Tests

    [Fact]
    public void SetActiveButtons_SetsButtonsOnMessage()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);
        var buttons = new List<ActionId> { new ActionId("Btn1"), new ActionId("Btn2") };

        _uiState.SetActiveButtons(uiMessage, buttons);

        Assert.Equal(buttons, uiMessage.ActiveButtons);
    }

    [Fact]
    public void SetActiveButtons_ClearsPreviousButtonsFromOtherMessage()
    {
        var model1 = new ChatMessageModel();
        var model2 = new ChatMessageModel();
        var ui1 = _uiState.CreateInitialUIMessage(model1);
        var ui2 = _uiState.CreateInitialUIMessage(model2);
        ChatUIState.MarkAsSent(ui1, new MessageId("1"));
        ChatUIState.MarkAsSent(ui2, new MessageId("2"));
        var buttons = new List<ActionId> { new ActionId("Btn") };

        _uiState.SetActiveButtons(ui1, buttons);
        _uiState.SetActiveButtons(ui2, buttons);

        Assert.Null(ui1.ActiveButtons);
        Assert.Equal(buttons, ui2.ActiveButtons);
    }

    [Fact]
    public void SetActiveButtons_WithNull_ClearsButtons()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage, new List<ActionId> { new ActionId("Btn") });

        _uiState.SetActiveButtons(uiMessage, null);

        Assert.Null(uiMessage.ActiveButtons);
    }

    #endregion

    #region ClearActiveButtons Tests

    [Fact]
    public void ClearActiveButtons_ReturnsMessageWithButtons()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);
        ChatUIState.MarkAsSent(uiMessage, new MessageId("123"));
        _uiState.SetActiveButtons(uiMessage, new List<ActionId> { new ActionId("Btn") });

        var result = _uiState.ClearActiveButtons();

        Assert.Same(uiMessage, result);
    }

    [Fact]
    public void ClearActiveButtons_ClearsButtonsFromMessage()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);
        ChatUIState.MarkAsSent(uiMessage, new MessageId("123"));
        _uiState.SetActiveButtons(uiMessage, new List<ActionId> { new ActionId("Btn") });

        _uiState.ClearActiveButtons();

        Assert.Null(uiMessage.ActiveButtons);
    }

    [Fact]
    public void ClearActiveButtons_WhenNoButtons_ReturnsNull()
    {
        var result = _uiState.ClearActiveButtons();

        Assert.Null(result);
    }

    #endregion

    #region RemoveUIMessages Tests

    [Fact]
    public void RemoveUIMessages_ReturnsRemovedMessages()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage);

        var removed = _uiState.RemoveUIMessages(modelMessage.Id);

        Assert.Single(removed);
        Assert.Same(uiMessage, removed[0]);
    }

    [Fact]
    public void RemoveUIMessages_RemovesFromDictionary()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);

        _uiState.RemoveUIMessages(modelMessage.Id);

        Assert.Empty(_uiState.GetUIMessages(modelMessage.Id));
    }

    [Fact]
    public void RemoveUIMessages_ClearsActiveButtonsIfRemoved()
    {
        var modelMessage = new ChatMessageModel();
        var uiMessage = _uiState.CreateInitialUIMessage(modelMessage, new List<ActionId> { new ActionId("Btn") });
        ChatUIState.MarkAsSent(uiMessage, new MessageId("123"));

        _uiState.RemoveUIMessages(modelMessage.Id);

        Assert.Null(_uiState.GetMessageWithActiveButtons());
    }

    [Fact]
    public void RemoveUIMessages_WithNonExistentId_ReturnsEmptyList()
    {
        var result = _uiState.RemoveUIMessages(new ModelMessageId());

        Assert.Empty(result);
    }

    #endregion

    #region RemoveLastUIMessage Tests

    [Fact]
    public void RemoveLastUIMessage_ReturnsLastMessage()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);
        var lastSegment = _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        var removed = _uiState.RemoveLastUIMessage(modelMessage.Id);

        Assert.Same(lastSegment, removed);
    }

    [Fact]
    public void RemoveLastUIMessage_KeepsOtherSegments()
    {
        var modelMessage = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(modelMessage);
        _uiState.CreateNextSegment(modelMessage.Id, MessageRole.eRoleAI, "Assistant");

        _uiState.RemoveLastUIMessage(modelMessage.Id);

        Assert.Single(_uiState.GetUIMessages(modelMessage.Id));
    }

    [Fact]
    public void RemoveLastUIMessage_WithNonExistentId_ReturnsNull()
    {
        var result = _uiState.RemoveLastUIMessage(new ModelMessageId());

        Assert.Null(result);
    }

    #endregion

    #region SplitTextByLength Tests

    [Fact]
    public void SplitTextByLength_ShortText_ReturnsSingleSegment()
    {
        var result = ChatUIState.SplitTextByLength("Short", 100);

        Assert.Single(result);
        Assert.Equal("Short", result[0]);
    }

    [Fact]
    public void SplitTextByLength_ExactLength_ReturnsSingleSegment()
    {
        var result = ChatUIState.SplitTextByLength("12345", 5);

        Assert.Single(result);
    }

    [Fact]
    public void SplitTextByLength_LongText_SplitsCorrectly()
    {
        var text = "1234567890";
        var result = ChatUIState.SplitTextByLength(text, 3);

        Assert.Equal(4, result.Count);
        Assert.Equal("123", result[0]);
        Assert.Equal("456", result[1]);
        Assert.Equal("789", result[2]);
        Assert.Equal("0", result[3]);
    }

    [Fact]
    public void SplitTextByLength_EmptyText_ReturnsEmptyString()
    {
        var result = ChatUIState.SplitTextByLength("", 100);

        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void SplitTextByLength_NullText_ReturnsEmptyString()
    {
        var result = ChatUIState.SplitTextByLength(null!, 100);

        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    #endregion

    #region WouldRequireSplitting Tests

    [Fact]
    public void WouldRequireSplitting_ShortText_ReturnsFalse()
    {
        var state = new ChatUIState("chat", 100, 50);

        Assert.False(state.WouldRequireSplitting("Short text"));
    }

    [Fact]
    public void WouldRequireSplitting_LongText_ReturnsTrue()
    {
        var state = new ChatUIState("chat", 10, 50);

        Assert.True(state.WouldRequireSplitting("This is a long text that exceeds the limit"));
    }

    [Fact]
    public void WouldRequireSplitting_EmptyText_ReturnsFalse()
    {
        Assert.False(_uiState.WouldRequireSplitting(""));
    }

    [Fact]
    public void WouldRequireSplitting_NullText_ReturnsFalse()
    {
        Assert.False(_uiState.WouldRequireSplitting(null!));
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        var model1 = new ChatMessageModel();
        var model2 = new ChatMessageModel();
        _uiState.CreateInitialUIMessage(model1);
        _uiState.CreateInitialUIMessage(model2);

        _uiState.Clear();

        Assert.Empty(_uiState.GetUIMessages(model1.Id));
        Assert.Empty(_uiState.GetUIMessages(model2.Id));
    }

    [Fact]
    public void Clear_ClearsActiveButtons()
    {
        var model = new ChatMessageModel();
        var ui = _uiState.CreateInitialUIMessage(model, new List<ActionId> { new ActionId("Btn") });
        ChatUIState.MarkAsSent(ui, new MessageId("123"));

        _uiState.Clear();

        Assert.Null(_uiState.GetMessageWithActiveButtons());
    }

    #endregion

    #region UIMessageViewModel Tests

    [Fact]
    public void UIMessageViewModel_DefaultValues_AreCorrect()
    {
        var vm = new UIMessageViewModel();

        Assert.Equal(string.Empty, vm.MessengerMessageId.Value);
        Assert.Equal(ModelMessageId.Empty, vm.ParentModelId);
        Assert.Equal(0, vm.SegmentIndex);
        Assert.Equal(string.Empty, vm.TextContent);
        Assert.Empty(vm.MediaContent);
        Assert.False(vm.IsSent);
        Assert.Null(vm.ActiveButtons);
        Assert.Equal(MessageRole.eRoleSystem, vm.Role);
        Assert.Equal(string.Empty, vm.Name);
    }

    [Fact]
    public void UIMessageViewModel_HasMedia_ReturnsFalseWhenEmpty()
    {
        var vm = new UIMessageViewModel();

        Assert.False(vm.HasMedia);
    }

    [Fact]
    public void UIMessageViewModel_HasMedia_ReturnsTrueWithContent()
    {
        var vm = new UIMessageViewModel();
        vm.MediaContent.Add(new ImageContentItem());

        Assert.True(vm.HasMedia);
    }

    [Fact]
    public void UIMessageViewModel_HasImage_ReturnsTrueWithImage()
    {
        var vm = new UIMessageViewModel();
        vm.MediaContent.Add(new ImageContentItem());

        Assert.True(vm.HasImage);
    }

    [Fact]
    public void UIMessageViewModel_HasAudio_ReturnsTrueWithAudio()
    {
        var vm = new UIMessageViewModel();
        vm.MediaContent.Add(new AudioContentItem());

        Assert.True(vm.HasAudio);
    }

    [Fact]
    public void UIMessageViewModel_IsButtonsActive_RequiresSentAndButtons()
    {
        var vm = new UIMessageViewModel
        {
            IsSent = false,
            ActiveButtons = new List<ActionId> { new ActionId("Btn") }
        };

        Assert.False(vm.IsButtonsActive);

        vm.IsSent = true;
        Assert.True(vm.IsButtonsActive);

        vm.ActiveButtons = null;
        Assert.False(vm.IsButtonsActive);
    }

    #endregion
}
