using System;
using System.Collections.Generic;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    public sealed class ProfileChoice
    {
        public ProfileChoice(ServerProfile profile, ServerDecision decision)
        {
            Profile = profile;
            Decision = decision;
        }

        public ServerProfile Profile { get; }
        public ServerDecision Decision { get; }
    }

    /// <summary>The profiles a player can pick from: the built-in one, then the registry's listed
    /// communities by name.</summary>
    public static class ProfileCatalog
    {
        public static IReadOnlyList<ProfileChoice> Build(RegistryLoadResult load, Func<string, bool> isInstalled)
        {
            var choices = new List<ProfileChoice>
            {
                new ProfileChoice(ServerProfile.Default, ServerDecision.Full)
            };

            if (load?.Index == null) return choices;

            choices.AddRange(load.Index.Entries
                .Where(entry => !string.Equals(entry.Id, ServerProfile.Default.Id, StringComparison.Ordinal))
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(entry => new ProfileChoice(entry.ToProfile(),
                    ServerPolicy.Evaluate(load, entry.Id, isInstalled(entry.Id))))
                .Where(choice => choice.Decision.Listed));
            return choices;
        }

        /// <summary>The remembered profile when it is still offered, otherwise the built-in one.</summary>
        public static ProfileChoice Resolve(string savedId, IReadOnlyList<ProfileChoice> choices) =>
            choices.FirstOrDefault(c => string.Equals(c.Profile.Id, savedId, StringComparison.Ordinal))
                ?? choices[0];
    }
}
