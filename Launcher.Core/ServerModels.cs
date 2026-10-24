using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>An entry from servers.info.</summary>
    public class Server
    {
        public string Name { get; set; }
        public bool Hidden { get; set; }
        public string Ip { get; set; }
        public int QueryPort { get; set; }
        public int HttpPort { get; set; }

        /// <summary>Free-text characteristics shown on the server tab (mode, wipe schedule,
        /// rules, …) — one line per "\n". Optional; absent in servers.json means no
        /// description is shown for that server.</summary>
        public string Description { get; set; }
    }

    public class ServerList
    {
        public List<Server> Servers { get; set; }
    }

    /// <summary>A community the launcher can connect to: where its servers.json and profile
    /// files are fetched from, tried in order.</summary>
    public sealed class ServerProfile
    {
        public ServerProfile(string id, string name, IReadOnlyList<string> mirrors,
            string website = null, string discord = null)
        {
            Id = id;
            Name = name;
            Mirrors = mirrors;
            Website = HttpsOrNull(website);
            Discord = HttpsOrNull(discord);
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Base URLs ending in "/", most preferred first.</summary>
        public IReadOnlyList<string> Mirrors { get; }

        /// <summary>Community links shown in the launcher; null when the community has none.
        /// Only https URLs are kept, since the launcher hands them to the OS to open.</summary>
        public string Website { get; }
        public string Discord { get; }

        private static string HttpsOrNull(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.AbsoluteUri
                : null;

        public static readonly ServerProfile Odinsons = new ServerProfile(
            "odinsons",
            "Odinsons",
            new[]
            {
                // Cloudflare-fronted first: ECH is enabled on the zone, giving it the best odds
                // under partial network blocking. Falls back to the direct domain.
                "https://server-cf.odinsons.club/Launcher/",
                "https://server.odinsons.club/Launcher/"
            },
            website: "https://odinsons.club/",
            discord: "https://discord.gg/eTteBxWcfu");

        public static ServerProfile Default => Odinsons;
    }
}
