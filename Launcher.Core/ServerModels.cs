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
    }

    public class ServerList
    {
        public List<Server> Servers { get; set; }
    }

    /// <summary>Mirrors that servers.info and build files are fetched from.</summary>
    public static class LauncherMirrors
    {
        public static readonly IReadOnlyList<string> Default = new[]
        {
            "https://server.odinsons.club/launcher/",
            "http://178.16.22.31/launcher/",
            "https://server-cf.odinsons.club/launcher/",
            "https://server-mirror.odinsons.club/launcher/"
        };
    }
}
