using NSubstitute;

namespace ChatWithAI.Tests;

public class ChatModeLoaderTests
{
    private readonly IModeStorage _modeStorage;

    public ChatModeLoaderTests()
    {
        _modeStorage = Substitute.For<IModeStorage>();
    }

    #region GetChatMode Tests

    [Fact]
    public async Task GetChatMode_ReturnsCorrectAiName()
    {
        _modeStorage.GetContent("TestMode", Arg.Any<CancellationToken>())
            .Returns("System message content");

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode("TestMode");

        Assert.Equal("AI_TestMode", result.AiName);
    }

    [Fact]
    public async Task GetChatMode_IncludesSystemMessageInAiSettings()
    {
        const string systemMessage = "You are a helpful assistant.";
        _modeStorage.GetContent("HelpfulMode", Arg.Any<CancellationToken>())
            .Returns(systemMessage);

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode("HelpfulMode");

        Assert.Contains(systemMessage, result.AiSettings);
    }

    [Fact]
    public async Task GetChatMode_IncludesPlatformSpecificMessage()
    {
        _modeStorage.GetContent("Mode", Arg.Any<CancellationToken>())
            .Returns("");

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode("Mode");

        // Default platform message mentions tables
        Assert.Contains("таблиц", result.AiSettings);
    }

    [Fact]
    public async Task GetChatMode_WithCustomPlatformMessage_IncludesIt()
    {
        _modeStorage.GetContent("Mode", Arg.Any<CancellationToken>())
            .Returns("");

        var customMessage = "Custom platform specific message";
        var loader = new ChatModeLoader(_modeStorage, customMessage);

        var result = await loader.GetChatMode("Mode");

        Assert.Contains(customMessage, result.AiSettings);
    }

    [Fact]
    public async Task GetChatMode_IncludesSessionStartTime()
    {
        _modeStorage.GetContent("Mode", Arg.Any<CancellationToken>())
            .Returns("");

        var loader = new ChatModeLoader(_modeStorage);
        var beforeTime = DateTime.Now;

        var result = await loader.GetChatMode("Mode");

        // Should contain date/time info
        Assert.Contains("Сеанс чата начат в", result.AiSettings);
    }

    [Fact]
    public async Task GetChatMode_WithEmptyModeName_ReturnsValidMode()
    {
        _modeStorage.GetContent("", Arg.Any<CancellationToken>())
            .Returns("");

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode("");

        Assert.Equal("AI_", result.AiName);
    }

    [Fact]
    public async Task GetChatMode_WithCancellationToken_PassesItToStorage()
    {
        using var cts = new CancellationTokenSource();
        _modeStorage.GetContent(Arg.Any<string>(), cts.Token)
            .Returns("");

        var loader = new ChatModeLoader(_modeStorage);

        await loader.GetChatMode("Mode", cts.Token);

        await _modeStorage.Received(1).GetContent("Mode", cts.Token);
    }

    [Fact]
    public async Task GetChatMode_WithSpecialCharactersInModeName_HandlesCorrectly()
    {
        const string modeName = "Mode_With-Special.Chars";
        _modeStorage.GetContent(modeName, Arg.Any<CancellationToken>())
            .Returns("Content");

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode(modeName);

        Assert.Equal($"AI_{modeName}", result.AiName);
    }

    [Fact]
    public async Task GetChatMode_WithMultilineSystemMessage_PreservesNewlines()
    {
        const string multilineMessage = "Line 1\nLine 2\nLine 3";
        _modeStorage.GetContent("Mode", Arg.Any<CancellationToken>())
            .Returns(multilineMessage);

        var loader = new ChatModeLoader(_modeStorage);

        var result = await loader.GetChatMode("Mode");

        Assert.Contains("Line 1", result.AiSettings);
        Assert.Contains("Line 2", result.AiSettings);
        Assert.Contains("Line 3", result.AiSettings);
    }

    [Fact]
    public async Task GetChatMode_ReturnsNewInstanceEachTime()
    {
        _modeStorage.GetContent(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Content");

        var loader = new ChatModeLoader(_modeStorage);

        var result1 = await loader.GetChatMode("Mode");
        var result2 = await loader.GetChatMode("Mode");

        // Should be different instances (not cached)
        Assert.NotSame(result1, result2);
    }

    #endregion
}
