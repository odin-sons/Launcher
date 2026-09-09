using System;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Operating system the launcher is targeting.</summary>
    public enum TargetOs
    {
        Windows,
        MacOS,
        Linux
    }

    /// <summary>
    /// The OS whose conventions the injector / downloader follow (executable names, native
    /// library names, per-OS manifest names, launch shape). Normally the real host OS.
    ///
    /// <see cref="Pretend"/> lets the tests run the Windows, macOS and Linux code paths on a
    /// single host — those paths only differ in strings and file layout, never in anything
    /// that actually needs the other OS's kernel, so faking the answer is enough and keeps
    /// the suite genuinely cross-platform instead of silently skipping half of it off-host.
    /// </summary>
    public static class RuntimePlatform
    {
        private static TargetOs? _forced;

        public static TargetOs Real =>
            OperatingSystem.IsWindows() ? TargetOs.Windows :
            OperatingSystem.IsMacOS() ? TargetOs.MacOS :
            TargetOs.Linux;

        public static TargetOs Current => _forced ?? Real;

        public static bool IsWindows => Current == TargetOs.Windows;
        public static bool IsMacOS => Current == TargetOs.MacOS;
        public static bool IsLinux => Current == TargetOs.Linux;

        /// <summary>
        /// Test seam: makes <see cref="Current"/> report <paramref name="os"/> until the
        /// returned scope is disposed. <c>Launcher.Tests</c> runs serially
        /// (<c>CollectionBehavior(DisableTestParallelization = true)</c>), so a plain static
        /// is safe here; the <c>using</c> restores the previous value even on assertion failure.
        /// </summary>
        public static IDisposable Pretend(TargetOs os)
        {
            TargetOs? previous = _forced;
            _forced = os;
            return new Scope(() => _forced = previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            public Scope(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }
    }
}
