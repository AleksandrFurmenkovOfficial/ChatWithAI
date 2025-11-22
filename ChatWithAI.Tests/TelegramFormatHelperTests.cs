namespace ChatWithAI.Tests;

public class TelegramFormatHelperTests
{
    #region Basic Markdown Conversion

    [Fact]
    public void ConvertToTelegramHtml_WithEmptyString_ReturnsEmpty()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("");
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithNull_ReturnsEmpty()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml(null!);
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithWhitespace_ReturnsEmpty()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("   ");
        Assert.Equal("", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithPlainText_ReturnsText()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("Hello World");
        Assert.Contains("Hello World", result);
    }

    #endregion

    #region Bold/Strong Text

    [Fact]
    public void ConvertToTelegramHtml_WithBoldMarkdown_ReturnsBoldHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("**bold text**");
        Assert.Contains("<b>bold text</b>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithDoubleBold_ReturnsBoldHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("__bold text__");
        Assert.Contains("<b>bold text</b>", result);
    }

    #endregion

    #region Italic/Emphasis Text

    [Fact]
    public void ConvertToTelegramHtml_WithItalicMarkdown_ReturnsItalicHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("*italic text*");
        Assert.Contains("<i>italic text</i>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithUnderscoreItalic_ReturnsItalicHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("_italic text_");
        Assert.Contains("<i>italic text</i>", result);
    }

    #endregion

    #region Strikethrough Text

    [Fact]
    public void ConvertToTelegramHtml_WithStrikethrough_ReturnsStrikethroughHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("~~deleted text~~");
        Assert.Contains("<s>deleted text</s>", result);
    }

    #endregion

    #region Headers

    [Fact]
    public void ConvertToTelegramHtml_WithH1_ReturnsBoldHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("# Header 1");
        Assert.Contains("<b>Header 1</b>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithH2_ReturnsBoldHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("## Header 2");
        Assert.Contains("<b>Header 2</b>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithH3_ReturnsBoldHtml()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("### Header 3");
        Assert.Contains("<b>Header 3</b>", result);
    }

    [Theory]
    [InlineData("#### Header 4")]
    [InlineData("##### Header 5")]
    [InlineData("###### Header 6")]
    public void ConvertToTelegramHtml_WithSubHeaders_ReturnsBoldHtml(string header)
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml(header);
        Assert.Contains("<b>", result);
        Assert.Contains("</b>", result);
    }

    #endregion

    #region Lists

    [Fact]
    public void ConvertToTelegramHtml_WithUnorderedList_ReturnsBulletPoints()
    {
        var markdown = "- Item 1\n- Item 2\n- Item 3";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        Assert.Contains("• Item 1", result);
        Assert.Contains("• Item 2", result);
        Assert.Contains("• Item 3", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithOrderedList_ReturnsBulletPoints()
    {
        var markdown = "1. First\n2. Second\n3. Third";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        // Ordered lists also become bullet points in this implementation
        Assert.Contains("•", result);
    }

    #endregion

    #region Paragraphs and Line Breaks

    [Fact]
    public void ConvertToTelegramHtml_RemovesParagraphTags()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("Line 1\n\nLine 2");

        Assert.DoesNotContain("<p>", result);
        Assert.DoesNotContain("</p>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_NormalizesMultipleNewlines()
    {
        var markdown = "Line 1\n\n\n\n\nLine 2";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        // Should not have more than 2 consecutive newlines
        Assert.DoesNotContain("\n\n\n", result);
    }

    #endregion

    #region Links

    [Fact]
    public void ConvertToTelegramHtml_WithMarkdownLink_CreatesLink()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("[Click here](https://example.com)");
        Assert.Contains("<a", result);
        Assert.Contains("https://example.com", result);
        Assert.Contains("Click here", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithAutoLink_CreatesLink()
    {
        var result = TelegramFormatHelper.ConvertToTelegramHtml("Visit https://example.com for more");
        Assert.Contains("<a", result);
        Assert.Contains("https://example.com", result);
    }

    #endregion

    #region Complex Content

    [Fact]
    public void ConvertToTelegramHtml_WithMixedFormatting_HandlesCorrectly()
    {
        var markdown = "**Bold** and *italic* and ~~strikethrough~~";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        Assert.Contains("<b>Bold</b>", result);
        Assert.Contains("<i>italic</i>", result);
        Assert.Contains("<s>strikethrough</s>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithNestedFormatting_HandlesCorrectly()
    {
        var markdown = "***bold and italic***";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        // Should contain both bold and italic tags
        Assert.Contains("<b>", result);
        Assert.Contains("<i>", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithCodeBlock_PreservesContent()
    {
        var markdown = "```\ncode block\n```";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        Assert.Contains("code", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithInlineCode_PreservesContent()
    {
        var markdown = "Use `inline code` here";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        Assert.Contains("inline code", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ConvertToTelegramHtml_WithSpecialCharacters_HandlesCorrectly()
    {
        var markdown = "Special chars: < > & \"";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        // HTML entities should be used
        Assert.Contains("&lt;", result);
        Assert.Contains("&gt;", result);
        Assert.Contains("&amp;", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithUnicodeText_PreservesText()
    {
        var markdown = "Привет мир! 你好世界! 🎉";
        var result = TelegramFormatHelper.ConvertToTelegramHtml(markdown);

        Assert.Contains("Привет мир!", result);
        Assert.Contains("你好世界!", result);
        Assert.Contains("🎉", result);
    }

    [Fact]
    public void ConvertToTelegramHtml_WithLongText_HandlesCorrectly()
    {
        var longText = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"Line {i} with **bold** text"));
        var result = TelegramFormatHelper.ConvertToTelegramHtml(longText);

        Assert.Contains("<b>bold</b>", result);
        Assert.Contains("Line 0", result);
        Assert.Contains("Line 99", result);
    }

    #endregion
}
