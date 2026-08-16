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
    }
}
