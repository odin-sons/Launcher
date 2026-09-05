using System.Text.Json;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// The handful of fields we care about from a mod's own manifest.json — the standard
    /// Thunderstore package manifest almost every plugin folder ships, since Thunderstore's
    /// mod manager and site both require one. A launcher-side data source of our own to
    /// duplicate would drift from these; reading what the mod already carries can't.
    /// </summary>
    public sealed class ModManifest
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public string VersionNumber { get; init; }
        public string WebsiteUrl { get; init; }

        public static ModManifest Parse(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string Field(string name) =>
                root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            return new ModManifest
            {
                Name = Field("name"),
                Description = Field("description"),
                VersionNumber = Field("version_number"),
                WebsiteUrl = Field("website_url")
            };
        }
    }
}
