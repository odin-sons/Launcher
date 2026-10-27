using System.Text;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Text a community wrote, made safe to draw: no control characters and nothing that reorders or hides text.</summary>
    public static class PlainText
    {
        /// <summary>One line: line breaks and tabs become spaces, runs of spaces are kept as written but the ends are trimmed.</summary>
        public static string Line(string text) => Clean(text, keepLineBreaks: false).Trim();

        /// <summary>Several lines: <c>\n</c> is kept (CR and CRLF become it), tabs become spaces.</summary>
        public static string Lines(string text) => Clean(text, keepLineBreaks: true);

        private static string Clean(string text, bool keepLineBreaks)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    result.Append(keepLineBreaks ? '\n' : ' ');
                }
                else if (c == '\n')
                {
                    result.Append(keepLineBreaks ? '\n' : ' ');
                }
                else if (c == '\t')
                {
                    result.Append(' ');
                }
                else if (!char.IsControl(c) && !IsInvisibleFormatting(c))
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        private static bool IsInvisibleFormatting(char c) =>
            (c >= '​' && c <= '‏')
            || (c >= '‪' && c <= '‮')
            || (c >= '⁠' && c <= '⁤')
            || (c >= '⁦' && c <= '⁩')
            || c == '﻿';
    }
}
