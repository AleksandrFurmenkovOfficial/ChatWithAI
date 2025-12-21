using ChatWithAI.Core.ChatMessageActions;
using NSubstitute;

namespace ChatWithAI.Tests;

public class ChatMessageActionProcessorTests
{
    private readonly IChat _mockChat;

    public ChatMessageActionProcessorTests()
    {
        _mockChat = Substitute.For<IChat>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithEmptyActions_DoesNotThrow()
    {
        var exception = Record.Exception(() => new ChatMessageActionProcessor([]));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithMultipleActions_RegistersAll()
    {
        var actions = new List<IChatMessageAction>
        {
            new ContinueAction(),
            new RegenerateAction(),
            new RetryAction()
        };

        var processor = new ChatMessageActionProcessor(actions);

        Assert.NotNull(processor);
    }

    #endregion

    #region HandleMessageAction Tests

    [Fact]
    public async Task HandleMessageAction_WithContinueAction_CallsContinueLastResponse()
    {
        var actions = new List<IChatMessageAction> { new ContinueAction() };
        var processor = new ChatMessageActionProcessor(actions);
        var parameters = new ActionParameters(ContinueAction.Id, "msg1");

        await processor.HandleMessageAction(_mockChat, parameters, CancellationToken.None);

        await _mockChat.Received(1).ContinueLastResponse(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessageAction_WithRegenerateAction_CallsRegenerateLastResponse()
    {
        var actions = new List<IChatMessageAction> { new RegenerateAction() };
        var processor = new ChatMessageActionProcessor(actions);
        var parameters = new ActionParameters(RegenerateAction.Id, "msg1");

        await processor.HandleMessageAction(_mockChat, parameters, CancellationToken.None);

        await _mockChat.Received(1).RegenerateLastResponse(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessageAction_WithRetryAction_CallsRegenerateLastResponse()
    {
        var actions = new List<IChatMessageAction> { new RetryAction() };
        var processor = new ChatMessageActionProcessor(actions);
        var parameters = new ActionParameters(RetryAction.Id, "msg1");

        await processor.HandleMessageAction(_mockChat, parameters, CancellationToken.None);

        // RetryAction extends RegenerateAction
        await _mockChat.Received(1).RegenerateLastResponse(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessageAction_WithUnknownAction_DoesNothing()
    {
        var actions = new List<IChatMessageAction> { new ContinueAction() };
        var processor = new ChatMessageActionProcessor(actions);
        var parameters = new ActionParameters(new ActionId("UnknownAction"), "msg1");

        await processor.HandleMessageAction(_mockChat, parameters, CancellationToken.None);

        await _mockChat.DidNotReceive().ContinueLastResponse(Arg.Any<CancellationToken>());
        await _mockChat.DidNotReceive().RegenerateLastResponse(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessageAction_PassesCancellationToken()
    {
        var actions = new List<IChatMessageAction> { new ContinueAction() };
        var processor = new ChatMessageActionProcessor(actions);
        var parameters = new ActionParameters(ContinueAction.Id, "msg1");
        using var cts = new CancellationTokenSource();

        await processor.HandleMessageAction(_mockChat, parameters, cts.Token);

        await _mockChat.Received(1).ContinueLastResponse(cts.Token);
    }

    #endregion

    #region Action Id Tests

    [Fact]
    public void ContinueAction_Id_ReturnsCorrectValue()
    {
        Assert.Equal("Continue", ContinueAction.Id.Name);
    }

    [Fact]
    public void RegenerateAction_Id_ReturnsCorrectValue()
    {
        Assert.Equal("Regenerate", RegenerateAction.Id.Name);
    }

    [Fact]
    public void RetryAction_Id_ReturnsCorrectValue()
    {
        Assert.Equal("Retry", RetryAction.Id.Name);
    }

    [Fact]
    public void ContinueAction_GetId_MatchesStaticId()
    {
        var action = new ContinueAction();
        Assert.Equal(ContinueAction.Id, action.GetId);
    }

    [Fact]
    public void RegenerateAction_GetId_MatchesStaticId()
    {
        var action = new RegenerateAction();
        Assert.Equal(RegenerateAction.Id, action.GetId);
    }

    [Fact]
    public void RetryAction_GetId_MatchesStaticId()
    {
        var action = new RetryAction();
        Assert.Equal(RetryAction.Id, action.GetId);
    }

    #endregion
}
