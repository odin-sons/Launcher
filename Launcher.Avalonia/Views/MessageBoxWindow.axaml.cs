using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Odinsons.ValheimLauncher;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    /// <summary>
    /// Avalonia has no built-in MessageBox — this is the minimal replacement used
    /// everywhere the WPF launcher called <c>MessageBox.Show</c>.
    /// </summary>
    public partial class MessageBoxWindow : Window
    {
        public MessageBoxWindow()
        {
            InitializeComponent();
        }

        public MessageBoxWindow(string message, string title = "") : this()
        {
            Title = title;
            MessageText.Text = message;
            OkButton.Content = Loc.T("gui.okButton");
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();

        /// <summary>Shows the message box modally and waits for it to close.</summary>
        public static Task ShowAsync(Window owner, string message, string title = "") =>
            new MessageBoxWindow(message, title).ShowDialog(owner);
    }
}
