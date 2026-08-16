using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Odinsons.ValheimLauncher
{
    public partial class ServerSelectionWindow : Window
    {
        public string SelectedServer { get; private set; }

        public ServerSelectionWindow(Dictionary<string, string> serverDirectories)
        {
            InitializeComponent();

            Title = Loc.T("serverSelection.title");
            HeaderText.Text = Loc.T("serverSelection.selectAServer");
            ConfirmButton.Content = Loc.T("serverSelection.ok");

            // Populate the ComboBox with available servers
            foreach (var server in serverDirectories.Keys)
            {
                ServerComboBox.Items.Add(new ComboBoxItem { Content = server, Tag = server });
            }

            // Select the first item by default, if the list isn't empty
            if (ServerComboBox.Items.Count > 0)
            {
                ServerComboBox.SelectedIndex = 0;
                ConfirmButton.IsEnabled = true;
            }
        }

        private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedServer = selectedItem.Tag.ToString();
                ConfirmButton.IsEnabled = true;
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedServer = selectedItem.Tag.ToString();
                DialogResult = true; // Close the window with an "OK" result
                Close();
            }
        }
    }
}