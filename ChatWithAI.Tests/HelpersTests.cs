using SixLabors.ImageSharp;

namespace ChatWithAI.Tests;

public class HelpersTests
{
    #region ConvertBase64ToImageBytes Tests

    [Fact]
    public void ConvertBase64ToImageBytes_WithPlainBase64_ReturnsBytes()
    {
        var originalBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var base64 = Convert.ToBase64String(originalBytes);

        var result = Helpers.ConvertBase64ToImageBytes(base64);

        Assert.Equal(originalBytes, result);
    }

    [Fact]
    public void ConvertBase64ToImageBytes_WithDataUrlPrefix_ReturnsBytes()
    {
        var originalBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var base64 = Convert.ToBase64String(originalBytes);
        var dataUrl = $"data:image/png;base64,{base64}";

        var result = Helpers.ConvertBase64ToImageBytes(dataUrl);

        Assert.Equal(originalBytes, result);
    }

    [Fact]
    public void ConvertBase64ToImageBytes_WithJpegDataUrl_ReturnsBytes()
    {
        var originalBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var base64 = Convert.ToBase64String(originalBytes);
        var dataUrl = $"data:image/jpeg;base64,{base64}";

        var result = Helpers.ConvertBase64ToImageBytes(dataUrl);

        Assert.Equal(originalBytes, result);
    }

    [Fact]
    public void ConvertBase64ToImageBytes_WithEmptyContent_ReturnsEmptyArray()
    {
        var emptyBytes = Array.Empty<byte>();
        var base64 = Convert.ToBase64String(emptyBytes);

        var result = Helpers.ConvertBase64ToImageBytes(base64);

        Assert.Empty(result);
    }

    [Fact]
    public void ConvertBase64ToImageBytes_WithInvalidBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Helpers.ConvertBase64ToImageBytes("not_valid_base64!@#$"));
    }

    #endregion

    #region ConvertBase64ToMemoryStream Tests

    [Fact]
    public void ConvertBase64ToMemoryStream_ReturnsReadableStream()
    {
        var originalBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var base64 = Convert.ToBase64String(originalBytes);

        using var stream = Helpers.ConvertBase64ToMemoryStream(base64);

        Assert.True(stream.CanRead);
        Assert.Equal(originalBytes.Length, stream.Length);

        var readBytes = new byte[stream.Length];
        stream.Read(readBytes, 0, readBytes.Length);
        Assert.Equal(originalBytes, readBytes);
    }

    [Fact]
    public void ConvertBase64ToMemoryStream_WithDataUrl_ReturnsCorrectStream()
    {
        var originalBytes = new byte[] { 0xFF, 0xFE, 0xFD };
        var base64 = Convert.ToBase64String(originalBytes);
        var dataUrl = $"data:image/gif;base64,{base64}";

        using var stream = Helpers.ConvertBase64ToMemoryStream(dataUrl);

        var readBytes = new byte[stream.Length];
        stream.Read(readBytes, 0, readBytes.Length);
        Assert.Equal(originalBytes, readBytes);
    }

    #endregion

    #region MessageIdToInt Tests

    [Fact]
    public void MessageIdToInt_WithValidNumericId_ReturnsInt()
    {
        var messageId = new MessageId("12345");

        var result = Helpers.MessageIdToInt(messageId);

        Assert.Equal(12345, result);
    }

    [Fact]
    public void MessageIdToInt_WithZero_ReturnsZero()
    {
        var messageId = new MessageId("0");

        var result = Helpers.MessageIdToInt(messageId);

        Assert.Equal(0, result);
    }

    [Fact]
    public void MessageIdToInt_WithNegativeNumber_ReturnsNegative()
    {
        var messageId = new MessageId("-999");

        var result = Helpers.MessageIdToInt(messageId);

        Assert.Equal(-999, result);
    }

    [Fact]
    public void MessageIdToInt_WithMaxInt_ReturnsMaxInt()
    {
        var messageId = new MessageId(int.MaxValue.ToString());

        var result = Helpers.MessageIdToInt(messageId);

        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void MessageIdToInt_WithNonNumericId_ThrowsFormatException()
    {
        var messageId = new MessageId("abc");

        Assert.Throws<FormatException>(() => Helpers.MessageIdToInt(messageId));
    }

    [Fact]
    public void MessageIdToInt_WithEmptyId_ThrowsFormatException()
    {
        var messageId = new MessageId("");

        Assert.Throws<FormatException>(() => Helpers.MessageIdToInt(messageId));
    }

    #endregion

    #region StrToLong Tests

    [Fact]
    public void StrToLong_WithValidNumber_ReturnsLong()
    {
        var result = Helpers.StrToLong("9876543210");

        Assert.Equal(9876543210L, result);
    }

    [Fact]
    public void StrToLong_WithZero_ReturnsZero()
    {
        var result = Helpers.StrToLong("0");

        Assert.Equal(0L, result);
    }

    [Fact]
    public void StrToLong_WithNegativeNumber_ReturnsNegative()
    {
        var result = Helpers.StrToLong("-123456789");

        Assert.Equal(-123456789L, result);
    }

    [Fact]
    public void StrToLong_WithMaxLong_ReturnsMaxLong()
    {
        var result = Helpers.StrToLong(long.MaxValue.ToString());

        Assert.Equal(long.MaxValue, result);
    }

    [Fact]
    public void StrToLong_WithMinLong_ReturnsMinLong()
    {
        var result = Helpers.StrToLong(long.MinValue.ToString());

        Assert.Equal(long.MinValue, result);
    }

    [Fact]
    public void StrToLong_WithNonNumeric_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Helpers.StrToLong("not_a_number"));
    }

    [Fact]
    public void StrToLong_WithEmpty_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Helpers.StrToLong(""));
    }

    [Fact]
    public void StrToLong_WithWhitespace_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Helpers.StrToLong("   "));
    }

    #endregion

    #region ConvertImageBytesToWebp Tests

    [Fact]
    public void ConvertImageBytesToWebp_WithValidPngBytes_ReturnsWebpBytes()
    {
        // Create a minimal valid PNG image (1x1 pixel, RGBA)
        var pngBytes = CreateMinimalPngImage();

        var result = Helpers.ConvertImageBytesToWebp(pngBytes);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // WebP signature: RIFF....WEBP
        Assert.Equal((byte)'R', result[0]);
        Assert.Equal((byte)'I', result[1]);
        Assert.Equal((byte)'F', result[2]);
        Assert.Equal((byte)'F', result[3]);
    }

    [Fact]
    public void ConvertImageBytesToWebp_WithInvalidBytes_ThrowsException()
    {
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        Assert.ThrowsAny<Exception>(() => Helpers.ConvertImageBytesToWebp(invalidBytes));
    }

    [Fact]
    public void DefaultEncoder_ReturnsWebpEncoder()
    {
        var encoder = Helpers.DefaultEncoder();

        Assert.NotNull(encoder);
        Assert.IsType<SixLabors.ImageSharp.Formats.Webp.WebpEncoder>(encoder);
    }

    private static byte[] CreateMinimalPngImage()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    #endregion
}
