using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Odinsons.ValheimLauncher.Avalonia.Theming;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    internal enum ArtMode { Hero, Column, Strip }

    /// <summary>
    /// The community's art with its name, logo and tagline at the bottom left. The text is always white and
    /// sits on a black layer whose strength is computed for the part of the picture this frame shows.
    /// </summary>
    internal sealed class ArtView : Decorator
    {
        private const double TextPadding = 16;
        private const double MaxTextWidth = 420;
        private const double FadeLength = 56;

        private readonly StackPanel _text = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Image _logo = new() { HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        private readonly TextBlock _name = new() { Foreground = Brushes.White, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _tagline = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private ThemeController? _theme;
        private ArtMode _mode = ArtMode.Hero;
        private Size _measuredFrame;
        private ArtSpec? _measuredArt;
        private int _darkening;
        private Rect _textArea;

        public ArtView()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            _text.Children.Add(_logo);
            _text.Children.Add(_name);
            _text.Children.Add(_tagline);
            Child = _text;
            ApplyMode();
        }

        public ArtMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                ApplyMode();
                Refresh();
            }
        }

        public int DarkeningPercent => _darkening;

        public Rect TextArea => _textArea;

        public string CommunityName
        {
            get => _name.Text ?? string.Empty;
            set => _name.Text = value;
        }

        public string? Tagline
        {
            get => _tagline.IsVisible ? _tagline.Text : null;
            set
            {
                _tagline.Text = value;
                _tagline.IsVisible = !string.IsNullOrWhiteSpace(value) && _mode == ArtMode.Hero;
            }
        }

        public void Bind(ThemeController theme)
        {
            if (_theme != null)
                _theme.Changed -= Refresh;
            _theme = theme;
            theme.Changed += Refresh;
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_theme != null)
                _theme.Changed -= Refresh;
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_theme != null)
            {
                _theme.Changed -= Refresh;
                _theme.Changed += Refresh;
                Refresh();
            }
        }

        private void ApplyMode()
        {
            _text.Margin = new Thickness(TextPadding);
            _text.Spacing = 4;
            _name.FontSize = _mode switch { ArtMode.Hero => 24, ArtMode.Strip => 18, _ => 16 };
            _logo.Height = _mode == ArtMode.Hero ? 40 : 28;
            _tagline.IsVisible = _tagline.IsVisible && _mode == ArtMode.Hero;
            if (_mode == ArtMode.Strip)
                _text.MaxWidth = MaxTextWidth;
            else
                _text.MaxWidth = double.PositiveInfinity;
        }

        private void Refresh()
        {
            if (_theme == null)
                return;

            ArtPicture? logo = _theme.Logo;
            _logo.Source = logo?.Bitmap;
            _logo.IsVisible = logo != null;
            _measuredArt = null;
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size result = base.ArrangeOverride(finalSize);
            Recompute(finalSize);
            return result;
        }

        private void Recompute(Size frame)
        {
            if (_theme == null || frame.Width < 1 || frame.Height < 1)
                return;

            ArtSpec art = _theme.Scheme.Art;
            Rect area = ComputeTextArea(frame);
            if (frame == _measuredFrame && ReferenceEquals(art, _measuredArt) && area == _textArea)
                return;

            _measuredFrame = frame;
            _measuredArt = art;
            _textArea = area;
            _darkening = art.Kind == ArtKind.Paint
                ? ArtPicture.DarkeningFor(art.Paint)
                : _theme.ArtPicture?.DarkeningFor(frame, art.Position, area) ?? 0;
            InvalidateVisual();
        }

        private Rect ComputeTextArea(Size frame)
        {
            double width = Math.Min(frame.Width - 2 * TextPadding, MaxTextWidth);
            double height = Math.Min(frame.Height - 2 * TextPadding, Math.Max(_text.DesiredSize.Height - 2 * TextPadding, 24));
            return new Rect(TextPadding, frame.Height - TextPadding - height, Math.Max(width, 1), Math.Max(height, 1));
        }

        public override void Render(DrawingContext context)
        {
            var frame = new Rect(Bounds.Size);
            if (_theme == null || frame.Width < 1 || frame.Height < 1)
                return;

            using (context.PushClip(frame))
            {
                ArtSpec art = _theme.Scheme.Art;
                if (art.Kind == ArtKind.Paint)
                    context.FillRectangle(ThemeBrushes.FromPaint(art.Paint), frame);
                else if (_theme.ArtPicture is { } picture)
                    context.DrawImage(picture.Bitmap, picture.VisibleSource(frame.Size, art.Position), frame);

                if (_darkening > 0)
                {
                    double layerHeight = Math.Min(frame.Height, _textArea.Height + 2 * TextPadding + FadeLength);
                    var layer = new Rect(0, frame.Height - layerHeight, frame.Width, layerHeight);
                    double solidFrom = Math.Min(1, FadeLength / layerHeight);
                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                            new GradientStop(Color.FromArgb((byte)Math.Round(_darkening * 2.55), 0, 0, 0), solidFrom),
                            new GradientStop(Color.FromArgb((byte)Math.Round(_darkening * 2.55), 0, 0, 0), 1)
                        }
                    };
                    context.FillRectangle(gradient, layer);
                }
            }
        }
    }
}
