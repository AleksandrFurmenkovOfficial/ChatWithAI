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