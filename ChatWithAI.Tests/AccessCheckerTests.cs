using NSubstitute;
using System.Collections.Concurrent;

namespace ChatWithAI.Tests;

public class AccessCheckerTests
{
    private readonly IAdminChecker _adminChecker;
    private readonly IAccessStorage _accessStorage;
    private readonly ConcurrentDictionary<string, AppVisitor> _visitorByChatId;

    public AccessCheckerTests()
    {
        _adminChecker = Substitute.For<IAdminChecker>();
        _accessStorage = Substitute.For<IAccessStorage>();
        _visitorByChatId = new ConcurrentDictionary<string, AppVisitor>();
    }

    private AccessChecker CreateChecker()
    {
        return new AccessChecker(_adminChecker, _visitorByChatId, _accessStorage);
    }

    #region HasAccessAsync Tests

    [Fact]
    public async Task HasAccessAsync_ForAdmin_ReturnsTrue()
    {
        _adminChecker.IsAdmin("admin123").Returns(true);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        var result = await checker.HasAccessAsync("admin123", "AdminUser");

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccessAsync_ForAllowedUser_ReturnsTrue()
    {
        _adminChecker.IsAdmin(Arg.Any<string>()).Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("user1\nuser2\nuser3");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        var result = await checker.HasAccessAsync("user2", "TestUser");

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccessAsync_ForNotAllowedUser_ReturnsFalse()
    {
        _adminChecker.IsAdmin(Arg.Any<string>()).Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("user1\nuser2");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        var result = await checker.HasAccessAsync("user999", "NotAllowed");

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccessAsync_UpdatesLatestAccess()
    {
        _adminChecker.IsAdmin("user1").Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("user1");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        var beforeAccess = DateTime.UtcNow;
        await checker.HasAccessAsync("user1", "TestUser");
        var afterAccess = DateTime.UtcNow;

        Assert.True(_visitorByChatId.TryGetValue("user1", out var visitor));
        Assert.InRange(visitor.LatestAccess, beforeAccess, afterAccess);
    }

    [Fact]
    public async Task HasAccessAsync_CachesDataLoader()
    {
        _adminChecker.IsAdmin(Arg.Any<string>()).Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("user1");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        await checker.HasAccessAsync("user1", "TestUser");
        await checker.HasAccessAsync("user2", "TestUser2");
        await checker.HasAccessAsync("user3", "TestUser3");

        // GetAllowedUsers should only be called once due to Lazy<T>
        await _accessStorage.Received(1).GetAllowedUsers(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasAccessAsync_WithWhitespaceInAllowedUsers_TrimsCorrectly()
    {
        _adminChecker.IsAdmin(Arg.Any<string>()).Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("  user1  \n  user2  \n  user3  ");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        Assert.True(await checker.HasAccessAsync("user1", "Test"));
        Assert.True(await checker.HasAccessAsync("user2", "Test"));
        Assert.True(await checker.HasAccessAsync("user3", "Test"));
    }

    [Fact]
    public async Task HasAccessAsync_WithEmptyAllowedUsers_OnlyAdminHasAccess()
    {
        _adminChecker.IsAdmin("admin").Returns(true);
        _adminChecker.IsAdmin("regular").Returns(false);
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        Assert.True(await checker.HasAccessAsync("admin", "Admin"));
        Assert.False(await checker.HasAccessAsync("regular", "Regular"));
    }

    #endregion

    #region IsPremiumUserAsync Tests

    [Fact]
    public async Task IsPremiumUserAsync_ForPremiumUser_ReturnsTrue()
    {
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("premium1\npremium2");

        var checker = CreateChecker();

        Assert.True(await checker.IsPremiumUserAsync("premium1"));
        Assert.True(await checker.IsPremiumUserAsync("premium2"));
    }

    [Fact]
    public async Task IsPremiumUserAsync_ForNonPremiumUser_ReturnsFalse()
    {
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("premium1");

        var checker = CreateChecker();

        Assert.False(await checker.IsPremiumUserAsync("regular_user"));
    }

    [Fact]
    public async Task IsPremiumUserAsync_WithEmptyPremiumList_ReturnsFalse()
    {
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("");

        var checker = CreateChecker();

        Assert.False(await checker.IsPremiumUserAsync("any_user"));
    }

    [Fact]
    public async Task IsPremiumUserAsync_WithWhitespaceInPremiumUsers_TrimsCorrectly()
    {
        _accessStorage.GetAllowedUsers(Arg.Any<CancellationToken>()).Returns("");
        _accessStorage.GetPremiumUsers(Arg.Any<CancellationToken>()).Returns("  premium1  \n\n  premium2  ");

        var checker = CreateChecker();

        Assert.True(await checker.IsPremiumUserAsync("premium1"));
        Assert.True(await checker.IsPremiumUserAsync("premium2"));
        Assert.False(await checker.IsPremiumUserAsync("  premium1  "));
    }

    #endregion
}
