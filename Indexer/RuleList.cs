using System;
using System.Collections.Generic;
using System.IO;

namespace Indexer
{
    /// <summary>
    /// Rule lists: exclusions, game files, the greylist.
    ///
    /// The format is parsed by <see cref="Odinsons.ValheimLauncher.RuleFile"/> — one rule
    /// per line, '#' starts a comment. The parsing lives in Launcher.Core because the same
    /// format is read by the launcher on the player's side (force_check.txt), and a
    /// discrepancy between the two would show up not as an error but as a silently lost rule.
    ///
    /// This used to also hold parsing for the old .json lists, during the migration. It
    /// parsed not with a real parser but with a regex over quotes, meaning it would silently
    /// lie on an escaped quote or a nested object. The lists have moved, and that code is gone.
    /// </summary>
    internal static class RuleList
    {
        /// <param name="purpose">What to call the list in the output.</param>
        /// <param name="whenMissing">
        /// What to print if the file is missing. The text is supplied by the caller, because
        /// the cost of a missing list varies wildly: without the greylist everything keeps
        /// working, while without the admin-mods list, admin tools ship to every player.
        /// </param>
        public static List<string> Load(string purpose, string whenMissing, string fileName)
        {
            string path = Path.Combine(Environment.CurrentDirectory, fileName);

            if (!File.Exists(path))
            {
                Console.WriteLine($"{purpose}: {whenMissing}");
                Console.WriteLine($"    (expected file: {fileName})");
                return new List<string>();
            }

            try
            {
                List<string> rules = Odinsons.ValheimLauncher.RuleFile.Parse(File.ReadAllLines(path));
                Console.WriteLine($"{purpose}: {rules.Count} rule(s) from {fileName}");
                return rules;
            }
            catch (Exception ex)
            {
                // Can't silently proceed: an empty exclusion list means a manifest with
                // stray content in it, and an empty admin list means admin tools ship out.
                Console.WriteLine($"{purpose}: FAILED to read {fileName}: {ex.Message}");
                return new List<string>();
            }
        }
    }
}
