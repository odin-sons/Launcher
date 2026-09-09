using System.Collections.Generic;

namespace Odinsons.ValheimLauncher
{
    /// <summary>Message type — maps to a dialog icon in the concrete implementation.</summary>
    public enum UpdateMessageKind
    {
        None,
        Warning,
        Error
    }

    /// <summary>
    /// Everything <see cref="FileDownloader"/> requires from a user interface.
    /// The implementation is responsible for marshaling to the UI thread itself —
    /// the downloader knows nothing about that.
    /// </summary>
    public interface IUpdateUi
    {
        /// <summary>The client folder the update is being run against.</summary>
        string ClientFolder { get; }

        void SetLoading(bool value);

        void ShowProgress();

        void HideProgress();

        /// <summary>Text of the current task (status line).</summary>
        void SetStatus(string text);

        /// <param name="percent">0..100, for the total-progress bar.</param>
        /// <param name="percentText">Ready-made percent label.</param>
        /// <param name="bytesText">Ready-made "downloaded/total" label.</param>
        void SetTotalProgress(double percent, string percentText, string bytesText);

        /// <param name="percent">0..100, for the current-file bar.</param>
        void SetFileProgress(double percent);

        void ShowMessage(string message, string title, UpdateMessageKind kind);

        /// <summary>
        /// A non-blocking degradation warning that doesn't require the player to respond —
        /// unlike <see cref="ShowMessage"/>, which pops up as a modal window in the GUI.
        /// An empty string or null clears the warning if one was showing.
        /// Called rarely: only when something genuinely looks like a problem with the
        /// player's environment (antivirus, permissions), not an ordinary update run.
        /// </summary>
        void SetDegradationWarning(string message);

        /// <summary>
        /// The ready injector-mode launch plan (see <see cref="InjectorLauncher"/>),
        /// if one was built this run; otherwise null — in which case the game launches
        /// as before, from files copied into the client folder.
        /// Called every run, even with null, so a plan from a previous run isn't left
        /// stale if the conditions no longer apply this time.
        /// </summary>
        void SetInjectorPlan(InjectorPlan plan);

        void OnUpdateComplete(bool startAfter, bool canStartGame);

        /// <summary>
        /// Declares every step this run expects to go through, in order — replaces the old
        /// two-bar model (<see cref="SetTotalProgress"/>/<see cref="SetFileProgress"/>), which
        /// visibly reset to 0% every time FileDownloader moved to an unrelated phase (Steam
        /// verification, then the client-file check, then the actual download). Called once,
        /// before the first <see cref="StartStep"/>. Default no-op: an implementation that
        /// doesn't care about step-by-step detail (the CLI, tests, WPF for now) simply ignores
        /// it — FileDownloader still calls the old progress members exactly as before too.
        /// Each step carries the collapsible group it belongs to (see <see cref="InstallStep"/>).
        /// </summary>
        void SetSteps(IReadOnlyList<InstallStep> steps) { }

        /// <summary>Marks the step at this index as active (spinner, in a real UI).</summary>
        void StartStep(int index) { }

        /// <summary>Fine-grained progress (0..100) within the currently active step — purely
        /// cosmetic, lets an overall bar move smoothly instead of jumping step to step.</summary>
        void SetStepProgress(double percent) { }

        /// <summary>Marks the step done — including a step that turned out unnecessary this
        /// run (e.g. nothing to download), which should still complete instantly rather than
        /// being skipped, so the step list and "X of Y" count stay stable across runs.</summary>
        void FinishStep(int index) { }

        /// <summary>
        /// Live detail for the download step: how many mod/data groups are done, how many MB,
        /// transfer rate, and the handful transferring right now (see <see cref="DownloadDetail"/>).
        /// Pushed on a throttle while the download step is active. Default no-op — the CLI,
        /// tests and WPF ignore it and keep using the plain progress members.
        /// </summary>
        void SetDownloadDetail(DownloadDetail detail) { }
    }
}
