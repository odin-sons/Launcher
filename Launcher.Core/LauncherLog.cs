using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Log verbosity level. A lower value means a more important message.</summary>
    public enum LogLevel
    {
        /// <summary>The work failed or the result is known to be wrong.</summary>
        Error = 0,

        /// <summary>Something didn't go as planned, but the work continues.</summary>
        Warn = 1,

        /// <summary>Big-picture progress: stages, outcomes, decisions made.</summary>
        Info = 2,

        /// <summary>Decision detail: why one branch or another was chosen.</summary>
        Debug = 3,

        /// <summary>Per-file minutiae. Enable only when digging into a specific complaint.</summary>
        Trace = 4
    }

    /// <summary>
    /// The launcher's log.
    ///
    /// Always written in English, with no localization: logs are read by support, and one
    /// language matters more here than player convenience. Player-facing messages go through
    /// IUpdateUi, not this file.
    ///
    /// No method throws: broken logging must not break the update. The worst that can
    /// happen is a line doesn't get written.
    /// </summary>
    public static class LauncherLog
    {
        private const long MaxBytes = 8L * 1024 * 1024;

        private static readonly object Gate = new();
        private static string _path;

        /// <summary>What to write. Defaults to Info: progress without per-file minutiae.</summary>
        public static LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>Where to write. While unset, the log silently goes nowhere.</summary>
        public static string FilePath
        {
            get { lock (Gate) return _path; }
            set { lock (Gate) _path = value; }
        }

        /// <summary>Whether to also mirror lines to stdout. Needed by the console updater.</summary>
        public static bool AlsoToConsole { get; set; }

        public static bool IsEnabled(LogLevel level) => level <= Level;

        /// <summary>Parses a level from settings. An unrecognized value keeps the current one.</summary>
        public static bool TryParseLevel(string text, out LogLevel level)
        {
            level = Level;
            if (string.IsNullOrWhiteSpace(text)) return false;

            return Enum.TryParse(text.Trim(), ignoreCase: true, out level)
                   && Enum.IsDefined(typeof(LogLevel), level);
        }

        public static void Error(string message, Exception ex = null) => Write(LogLevel.Error, message, ex);
        public static void Warn(string message, Exception ex = null) => Write(LogLevel.Warn, message, ex);

        /// <summary>
        /// A recurring problem of one kind. The first occurrence is logged in full,
        /// subsequent ones are only counted — when digging into a complaint, what matters is
        /// seeing the problem, not a thousand copies of it. <see cref="FlushRepeats"/> prints the total.
        /// </summary>
        /// <param name="kind">A stable key for the kind of problem, not the message text.</param>
        public static void ErrorOnce(string kind, string message, Exception ex = null) =>
            WriteOnce(LogLevel.Error, kind, message, ex);

        /// <inheritdoc cref="ErrorOnce"/>
        public static void WarnOnce(string kind, string message, Exception ex = null) =>
            WriteOnce(LogLevel.Warn, kind, message, ex);

        private static readonly Dictionary<string, int> Repeats = new(StringComparer.Ordinal);

        private static void WriteOnce(LogLevel level, string kind, string message, Exception ex)
        {
            bool first;
            lock (Gate)
            {
                first = !Repeats.ContainsKey(kind);
                Repeats[kind] = first ? 0 : Repeats[kind] + 1;
            }

            if (first)
            {
                Write(level, message, ex);
                return;
            }

            // At Trace, every occurrence is still shown: when Trace is turned on, someone
            // is already digging into a specific complaint and wants to see everything.
            Write(LogLevel.Trace, $"[repeat:{kind}] {message}", ex);
        }

        /// <summary>Summarizes suppressed repeats. Call at the end of an operation.</summary>
        public static void FlushRepeats()
        {
            KeyValuePair<string, int>[] snapshot;
            lock (Gate)
            {
                snapshot = Repeats.Where(p => p.Value > 0).ToArray();
                Repeats.Clear();
            }

            foreach ((string kind, int extra) in snapshot)
                Write(LogLevel.Warn, $"problem '{kind}' occurred {extra} more time(s), not logged individually", null);
        }
        public static void Info(string message) => Write(LogLevel.Info, message, null);
        public static void Debug(string message) => Write(LogLevel.Debug, message, null);
        public static void Trace(string message) => Write(LogLevel.Trace, message, null);

        /// <summary>Session header: without it, the log doesn't say which version did what.</summary>
        public static void SessionHeader(string application, string version)
        {
            Info(new string('=', 72));
            Info($"{application} {version} started");
            Info($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")}), " +
                 $"process {(Environment.Is64BitProcess ? "x64" : "x86")}");
            string culture = CultureInfo.CurrentUICulture.Name;
            Info($"Runtime: {Environment.Version}, culture {(culture.Length == 0 ? "(invariant)" : culture)}, " +
                 $"UI language {Loc.Language}");
            Info($"Working directory: {Environment.CurrentDirectory}");
            Info($"Log level: {Level}");
        }

        private static void Write(LogLevel level, string message, Exception ex)
        {
            if (!IsEnabled(level)) return;

            try
            {
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-5}] [T{2:00}] {3}",
                    DateTime.Now, level.ToString().ToUpperInvariant(),
                    Environment.CurrentManagedThreadId, message);

                if (ex is not null)
                {
                    var details = new StringBuilder();
                    details.Append(line);
                    details.Append(Environment.NewLine);
                    details.Append("    ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);

                    // The stack trace helps debugging but bloats the file — only at Debug and below.
                    if (IsEnabled(LogLevel.Debug) && ex.StackTrace is { Length: > 0 })
                        details.Append(Environment.NewLine).Append("    ").Append(ex.StackTrace);

                    for (Exception inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                        details.Append(Environment.NewLine).Append("    caused by ")
                               .Append(inner.GetType().FullName).Append(": ").Append(inner.Message);

                    line = details.ToString();
                }

                lock (Gate)
                {
                    if (AlsoToConsole) Console.WriteLine(line);

                    if (string.IsNullOrEmpty(_path)) return;

                    RollIfTooBig();
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging has no right to bring down the update.
            }
        }

        /// <summary>One previous file is kept: Trace writes a lot on a 2.7 GB build.</summary>
        private static void RollIfTooBig()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || info.Length < MaxBytes) return;

                string previous = _path + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(_path, previous);
            }
            catch
            {
                // Couldn't roll over — keep writing to the current file.
            }
        }
    }
}
