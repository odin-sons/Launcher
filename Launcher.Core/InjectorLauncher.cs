using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
            if (RuntimePlatform.IsWindows) return new[] { "valheim.exe" };
            if (RuntimePlatform.IsMacOS) return new[] { "valheim.app", "valheim" };
            return new[] { "valheim.x86_64", "valheim.x86" };
        }

        /// <summary>
        /// The primary game-executable name this OS launches (valheim.exe / valheim.app /
        /// valheim.x86_64). Exposed so tests and diagnostics don't hard-code the Windows name.
        /// </summary>
        public static string PrimaryExecutableName => ExecutableCandidates().First();

        private static string DoorstopLibraryName() =>
            RuntimePlatform.IsMacOS ? "libdoorstop_x64.dylib" : "libdoorstop_x64.so";

        /// <summary>
        /// Client-folder-relative path to the native Doorstop library for this OS
        /// (empty on Windows, which injects through a winhttp.dll proxy instead).
        /// </summary>
        public static string DoorstopLibraryRelativePath =>
            RuntimePlatform.IsWindows ? "" : $"{DoorstopLibsFolder}/{DoorstopLibraryName()}";

        /// <summary>
        /// Resolves what <see cref="Process.Start"/> can actually run. On macOS the Steam
        /// executable is a <c>.app</c> bundle directory — the real binary lives at
        /// <c>Contents/MacOS/&lt;CFBundleExecutable&gt;</c>. Elsewhere the candidate is already
        /// a plain file.
        /// </summary>
        public static string ResolveGameExecutable(string gameFolder)
        {
            foreach (string name in ExecutableCandidates())
            {
                string candidate = Path.Combine(gameFolder, name);

                if (RuntimePlatform.IsMacOS && Directory.Exists(candidate) &&
                    candidate.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    string inner = MacBundleExecutable(candidate);
                    if (inner is not null) return inner;
                    continue;
                }

                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string MacBundleExecutable(string appPath)
        {
            string macOsDir = Path.Combine(appPath, "Contents", "MacOS");
            if (!Directory.Exists(macOsDir)) return null;

            string plist = Path.Combine(appPath, "Contents", "Info.plist");
            if (File.Exists(plist))
            {
                try
                {
                    Match m = Regex.Match(File.ReadAllText(plist),
                        @"<key>\s*CFBundleExecutable\s*</key>\s*<string>\s*(.*?)\s*</string>",
                        RegexOptions.Singleline);
                    if (m.Success)
                    {
                        string p = Path.Combine(macOsDir, m.Groups[1].Value.Trim());
                        if (File.Exists(p)) return p;
                    }
                }
                catch { /* fall through to the directory scan */ }
            }

            string[] files = Directory.GetFiles(macOsDir);
            return files.Length == 1 ? files[0] : null;
        }

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

            string executable = ResolveGameExecutable(gameFolder);

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

            if (!RuntimePlatform.IsWindows)
            {
                string libs = Path.GetFullPath(Path.Combine(profileFolder, DoorstopLibsFolder));
                string libraryPath = Path.Combine(libs, DoorstopLibraryName());

                if (!File.Exists(libraryPath))
                {
                    reason = Loc.T("injector.noDoorstopLibrary", libraryPath);
                    return null;
                }

                if (RuntimePlatform.IsMacOS)
                {
                    // Absolute path in DYLD_INSERT_LIBRARIES — a bare name only resolves if the
                    // lib also happens to sit on the default search path. Matches the launch
                    // scripts that ship with the BepInEx pack (start_game_bepinex.sh / Vortex).
                    environment["DYLD_LIBRARY_PATH"] = libs;
                    environment["DYLD_INSERT_LIBRARIES"] = libraryPath;
                }
                else
                {
                    environment["LD_LIBRARY_PATH"] = libs;
                    environment["LD_PRELOAD"] = libraryPath;
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
        // What target_assembly-disabled doorstop_config.ini actually contains — a named
        // constant so PrepareGameFolder's write and NeedsPreparation's read-only comparison
        // can't drift apart.
        private const string DisabledDoorstopConfig = "[General]\r\nenabled = false\r\ntarget_assembly = \r\n";

        public static bool PrepareGameFolder(string gameFolder, string profileFolder, out string reason)
        {
            reason = null;
            if (!RuntimePlatform.IsWindows) return true;

            string ourProxy = Path.Combine(profileFolder, WindowsProxyName);
            if (!File.Exists(ourProxy))
            {
                reason = Loc.T("injector.noProxy", ourProxy);
                return false;
            }

            string targetProxy = Path.Combine(gameFolder, WindowsProxyName);
            string targetConfig = Path.Combine(gameFolder, DoorstopConfigName);

            // First, back up everything we're about to overwrite — skip anything that's already
            // exactly what we'd write anyway, so a run that already has everything set up from a
            // past pass doesn't churn a fresh (ever-accumulating, see GameFolderInspector.
            // UniqueTarget) backup entry on every single launch for no reason.
            var toBackup = new List<string>();
            if (File.Exists(targetProxy) && !SameContent(targetProxy, ourProxy)) toBackup.Add(targetProxy);
            if (File.Exists(targetConfig) && !SameConfigContent(targetConfig)) toBackup.Add(targetConfig);

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
                if (!File.Exists(targetConfig)) File.WriteAllText(targetConfig, DisabledDoorstopConfig);

                return true;
            }
            catch (Exception ex)
            {
                reason = Loc.T("injector.prepareFailed", ex.Message);
                return false;
            }
        }

        private static bool SameConfigContent(string targetConfig)
        {
            try { return File.ReadAllText(targetConfig) == DisabledDoorstopConfig; }
            catch { return false; }
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

            if (!EnsureGameFolderPrepared(gameFolder, profileFolder, out reason))
            {
                plan = null;
                return false;
            }

            return true;
        }

        /// <summary>Backs up anything blocking and drops the substitute winhttp.dll/disabled
        /// doorstop_config.ini into the game folder — the combined "prepare" step, run either
        /// in this process directly (the common case: the Steam library is writable by the
        /// current user) or, on Windows, in a one-shot elevated re-launch of this same exe when
        /// it isn't (see EnsureGameFolderPrepared).</summary>
        private static bool PrepareGameFolderCore(string gameFolder, string profileFolder, out string reason)
        {
            GameFolderInspection inspection = GameFolderInspector.Inspect(
                gameFolder, Path.Combine(profileFolder, WindowsProxyName));

            if (inspection.Blocking.Count > 0 &&
                !GameFolderInspector.MoveToBackup(gameFolder, inspection.Blocking, out _, out reason))
                return false;

            return PrepareGameFolder(gameFolder, profileFolder, out reason);
        }

        /// <summary>Command-line argument Program.cs checks for before starting the normal GUI —
        /// present means "run RunElevatedHelperMain and exit", nothing else.</summary>
        public const string ElevatedHelperArg = "--prepare-injector-folder";

        /// <summary>
        /// Least privilege, in two layers. First: is there even anything to write? A repeat
        /// launch with everything already set up exactly as a past pass left it (the common
        /// case after the very first run) needs no write at all — NeedsPreparation checks that
        /// with plain reads, which essentially never need elevation even inside Program Files.
        /// Only if something genuinely needs writing does it even ask whether the folder is
        /// writable by the current user, and only then — if it isn't — re-launch this same exe
        /// elevated (ElevatedHelperArg), the one-shot-elevation pattern
        /// DefenderExclusion.TryAddExclusion already uses, instead of the whole app requesting
        /// admin via its manifest for something only this one operation, on some players'
        /// machines, on a fraction of their launches, ever needs.
        /// </summary>
        private static bool EnsureGameFolderPrepared(string gameFolder, string profileFolder, out string reason)
        {
            reason = null;
            if (RuntimePlatform.IsWindows && !NeedsPreparation(gameFolder, profileFolder))
                return true;

            if (!RuntimePlatform.IsWindows || ClientFolderGuard.IsWritable(gameFolder, out _, out _))
                return PrepareGameFolderCore(gameFolder, profileFolder, out reason);

            return RunElevatedHelper(gameFolder, profileFolder, out reason);
        }

        /// <summary>Whether PrepareGameFolderCore would actually change anything — read-only
        /// (file existence + content comparison), so it essentially never needs elevation even
        /// when the folder itself is otherwise write-protected. Mirrors PrepareGameFolder's own
        /// "already matches, don't touch it" checks for winhttp.dll/doorstop_config.ini, plus
        /// GameFolderInspector's blocking-folder check.</summary>
        private static bool NeedsPreparation(string gameFolder, string profileFolder)
        {
            try
            {
                GameFolderInspection inspection = GameFolderInspector.Inspect(
                    gameFolder, Path.Combine(profileFolder, WindowsProxyName));
                if (inspection.Blocking.Count > 0) return true;

                string ourProxy = Path.Combine(profileFolder, WindowsProxyName);
                string targetProxy = Path.Combine(gameFolder, WindowsProxyName);
                if (!File.Exists(targetProxy) || !SameContent(targetProxy, ourProxy)) return true;

                string targetConfig = Path.Combine(gameFolder, DoorstopConfigName);
                return !File.Exists(targetConfig) || !SameConfigContent(targetConfig);
            }
            catch
            {
                // Couldn't tell from a read alone — fall through to the normal
                // writability-check/elevate path rather than silently assuming "nothing to do".
                return true;
            }
        }

        private static bool RunElevatedHelper(string gameFolder, string profileFolder, out string reason)
        {
            reason = null;
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                reason = Loc.T("injector.elevationFailed", "no process path");
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                psi.ArgumentList.Add(ElevatedHelperArg);
                psi.ArgumentList.Add(gameFolder);
                psi.ArgumentList.Add(profileFolder);

                using Process process = Process.Start(psi);
                process.WaitForExit(30000);

                if (process.ExitCode == 0) return true;

                reason = Loc.T("injector.elevationFailed", $"exit code {process.ExitCode}");
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The UAC prompt was declined — not an error, just "no".
                reason = Loc.T("injector.elevationDeclined");
                return false;
            }
            catch (Exception ex)
            {
                reason = Loc.T("injector.elevationFailed", ex.Message);
                return false;
            }
        }

        /// <summary>Entry point for the elevated re-launch (see ElevatedHelperArg) — Program.cs
        /// calls this instead of starting the GUI when the process was started with that flag,
        /// then exits with the returned code. No UI: this process only exists to do the one
        /// privileged file operation and hand the result back to the un-elevated parent.</summary>
        public static int RunElevatedHelperMain(string gameFolder, string profileFolder) =>
            PrepareGameFolderCore(gameFolder, profileFolder, out _) ? 0 : 1;

        /// <summary>Actually launches the game according to the built plan.</summary>
        public static Process Launch(InjectorPlan plan)
        {
            if (RuntimePlatform.IsMacOS) EnsureSteamRunning();
            return Process.Start(BuildStartInfo(plan));
        }

        /// <summary>
        /// Turns a plan into the exact process invocation, without starting it — the seam the
        /// tests use to check the macOS launch shape.
        /// </summary>
        public static ProcessStartInfo BuildStartInfo(InjectorPlan plan)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false
            };

            if (RuntimePlatform.IsMacOS)
            {
                // Two macOS-specific hoops, both taken straight from the launch scripts that ship
                // with the BepInEx pack (start_game_bepinex.sh / Vortex's .vmm_launch.sh):
                //
                //  * `arch -x86_64` — Valheim's macOS build is a universal binary, but the shipped
                //    doorstop library is x86_64 only, so the game must run as x86_64 (Rosetta).
                //
                //  * the DOORSTOP_*/DYLD_* variables are passed as arguments to `env`, NOT through
                //    ProcessStartInfo.Environment. The launcher host (dotnet) runs without the
                //    `allow-dyld-environment-variables` entitlement, so the kernel strips inherited
                //    DYLD_* on the way down; `env` sets them fresh right before exec'ing the game,
                //    which does carry that entitlement.
                startInfo.FileName = "/usr/bin/arch";
                startInfo.ArgumentList.Add("-x86_64");
                startInfo.ArgumentList.Add("/usr/bin/env");
                foreach ((string key, string value) in plan.Environment)
                    startInfo.ArgumentList.Add($"{key}={value}");
                startInfo.ArgumentList.Add(plan.Executable);
                foreach (string argument in plan.Arguments) startInfo.ArgumentList.Add(argument);
            }
            else
            {
                startInfo.FileName = plan.Executable;
                foreach (string argument in plan.Arguments) startInfo.ArgumentList.Add(argument);
                foreach ((string key, string value) in plan.Environment)
                    startInfo.EnvironmentVariables[key] = value;
            }

            return startInfo;
        }

        /// <summary>
        /// Valheim needs the Steam client running for the Steamworks API. Best-effort, in the
        /// background — if Steam is already up this is a no-op, and a failure here isn't fatal.
        /// </summary>
        private static void EnsureSteamRunning()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    ArgumentList = { "-g", "-b", "com.valvesoftware.steam" },
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                LauncherLog.Debug($"could not nudge Steam to start: {ex.Message}");
            }
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
