using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    public static class ByteSize
    {
        public static string Format(long bytes)
        {
            string[] suffixes = Loc.T("dl.sizeUnits").Split('|');
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            return $"{size:0.0} {suffixes[suffixIndex]}";
        }
    }

    /// <summary>The facts of the About zone that the launcher itself knows; a community cannot set them. Null means not known.</summary>
    public sealed record MeasuredFacts(string GameVersion, int? ModCount, long? DownloadBytes, int? Online, int? Capacity)
    {
        public static MeasuredFacts None { get; } = new MeasuredFacts(null, null, null, null, null);

        public static string GameManifestName =>
            RuntimePlatform.IsMacOS ? "game_macos.info" : RuntimePlatform.IsLinux ? "game_linux.info" : "game.info";

        /// <summary>Reads the mod count and the download size from the modpack manifest and the game version from the game manifest; either text may be null.</summary>
        public static MeasuredFacts FromManifests(string modpackManifest, string gameManifest)
        {
            int? mods = null;
            long? bytes = null;
            if (modpackManifest != null)
            {
                try
                {
                    List<Manifest.Entry> entries = Manifest.Read(new StringReader(modpackManifest));
                    mods = ModGrouping.GroupByPluginFolder(entries.Select(e => e.Path)).Count;
                    bytes = entries.Sum(e => e.Size);
                }
                catch (InvalidDataException)
                {
                }
            }

            string version = null;
            if (gameManifest != null)
                version = Manifest.ReadDirective(new StringReader(gameManifest), GameLocation.VersionDirective);

            return None with { GameVersion = string.IsNullOrWhiteSpace(version) ? null : PlainText.Line(version), ModCount = mods, DownloadBytes = bytes };
        }

        public MeasuredFacts WithOnline(int? online, int? capacity) => this with { Online = online, Capacity = capacity };
    }

    /// <summary>One line of the facts list; a measured one is marked.</summary>
    public sealed record FactLine(string Label, string Value, bool Measured);

    /// <summary>What the Server step shows besides the Online list: the About facts, description and tagline, and the News text.</summary>
    public sealed class ServerPage
    {
        public static ServerPage Empty { get; } = new ServerPage { About = AboutInfo.Empty, News = string.Empty };

        public AboutInfo About { get; init; }

        /// <summary>Markdown of the community's changelog; empty when there is none.</summary>
        public string News { get; init; }

        /// <summary>Measured facts first, then the community's in vocabulary order; labels are in the interface language.</summary>
        public static IReadOnlyList<FactLine> Facts(MeasuredFacts measured, AboutInfo about)
        {
            var lines = new List<FactLine>();
            measured ??= MeasuredFacts.None;

            if (measured.GameVersion != null)
                lines.Add(new FactLine(Loc.T("about.fact.gameVersion"), measured.GameVersion, true));
            if (measured.ModCount is { } mods)
                lines.Add(new FactLine(Loc.T("about.fact.mods"), mods.ToString(System.Globalization.CultureInfo.CurrentCulture), true));
            if (measured.DownloadBytes is { } bytes)
                lines.Add(new FactLine(Loc.T("about.fact.download"), ByteSize.Format(bytes), true));
            if (measured.Online is { } online)
            {
                string value = measured.Capacity is { } capacity && capacity > 0 ? $"{online} / {capacity}" : online.ToString(System.Globalization.CultureInfo.CurrentCulture);
                lines.Add(new FactLine(Loc.T("about.fact.online"), value, true));
            }

            foreach (KeyValuePair<AboutFact, string> fact in about?.Facts ?? Array.Empty<KeyValuePair<AboutFact, string>>())
                lines.Add(new FactLine(Loc.T("about.fact." + Key(fact.Key)), fact.Value, false));
            return lines;
        }

        private static string Key(AboutFact fact) => char.ToLowerInvariant(fact.ToString()[0]) + fact.ToString().Substring(1);
    }

    public static class ServerPageLoader
    {
        /// <summary>Loads the pinned about.json and changelog of a registry entry; a file that is not pinned or fails its checks is left out.</summary>
        public static async Task<ServerPage> LoadAsync(RegistryEntry entry, DisplayFiles files, CancellationToken cancellationToken = default)
        {
            AboutInfo about = AboutInfo.Empty;
            if (entry.About != null)
            {
                DisplayFileResult file = await files.LoadAsync(entry.About, DisplayFileLimits.About, cancellationToken).ConfigureAwait(false);
                if (file.Ok)
                    about = AboutReader.Read(file.Bytes) ?? AboutInfo.Empty;
            }

            string news = string.Empty;
            if (entry.Changelog != null)
            {
                DisplayFileResult file = await files.LoadAsync(entry.Changelog, DisplayFileLimits.Changelog, cancellationToken).ConfigureAwait(false);
                if (file.Ok)
                    news = Encoding.UTF8.GetString(file.Bytes);
            }

            return new ServerPage { About = about, News = news };
        }
    }
}
