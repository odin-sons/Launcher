using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    /// <summary>
    /// The frame a layout template draws around one step: the community's art as a column, a strip or the
    /// Server step's hero, and the places of the step's own zones. The zones are the frame's other children.
    /// </summary>
    internal sealed class StepFrame : Grid
    {
        private readonly ArtView _art = new();
        private readonly StackPanel _nameBar = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(16, 12, 16, 4), IsVisible = false };
        private readonly Image _nameBarLogo = new() { Height = 26, IsVisible = false };
        private readonly TextBlock _nameBarText = new() { FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        private ThemeController? _theme;

        public StepFrame()
        {
            _nameBar.Children.Add(_nameBarLogo);
            _nameBar.Children.Add(_nameBarText);
            _nameBarText.Bind(TextBlock.ForegroundProperty, _nameBarText.GetResourceObservable(LauncherBrushes.TextKey));
            Children.Add(_art);
            Children.Add(_nameBar);
        }

        public StepKind Step { get; set; }

        public Control? Online { get; set; }

        public Control? AboutNews { get; set; }

        public Control? Body { get; set; }

        public ArtView Art => _art;

        public FramePlan Plan { get; private set; } = FrameLayout.Plan(LayoutTemplate.Classic, StepKind.Server, OnlineSide.Left);

        public event Action<FramePlan>? PlanApplied;

        public void Bind(ThemeController theme, string communityName)
        {
            if (_theme != null)
                _theme.Changed -= Apply;
            _theme = theme;
            theme.Changed += Apply;
            _art.CommunityName = communityName;
            _nameBarText.Text = communityName;
            _art.Bind(theme);
            Apply();
        }

        public void Apply()
        {
            if (_theme == null)
                return;

            FramePlan plan = FrameLayout.Plan(_theme.Spec.Layout, Step, _theme.Spec.Online);
            Plan = plan;
            ColumnDefinitions = new ColumnDefinitions(plan.Columns);
            RowDefinitions = new RowDefinitions(plan.Rows);

            Place(Online, plan.Online);
            Place(_art, plan.Art);
            Place(AboutNews, plan.AboutNews);
            Place(Body, plan.Body);
            Place(_nameBar, plan.Header);

            _art.Mode = plan.ArtMode;
            ArtPicture? logo = _theme.Logo;
            _nameBarLogo.Source = logo?.Bitmap;
            _nameBarLogo.IsVisible = logo != null;

            if (AboutNews != null)
            {
                AboutNews.HorizontalAlignment = plan.AboutIsCard ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                AboutNews.VerticalAlignment = plan.AboutIsCard ? VerticalAlignment.Top : VerticalAlignment.Stretch;
                AboutNews.Margin = plan.AboutIsCard ? new Thickness(16) : new Thickness(0);
            }

            // The zone that is drawn last sits above the art when both share a cell.
            if (AboutNews != null && plan.AboutNews is { } about && plan.Art is { } art && about == art)
                Children.Move(Children.IndexOf(AboutNews), Children.Count - 1);

            PlanApplied?.Invoke(plan);
        }

        private static void Place(Control? control, Cell? cell)
        {
            if (control == null)
                return;
            control.IsVisible = cell != null;
            if (cell is not { } c)
                return;
            SetRow(control, c.Row);
            SetColumn(control, c.Column);
            SetRowSpan(control, c.RowSpan);
            SetColumnSpan(control, c.ColumnSpan);
        }
    }
}
