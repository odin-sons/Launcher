using Avalonia.Controls;
using Avalonia.Media;

namespace Odinsons.ValheimLauncher.Avalonia.Theming
{
    /// <summary>
    /// The launcher's own colours for the active scheme. Each brush is one shared instance whose colour
    /// changes with the scheme, so everything holding it follows a switch without being rebuilt.
    /// </summary>
    internal static class LauncherBrushes
    {
        public const string BackgroundKey = "LauncherBackground";
        public const string SurfaceKey = "LauncherSurface";
        public const string TextKey = "LauncherText";
        public const string MutedKey = "LauncherMuted";
        public const string LineKey = "LauncherLine";
        public const string NoticeBackgroundKey = "NoticeBackground";
        public const string NoticeTextKey = "NoticeText";
        public const string NoticeBorderKey = "NoticeBorder";
        public const string NoticeIconKey = "NoticeIcon";
        public const string AccentKey = "ThemeAccent";
        public const string PositiveKey = "LauncherPositive";
        public const string SkeletonKey = "LauncherSkeleton";

        public static readonly SolidColorBrush Background = new();
        public static readonly SolidColorBrush Surface = new();
        public static readonly SolidColorBrush Text = new();
        public static readonly SolidColorBrush Muted = new();
        public static readonly SolidColorBrush Line = new();
        public static readonly SolidColorBrush NoticeBackground = new();
        public static readonly SolidColorBrush NoticeText = new();
        public static readonly SolidColorBrush NoticeBorder = new();
        public static readonly SolidColorBrush NoticeIcon = new();
        public static readonly SolidColorBrush Accent = new();
        public static readonly SolidColorBrush Positive = new();
        public static readonly SolidColorBrush Skeleton = new();

        private static readonly Rgb DarkPositive = Rgb.Parse("#6FCF6F");
        private static readonly Rgb LightPositive = Rgb.Parse("#1B6E2E");

        static LauncherBrushes() => Apply(LauncherPalette.Dark, ThemeSpec.DefaultDarkAccent);

        public static void Apply(LauncherPalette palette, Rgb accent)
        {
            Background.Color = ToColor(palette.Background);
            Surface.Color = ToColor(palette.Surface);
            Text.Color = ToColor(palette.Text);
            Muted.Color = ToColor(palette.Muted);
            Line.Color = ToColor(palette.Line);
            NoticeBackground.Color = ToColor(palette.NoticeBackground);
            NoticeText.Color = ToColor(palette.NoticeText);
            NoticeBorder.Color = ToColor(palette.NoticeBorder);
            NoticeIcon.Color = ToColor(palette.NoticeIcon);
            Accent.Color = ToColor(accent);
            Positive.Color = ToColor(palette.Kind == ColorSchemeKind.Dark ? DarkPositive : LightPositive);
            Skeleton.Color = ToColor(palette.Line, 0xA0);
        }

        public static Color ToColor(Rgb color, byte alpha = 0xFF) => Color.FromArgb(alpha, color.R, color.G, color.B);

        public static void Register(IResourceDictionary resources)
        {
            resources[BackgroundKey] = Background;
            resources[SurfaceKey] = Surface;
            resources[TextKey] = Text;
            resources[MutedKey] = Muted;
            resources[LineKey] = Line;
            resources[NoticeBackgroundKey] = NoticeBackground;
            resources[NoticeTextKey] = NoticeText;
            resources[NoticeBorderKey] = NoticeBorder;
            resources[NoticeIconKey] = NoticeIcon;
            resources[AccentKey] = Accent;
            resources[PositiveKey] = Positive;
            resources[SkeletonKey] = Skeleton;
        }
    }
}
