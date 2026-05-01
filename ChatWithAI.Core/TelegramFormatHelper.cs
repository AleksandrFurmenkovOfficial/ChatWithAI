using Markdig;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatWithAI.Core
{
    public static class TelegramFormatHelper
    {
        private static readonly HashSet<string> AllowedTelegramTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "strong",
            "i", "em",
            "u", "ins",
            "s", "strike", "del",
            "a",
            "code",
            "pre"
        };

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UsePipeTables()
            .Build();

        private static readonly Regex MultipleNewLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"</?([a-zA-Z0-9]+)(?:\s+[^>]*?)?/?>", RegexOptions.Compiled);
        private static readonly Regex HrefRegex = new("""href\s*=\s*(["'])(.*?)\1""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ConvertToTelegramHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            var html = Markdown.ToHtml(markdown, Pipeline);
            var sb = new StringBuilder(html);

            sb.Replace("<p>", "");
            sb.Replace("</p>", "\n");
            sb.Replace("<br>", "\n");
            sb.Replace("<br />", "\n");

            sb.Replace("<ul>", "");
            sb.Replace("</ul>", "");
            sb.Replace("<ol>", "");
            sb.Replace("</ol>", "");

            sb.Replace("<li>", "• ");
            sb.Replace("</li>", "\n");

            for (int i = 1; i <= 6; i++)
            {
                sb.Replace($"<h{i}>", "<b>");
                sb.Replace($"</h{i}>", "</b>\n");
            }

            sb.Replace("<strong>", "<b>");
            sb.Replace("</strong>", "</b>");
            sb.Replace("<em>", "<i>");
            sb.Replace("</em>", "</i>");
            sb.Replace("<del>", "<s>");
            sb.Replace("</del>", "</s>");

            sb.Replace("<sup>", "");
            sb.Replace("</sup>", "");
            sb.Replace("<sub>", "");
            sb.Replace("</sub>", "");
            sb.Replace("<hr>", "\n────────\n");
            sb.Replace("<hr />", "\n────────\n");
            sb.Replace("<hr/>", "\n────────\n");

            var result = SanitizeTelegramHtml(sb.ToString()).Trim();

            return MultipleNewLinesRegex.Replace(result, "\n\n");
        }

        private static string SanitizeTelegramHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            return HtmlTagRegex.Replace(html, static match =>
            {
                var tagName = match.Groups[1].Value;
                if (!AllowedTelegramTags.Contains(tagName))
                {
                    return string.Empty;
                }

                var isClosing = match.Value.StartsWith("</", StringComparison.Ordinal);
                if (isClosing)
                {
                    return $"</{tagName.ToLowerInvariant()}>";
                }

                if (tagName.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    var hrefMatch = HrefRegex.Match(match.Value);
                    if (!hrefMatch.Success)
                    {
                        return string.Empty;
                    }

                    var href = hrefMatch.Groups[2].Value;
                    return $"<a href=\"{href}\">";
                }

                return $"<{tagName.ToLowerInvariant()}>";
            });
        }
    }
}
