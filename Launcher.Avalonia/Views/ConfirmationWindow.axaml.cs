using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    /// <summary>
    /// A two-button variant of MessageBoxWindow, for the few places a plain "OK" isn't enough
    /// (see the Defender-exclusion prompt) — kept separate rather than extending MessageBoxWindow,
    /// so every existing single-button call site stays untouched.
    /// </summary>
    public partial class ConfirmationWindow : Window
    {
        private bool _result;

        public ConfirmationWindow()
        {
            InitializeComponent();
        }

        public ConfirmationWindow(string title, string message, string yesText, string noText) : this()
        {
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            YesButton.Content = yesText;
            NoButton.Content = noText;
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }

        /// <summary>Shows the dialog modally and returns whether the player picked the "yes" button.</summary>
        public static async Task<bool> ShowAsync(Window owner, string title, string message, string yesText, string noText)
        {
            var window = new ConfirmationWindow(title, message, yesText, noText);
            await window.ShowDialog(owner);
            return window._result;
        }
    }
}
