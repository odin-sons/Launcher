using System;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Reads just enough of a "Keep a Changelog"-style changelog.md to know what the newest
    /// entry is — not a full markdown renderer (MainWindow's own ParseMarkdownToUI handles
    /// display).
    ///
    /// A version boundary is a line starting with exactly "## " — a line with more hashes
    /// ("### Fixed", a sub-section) doesn't match, since the third character is "#" there,
    /// not a space. Whatever follows on that line is the version identifier, taken verbatim:
    /// not necessarily semver, since the mods/game changelog is dated rather than numbered.
    /// The newest entry must be first in the file; nothing here enforces that.
    /// </summary>
    public static class ChangelogMarkdown
    {
        private const string VersionHeaderPrefix = "## ";

        /// <returns>The identifier from the first version-boundary line, or null if there isn't one.</returns>
        public static string LatestVersion(string markdownText)
        {
            if (string.IsNullOrEmpty(markdownText)) return null;

            foreach (string rawLine in markdownText.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (!line.StartsWith(VersionHeaderPrefix, StringComparison.Ordinal)) continue;

                string header = line.Substring(VersionHeaderPrefix.Length).Trim();
                if (header.Length == 0) continue;

                // "[1.4.3] - 2026-09-10" -> "1.4.3"
                if (header.StartsWith("[", StringComparison.Ordinal))
                {
                    int close = header.IndexOf(']');
                    if (close > 1) return header.Substring(1, close - 1).Trim();
                }

                // "2026-09-10 - Ashlands hotfix" -> "2026-09-10"; a header with no " - " at
                // all (e.g. "Beta") is taken as-is, whole.
                int dash = header.IndexOf(" - ", StringComparison.Ordinal);
                return dash > 0 ? header.Substring(0, dash).Trim() : header;
            }

            return null;
        }
    }
}
