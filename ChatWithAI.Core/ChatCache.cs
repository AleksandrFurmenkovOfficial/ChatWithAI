using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ChatWithAI.Core
{
    /// <summary>
    /// Cache with "expire after write" semantics:
    /// - Set(key, value, ttl) stores value and computes ExpiresAt = now + ttl
    /// - When expired, item is NOT removed; it is atomically marked as expired and ExpirationObservable emits an event once.
    /// - Cache user decides whether to refresh (Set) or remove (Remove).
    /// </summary>
    public class ChatCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cacheItems = new();
        private readonly Timer _expirationTimer;
        private readonly TimeSpan _checkInterval;
        private readonly ILogger _logger;

        private readonly ISubject<ExpirationEventArgs> _expirationSubject;

        private int _isDisposed;  // 0 alive, 1 disposed
        private int _isChecking;  // timer reentrancy guard

        public IObservable<ExpirationEventArgs> ExpirationObservable { get; }

        public ChatCache(TimeSpan checkInterval, ILogger logger)
        {
            _checkInterval = checkInterval;
            _logger = logger;

            // Thread-safe wrapper over Subject to avoid concurrent OnNext/OnCompleted issues.
            _expirationSubject = Subject.Synchronize(new Subject<ExpirationEventArgs>());
            ExpirationObservable = _expirationSubject.AsObservable();

            _expirationTimer = new Timer(CheckExpirations, null, _checkInterval, _checkInterval);
            Log("Cache initialized");
        }

        public void Set<T>(string key, T value, TimeSpan expiration)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            EnsureNotDisposed();

            var now = DateTime.UtcNow;
            var expiresAt = ComputeExpiresAt(now, expiration);

            var item = new CacheItem(value, expiresAt, isExpired: false);

            // Atomic replace per key (there is always exactly one value per key).
            _cacheItems.AddOrUpdate(key, item, (_, __) => item);
        }

        public T? Get<T>(string key)
        {
            if (!TryGetInternal(key, out var item) || item == null)
                return default;

            if (item.Value is T typed)
                return typed;

            Log($"Type mismatch for key '{key}'. Requested {typeof(T).Name}, stored {(item.Value?.GetType().Name ?? "null")}.");
            return default;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            value = default;

            if (!TryGetInternal(key, out var item) || item == null)
                return false;

            if (item.Value is T typed)
            {
                value = typed;
                return true;
            }

            Log($"Type mismatch for key '{key}'. Requested {typeof(T).Name}, stored {(item.Value?.GetType().Name ?? "null")}.");
            return false;
        }

        private bool TryGetInternal(string key, out CacheItem? item)
        {
            item = null;

            if (string.IsNullOrEmpty(key))
                return false;

            try { EnsureNotDisposed(); }
            catch { return false; }

            return _cacheItems.TryGetValue(key, out item) && item != null;
        }

        public bool Contains(string key)
        {
            return TryGetInternal(key, out _);
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            try { EnsureNotDisposed(); }
            catch { return false; }

            return _cacheItems.TryRemove(key, out _);
        }

        public void Clear()
        {
            EnsureNotDisposed();

            _cacheItems.Clear();
            Log("Cache cleared");
        }

        public int Count
        {
            get
            {
                EnsureNotDisposed();
                return _cacheItems.Count;
            }
        }

        public IEnumerable<string> Keys
        {
            get
            {
                EnsureNotDisposed();
                // Snapshot for predictable enumeration semantics.
                return new List<string>(_cacheItems.Keys);
            }
        }

        private void CheckExpirations(object? _)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
                return;

            // Prevent overlapping callbacks.
            if (Interlocked.Exchange(ref _isChecking, 1) != 0)
                return;

            try
            {
                var now = DateTime.UtcNow;

                foreach (var kvp in _cacheItems)
                {
                    var key = kvp.Key;
                    var snapshot = kvp.Value;

                    if (snapshot.IsExpired)
                        continue;

                    if (snapshot.ExpiresAt > now)
                        continue;

                    // Atomically mark expired ONLY if the dictionary still contains exactly this snapshot instance.
                    var expired = snapshot.WithExpired();
                    if (!_cacheItems.TryUpdate(key, expired, snapshot))
                        continue;

                    // Emit once per value instance. Cache item stays in dictionary (per requirements).
                    try
                    {
                        if (Volatile.Read(ref _isDisposed) != 0)
                            return;

                        _expirationSubject.OnNext(new ExpirationEventArgs(key));
                    }
                    catch (ObjectDisposedException)
                    {
                        // Dispose can race with timer callback; ignore.
                    }
                    catch (Exception ex)
                    {
                        Log($"Error notifying expiration for '{key}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in expiration check: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        }

        private static DateTime ComputeExpiresAt(DateTime nowUtc, TimeSpan expiration)
        {
            if (expiration == TimeSpan.MaxValue)
                return DateTime.MaxValue;

            try { return nowUtc.Add(expiration); }
            catch { return DateTime.MaxValue; } // overflow-safe
        }

        private void Log(string message)
        {
            try { _logger?.LogDebugMessage(message); }
            catch { }
        }

        private void EnsureNotDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, nameof(ChatCache));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;

            if (!disposing)
                return;

            try
            {
                // Stop future callbacks ASAP (doesn't wait for an already-running callback).
                try { _expirationTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                _expirationTimer?.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Error disposing timer: {ex.Message}");
            }

            try
            {
                _expirationSubject.OnCompleted();
                if (_expirationSubject is IDisposable disposableSubject)
                    disposableSubject.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Error disposing subject: {ex.Message}");
            }

            try
            {
                _cacheItems.Clear();
                Log("Cache disposed");
            }
            catch (Exception ex)
            {
                Log($"Error clearing cache during dispose: {ex.Message}");
            }
        }

        ~ChatCache()
        {
            Dispose(false);
        }

        private sealed class CacheItem
        {
            public CacheItem(object? value, DateTime expiresAt, bool isExpired)
            {
                Value = value;
                ExpiresAt = expiresAt;
                IsExpired = isExpired;
            }

            public object? Value { get; }
            public DateTime ExpiresAt { get; }
            public bool IsExpired { get; }

            public CacheItem WithExpired()
            {
                return IsExpired ? this : new CacheItem(Value, ExpiresAt, isExpired: true);
            }
        }
    }

    public class ExpirationEventArgs(string chatId) : EventArgs
    {
        public string ChatId { get; } = chatId;
    }
}
