using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    /// <summary>What the front end does next, after the registry has been asked about a profile.</summary>
    public enum GateStep
    {
        /// <summary>Carry on with the update and launch.</summary>
        Proceed,

        /// <summary>Nothing may be installed or started; say why.</summary>
        Refuse,

        /// <summary>The launch is refused and the installed files can be removed.</summary>
        OfferRemoval,

        /// <summary>Updates are paused; the installed files start only after the player confirms a warning.</summary>
        ConfirmThenLaunch,

        /// <summary>Updates are paused; the installed files start as they are.</summary>
        LaunchExisting,

        /// <summary>Updates are paused and the player did not ask to play; say why.</summary>
        Paused
    }

    public sealed class ServerCheck
    {
        public ServerDecision Decision { get; init; }
        public bool Installed { get; init; }
        public GateStep Step { get; init; }
    }

    /// <summary>
    /// Asks the registry about a profile before an update or launch and keeps the mod loader's entry
    /// point in step with the answer: disabled while the profile is suspended, back in place before
    /// a launch the status allows.
    /// </summary>
    public sealed class ServerAccess
    {
        private readonly ServerGate _gate;
        private readonly ProfileQuarantine _quarantine;

        public ServerAccess(ServerGate gate, ProfileQuarantine quarantine)
        {
            _gate = gate;
            _quarantine = quarantine;
        }

        public static GateStep Plan(ServerDecision decision, bool installed, bool start)
        {
            if ((installed ? decision.CanUpdate : decision.CanInstall) && decision.CanLaunch)
                return GateStep.Proceed;

            if (!decision.CanLaunch)
                return decision.OffersRemoval ? GateStep.OfferRemoval : GateStep.Refuse;

            if (!start) return GateStep.Paused;
            return decision.NeedsConfirmation ? GateStep.ConfirmThenLaunch : GateStep.LaunchExisting;
        }

        /// <param name="start">The player asked to play, not only to check or update.</param>
        public async Task<ServerCheck> CheckAsync(ServerProfile profile, string profileFolder, bool start,
            CancellationToken cancellationToken = default)
        {
            bool installed = ClientFolderGuard.HoldsClient(profileFolder);

            if (string.Equals(profile.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                return new ServerCheck { Decision = ServerDecision.Full, Installed = installed, Step = GateStep.Proceed };

            ServerDecision decision = await _gate.CheckAsync(profile.Id, installed, cancellationToken).ConfigureAwait(false);
            GateStep step = Plan(decision, installed, start);

            if (decision.Notice == ServerNotice.Suspended)
                Log(profileFolder, _quarantine.Apply(profileFolder));
            else if (step is GateStep.Proceed or GateStep.LaunchExisting)
                Log(profileFolder, _quarantine.Restore(profileFolder));

            return new ServerCheck { Decision = decision, Installed = installed, Step = step };
        }

        /// <summary>Puts the entry point back once the player has confirmed starting a profile that needs approval.</summary>
        public QuarantineResult ConfirmLaunch(string profileFolder)
        {
            QuarantineResult result = _quarantine.Restore(profileFolder);
            Log(profileFolder, result);
            return result;
        }

        /// <summary>Disables the entry point of every installed profile a freshly loaded index suspends, not only the one in view.</summary>
        public void ApplySuspensions(RegistryLoadResult load, Func<string, IEnumerable<string>> profileFoldersOf)
        {
            if (load?.Index == null) return;

            foreach (RegistryEntry entry in load.Index.Entries)
            {
                if (entry.Status != RegistryStatus.Suspended
                    || string.Equals(entry.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                    continue;

                foreach (string folder in profileFoldersOf(entry.Id))
                    Log(folder, _quarantine.Apply(folder));
            }
        }

        private static void Log(string folder, QuarantineResult result)
        {
            switch (result.Outcome)
            {
                case QuarantineOutcome.Applied:
                    LauncherLog.Info($"mod loader entry point disabled in '{folder}'");
                    break;
                case QuarantineOutcome.Restored:
                    LauncherLog.Info($"mod loader entry point restored in '{folder}'");
                    break;
                case QuarantineOutcome.Failed:
                    LauncherLog.Warn($"could not change the mod loader entry point in '{folder}': {result.Reason}");
                    break;
            }
        }
    }
}
