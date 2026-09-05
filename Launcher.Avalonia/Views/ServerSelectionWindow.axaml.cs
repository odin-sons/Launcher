using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Odinsons.ValheimLauncher;

namespace Odinsons.ValheimLauncher.Avalonia.Views
{
    public partial class ServerSelectionWindow : Window
    {
        public string? SelectedServer { get; private set; }

        public ServerSelectionWindow(Dictionary<string, string> serverDirectories)
        {
            InitializeComponent();

            Title = Loc.T("serverSelection.title");
            HeaderText.Text = Loc.T("serverSelection.selectAServer");
            ConfirmButton.Content = Loc.T("serverSelection.ok");

            foreach (var server in serverDirectories.Keys)
            {
                ServerComboBox.Items.Add(new ComboBoxItem { Content = server, Tag = server });
            }

            if (ServerComboBox.ItemCount > 0)
            {
                ServerComboBox.SelectedIndex = 0;
                ConfirmButton.IsEnabled = true;
            }
        }

        private void ServerComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedServer = selectedItem.Tag?.ToString();
                ConfirmButton.IsEnabled = true;
            }
        }

        private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedServer = selectedItem.Tag?.ToString();
                Close(true);
            }
        }
    }
}
