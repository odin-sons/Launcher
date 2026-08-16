using System;
using System.Diagnostics;

namespace Odinsons.ValheimLauncher.Cli
{
    /// <summary>
    /// Console implementation of <see cref="IUpdateUi"/>.
    /// FileDownloader calls it from multiple threads at once (parallel downloads), so all
    /// output is serialized through a lock, and redraws are rate-limited — otherwise the
    /// console becomes a bottleneck on every 8 KB chunk.
    /// </summary>
    internal sealed class ConsoleUpdateUi : IUpdateUi
    {
        private const int RedrawIntervalMs = 100;

        private readonly object _gate = new();
        private readonly bool _interactive;
        private readonly Stopwatch _sinceRedraw = Stopwatch.StartNew();

        private string _status = string.Empty;
        private string _percent = string.Empty;
        private string _bytes = string.Empty;
        private int _lastLineLength;
        private bool _lineOpen;

        public ConsoleUpdateUi(string clientFolder, bool interactive)
        {
            ClientFolder = clientFolder;
            _interactive = interactive;
        }

        public string ClientFolder { get; }

        /// <summary>Set in OnUpdateComplete; Program reads the exit code from here.</summary>
        public bool? CanStartGame { get; private set; }

        // Nothing to block or show/hide in the console.
        public void SetLoading(bool value) { }

        public void ShowProgress() { }

        public void HideProgress()
        {
            lock (_gate) CloseLine();
        }

        public void SetStatus(string text)
        {
            lock (_gate)
            {
                string incoming = text ?? string.Empty;
                if (incoming == _status) return;
                _status = incoming;
                Draw(force: true);
            }
        }

        public void SetTotalProgress(double percent, string percentText, string bytesText)
        {
            lock (_gate)
            {
                _percent = percentText ?? string.Empty;
                _bytes = bytesText ?? string.Empty;
                Draw(force: false);
            }
        }

        /// <summary>In the console, total progress is more informative than per-file — skip drawing this.</summary>
        public void SetFileProgress(double percent) { }

        public void ShowMessage(string message, string title, UpdateMessageKind kind)
        {
            lock (_gate)
            {
                CloseLine();
                ConsoleColor previous = Console.ForegroundColor;
                if (kind == UpdateMessageKind.Error) Console.ForegroundColor = ConsoleColor.Red;
                else if (kind == UpdateMessageKind.Warning) Console.ForegroundColor = ConsoleColor.Yellow;

                Console.Error.WriteLine($"[{title}] {message}");
                Console.ForegroundColor = previous;
            }
        }

        public void SetDegradationWarning(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            lock (_gate)
            {
                CloseLine();
                ConsoleColor previous = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"[WARN] {message}");
                Console.ForegroundColor = previous;
            }
        }

        /// <summary>launcher-cli never launches the game itself (there's no --start), so the
        /// injector plan is only logged here — useful for diagnostics.</summary>
        public void SetInjectorPlan(InjectorPlan plan)
        {
            if (plan is null) return;
            LauncherLog.Info($"injector mode: ready to launch '{plan.Executable}' directly from Steam");
        }

        public void OnUpdateComplete(bool startAfter, bool canStartGame)
        {
            lock (_gate)
            {
                CloseLine();
                CanStartGame = canStartGame;
                Console.WriteLine(canStartGame
                    ? Loc.T("cli.done.ready")
                    : Loc.T("cli.done.notReady"));
            }
        }

        private void Draw(bool force)
        {
            if (!force && _sinceRedraw.ElapsedMilliseconds < RedrawIntervalMs) return;
            _sinceRedraw.Restart();

            string line = _status;
            if (_percent.Length > 0) line += "  " + _percent;
            if (_bytes.Length > 0) line += "  " + _bytes;

            if (_interactive)
            {
                Console.Write("\r" + line.PadRight(_lastLineLength));
                _lastLineLength = line.Length;
                _lineOpen = true;
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        private void CloseLine()
        {
            if (!_interactive || !_lineOpen) return;
            Console.WriteLine();
            _lineOpen = false;
            _lastLineLength = 0;
        }
    }
}
