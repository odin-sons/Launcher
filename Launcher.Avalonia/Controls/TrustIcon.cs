using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Odinsons.ValheimLauncher.Avalonia.Controls
{
    /// <summary>
    /// The registry status as a shape and a colour: a shield with a tick, a warning triangle or a blocked
    /// circle. Its colours and shapes are fixed and it is drawn only by the launcher's header, so a theme
    /// cannot restyle or hide it.
    /// </summary>
    internal sealed class TrustIcon : Border
    {
        private static readonly IBrush ApprovedFill = new SolidColorBrush(Color.Parse("#4FBF7A"));
        private static readonly IBrush WarningFill = new SolidColorBrush(Color.Parse("#F2B84B"));
        private static readonly IBrush BlockedFill = new SolidColorBrush(Color.Parse("#E5626A"));
        private static readonly IBrush Mark = new SolidColorBrush(Color.Parse("#14171E"));

        private readonly Canvas _canvas = new() { Width = 24, Height = 24 };

        public TrustIcon()
        {
            Width = 32;
            Height = 32;
            Focusable = true;
            Child = new Viewbox { Width = 26, Height = 26, Child = _canvas };
            AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Image);
            IsVisible = false;
        }

        public RegistryStatus? Status { get; private set; }

        public static RegistryStatus? FromNotice(ServerNotice notice) => notice switch
        {
            ServerNotice.None => RegistryStatus.Approved,
            ServerNotice.NeedsApproval => RegistryStatus.NeedsApproval,
            ServerNotice.Suspended => RegistryStatus.Suspended,
            _ => null
        };

        public void Show(RegistryStatus? status)
        {
            Status = status;
            IsVisible = status != null;
            _canvas.Children.Clear();
            if (status == null)
                return;

            switch (status.Value)
            {
                case RegistryStatus.Approved:
                    _canvas.Children.Add(Shape("M12 1.5 L21 5 V11.5 C21 16.8 17.3 20.8 12 22.5 C6.7 20.8 3 16.8 3 11.5 V5 Z", ApprovedFill));
                    _canvas.Children.Add(Stroke("M7.5 12.2 L10.8 15.5 L16.8 8.8"));
                    break;
                case RegistryStatus.NeedsApproval:
                    _canvas.Children.Add(Shape("M12 2.5 L22.5 20.5 H1.5 Z", WarningFill));
                    _canvas.Children.Add(Stroke("M12 9 V14.5"));
                    _canvas.Children.Add(Stroke("M12 17.3 V17.4"));
                    break;
                default:
                    _canvas.Children.Add(new Ellipse { Width = 21, Height = 21, Fill = BlockedFill, [Canvas.LeftProperty] = 1.5, [Canvas.TopProperty] = 1.5 });
                    _canvas.Children.Add(Stroke("M7 7 L17 17"));
                    break;
            }
        }

        public void Describe(string name, string tooltip)
        {
            AutomationProperties.SetName(this, name);
            ToolTip.SetTip(this, tooltip);
        }

        private static Path Shape(string data, IBrush fill) => new() { Data = Geometry.Parse(data), Fill = fill };

        private static Path Stroke(string data) => new()
        {
            Data = Geometry.Parse(data),
            Stroke = Mark,
            StrokeThickness = 2.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
    }
}
