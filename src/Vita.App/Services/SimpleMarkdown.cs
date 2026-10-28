using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Vita.App.Services;

/// <summary>Tiny, safe Markdown renderer for chat answers (headings, lists, bold, code, links).</summary>
public static class SimpleMarkdown
{
    private static readonly Regex BoldRx = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRx = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex CodeRx = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex LinkRx = new(@"\[([^\]]+)\]\((https?://[^\s)]+)\)", RegexOptions.Compiled);

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var sb = new StringBuilder();
        var paragraph = new List<string>();
        var list = new List<string>();
        var listTag = "ul";
        var inCode = false;
        var code = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br />", paragraph.Select(Inline))).Append("</p>");
            paragraph.Clear();
        }

        void FlushList()
        {
            if (list.Count == 0) return;
            sb.Append($"<{listTag}>");
            foreach (var item in list) sb.Append("<li>").Append(Inline(item)).Append("</li>");
            sb.Append($"</{listTag}>");
            list.Clear();
            listTag = "ul";
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode)
                {
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(code.ToString().TrimEnd())).Append("</code></pre>");
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    FlushParagraph();
                    FlushList();
                    inCode = true;
                }
                continue;
            }
            if (inCode)
            {
                code.AppendLine(raw);
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (trimmed.StartsWith("### "))
            {
                FlushParagraph(); FlushList();
                sb.Append("<h4>").Append(Inline(trimmed[4..])).Append("</h4>");
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                FlushParagraph(); FlushList();
                sb.Append("<h3>").Append(Inline(trimmed[3..])).Append("</h3>");
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                FlushParagraph(); FlushList();
                sb.Append("<h3>").Append(Inline(trimmed[2..])).Append("</h3>");
                continue;
            }

            var bulletMatch = Regex.Match(trimmed, @"^[-*•]\s+(.*)$");
            if (bulletMatch.Success)
            {
                FlushParagraph();
                if (listTag != "ul") FlushList();
                listTag = "ul";
                list.Add(bulletMatch.Groups[1].Value);
                continue;
            }

            var numbered = Regex.Match(trimmed, @"^\d+[\.\)]\s+(.*)$");
            if (numbered.Success)
            {
                FlushParagraph();
                if (listTag != "ol") FlushList();
                listTag = "ol";
                list.Add(numbered.Groups[1].Value);
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        if (inCode && code.Length > 0)
            sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(code.ToString().TrimEnd())).Append("</code></pre>");
        FlushParagraph();
        FlushList();
        return sb.ToString();
    }

    private static string Inline(string raw)
    {
        var text = WebUtility.HtmlEncode(raw);
        text = LinkRx.Replace(text, m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
        });
        text = CodeRx.Replace(text, "<code>$1</code>");
        text = BoldRx.Replace(text, "<strong>$1</strong>");
        text = ItalicRx.Replace(text, "<em>$1</em>");
        return text;
    }
}
