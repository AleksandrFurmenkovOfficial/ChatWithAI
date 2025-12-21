namespace ChatWithAI.Tests;

public class ModeStorageTests : IDisposable
{
    private readonly string _testDirectory;

    public ModeStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ModeStorageTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidPath_DoesNotThrow()
    {
        var exception = Record.Exception(() => new ModeStorage(_testDirectory));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidPath_ThrowsArgumentException(string? invalidPath)
    {
        Assert.Throws<ArgumentException>(() => new ModeStorage(invalidPath!));
    }

    #endregion

    #region GetContent Tests

    [Fact]
    public async Task GetContent_WithExistingFile_ReturnsContent()
    {
        const string modeName = "TestMode";
        const string content = "This is the mode content.";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), content);

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Equal(content, result);
    }

    [Fact]
    public async Task GetContent_WithNonExistentFile_ReturnsEmpty()
    {
        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent("NonExistentMode");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetContent_WithNonExistentDirectory_ReturnsEmpty()
    {
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent_subdir");
        var storage = new ModeStorage(nonExistentPath);

        var result = await storage.GetContent("SomeMode");

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetContent_WithEmptyModeName_ReturnsEmpty(string? modeName)
    {
        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetContent_WithUnicodeContent_ReturnsCorrectContent()
    {
        const string modeName = "UnicodeMode";
        const string content = "Привет мир! 你好世界! 🎉";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), content);

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Equal(content, result);
    }

    [Fact]
    public async Task GetContent_WithMultilineContent_PreservesNewlines()
    {
        const string modeName = "MultilineMode";
        const string content = "Line 1\nLine 2\r\nLine 3";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), content);

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);
    }

    [Fact]
    public async Task GetContent_WithEmptyFile_ReturnsEmptyString()
    {
        const string modeName = "EmptyMode";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), "");

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetContent_WithLargeFile_ReturnsAllContent()
    {
        const string modeName = "LargeMode";
        var content = new string('X', 100000);
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), content);

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Equal(100000, result.Length);
    }

    [Fact]
    public async Task GetContent_WithCancellationToken_RespectsCancellation()
    {
        const string modeName = "CancelMode";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), "Content");

        var storage = new ModeStorage(_testDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.GetContent(modeName, cts.Token));
    }

    [Fact]
    public async Task GetContent_SanitizesModeName()
    {
        // Path.GetFileName should remove path traversal attempts
        const string modeName = "../../../etc/passwd";
        var storage = new ModeStorage(_testDirectory);

        // Should not throw, should just return empty for non-existent file
        var result = await storage.GetContent(modeName);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetContent_WithSpecialCharactersInModeName_HandlesCorrectly()
    {
        const string modeName = "Mode-With_Special.Chars";
        const string content = "Special content";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, $"{modeName}.txt"), content);

        var storage = new ModeStorage(_testDirectory);

        var result = await storage.GetContent(modeName);

        Assert.Equal(content, result);
    }

    #endregion
}
