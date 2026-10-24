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
        public ServerProfile(string id, string name, IReadOnlyList<string> mirrors)
        {
            Id = id;
            Name = name;
            Mirrors = mirrors;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Base URLs ending in "/", most preferred first.</summary>
        public IReadOnlyList<string> Mirrors { get; }

        public static readonly ServerProfile Odinsons = new ServerProfile(
            "odinsons",
            "Odinsons",
            new[]
            {
                // Cloudflare-fronted first: ECH is enabled on the zone, giving it the best odds
                // under partial network blocking. Falls back to the direct domain.
                "https://server-cf.odinsons.club/Launcher/",
                "https://server.odinsons.club/Launcher/"
            });

        public static ServerProfile Default => Odinsons;
    }
}
