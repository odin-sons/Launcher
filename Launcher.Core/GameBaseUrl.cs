using System;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Resolves the "# base:" directive of a game manifest into the URL game files are
    /// downloaded from. The value is relative to the server directory ("../Game/892972_123/"),
    /// so every mirror serving the same tree resolves it to its own host.
    /// </summary>
    public static class GameBaseUrl
    {
        /// <returns>
        /// An absolute URL ending in '/', or null when there is no directive or it is unusable;
        /// <paramref name="problem"/> is set only in the second case.
        /// </returns>
        public static string Resolve(string directive, string serverDirectory, out string problem)
        {
            problem = null;

            if (string.IsNullOrWhiteSpace(directive)) return null;

            if (!Uri.TryCreate(serverDirectory, UriKind.Absolute, out Uri server) ||
                !Uri.TryCreate(directive.Trim(), UriKind.Relative, out Uri relative))
            {
                problem = $"'{directive}' is not a relative URL";
                return null;
            }

            var resolved = new Uri(server, relative);

            if (!string.Equals(resolved.Scheme, server.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolved.Authority, server.Authority, StringComparison.OrdinalIgnoreCase))
            {
                problem = $"'{directive}' points outside the server host";
                return null;
            }

            string url = resolved.AbsoluteUri;
            return url.EndsWith('/') ? url : url + "/";
        }
    }
}
