using NSubstitute;
using ILogger = ChatWithAI.Contracts.ILogger;

namespace ChatWithAI.Tests;

public class ChatCacheTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly ChatCache _cache;

    public ChatCacheTests()
    {
        _logger = Substitute.For<ILogger>();
        _cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
    }

    public void Dispose()
    {
        _cache?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Set Tests

    [Fact]
    public void Set_WithValidKey_StoresValue()
    {
        _cache.Set("key1", "value1", TimeSpan.FromMinutes(5));

        var result = _cache.Get<string>("key1");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void Set_WithNullKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _cache.Set<string>(null!, "value", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Set_WithEmptyKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _cache.Set("", "value", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        _cache.Set("key1", "original", TimeSpan.FromMinutes(5));
        _cache.Set("key1", "updated", TimeSpan.FromMinutes(5));

        var result = _cache.Get<string>("key1");
        Assert.Equal("updated", result);
    }

    [Fact]
    public void Set_WithMaxTimeSpan_DoesNotThrow()
    {
        var exception = Record.Exception(() => _cache.Set("key1", "value", TimeSpan.MaxValue));
        Assert.Null(exception);
    }

    [Fact]
    public void Set_WithComplexObject_StoresCorrectly()
    {
        var obj = new TestData { Id = 1, Name = "Test" };
        _cache.Set("complex", obj, TimeSpan.FromMinutes(5));

        var result = _cache.Get<TestData>("complex");
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Test", result.Name);
    }

    #endregion

    #region Get Tests

    [Fact]
    public void Get_WithValidKey_ReturnsValue()
    {
        _cache.Set("key1", 42, TimeSpan.FromMinutes(5));

        var result = _cache.Get<int>("key1");
        Assert.Equal(42, result);
    }

    [Fact]
    public void Get_WithNonExistentKey_ReturnsDefault()
    {
        var result = _cache.Get<string>("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void Get_WithNullKey_ReturnsDefault()
    {
        var result = _cache.Get<string>(null!);
        Assert.Null(result);
    }

    [Fact]
    public void Get_WithEmptyKey_ReturnsDefault()
    {
        var result = _cache.Get<string>("");
        Assert.Null(result);
    }

    [Fact]
    public void Get_WithTypeMismatch_ReturnsDefault()
    {
        _cache.Set("key1", "string_value", TimeSpan.FromMinutes(5));

        var result = _cache.Get<int>("key1");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Get_WithNullValue_ReturnsNull()
    {
        _cache.Set<string?>("key1", null, TimeSpan.FromMinutes(5));

        var result = _cache.Get<string>("key1");
        Assert.Null(result);
    }

    #endregion

    #region TryGet Tests

    [Fact]
    public void TryGet_WithValidKey_ReturnsTrueAndValue()
    {
        _cache.Set("key1", "value1", TimeSpan.FromMinutes(5));

        var success = _cache.TryGet<string>("key1", out var value);

        Assert.True(success);
        Assert.Equal("value1", value);
    }

    [Fact]
    public void TryGet_WithNonExistentKey_ReturnsFalse()
    {
        var success = _cache.TryGet<string>("nonexistent", out var value);

        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_WithTypeMismatch_ReturnsFalse()
    {
        _cache.Set("key1", "string_value", TimeSpan.FromMinutes(5));

        var success = _cache.TryGet<int>("key1", out var value);

        Assert.False(success);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryGet_WithNullKey_ReturnsFalse()
    {
        var success = _cache.TryGet<string>(null!, out var value);

        Assert.False(success);
        Assert.Null(value);
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_WithExistingKey_ReturnsTrue()
    {
        _cache.Set("key1", "value", TimeSpan.FromMinutes(5));

        Assert.True(_cache.Contains("key1"));
    }

    [Fact]
    public void Contains_WithNonExistentKey_ReturnsFalse()
    {
        Assert.False(_cache.Contains("nonexistent"));
    }

    [Fact]
    public void Contains_WithNullKey_ReturnsFalse()
    {
        Assert.False(_cache.Contains(null!));
    }

    [Fact]
    public void Contains_WithEmptyKey_ReturnsFalse()
    {
        Assert.False(_cache.Contains(""));
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_WithExistingKey_ReturnsTrueAndRemovesItem()
    {
        _cache.Set("key1", "value", TimeSpan.FromMinutes(5));

        var removed = _cache.Remove("key1");

        Assert.True(removed);
        Assert.False(_cache.Contains("key1"));
    }

    [Fact]
    public void Remove_WithNonExistentKey_ReturnsFalse()
    {
        var removed = _cache.Remove("nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public void Remove_WithNullKey_ReturnsFalse()
    {
        var removed = _cache.Remove(null!);

        Assert.False(removed);
    }

    [Fact]
    public void Remove_WithEmptyKey_ReturnsFalse()
    {
        var removed = _cache.Remove("");

        Assert.False(removed);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllItems()
    {
        _cache.Set("key1", "value1", TimeSpan.FromMinutes(5));
        _cache.Set("key2", "value2", TimeSpan.FromMinutes(5));
        _cache.Set("key3", "value3", TimeSpan.FromMinutes(5));

        _cache.Clear();

        Assert.Equal(0, _cache.Count);
        Assert.False(_cache.Contains("key1"));
        Assert.False(_cache.Contains("key2"));
        Assert.False(_cache.Contains("key3"));
    }

    #endregion

    #region Count Tests

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        Assert.Equal(0, _cache.Count);

        _cache.Set("key1", "value1", TimeSpan.FromMinutes(5));
        Assert.Equal(1, _cache.Count);

        _cache.Set("key2", "value2", TimeSpan.FromMinutes(5));
        Assert.Equal(2, _cache.Count);

        _cache.Remove("key1");
        Assert.Equal(1, _cache.Count);
    }

    #endregion

    #region Keys Tests

    [Fact]
    public void Keys_ReturnsAllKeys()
    {
        _cache.Set("key1", "value1", TimeSpan.FromMinutes(5));
        _cache.Set("key2", "value2", TimeSpan.FromMinutes(5));
        _cache.Set("key3", "value3", TimeSpan.FromMinutes(5));

        var keys = _cache.Keys.ToList();

        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public void Keys_ReturnsEmptyWhenCacheEmpty()
    {
        var keys = _cache.Keys.ToList();

        Assert.Empty(keys);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);

        var exception = Record.Exception(() =>
        {
            cache.Dispose();
            cache.Dispose();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AfterDispose_Set_ThrowsObjectDisposedException()
    {
        var cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Set("key", "value", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void AfterDispose_Get_ReturnsDefault()
    {
        var cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
        cache.Set("key", "value", TimeSpan.FromMinutes(5));
        cache.Dispose();

        // Get should not throw but return default
        var result = cache.Get<string>("key");
        Assert.Null(result);
    }

    [Fact]
    public void AfterDispose_Count_ThrowsObjectDisposedException()
    {
        var cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = cache.Count);
    }

    [Fact]
    public void AfterDispose_Clear_ThrowsObjectDisposedException()
    {
        var cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Clear());
    }

    #endregion

    #region Expiration Tests

    [Fact]
    public async Task Expiration_EmitsExpirationEvent()
    {
        using var shortLivedCache = new ChatCache(TimeSpan.FromMilliseconds(50), _logger);
        var expirationReceived = new TaskCompletionSource<string>();

        shortLivedCache.ExpirationObservable.Subscribe(e => expirationReceived.TrySetResult(e.ChatId));

        shortLivedCache.Set("expire_key", "value", TimeSpan.FromMilliseconds(10));

        var result = await expirationReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("expire_key", result);
    }

    [Fact]
    public void ItemWithMaxExpiration_DoesNotExpire()
    {
        _cache.Set("permanent", "value", TimeSpan.MaxValue);

        var result = _cache.Get<string>("permanent");
        Assert.Equal("value", result);
    }

    #endregion

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
