using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Reads index.json: an envelope {keyId, signature, payload} where payload is the base64 of the
    /// UTF-8 JSON that was signed, so the signed bytes never need re-serialising. The payload is
    /// parsed only after the signature checks out.
    /// </summary>
    public static class RegistryIndexReader
    {
        public const int SupportedSchema = 1;
        public const int MaxEnvelopeBytes = 1 << 20;
        public const int MaxEntries = 500;
        public const int MaxMirrors = 8;
        public const int MaxDigests = 64;
        public const int MaxTermsLanguages = 16;
        public static readonly TimeSpan MaxValidity = TimeSpan.FromDays(31);

        private static readonly Regex IdPattern = new Regex("^[a-z0-9][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
        private static readonly Regex DigestPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex LanguagePattern = new Regex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.CultureInvariant);

        public static RegistryIndex Read(byte[] envelope, RegistryKeySet keys)
        {
            if (envelope == null || envelope.Length == 0 || envelope.Length > MaxEnvelopeBytes)
                throw new RegistryException(RegistryFailure.Malformed, "The registry index has an unexpected size.");

            string keyId;
            byte[] signature, payload;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(envelope);
                JsonElement root = RequireObject(doc.RootElement, "envelope");
                keyId = RequireString(root, "keyId");
                signature = Convert.FromBase64String(RequireString(root, "signature"));
                payload = Convert.FromBase64String(RequireString(root, "payload"));
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is RegistryException)
            {
                throw new RegistryException(RegistryFailure.Malformed, "The registry index is not a valid envelope.");
            }

            if (!keys.Verify(keyId, payload, signature))
                throw new RegistryException(RegistryFailure.BadSignature, "The registry index signature is not valid.");

            return ParsePayload(payload);
        }

        /// <summary>Parses and validates a payload whose signature was already checked.</summary>
        public static RegistryIndex ParsePayload(byte[] payload)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                JsonElement root = RequireObject(doc.RootElement, "payload");

                int schema = RequireInt(root, "schema");
                if (schema != SupportedSchema)
                    throw new RegistryException(RegistryFailure.UnsupportedSchema,
                        $"The registry index uses schema {schema}; this launcher supports {SupportedSchema}.");

                DateTimeOffset issuedAt = RequireTime(root, "issuedAt");
                DateTimeOffset expiresAt = RequireTime(root, "expiresAt");
                if (expiresAt <= issuedAt || expiresAt - issuedAt > MaxValidity)
                    throw Invalid("The registry index has an invalid validity window.");

                long serial = RequireLong(root, "serial");
                if (serial < 1)
                    throw Invalid("The registry index serial must be positive.");

                if (!Version.TryParse(RequireString(root, "minLauncher"), out Version minLauncher))
                    throw Invalid("The registry index minLauncher is not a version.");

                JsonElement servers = RequireProperty(root, "servers");
                if (servers.ValueKind != JsonValueKind.Array || servers.GetArrayLength() > MaxEntries)
                    throw Invalid("The registry index server list is invalid.");

                var entries = new List<RegistryEntry>();
                foreach (JsonElement element in servers.EnumerateArray())
                    entries.Add(ParseEntry(element));

                if (entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                    throw Invalid("The registry index lists the same id twice.");

                return new RegistryIndex
                {
                    Schema = schema,
                    Serial = serial,
                    IssuedAt = issuedAt,
                    ExpiresAt = expiresAt,
                    MinLauncher = minLauncher,
                    Entries = entries
                };
            }
            catch (JsonException)
            {
                throw new RegistryException(RegistryFailure.Malformed, "The registry index payload is not valid JSON.");
            }
        }

        /// <summary>Parses one servers/&lt;id&gt;.json source file.</summary>
        public static RegistryEntry ParseEntry(JsonElement element)
        {
            JsonElement obj = RequireObject(element, "server entry");

            string id = RequireString(obj, "id");
            if (!IdPattern.IsMatch(id))
                throw Invalid($"'{id}' is not a valid server id.");

            string name = RequireString(obj, "name");
            if (name.Length == 0 || name.Length > 48 || name.Any(char.IsControl))
                throw Invalid($"Server '{id}' has an invalid name.");

            JsonElement mirrorsElement = RequireProperty(obj, "mirrors");
            if (mirrorsElement.ValueKind != JsonValueKind.Array)
                throw Invalid($"Server '{id}' mirrors must be an array.");
            var mirrors = new List<string>();
            foreach (JsonElement mirror in mirrorsElement.EnumerateArray())
            {
                string url = mirror.ValueKind == JsonValueKind.String ? mirror.GetString() : null;
                if (!IsHttpsBaseUrl(url))
                    throw Invalid($"Server '{id}' has an invalid mirror.");
                mirrors.Add(url);
            }
            if (mirrors.Count == 0 || mirrors.Count > MaxMirrors)
                throw Invalid($"Server '{id}' needs between 1 and {MaxMirrors} mirrors.");

            IReadOnlyDictionary<string, string> digests = ParseDigests(obj, id);

            return new RegistryEntry
            {
                Id = id,
                Name = name,
                Mirrors = mirrors,
                Website = OptionalHttpsLink(obj, "website", id),
                Community = OptionalHttpsLink(obj, "community", id),
                Status = ParseStatus(RequireString(obj, "status"), id),
                Digests = digests,
                Theme = OptionalPinnedPath(obj, "theme", id, digests),
                About = OptionalPinnedPath(obj, "about", id, digests),
                Terms = ParseTerms(obj, id, digests)
            };
        }

        private static string OptionalPinnedPath(JsonElement obj, string name, string id,
            IReadOnlyDictionary<string, string> digests)
        {
            if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                return null;

            string path = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (path == null || !IsRelativePath(path) || !digests.ContainsKey(path))
                throw Invalid($"Server '{id}' has a {name} that is not a pinned path.");
            return path;
        }

        private static IReadOnlyDictionary<string, string> ParseTerms(JsonElement obj, string id,
            IReadOnlyDictionary<string, string> digests)
        {
            if (!obj.TryGetProperty("terms", out JsonElement terms) || terms.ValueKind == JsonValueKind.Null)
                return new Dictionary<string, string>();
            if (terms.ValueKind != JsonValueKind.Object)
                throw Invalid($"Server '{id}' terms must be an object.");

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in terms.EnumerateObject())
            {
                string path = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (!LanguagePattern.IsMatch(property.Name) || path == null || !IsRelativePath(path)
                    || !digests.ContainsKey(path))
                    throw Invalid($"Server '{id}' has an invalid terms entry '{property.Name}'.");
                result[property.Name] = path;
            }

            if (result.Count > MaxTermsLanguages || (result.Count > 0 && !result.ContainsKey("en")))
                throw Invalid($"Server '{id}' terms need an 'en' text and at most {MaxTermsLanguages} languages.");
            return result;
        }

        private static RegistryStatus ParseStatus(string value, string id) => value switch
        {
            "approved" => RegistryStatus.Approved,
            "needs-approval" => RegistryStatus.NeedsApproval,
            "suspended" => RegistryStatus.Suspended,
            _ => throw Invalid($"Server '{id}' has an unknown status '{value}'.")
        };

        private static IReadOnlyDictionary<string, string> ParseDigests(JsonElement obj, string id)
        {
            JsonElement digests = RequireProperty(obj, "digests");
            if (digests.ValueKind != JsonValueKind.Object)
                throw Invalid($"Server '{id}' digests must be an object.");

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in digests.EnumerateObject())
            {
                string value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (!IsRelativePath(property.Name) || value == null || !DigestPattern.IsMatch(value))
                    throw Invalid($"Server '{id}' has an invalid digest entry '{property.Name}'.");
                result[property.Name] = value;
            }

            if (result.Count == 0 || result.Count > MaxDigests)
                throw Invalid($"Server '{id}' needs between 1 and {MaxDigests} digests.");
            return result;
        }

        private static string OptionalHttpsLink(JsonElement obj, string name, string id)
        {
            if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                return null;

            string url = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length > 0)
                throw Invalid($"Server '{id}' has an invalid {name} link.");
            return uri.AbsoluteUri;
        }

        private static bool IsHttpsBaseUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && url.EndsWith("/", StringComparison.Ordinal);

        internal static bool IsRelativePath(string path) =>
            path.Length > 0
            && !path.StartsWith("/", StringComparison.Ordinal)
            && !path.Contains('\\')
            && !path.Split('/').Any(part => part.Length == 0 || part == "." || part == "..");

        private static RegistryException Invalid(string message) => new RegistryException(RegistryFailure.Invalid, message);

        private static JsonElement RequireObject(JsonElement element, string what)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw Invalid($"The {what} must be a JSON object.");
            return element;
        }

        private static JsonElement RequireProperty(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out JsonElement value))
                throw Invalid($"Missing field '{name}'.");
            return value;
        }

        private static string RequireString(JsonElement obj, string name)
        {
            JsonElement value = RequireProperty(obj, name);
            if (value.ValueKind != JsonValueKind.String)
                throw Invalid($"Field '{name}' must be a string.");
            return value.GetString();
        }

        private static long RequireLong(JsonElement obj, string name)
        {
            JsonElement value = RequireProperty(obj, name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
                throw Invalid($"Field '{name}' must be an integer.");
            return number;
        }

        private static int RequireInt(JsonElement obj, string name)
        {
            long number = RequireLong(obj, name);
            if (number < int.MinValue || number > int.MaxValue)
                throw Invalid($"Field '{name}' is out of range.");
            return (int)number;
        }

        private static DateTimeOffset RequireTime(JsonElement obj, string name)
        {
            string text = RequireString(obj, name);
            if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time))
                throw Invalid($"Field '{name}' must be a UTC time like 2026-01-01T00:00:00Z.");
            return time;
        }
    }
}
