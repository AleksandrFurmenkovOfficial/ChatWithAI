using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;


namespace ChatWithAI.Core
{
    public static class Helpers
    {
        public static byte[] ConvertImageBytesToWebp(byte[] imageBytes)
        {
            using (var inputStream = new MemoryStream(imageBytes))
            using (var outputStream = new MemoryStream())
            {
                using (var image = SixLabors.ImageSharp.Image.Load(inputStream))
                {
                    image.Save(outputStream, DefaultEncoder());
                }

                return outputStream.ToArray();
            }
        }

        public static async Task<string> EncodeImageToWebpBase64(Uri imageUrl,
            CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient())
            {
                var imageBytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken).ConfigureAwait(false);
                return Convert.ToBase64String(ConvertImageBytesToWebp(imageBytes));
            }
        }

        public static async Task<Stream> GetStreamFromUrlAsync(Uri url, CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient())
            {
                var bytes = await httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                return new MemoryStream(bytes);
            }
        }

        public static ImageEncoder DefaultEncoder()
        {
            return new WebpEncoder() { Quality = 100, FileFormat = WebpFileFormatType.Lossless };
        }

        public static byte[] ConvertBase64ToImageBytes(string base64String)
        {
            string cleanBase64 = base64String.Contains(',', StringComparison.InvariantCulture) ? base64String.Split(',')[1] : base64String;
            return Convert.FromBase64String(cleanBase64);
        }

        public static MemoryStream ConvertBase64ToMemoryStream(string base64String)
        {
            byte[] imageBytes = ConvertBase64ToImageBytes(base64String);
            return new MemoryStream(imageBytes);
        }

        public static string ConvertBase64ToBase64Webp(string base64)
        {
            using (var inputStream = ConvertBase64ToMemoryStream(base64))
            using (var outputStream = new MemoryStream())
            {
                using (var image = SixLabors.ImageSharp.Image.Load(inputStream))
                {
                    image.Save(outputStream, DefaultEncoder());
                }

                return Convert.ToBase64String(outputStream.ToArray());
            }
        }

        public static async Task<string> EncodeAudioToWebpBase64(Uri audioUrl,
            CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient())
            {
                var audioBytes = await httpClient.GetByteArrayAsync(audioUrl, cancellationToken).ConfigureAwait(false);
                return Convert.ToBase64String(audioBytes);
            }
        }

        public static int MessageIdToInt(MessageId s)
        {
            return int.Parse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        public static long StrToLong(string s)
        {
            return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}