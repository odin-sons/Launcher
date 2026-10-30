using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    /// <summary>The About facts as label and value rows; the ones the launcher measured carry a diamond.</summary>
    internal sealed class FactsView : Grid
    {
        public FactsView() => ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*");

        public int FactCount => RowDefinitions.Count;

        public void Show(IReadOnlyList<FactLine> facts)
        {
            Children.Clear();
            RowDefinitions = new RowDefinitions();

            for (int row = 0; row < facts.Count; row++)
            {
                FactLine fact = facts[row];
                RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                var marker = new TextBlock { Text = fact.Measured ? "◆" : string.Empty, FontSize = 10, Width = 14, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Foreground = LauncherBrushes.Accent };
                if (fact.Measured)
                    ToolTip.SetTip(marker, Loc.T("about.measured"));
                var label = new TextBlock { Text = fact.Label, FontSize = 13, Foreground = LauncherBrushes.Muted, Margin = new Thickness(0, 2, 16, 2) };
                var value = new TextBlock { Text = fact.Value, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = LauncherBrushes.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

                SetRow(marker, row);
                SetRow(label, row);
                SetColumn(label, 1);
                SetRow(value, row);
                SetColumn(value, 2);
                Children.Add(marker);
                Children.Add(label);
                Children.Add(value);
            }
        }
    }
}
