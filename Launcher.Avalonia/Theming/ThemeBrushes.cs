using Avalonia;
using Avalonia.Media;

namespace Odinsons.ValheimLauncher.Avalonia.Theming
{
    /// <summary>Turns the engine's paints into Avalonia brushes.</summary>
    internal static class ThemeBrushes
    {
        public static IBrush FromPaint(Paint paint)
        {
            switch (paint.Kind)
            {
                case PaintKind.Solid:
                    return new SolidColorBrush(LauncherBrushes.ToColor(paint.From));
                case PaintKind.Gradient:
                    (RelativePoint start, RelativePoint end) = Direction(paint.Direction);
                    return new LinearGradientBrush
                    {
                        StartPoint = start,
                        EndPoint = end,
                        GradientStops =
                        {
                            new GradientStop(LauncherBrushes.ToColor(paint.From), 0),
                            new GradientStop(LauncherBrushes.ToColor(paint.To), 1)
                        }
                    };
                default:
                    return Brushes.Transparent;
            }
        }

        public static (RelativePoint start, RelativePoint end) Direction(GradientDirection direction) => direction switch
        {
            GradientDirection.Vertical => (new RelativePoint(0.5, 0, RelativeUnit.Relative), new RelativePoint(0.5, 1, RelativeUnit.Relative)),
            GradientDirection.Diagonal => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative)),
            _ => (new RelativePoint(0, 0.5, RelativeUnit.Relative), new RelativePoint(1, 0.5, RelativeUnit.Relative))
        };
    }
}
