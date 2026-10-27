using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Odinsons.ValheimLauncher
{
    public abstract record MarkdownBlock;

    public sealed record MarkdownParagraph(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

    /// <summary>Level 2 or 3.</summary>
    public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

    public sealed record MarkdownList(IReadOnlyList<IReadOnlyList<MarkdownSpan>> Items) : MarkdownBlock;

    /// <summary>A run of text in one style; a span with a Link is drawn as one.</summary>
    public sealed record MarkdownSpan(string Text, bool Bold = false, bool Italic = false, MarkdownLink Link = null);

    /// <summary>An https target with the host the player is shown before it opens.</summary>
    public sealed record MarkdownLink(Uri Target, string Host);

    /// <summary>
    /// The Markdown subset of docs/server-page.md: paragraphs, bold, italic, bullet lists, headings of
    /// level 2 and 3, and https links. Everything else (images, raw HTML, other link schemes) stays
    /// literal text, and the result holds nothing a renderer could mistake for markup.
    /// </summary>
    public static class MarkdownSubset
    {
        public const int MaxBlocks = 400;
        public const int MaxLinkLength = 2048;

        public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
        {
            var blocks = new List<MarkdownBlock>();
            var paragraph = new List<string>();
            List<StringBuilder> items = null;

            void FlushParagraph()
            {
                if (paragraph.Count > 0 && blocks.Count < MaxBlocks)
                    blocks.Add(new MarkdownParagraph(Inline(string.Join(" ", paragraph))));
                paragraph.Clear();
            }

            void FlushList()
            {
                if (items != null && blocks.Count < MaxBlocks)
                    blocks.Add(new MarkdownList(items.Select(item => Inline(item.ToString())).ToList()));
                items = null;
            }

            foreach (string line in PlainText.Lines(markdown).Split('\n'))
            {
                string text = line.TrimEnd();
                string trimmed = text.TrimStart();

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    FlushList();
                    continue;
                }

                if (text.StartsWith("### ", StringComparison.Ordinal) || text.StartsWith("## ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    FlushList();
                    int level = text[2] == '#' ? 3 : 2;
                    string heading = text.Substring(level + 1).Trim();
                    if (heading.Length > 0 && blocks.Count < MaxBlocks)
                        blocks.Add(new MarkdownHeading(level, Inline(heading)));
                }
                else if (trimmed.Length > 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
                {
                    FlushParagraph();
                    items ??= new List<StringBuilder>();
                    items.Add(new StringBuilder(trimmed.Substring(2).Trim()));
                }
                else if (items != null && trimmed.Length != text.Length)
                {
                    items[^1].Append(' ').Append(trimmed);
                }
                else
                {
                    FlushList();
                    paragraph.Add(trimmed);
                }

                if (blocks.Count >= MaxBlocks)
                    break;
            }

            FlushParagraph();
            FlushList();
            return blocks;
        }

        private enum TokenKind { Text, Bold, Italic, Link }

        private sealed class Token
        {
            public TokenKind Kind;
            public string Text;
            public MarkdownLink Link;
            public bool CanOpen, CanClose, Paired;
        }

        private static IReadOnlyList<MarkdownSpan> Inline(string text)
        {
            List<Token> tokens = Tokenize(text);
            Pair(tokens);
            return Build(tokens);
        }

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            var buffer = new StringBuilder();

            void Flush()
            {
                if (buffer.Length > 0)
                    tokens.Add(new Token { Kind = TokenKind.Text, Text = buffer.ToString() });
                buffer.Clear();
            }

            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];

                if (c == '\\' && i + 1 < s.Length && "\\*_[]()!".IndexOf(s[i + 1]) >= 0)
                {
                    buffer.Append(s[i + 1]);
                    i += 2;
                }
                else if (c == '!' && i + 1 < s.Length && s[i + 1] == '[' && TryLink(s, i + 1, out _, out _, out int imageEnd))
                {
                    buffer.Append(s, i, imageEnd - i);
                    i = imageEnd;
                }
                else if (c == '[' && TryLink(s, i, out string label, out string url, out int end))
                {
                    Flush();
                    MarkdownLink link = ValidLink(url);
                    tokens.Add(link == null
                        ? new Token { Kind = TokenKind.Text, Text = label }
                        : new Token { Kind = TokenKind.Link, Text = label, Link = link });
                    i = end;
                }
                else if (c == '*')
                {
                    int length = i + 1 < s.Length && s[i + 1] == '*' ? 2 : 1;
                    Mark(length == 2, s, i, length);
                    i += length;
                }
                else if (c == '_' && i + 1 < s.Length && s[i + 1] == '_')
                {
                    buffer.Append("__");
                    i += 2;
                }
                else if (c == '_')
                {
                    bool opens = i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && (i == 0 || !char.IsLetterOrDigit(s[i - 1]));
                    bool closes = i > 0 && !char.IsWhiteSpace(s[i - 1]) && (i + 1 == s.Length || !char.IsLetterOrDigit(s[i + 1]));
                    if (opens || closes)
                        Mark(false, s, i, 1);
                    else
                        buffer.Append(c);
                    i++;
                }
                else
                {
                    buffer.Append(c);
                    i++;
                }
            }

            Flush();
            return tokens;

            void Mark(bool bold, string text, int at, int length)
            {
                Flush();
                tokens.Add(new Token
                {
                    Kind = bold ? TokenKind.Bold : TokenKind.Italic,
                    Text = text.Substring(at, length),
                    CanOpen = at + length < text.Length && !char.IsWhiteSpace(text[at + length]),
                    CanClose = at > 0 && !char.IsWhiteSpace(text[at - 1])
                });
            }
        }

        private static void Pair(List<Token> tokens)
        {
            Token openBold = null, openItalic = null;
            foreach (Token token in tokens)
            {
                if (token.Kind != TokenKind.Bold && token.Kind != TokenKind.Italic)
                    continue;

                bool bold = token.Kind == TokenKind.Bold;
                Token open = bold ? openBold : openItalic;
                if (token.CanClose && open != null)
                {
                    open.Paired = token.Paired = true;
                    open = null;
                }
                else if (token.CanOpen)
                {
                    open = token;
                }

                if (bold) openBold = open; else openItalic = open;
            }
        }

        private static IReadOnlyList<MarkdownSpan> Build(List<Token> tokens)
        {
            var spans = new List<MarkdownSpan>();
            var run = new StringBuilder();
            bool bold = false, italic = false;
            bool runBold = false, runItalic = false;

            void Flush()
            {
                if (run.Length > 0)
                    spans.Add(new MarkdownSpan(run.ToString(), runBold, runItalic));
                run.Clear();
            }

            void Append(string text)
            {
                if (run.Length > 0 && (runBold != bold || runItalic != italic))
                    Flush();
                runBold = bold;
                runItalic = italic;
                run.Append(text);
            }

            foreach (Token token in tokens)
            {
                if (token.Kind == TokenKind.Link)
                {
                    Flush();
                    spans.Add(new MarkdownSpan(token.Text, bold, italic, token.Link));
                }
                else if (token.Kind == TokenKind.Bold && token.Paired)
                {
                    Flush();
                    bold = !bold;
                }
                else if (token.Kind == TokenKind.Italic && token.Paired)
                {
                    Flush();
                    italic = !italic;
                }
                else
                {
                    Append(token.Text);
                }
            }

            Flush();
            return spans;
        }

        private static bool TryLink(string s, int open, out string label, out string url, out int end)
        {
            label = url = null;
            end = 0;

            int j = open + 1;
            while (j < s.Length && s[j] != ']')
            {
                if (s[j] == '[')
                    return false;
                j += s[j] == '\\' ? 2 : 1;
            }
            if (j >= s.Length || j == open + 1 || j + 1 >= s.Length || s[j + 1] != '(')
                return false;

            int k = j + 2;
            while (k < s.Length && s[k] != ')')
            {
                if (char.IsWhiteSpace(s[k]))
                    return false;
                k++;
            }
            if (k >= s.Length)
                return false;

            label = Unescape(s.Substring(open + 1, j - open - 1));
            url = s.Substring(j + 2, k - j - 2);
            end = k + 1;
            return true;
        }

        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
                return text;

            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && "\\*_[]()!".IndexOf(text[i + 1]) >= 0)
                    i++;
                result.Append(text[i]);
            }
            return result.ToString();
        }

        private static MarkdownLink ValidLink(string url)
        {
            if (url.Length == 0 || url.Length > MaxLinkLength
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
                return null;

            string host = uri.IdnHost + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
            return new MarkdownLink(uri, host);
        }
    }
}
