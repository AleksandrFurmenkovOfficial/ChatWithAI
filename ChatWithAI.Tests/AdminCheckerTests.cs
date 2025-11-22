namespace ChatWithAI.Tests;

public class AdminCheckerTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidAdminUserId_DoesNotThrow()
    {
        var exception = Record.Exception(() => new AdminChecker("admin123"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_WithInvalidAdminUserId_ThrowsArgumentException(string? invalidId)
    {
        Assert.Throws<ArgumentException>(() => new AdminChecker(invalidId!));
    }

    #endregion

    #region IsAdmin Tests

    [Fact]
    public void IsAdmin_WithMatchingUserId_ReturnsTrue()
    {
        var checker = new AdminChecker("admin123");
        Assert.True(checker.IsAdmin("admin123"));
    }

    [Fact]
    public void IsAdmin_WithMatchingUserIdDifferentCase_ReturnsTrue()
    {
        var checker = new AdminChecker("Admin123");
        Assert.True(checker.IsAdmin("ADMIN123"));
        Assert.True(checker.IsAdmin("admin123"));
        Assert.True(checker.IsAdmin("AdMiN123"));
    }

    [Fact]
    public void IsAdmin_WithNonMatchingUserId_ReturnsFalse()
    {
        var checker = new AdminChecker("admin123");
        Assert.False(checker.IsAdmin("user456"));
        Assert.False(checker.IsAdmin("admin"));
        Assert.False(checker.IsAdmin("123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsAdmin_WithNullOrEmptyUserId_ReturnsFalse(string? invalidId)
    {
        var checker = new AdminChecker("admin123");
        Assert.False(checker.IsAdmin(invalidId!));
    }

    [Fact]
    public void IsAdmin_WithNumericIds_WorksCorrectly()
    {
        var checker = new AdminChecker("123456789");
        Assert.True(checker.IsAdmin("123456789"));
        Assert.False(checker.IsAdmin("987654321"));
    }

    [Fact]
    public void IsAdmin_WithUnicodeCharacters_WorksCorrectly()
    {
        var checker = new AdminChecker("админ_пользователь");
        Assert.True(checker.IsAdmin("АДМИН_ПОЛЬЗОВАТЕЛЬ"));
        Assert.True(checker.IsAdmin("админ_пользователь"));
    }

    #endregion
}
