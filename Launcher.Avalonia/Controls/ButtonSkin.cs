using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    /// <summary>The look of the wizard button (the theme's fill, border, radius and label) and of the Stop button (fixed).</summary>
    internal static class ButtonSkin
    {
        public const double FontSize = 22;
        private const double TextPadding = 28;

        private static readonly Typeface LabelTypeface =
            new(new FontFamily("avares://OdinsonsLauncher/Resources/Fonts#Montserrat"), FontStyle.Normal, FontWeight.Bold);

        private static readonly IBrush StopFill = new SolidColorBrush(Color.Parse("#9B2C36"));
        private static readonly IBrush StopHover = new SolidColorBrush(Colors.White, 0.16);

        public static double MeasureText(string text) =>
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, FontSize, Brushes.White).Width;

        /// <summary>A button face wide enough for the longest of <paramref name="possibleTexts"/>.</summary>
        public static Panel Build(double height, IReadOnlyList<string> possibleTexts, bool themed, out TextBlock label)
        {
            double width = System.Math.Round(possibleTexts.Count == 0 ? 0 : possibleTexts.Max(MeasureText)) + TextPadding * 2;

            var skin = new Border { Width = width, Height = height, IsHitTestVisible = false };
            var hover = new Border { IsHitTestVisible = false };
            hover.Classes.Add("hoverImg");
            label = new TextBlock
            {
                FontWeight = FontWeight.Bold,
                FontSize = FontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            if (themed)
            {
                skin.Bind(Border.BackgroundProperty, skin.GetResourceObservable(ThemeController.WizardFillKey));
                skin.Bind(Border.BorderBrushProperty, skin.GetResourceObservable(ThemeController.WizardBorderKey));
                skin.Bind(Border.BorderThicknessProperty, skin.GetResourceObservable(ThemeController.WizardBorderWidthKey));
                skin.Bind(Border.CornerRadiusProperty, skin.GetResourceObservable(ThemeController.WizardRadiusKey));
                hover.Bind(Border.BackgroundProperty, hover.GetResourceObservable(ThemeController.WizardHoverKey));
                hover.Bind(Border.CornerRadiusProperty, hover.GetResourceObservable(ThemeController.WizardRadiusKey));
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable(ThemeController.WizardLabelKey));
            }
            else
            {
                skin.Background = StopFill;
                skin.CornerRadius = hover.CornerRadius = new CornerRadius(ThemeSpec.DefaultButtonRadius);
                hover.Background = StopHover;
                label.Foreground = Brushes.White;
            }

            return new Panel { Width = width, Height = height, Children = { skin, hover, label } };
        }
    }
}
