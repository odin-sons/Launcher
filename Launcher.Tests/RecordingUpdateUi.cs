using System.ComponentModel;
using Odinsons.ValheimLauncher;

namespace Launcher.Tests
{
    /// <summary>
    /// A minimal <see cref="IUpdateUi"/> implementation for tests: draws nothing,
    /// just remembers what it was given — enough to verify
    /// <see cref="FileDownloader"/>'s behavior without a GUI or a console.
    /// </summary>
    public sealed class RecordingUpdateUi : IUpdateUi
    {
        public string ClientFolder { get; }

        public List<(string Message, string Title, UpdateMessageKind Kind)> Messages { get; } = new();

        public bool CompleteCalled { get; private set; }
        public bool StartAfterAtComplete { get; private set; }
        public bool CanStartGameAtComplete { get; private set; }

        /// <summary>
        /// A snapshot of "the file exists under its own name" taken exactly at the moment
        /// of the ready report — a regression test for a specific bug: valheim.exe used to
        /// be restored from .updating AFTER the update reported "ready", and the launcher
        /// would try to start the game at a path that didn't exist yet.
        /// </summary>
        public bool WatchedFileExistedAtComplete { get; private set; }

        private readonly string _watchedFullPath;

        public RecordingUpdateUi(string clientFolder, string watchedRelativePath = "valheim.exe")
        {
            ClientFolder = clientFolder;
            _watchedFullPath = Path.Combine(clientFolder, watchedRelativePath);
        }

        public void SetLoading(bool value) { }
        public void ShowProgress() { }
        public void HideProgress() { }
        public void SetStatus(string text) { }
        public void SetTotalProgress(double percent, string percentText, string bytesText) { }
        public void SetFileProgress(double percent) { }

        public void ShowMessage(string message, string title, UpdateMessageKind kind) =>
            Messages.Add((message, title, kind));

        public string LastDegradationWarning { get; private set; }

        public void SetDegradationWarning(string message) => LastDegradationWarning = message;

        public InjectorPlan LastInjectorPlan { get; private set; }

        public void SetInjectorPlan(InjectorPlan plan) => LastInjectorPlan = plan;

        public void OnUpdateComplete(bool startAfter, bool canStartGame)
        {
            CompleteCalled = true;
            StartAfterAtComplete = startAfter;
            CanStartGameAtComplete = canStartGame;
            WatchedFileExistedAtComplete = File.Exists(_watchedFullPath);
        }

        /// <summary>Set by a test that wants to trigger a deterministic mid-run cancellation.</summary>
        public BackgroundWorker Worker { get; set; }

        /// <summary>When <see cref="StartStep"/> reaches the step with this label, cancels <see cref="Worker"/>.</summary>
        public string CancelOnStepLabel { get; set; }

        private IReadOnlyList<string> _stepLabels = Array.Empty<string>();

        public void SetSteps(IReadOnlyList<string> stepLabels) => _stepLabels = stepLabels;

        public void StartStep(int index)
        {
            if (CancelOnStepLabel is not null && index >= 0 && index < _stepLabels.Count &&
                _stepLabels[index] == CancelOnStepLabel)
                Worker?.CancelAsync();
        }
    }
}
