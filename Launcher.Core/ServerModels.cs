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

    /// <summary>Mirrors that servers.info and profile files are fetched from.</summary>
    public static class LauncherMirrors
    {
        public static readonly IReadOnlyList<string> Default = new[]
        {
            // Cloudflare-fronted first: ECH is enabled on the zone, giving it the best odds
            // under partial network blocking. Falls back to the direct domain.
            "https://server-cf.odinsons.club/Launcher/",
            "https://server.odinsons.club/Launcher/"
        };
    }
}
