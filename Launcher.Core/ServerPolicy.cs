using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    public enum ServerNotice
    {
        None,
        NeedsApproval,
        Suspended,
        Unlisted,
        RegistryUnavailable
    }

    /// <summary>What the launcher may do with one server profile right now.</summary>
    public sealed class ServerDecision
    {
        public bool Listed { get; init; }
        public bool CanInstall { get; init; }
        public bool CanUpdate { get; init; }
        public bool CanLaunch { get; init; }

        /// <summary>Launching needs an explicit yes from the player after the notice.</summary>
        public bool NeedsConfirmation { get; init; }

        /// <summary>The installed files can be removed from the launcher.</summary>
        public bool OffersRemoval { get; init; }

        public ServerNotice Notice { get; init; }

        public static readonly ServerDecision Full = new ServerDecision
        {
            Listed = true, CanInstall = true, CanUpdate = true, CanLaunch = true
        };
    }

    /// <summary>Maps a registry status to what a profile may do. Pure, so every status is easy to test.</summary>
    public static class ServerPolicy
    {
        public static ServerDecision Evaluate(RegistryLoadResult load, string profileId, bool installed)
        {
            if (string.Equals(profileId, ServerProfile.Default.Id, StringComparison.Ordinal))
                return ServerDecision.Full;

            if (load?.Index == null)
                return installed
                    ? new ServerDecision { Listed = true, CanLaunch = true, Notice = ServerNotice.RegistryUnavailable }
                    : new ServerDecision { Notice = ServerNotice.RegistryUnavailable };

            RegistryEntry entry = load.Index.Find(profileId);
            if (entry == null)
                return new ServerDecision { Listed = installed, CanLaunch = installed, Notice = ServerNotice.Unlisted };

            switch (entry.Status)
            {
                case RegistryStatus.Approved:
                    return ServerDecision.Full;

                case RegistryStatus.NeedsApproval:
                    return new ServerDecision
                    {
                        Listed = installed,
                        CanLaunch = installed,
                        NeedsConfirmation = installed,
                        Notice = ServerNotice.NeedsApproval
                    };

                default:
                    return new ServerDecision { OffersRemoval = installed, Notice = ServerNotice.Suspended };
            }
        }
    }

    /// <summary>Loads a fresh index and decides, so a status change takes effect at the next update or launch.</summary>
    public sealed class ServerGate
    {
        private readonly RegistryClient _client;

        public ServerGate(RegistryClient client)
        {
            _client = client;
        }

        public async Task<ServerDecision> CheckAsync(string profileId, bool installed,
            CancellationToken cancellationToken = default)
        {
            RegistryLoadResult load = await _client.LoadAsync(cancellationToken).ConfigureAwait(false);
            return ServerPolicy.Evaluate(load, profileId, installed);
        }
    }
}
