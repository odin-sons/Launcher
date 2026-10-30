using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The facts a community may state about itself; the launcher owns and localises their labels.</summary>
    public enum AboutFact { Playstyle, Pvp, Difficulty, Region, Languages, Wipes, Restarts }

    public sealed class AboutInfo
    {
        public static AboutInfo Empty { get; } = new AboutInfo
        {
            Tagline = new Dictionary<string, string>(),
            Facts = Array.Empty<KeyValuePair<AboutFact, string>>(),
            Description = new Dictionary<string, string>()
        };

        /// <summary>Language code to one line of plain text; has <c>en</c> or is empty.</summary>
        public IReadOnlyDictionary<string, string> Tagline { get; init; }

        /// <summary>In the order of the vocabulary, not the file.</summary>
        public IReadOnlyList<KeyValuePair<AboutFact, string>> Facts { get; init; }

        /// <summary>Language code to Markdown text; has <c>en</c> or is empty.</summary>
        public IReadOnlyDictionary<string, string> Description { get; init; }

        public bool IsEmpty => Tagline.Count == 0 && Facts.Count == 0 && Description.Count == 0;

        /// <summary>The built-in profile has no about.json: its servers.json description is the tagline and its info.md the description.</summary>
        public static AboutInfo FromLegacy(string tagline, string infoMarkdown)
        {
            string line = PlainText.Line(tagline);
            if (line.Length > AboutReader.MaxTaglineLength)
                line = line.Substring(0, AboutReader.MaxTaglineLength).TrimEnd();
            string description = PlainText.Lines(infoMarkdown).Trim();

            return new AboutInfo
            {
                Tagline = line.Length == 0 ? Empty.Tagline : new Dictionary<string, string> { [LocalizedText.Fallback] = line },
                Facts = Empty.Facts,
                Description = description.Length == 0 ? Empty.Description : new Dictionary<string, string> { [LocalizedText.Fallback] = description }
            };
        }

        public string TaglineFor(string language) => LocalizedText.Pick(Tagline, language);

        public string DescriptionFor(string language) => LocalizedText.Pick(Description, language);
    }

    /// <summary>
    /// Reads about.json. A file the launcher cannot use gives null (no About zone); a field that breaks
    /// its rules is left out on its own and the rest still applies.
    /// </summary>
    public static class AboutReader
    {
        public const int SupportedSchema = 1;
        public const int MaxBytes = DisplayFileLimits.About;
        public const int MaxTaglineLength = 80;
        public const int MaxFactLength = 48;
        public const int MaxDescriptionLength = 2000;
        public const int MaxLanguages = 16;

        private static readonly Regex LanguagePattern = new Regex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.CultureInvariant);

        private static readonly (string Name, AboutFact Fact)[] Vocabulary =
        {
            ("playstyle", AboutFact.Playstyle),
            ("pvp", AboutFact.Pvp),
            ("difficulty", AboutFact.Difficulty),
            ("region", AboutFact.Region),
            ("languages", AboutFact.Languages),
            ("wipes", AboutFact.Wipes),
            ("restarts", AboutFact.Restarts)
        };

        public static AboutInfo Read(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxBytes)
                return null;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(data);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("aboutSchema", out JsonElement schema)
                    || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != SupportedSchema)
                    return null;

                return new AboutInfo
                {
                    Tagline = ReadTexts(root, "tagline", MaxTaglineLength, singleLine: true),
                    Facts = ReadFacts(root),
                    Description = ReadTexts(root, "description", MaxDescriptionLength, singleLine: false)
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static IReadOnlyDictionary<string, string> ReadTexts(JsonElement root, string name, int maxLength, bool singleLine)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!root.TryGetProperty(name, out JsonElement texts) || texts.ValueKind != JsonValueKind.Object)
                return result;

            foreach (JsonProperty property in texts.EnumerateObject())
            {
                if (result.Count >= MaxLanguages || !LanguagePattern.IsMatch(property.Name)
                    || property.Value.ValueKind != JsonValueKind.String)
                    continue;

                string text = singleLine ? PlainText.Line(property.Value.GetString()) : PlainText.Lines(property.Value.GetString()).Trim();
                if (text.Length > 0 && text.Length <= maxLength)
                    result[property.Name.ToLowerInvariant()] = text;
            }

            if (!result.ContainsKey(LocalizedText.Fallback))
                result.Clear();
            return result;
        }

        private static IReadOnlyList<KeyValuePair<AboutFact, string>> ReadFacts(JsonElement root)
        {
            var result = new List<KeyValuePair<AboutFact, string>>();
            if (!root.TryGetProperty("facts", out JsonElement facts) || facts.ValueKind != JsonValueKind.Object)
                return result;

            foreach ((string name, AboutFact fact) in Vocabulary)
            {
                if (!facts.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                    continue;

                string text = PlainText.Line(value.GetString());
                if (text.Length > 0 && text.Length <= MaxFactLength)
                    result.Add(new KeyValuePair<AboutFact, string>(fact, text));
            }
            return result;
        }
    }
}
