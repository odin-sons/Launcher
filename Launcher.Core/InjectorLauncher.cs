using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>How to run the game from its Steam install with our mod set.</summary>
    public sealed class InjectorPlan
    {
        public string Executable { get; init; }
        public string WorkingDirectory { get; init; }
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> Environment { get; init; } =
            new Dictionary<string, string>();
    }

    /// <summary>
    /// Injector mode: the game stays in its Steam folder, mods come from our profile.
    /// No copy of the game is made — saves about 1.5 GB of disk space.
    ///
    /// The Doorstop contract wasn't taken from memory but from start_game_bepinex.sh, which
    /// ships with the build: the DOORSTOP_* variables, the doorstop_libs folder, the library name
    /// libdoorstop_{arch}.{so|dylib}. Windows has a different entry point — a substitute winhttp.dll
    /// next to the executable, otherwise there's nothing to inject into.
    ///
    /// Important to remember: this mode does NOT protect against Valheim auto-updating in Steam.
    /// The build-match check is the caller's job.
    /// </summary>
    public static class InjectorLauncher
    {
        private const string PreloaderRelativePath = "BepInEx/core/BepInEx.Preloader.dll";
        private const string DoorstopLibsFolder = "doorstop_libs";
        private const string WindowsProxyName = "winhttp.dll";
        private const string DoorstopConfigName = "doorstop_config.ini";

        /// <summary>Game executables in order of preference for the current OS.</summary>
        private static IEnumerable<string> ExecutableCandidates()
        {
            if (OperatingSystem.IsWindows()) return new[] { "valheim.exe" };
            if (OperatingSystem.IsMacOS()) return new[] { "valheim.app", "valheim" };
            return new[] { "valheim.x86_64", "valheim.x86" };
        }

        private static string DoorstopLibraryName() =>
            OperatingSystem.IsMacOS() ? "libdoorstop_x64.dylib" : "libdoorstop_x64.so";

        /// <summary>
        /// Builds the launch command. Changes nothing on disk — a pure function of paths,
        /// so it's tested against fixtures without an installed game.
        /// </summary>
        /// <param name="gameFolder">The game's folder from a Steam install.</param>
        /// <param name="profileFolder">Our client folder: BepInEx, doorstop_libs.</param>
        public static InjectorPlan BuildPlan(string gameFolder, string profileFolder, out string reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            {
                reason = Loc.T("injector.noGameFolder");
                return null;
            }

            string executable = ExecutableCandidates()
                .Select(name => Path.Combine(gameFolder, name))
                .FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));

            if (executable is null)
            {
                reason = Loc.T("injector.noExecutable", string.Join(" / ", ExecutableCandidates()));
                return null;
            }

            string preloader = Path.GetFullPath(Path.Combine(profileFolder, PreloaderRelativePath));
            if (!File.Exists(preloader))
            {
                reason = Loc.T("injector.noPreloader", preloader);
                return null;
            }

            var environment = new Dictionary<string, string>
            {
                ["DOORSTOP_ENABLED"] = "1",
                ["DOORSTOP_TARGET_ASSEMBLY"] = preloader,
                // The game shouldn't pick up someone else's boot.config or debug settings.
                ["DOORSTOP_MONO_DEBUG_ENABLED"] = "0",
                ["DOORSTOP_IGNORE_DISABLED_ENV"] = "0"
            };

            if (!OperatingSystem.IsWindows())
            {
                string libs = Path.GetFullPath(Path.Combine(profileFolder, DoorstopLibsFolder));
                string library = DoorstopLibraryName();

                if (OperatingSystem.IsMacOS())
                {
                    environment["DYLD_LIBRARY_PATH"] = libs;
                    environment["DYLD_INSERT_LIBRARIES"] = library;
                }
                else
                {
                    environment["LD_LIBRARY_PATH"] = libs;
                    environment["LD_PRELOAD"] = library;
                }
            }

            // The arguments deliberately duplicate the environment variables: Doorstop 4 accepts
            // both, and which channel actually works on a given build can only be verified by
            // launching. The duplication is free and eliminates a whole class of failures.
            var arguments = new List<string>
            {
                "--doorstop-enabled", "true",
                "--doorstop-target-assembly", preloader
            };

            return new InjectorPlan
            {
                Executable = executable,
                WorkingDirectory = gameFolder,
                Arguments = arguments,
                Environment = environment
            };
        }

        /// <summary>
        /// Prepares the game folder: on Windows, drops the substitute winhttp.dll and disables
        /// doorstop_config.ini, so launching directly from Steam stays vanilla.
        /// Anything about to be overwritten is moved to a backup first — nothing is lost.
        /// Nothing needs doing on Unix, injection happens through environment variables.
        /// </summary>
        public static bool PrepareGameFolder(string gameFolder, string profileFolder, out string reason)
        {
            reason = null;
            if (!OperatingSystem.IsWindows()) return true;

            string ourProxy = Path.Combine(profileFolder, WindowsProxyName);
            if (!File.Exists(ourProxy))
            {
                reason = Loc.T("injector.noProxy", ourProxy);
                return false;
            }

            string targetProxy = Path.Combine(gameFolder, WindowsProxyName);
            string targetConfig = Path.Combine(gameFolder, DoorstopConfigName);

            // First, back up everything we're about to overwrite.
            var toBackup = new List<string>();
            if (File.Exists(targetProxy) && !SameContent(targetProxy, ourProxy)) toBackup.Add(targetProxy);
            if (File.Exists(targetConfig)) toBackup.Add(targetConfig);

            if (toBackup.Count > 0 &&
                !GameFolderInspector.MoveToBackup(gameFolder, toBackup, out _, out string backupReason))
            {
                reason = backupReason;
                return false;
            }

            try
            {
                if (!File.Exists(targetProxy)) File.Copy(ourProxy, targetProxy);

                // enabled=false: double-clicking the game in Steam launches vanilla,
                // while our launcher turns Doorstop on via environment variables and arguments.
                File.WriteAllText(targetConfig,
                    "[General]\r\nenabled = false\r\ntarget_assembly = \r\n");

                return true;
            }
            catch (Exception ex)
            {
                reason = Loc.T("injector.prepareFailed", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The single entry point: ties together inspecting the game folder, backing up
        /// anything blocking, preparing the proxy/config, and building the launch plan
        /// into one step.
        ///
        /// The plan is built first (<see cref="BuildPlan"/> writes nothing to disk), and only
        /// then is the game folder prepared — if the preloader or the game itself is missing,
        /// injector mode isn't available, and there's no reason to touch the game folder at all.
        /// </summary>
        public static bool TryPrepareLaunch(string gameFolder, string profileFolder,
                                            out InjectorPlan plan, out string reason)
        {
            plan = BuildPlan(gameFolder, profileFolder, out reason);
            if (plan is null) return false;

            GameFolderInspection inspection = GameFolderInspector.Inspect(
                gameFolder, Path.Combine(profileFolder, WindowsProxyName));

            if (inspection.Blocking.Count > 0 &&
                !GameFolderInspector.MoveToBackup(gameFolder, inspection.Blocking, out _, out reason))
            {
                plan = null;
                return false;
            }

            if (!PrepareGameFolder(gameFolder, profileFolder, out reason))
            {
                plan = null;
                return false;
            }

            return true;
        }

        /// <summary>Actually launches the game according to the built plan.</summary>
        public static System.Diagnostics.Process Launch(InjectorPlan plan)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = plan.Executable,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false
            };

            foreach (string argument in plan.Arguments) startInfo.ArgumentList.Add(argument);
            foreach ((string key, string value) in plan.Environment) startInfo.EnvironmentVariables[key] = value;

            return System.Diagnostics.Process.Start(startInfo);
        }

        private static bool SameContent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
            }
            catch
            {
                return false;
            }
        }
    }
}
