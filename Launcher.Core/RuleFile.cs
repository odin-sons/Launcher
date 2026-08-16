using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Parses our line-based lists: one rule per line, '#' starts a comment,
    /// blank lines are skipped.
    ///
    /// Lives next to <see cref="Manifest"/> for the same reason: the format must be
    /// described in exactly one place. Rule lists are read by the Indexer on the server,
    /// while force_check.txt is read by the launcher on the player's side, and a parsing
    /// discrepancy between them would show up not as an error but as a silently lost rule.
    /// </summary>
    public static class RuleFile
    {
        public static List<string> Parse(IEnumerable<string> lines)
        {
            var rules = new List<string>();

            foreach (string raw in lines)
            {
                string line = StripComment(raw).Trim();

                if (line.Length == 0) continue;
                if (!Contains(rules, line)) rules.Add(line);
            }

            return rules;
        }

        /// <summary>
        /// Strips a comment: both a whole-line comment and a trailing one after a rule —
        /// paths deserve human-readable labels.
        ///
        /// In a trailing comment, '#' only starts one when preceded by whitespace.
        /// Otherwise a filename containing a '#' would get silently truncated, and a
        /// silently broken rule is expensive here.
        /// </summary>
        public static string StripComment(string line)
        {
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) return string.Empty;

            for (int i = 1; i < line.Length; i++)
                if (line[i] == '#' && char.IsWhiteSpace(line[i - 1]))
                    return line.Substring(0, i);

            return line;
        }

        private static bool Contains(List<string> rules, string value)
        {
            foreach (string rule in rules)
                if (string.Equals(rule, value, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }
    }
}
