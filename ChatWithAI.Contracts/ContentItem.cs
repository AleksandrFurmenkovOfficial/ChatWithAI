using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public abstract class ContentItem
    {
        abstract public string MimeType { get; set; }
        public string? Signature { get; set; }
    }

    public class TextContentItem : ContentItem
    {
        public override string MimeType { get; set; } = MediaTypeNames.Text.Plain;
        public string? Text { get; set; }
    }

    public class JsonObjectContentItem : ContentItem
    {
        public override string MimeType { get; set; } = MediaTypeNames.Application.Json;
        public object? JsonObject { get; set; }
    }

    public class ImageContentItem : ContentItem
    {
        private string? _imageInBase64;
        private Func<Uri, CancellationToken, Task<string?>>? _loader;
        private Task<string?>? _loadingTask;
        private readonly object _lock = new();
        private bool _isContentUnavailable;

        public override string MimeType { get; set; } = MediaTypeNames.Image.Webp;
        public Uri? ImageUrl { get; set; }

        /// <summary>
        /// Indicates that content loading failed (e.g., 404) and should not be retried.
        /// </summary>
        public bool IsContentUnavailable
        {
            get => _isContentUnavailable;
            set => _isContentUnavailable = value;
        }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<Uri, CancellationToken, Task<string?>>? Loader
        {
            get => _loader;
            set => _loader = value;
        }

        public string? ImageInBase64
        {
            get => _imageInBase64;
            set
            {
                _imageInBase64 = value;
                if (value != null)
                {
                    _loadingTask = null;
                }
            }
        }

        public async Task<string?> GetImageBase64Async(CancellationToken cancellationToken = default)
        {
            if (_imageInBase64 != null)
            {
                return _imageInBase64;
            }

            if (_isContentUnavailable || ImageUrl == null || _loader == null)
            {
                return null;
            }

            lock (_lock)
            {
                _loadingTask ??= LoadImageAsync(cancellationToken);
            }

            return await _loadingTask.ConfigureAwait(false);
        }

        private async Task<string?> LoadImageAsync(CancellationToken cancellationToken)
        {
            if (_loader == null || ImageUrl == null)
            {
                return null;
            }

            var result = await _loader(ImageUrl, cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                _isContentUnavailable = true;
            }
            else
            {
                _imageInBase64 = result;
            }
            return result;
        }

        public ImageContentItem CloneWithLoader(Func<Uri, CancellationToken, Task<string?>>? loader)
        {
            return new ImageContentItem
            {
                ImageUrl = ImageUrl == null ? null : new Uri(ImageUrl.OriginalString),
                ImageInBase64 = _imageInBase64,
                IsContentUnavailable = _isContentUnavailable,
                Loader = loader,
                Signature = Signature
            };
        }
    }

    public class AudioContentItem : ContentItem
    {
        private string? _audioInBase64;
        private Func<Uri, CancellationToken, Task<string?>>? _loader;
        private Task<string?>? _loadingTask;
        private readonly object _lock = new();
        private bool _isContentUnavailable;

        public override string MimeType { get; set; } = "audio/mpeg";
        public Uri? AudioUrl { get; set; }

        /// <summary>
        /// Indicates that content loading failed (e.g., 404) and should not be retried.
        /// </summary>
        public bool IsContentUnavailable
        {
            get => _isContentUnavailable;
            set => _isContentUnavailable = value;
        }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<Uri, CancellationToken, Task<string?>>? Loader
        {
            get => _loader;
            set => _loader = value;
        }

        public string? AudioInBase64
        {
            get => _audioInBase64;
            set
            {
                _audioInBase64 = value;
                if (value != null)
                {
                    _loadingTask = null;
                }
            }
        }

        public async Task<string?> GetAudioBase64Async(CancellationToken cancellationToken = default)
        {
            if (_audioInBase64 != null)
            {
                return _audioInBase64;
            }

            if (_isContentUnavailable || AudioUrl == null || _loader == null)
            {
                return null;
            }

            lock (_lock)
            {
                _loadingTask ??= LoadAudioAsync(cancellationToken);
            }

            return await _loadingTask.ConfigureAwait(false);
        }

        private async Task<string?> LoadAudioAsync(CancellationToken cancellationToken)
        {
            if (_loader == null || AudioUrl == null)
            {
                return null;
            }

            var result = await _loader(AudioUrl, cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                _isContentUnavailable = true;
            }
            else
            {
                _audioInBase64 = result;
            }
            return result;
        }

        public AudioContentItem CloneWithLoader(Func<Uri, CancellationToken, Task<string?>>? loader)
        {
            return new AudioContentItem
            {
                AudioUrl = AudioUrl == null ? null : new Uri(AudioUrl.OriginalString),
                AudioInBase64 = _audioInBase64,
                IsContentUnavailable = _isContentUnavailable,
                Loader = loader,
                Signature = Signature
            };
        }
    }

    public class DocumentContentItem : ContentItem
    {
        private string? _documentInBase64;
        private Func<Uri, CancellationToken, Task<string?>>? _loader;
        private Task<string?>? _loadingTask;
        private readonly object _lock = new();
        private bool _isContentUnavailable;

        public override string MimeType { get; set; } = MediaTypeNames.Application.Pdf;
        public Uri? DocumentUrl { get; set; }

        /// <summary>
        /// Indicates that content loading failed (e.g., 404) and should not be retried.
        /// </summary>
        public bool IsContentUnavailable
        {
            get => _isContentUnavailable;
            set => _isContentUnavailable = value;
        }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<Uri, CancellationToken, Task<string?>>? Loader
        {
            get => _loader;
            set => _loader = value;
        }

        public string? DocumentInBase64
        {
            get => _documentInBase64;
            set
            {
                _documentInBase64 = value;
                if (value != null)
                {
                    _loadingTask = null;
                }
            }
        }

        public async Task<string?> GetDocumentBase64Async(CancellationToken cancellationToken = default)
        {
            if (_documentInBase64 != null)
            {
                return _documentInBase64;
            }

            if (_isContentUnavailable || DocumentUrl == null || _loader == null)
            {
                return null;
            }

            lock (_lock)
            {
                _loadingTask ??= LoadDocumentAsync(cancellationToken);
            }

            return await _loadingTask.ConfigureAwait(false);
        }

        private async Task<string?> LoadDocumentAsync(CancellationToken cancellationToken)
        {
            if (_loader == null || DocumentUrl == null)
            {
                return null;
            }

            var result = await _loader(DocumentUrl, cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                _isContentUnavailable = true;
            }
            else
            {
                _documentInBase64 = result;
            }
            return result;
        }

        public DocumentContentItem CloneWithLoader(Func<Uri, CancellationToken, Task<string?>>? loader)
        {
            return new DocumentContentItem
            {
                DocumentUrl = DocumentUrl == null ? null : new Uri(DocumentUrl.OriginalString),
                DocumentInBase64 = _documentInBase64,
                IsContentUnavailable = _isContentUnavailable,
                Loader = loader,
                Signature = Signature,
                MimeType = MimeType
            };
        }
    }

    public class VideoContentItem : ContentItem
    {
        private string? _videoInBase64;
        private Func<Uri, CancellationToken, Task<string?>>? _loader;
        private Task<string?>? _loadingTask;
        private readonly object _lock = new();
        private bool _isContentUnavailable;

        public override string MimeType { get; set; } = "video/mp4";
        public Uri? VideoUrl { get; set; }

        /// <summary>
        /// Indicates that content loading failed (e.g., 404) and should not be retried.
        /// </summary>
        public bool IsContentUnavailable
        {
            get => _isContentUnavailable;
            set => _isContentUnavailable = value;
        }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<Uri, CancellationToken, Task<string?>>? Loader
        {
            get => _loader;
            set => _loader = value;
        }

        public string? VideoInBase64
        {
            get => _videoInBase64;
            set
            {
                _videoInBase64 = value;
                if (value != null)
                {
                    _loadingTask = null;
                }
            }
        }

        public async Task<string?> GetVideoBase64Async(CancellationToken cancellationToken = default)
        {
            if (_videoInBase64 != null)
            {
                return _videoInBase64;
            }

            if (_isContentUnavailable || VideoUrl == null || _loader == null)
            {
                return null;
            }

            lock (_lock)
            {
                _loadingTask ??= LoadVideoAsync(cancellationToken);
            }

            return await _loadingTask.ConfigureAwait(false);
        }

        private async Task<string?> LoadVideoAsync(CancellationToken cancellationToken)
        {
            if (_loader == null || VideoUrl == null)
            {
                return null;
            }

            var result = await _loader(VideoUrl, cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                _isContentUnavailable = true;
            }
            else
            {
                _videoInBase64 = result;
            }
            return result;
        }

        public VideoContentItem CloneWithLoader(Func<Uri, CancellationToken, Task<string?>>? loader)
        {
            return new VideoContentItem
            {
                VideoUrl = VideoUrl == null ? null : new Uri(VideoUrl.OriginalString),
                VideoInBase64 = _videoInBase64,
                IsContentUnavailable = _isContentUnavailable,
                Loader = loader,
                Signature = Signature,
                MimeType = MimeType
            };
        }
    }

    public enum MessageRole
    {
        eRoleUnknown,
        eRoleSystem,
        eRoleAI,
        eRoleUser,
        eRoleTool
    }
}
