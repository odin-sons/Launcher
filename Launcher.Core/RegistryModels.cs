using System;
using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    public enum RegistryStatus
    {
        Approved,
        NeedsApproval,
        Suspended
    }

    public enum RegistryFailure
    {
        Malformed,
        UnknownKey,
        BadSignature,
        UnsupportedSchema,
        Invalid
    }

    public sealed class RegistryException : Exception
    {
        public RegistryException(RegistryFailure failure, string message) : base(message)
        {
            Failure = failure;
        }

        public RegistryFailure Failure { get; }
    }

    /// <summary>One community in the registry.</summary>
    public sealed class RegistryEntry
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public IReadOnlyList<string> Mirrors { get; init; }
        public string Website { get; init; }
        public string Community { get; init; }
        public RegistryStatus Status { get; init; }

        /// <summary>SHA-256 (lowercase hex) of each file the entry pins, keyed by its path
        /// relative to a mirror's base URL.</summary>
        public IReadOnlyDictionary<string, string> Digests { get; init; }

        public ServerProfile ToProfile() => new ServerProfile(Id, Name, Mirrors, Website, Community);
    }

    public sealed class RegistryIndex
    {
        public int Schema { get; init; }

        /// <summary>Grows with every issued index; a launcher never accepts a lower one.</summary>
        public long Serial { get; init; }

        public DateTimeOffset IssuedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }

        /// <summary>The oldest launcher that may use this index.</summary>
        public Version MinLauncher { get; init; }

        public IReadOnlyList<RegistryEntry> Entries { get; init; }

        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

        public RegistryEntry Find(string id)
        {
            foreach (RegistryEntry entry in Entries)
                if (string.Equals(entry.Id, id, StringComparison.Ordinal))
                    return entry;
            return null;
        }
    }
}
