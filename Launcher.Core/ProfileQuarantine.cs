using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Odinsons.ValheimLauncher
{
    public enum QuarantineOutcome
    {
        /// <summary>Nothing to do: no such folder, no mod loader, injector mode, or no record to undo.</summary>
        NotApplicable,
        Applied,
        AlreadyApplied,
        Restored,
        Failed
    }

    public readonly record struct QuarantineResult(QuarantineOutcome Outcome, string Reason = null);

    /// <summary>
    /// Disables the mod loader's entry point in a profile folder that holds a game copy by renaming it
    /// to <c>&lt;name&gt;.suspended</c>, and puts it back. The rename is recorded in
    /// <see cref="RecordFileName"/> first, so an interruption on either side of it is recoverable by
    /// running <see cref="Apply"/> or <see cref="Restore"/> again.
    /// </summary>
    public sealed class ProfileQuarantine
    {
        public const string RecordFileName = "quarantine.json";
        public const string Suffix = ".suspended";

        private const int RecordVersion = 1;
        private const string WindowsEntryPoint = "winhttp.dll";

        private static readonly string[] UnixEntryPoints = { "start_game_bepinex.sh", "run_bepinex.sh" };

        private static readonly object Sync = new();

        private readonly Action<string, string> _rename;

        public ProfileQuarantine(Action<string, string> rename = null)
        {
            _rename = rename ?? ((from, to) => File.Move(from, to));
        }

        /// <summary>The files that load the mods when the game is started by hand, on this OS.</summary>
        public static IReadOnlyList<string> EntryPoints =>
            RuntimePlatform.IsWindows ? new[] { WindowsEntryPoint } : UnixEntryPoints;

        private static IEnumerable<string> KnownEntryPoints => UnixEntryPoints.Append(WindowsEntryPoint);

        /// <summary>Whether the folder holds a game copy, as opposed to injector mode.</summary>
        public static bool HoldsGameCopy(string folder) =>
            InjectorLauncher.ResolveGameExecutable(folder) != null
            || UpdateSession.ArtifactNames.Any(name =>
                name.EndsWith(UpdateSession.StashSuffix, StringComparison.Ordinal) && File.Exists(Path.Combine(folder, name)));

        public QuarantineResult Apply(string profileFolder)
        {
            lock (Sync)
            {
                if (!Directory.Exists(profileFolder) || !HoldsGameCopy(profileFolder))
                    return new QuarantineResult(QuarantineOutcome.NotApplicable);

                List<string> active = EntryPoints.Where(name => File.Exists(Path.Combine(profileFolder, name))).ToList();
                List<string> renamed = EntryPoints
                    .Where(name => !File.Exists(Path.Combine(profileFolder, name))
                                   && File.Exists(Path.Combine(profileFolder, name + Suffix)))
                    .ToList();

                if (active.Count == 0 && renamed.Count == 0)
                    return new QuarantineResult(QuarantineOutcome.NotApplicable);

                try
                {
                    WriteRecord(profileFolder, active.Concat(renamed));
                    foreach (string name in active)
                    {
                        string path = Path.Combine(profileFolder, name);
                        string target = path + Suffix;
                        if (File.Exists(target)) File.Delete(target);
                        _rename(path, target);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new QuarantineResult(QuarantineOutcome.Failed, ex.Message);
                }

                return new QuarantineResult(active.Count > 0 ? QuarantineOutcome.Applied : QuarantineOutcome.AlreadyApplied);
            }
        }

        public QuarantineResult Restore(string profileFolder)
        {
            lock (Sync)
            {
                IReadOnlyList<string> recorded = ReadRecord(profileFolder);
                if (recorded == null) return new QuarantineResult(QuarantineOutcome.NotApplicable);

                bool restored = false;
                try
                {
                    foreach (string name in recorded)
                    {
                        string path = Path.Combine(profileFolder, name);
                        string suspended = path + Suffix;
                        if (File.Exists(path) || !File.Exists(suspended)) continue;

                        _rename(suspended, path);
                        restored = true;
                    }

                    File.Delete(Path.Combine(profileFolder, RecordFileName));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new QuarantineResult(QuarantineOutcome.Failed, ex.Message);
                }

                return new QuarantineResult(restored ? QuarantineOutcome.Restored : QuarantineOutcome.NotApplicable);
            }
        }

        private static void WriteRecord(string profileFolder, IEnumerable<string> names)
        {
            string path = Path.Combine(profileFolder, RecordFileName);
            string temp = path + ".tmp";

            using (var stream = File.Create(temp))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", RecordVersion);
                writer.WriteStartArray("entries");
                foreach (string name in names) writer.WriteStringValue(name);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(temp, path, true);
        }

        /// <summary>The known entry points the record names, or null without a usable record.</summary>
        private static IReadOnlyList<string> ReadRecord(string profileFolder)
        {
            string path = Path.Combine(profileFolder, RecordFileName);
            try
            {
                if (!File.Exists(path)) return null;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("entries", out JsonElement entries)
                    || entries.ValueKind != JsonValueKind.Array)
                    return null;

                var known = new HashSet<string>(KnownEntryPoints, StringComparer.Ordinal);
                return entries.EnumerateArray()
                    .Where(entry => entry.ValueKind == JsonValueKind.String)
                    .Select(entry => entry.GetString())
                    .Where(known.Contains)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
    }
}
