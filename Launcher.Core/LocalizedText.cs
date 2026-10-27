using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Picks the text for the interface language from a language code to text map.</summary>
    public static class LocalizedText
    {
        public const string Fallback = "en";

        /// <summary>The exact language, else its base language ("pt-BR" to "pt"), else English; default when none apply.</summary>
        public static T Pick<T>(IReadOnlyDictionary<string, T> texts, string language)
        {
            if (texts == null || texts.Count == 0)
                return default;

            string wanted = language?.Trim();
            if (!string.IsNullOrEmpty(wanted))
            {
                if (TryGet(texts, wanted, out T exact))
                    return exact;

                int cut = wanted.IndexOfAny(new[] { '-', '_' });
                if (cut > 0 && TryGet(texts, wanted.Substring(0, cut), out T baseLanguage))
                    return baseLanguage;
            }

            return TryGet(texts, Fallback, out T fallback) ? fallback : default;
        }

        private static bool TryGet<T>(IReadOnlyDictionary<string, T> texts, string language, out T value)
        {
            foreach (KeyValuePair<string, T> pair in texts)
            {
                if (string.Equals(pair.Key, language, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }
}
