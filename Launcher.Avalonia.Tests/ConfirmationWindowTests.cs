using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Odinsons.ValheimLauncher.Avalonia.Views;
using Xunit;

namespace Launcher.Avalonia.Tests
{
    /// <summary>
    /// The buttons of the confirmation dialog (launcher update, Defender exclusion) must be as wide
    /// as their labels: a fixed width cut a longer translation off, "Отложить и выйти" among them.
    /// </summary>
    public class ConfirmationWindowTests
    {
        private static double NaturalLabelWidth(Button button)
        {
            var label = Assert.IsAssignableFrom<TextBlock>(button.Presenter!.Child);
            label.Measure(Size.Infinity);
            return label.DesiredSize.Width;
        }

        private static ConfirmationWindow Shown(string yes, string no)
        {
            var window = new ConfirmationWindow("Title", "Message", yes, no);
            window.Show();
            window.UpdateLayout();
            return window;
        }

        private static (Button Yes, Button No) Buttons(ConfirmationWindow window) =>
            (window.FindControl<Button>("YesButton")!, window.FindControl<Button>("NoButton")!);

        [AvaloniaTheory]
        [InlineData("Обновить сейчас", "Отложить и выйти")]
        [InlineData("Jetzt aktualisieren", "Verschieben und Launcher beenden")]
        [InlineData("Update now", "Remind me later, when the launcher is restarted")]
        public void EachButton_IsAsWideAsItsLabel(string yes, string no)
        {
            ConfirmationWindow window = Shown(yes, no);
            (Button yesButton, Button noButton) = Buttons(window);

            foreach (Button button in new[] { yesButton, noButton })
            {
                double needed = NaturalLabelWidth(button) + button.Padding.Left + button.Padding.Right;

                Assert.True(button.Bounds.Width >= needed - 0.5,
                    $"'{button.Content}' needs {needed:0.#}px but the button is {button.Bounds.Width:0.#}px wide");
            }
        }

        [AvaloniaTheory]
        [InlineData("Yes", "No")]
        [InlineData("Update now", "Remind me later, when the launcher is restarted")]
        public void Buttons_StayInsideTheWindow(string yes, string no)
        {
            ConfirmationWindow window = Shown(yes, no);
            (Button yesButton, Button noButton) = Buttons(window);

            foreach (Button button in new[] { yesButton, noButton })
            {
                Point topLeft = button.TranslatePoint(new Point(0, 0), window)!.Value;

                Assert.True(topLeft.X >= 0, $"'{button.Content}' starts left of the window");
                Assert.True(topLeft.X + button.Bounds.Width <= window.Bounds.Width + 0.5,
                    $"'{button.Content}' runs past the right edge of the window");
            }
        }

        [AvaloniaFact]
        public void ShortLabels_KeepTheUsualButtonSize()
        {
            ConfirmationWindow window = Shown("Yes", "No");
            (Button yesButton, Button noButton) = Buttons(window);

            Assert.True(noButton.Bounds.Width >= 110);
            Assert.True(yesButton.Bounds.Width >= 150);
        }
    }
}
