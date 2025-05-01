using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ChatWithAI.Core
{
    public class CacheWithExpirationCallback : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cacheItems = new();
        private readonly Timer _expirationTimer;
        private readonly TimeSpan _checkInterval;
        private readonly ILogger _logger;
        private int _isDisposed;
        private readonly Subject<ExpirationEventArgs> _expirationSubject;

        public IObservable<ExpirationEventArgs> ExpirationObservable { get; }

        public CacheWithExpirationCallback(
            TimeSpan checkInterval,
            ILogger logger)
        {
            _checkInterval = checkInterval;
            _logger = logger;

            _expirationSubject = new Subject<ExpirationEventArgs>();

            ExpirationObservable = _expirationSubject.AsObservable();

            _expirationTimer = new Timer(CheckExpirations, null, _checkInterval, _checkInterval);
            Log("Cache initialized");
        }

        public void Set<T>(string key, T value, TimeSpan expiration)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            EnsureNotDisposed();

            var item = new CacheItem
            {
                Key = key,
                Value = value,
                ValueType = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiration == TimeSpan.MaxValue ? DateTime.MaxValue : DateTime.UtcNow.Add(expiration),
                IsExpired = 0
            };

            _cacheItems[key] = item;

            Log($"Set '{key}' with expiration at {item.ExpiresAt:yyyy-MM-dd HH:mm:ss.fff} UTC");
        }

        public T? Get<T>(string key)
        {
            if (!TryGetInternal(key, out var item) || item == null)
            {
                return default;
            }

            try
            {
                if (item.Value is T typedValue)
                {
                    return typedValue;
                }

                Type? storedType = null;
                try
                {
                    storedType = Type.GetType(item.ValueType);
                }
                catch (Exception ex)
                {
                    Log($"Error resolving type '{item.ValueType}' for key '{key}': {ex.Message}");
                    return default;
                }

                if (storedType == null)
                {
                    string message = "Could not resolve type '" + item.ValueType + "' for key '" + key + "'.";
                    Log(message);
                    return default;
                }

                if (!typeof(T).IsAssignableFrom(storedType))
                {
                    string requestedTypeName = typeof(T).Name;
                    string storedTypeName = storedType.Name;
                    string message = "Type mismatch for key '" + key + "'. Requested " + requestedTypeName + ", Stored " + storedTypeName + ".";
                    Log(message);
                    return default;
                }

                return (T)item.Value!;
            }
            catch (Exception ex)
            {
                Log($"Error getting value for key '{key}': {ex.Message}. Removing item.");
                Remove(key);
                return default;
            }
        }

        public bool TryGet<T>(string key, out T? value)
        {
            value = default;
            if (!TryGetInternal(key, out var item) || item == null)
            {
                return false;
            }

            try
            {
                if (item.Value is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                Type? storedType = null;
                try
                {
                    storedType = Type.GetType(item.ValueType);
                }
                catch (Exception ex)
                {
                    Log($"Error resolving type '{item.ValueType}' for key '{key}': {ex.Message}");
                    return false;
                }

                if (storedType == null)
                {
                    string message = "Could not resolve type '" + item.ValueType + "' for key '" + key + "'.";
                    Log(message);
                    return false;
                }

                if (!typeof(T).IsAssignableFrom(storedType))
                {
                    string requestedTypeName = typeof(T).Name;
                    string storedTypeName = storedType.Name;
                    string message = "Type mismatch for key '" + key + "'. Requested " + requestedTypeName + ", Stored " + storedTypeName + ".";
                    Log(message);
                    return false;
                }

                value = (T)item.Value!;
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error getting value for key '{key}': {ex.Message}. Removing item.");
                Remove(key);
                return false;
            }
        }

        private bool TryGetInternal(string key, out CacheItem? item)
        {
            item = null;
            if (string.IsNullOrEmpty(key))
                return false;

            try
            {
                EnsureNotDisposed();
            }
            catch
            {
                return false;
            }

            if (!_cacheItems.TryGetValue(key, out item) || item == null)
                return false;

            return true;
        }

        public bool Contains(string key)
        {
            return TryGetInternal(key, out _);
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            try
            {
                EnsureNotDisposed();
            }
            catch
            {
                return false;
            }

            bool itemRemoved = _cacheItems.TryRemove(key, out _);

            if (itemRemoved)
            {
                Log($"Manually removed item '{key}'");
                return true;
            }
            return false;
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
                return new List<string>(_cacheItems.Keys);
            }
        }

        private void CheckExpirations(object? state)
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0) return;

            try
            {
                var now = DateTime.UtcNow;
                var expiredItems = new List<(string Key, CacheItem Item)>();

                foreach (var kvp in _cacheItems)
                {
                    if (kvp.Value.ExpiresAt <= now && kvp.Value.IsExpired == 0)
                    {
                        if (Interlocked.CompareExchange(ref kvp.Value.IsExpired, 1, 0) == 0)
                        {
                            expiredItems.Add((kvp.Key, kvp.Value));
                        }
                    }
                }

                foreach (var (key, item) in expiredItems)
                {
                    NotifyItemExpiration(key, item);
                }
            }
            catch (Exception ex)
            {
                Log($"Error in expiration check: {ex.Message}");
            }
        }

        private void NotifyItemExpiration(string key, CacheItem item)
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0) return;

            try
            {
                Type? valueType = null;
                try
                {
                    valueType = Type.GetType(item.ValueType);
                }
                catch (Exception ex)
                {
                    Log($"Error resolving type '{item.ValueType}' for key '{key}': {ex.Message}");
                }

                if (valueType != null && item.Value != null)
                {
                    Log($"Notifying about expired item '{key}'");
                    var args = new ExpirationEventArgs(key, item.Value, valueType);
                    _expirationSubject.OnNext(args);
                }
            }
            catch (Exception ex)
            {
                Log($"Error preparing expiration notification: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            try
            {
                _logger?.LogInfoMessage(message);
            }
            catch
            {
            }
        }

        private void EnsureNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_isDisposed == 1, nameof(CacheWithExpirationCallback));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            if (disposing)
            {
                try
                {
                    _expirationTimer?.Dispose();

                    _expirationSubject.OnCompleted();
                    _expirationSubject.Dispose();
                }
                catch (Exception ex)
                {
                    Log($"Error disposing timer or subject: {ex.Message}");
                }

                try
                {
                    _cacheItems.Clear();
                    Log("Cache disposed");
                }
                catch (Exception ex)
                {
                    Log($"Error clearing collections during dispose: {ex.Message}");
                }
            }
        }

        ~CacheWithExpirationCallback()
        {
            Dispose(false);
        }

        private sealed class CacheItem
        {
            public string Key { get; set; } = string.Empty;
            public object? Value { get; set; }
            public string ValueType { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public int IsExpired;
        }
    }

    public class ExpirationEventArgs : EventArgs
    {
        public string Key { get; }
        public object Value { get; }
        public Type ValueType { get; }

        public ExpirationEventArgs(string key, object value, Type valueType)
        {
            Key = key;
            Value = value;
            ValueType = valueType;
        }
    }
}