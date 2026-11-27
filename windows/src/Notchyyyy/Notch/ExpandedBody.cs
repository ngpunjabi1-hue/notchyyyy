using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// The open notch: a header row ("Notchyyyy / Home" on the left, the time and the menu on the right)
/// and the tab's content below it. The tabs arrive in later steps.
/// </summary>
public sealed class ExpandedBody : Grid
{
    private readonly TextBlock _time;

    public ExpandedBody(NotchModel m, IClock clock)
    {
        Width = m.BodySize.Width; Height = m.BodySize.Height;
        Margin = new Thickness(0);
        RowDefinitions = new RowDefinitions($"{m.NotchSize.Height},*");

        TextBlock T(string s, Color c) => new()
        {
            Text = s, Foreground = new SolidColorBrush(c), FontFamily = LT.Ui,
            FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
        };
        var crumbs = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { T("Notchyyyy", LT.Faint), T("/", LT.Faint), T("Home", LT.Text) },
        };

        _time = new TextBlock
        {
            FontFamily = LT.Mono, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Muted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _time, new Dots { Width = 22, Height = 18 } },
        };

        var header = new Grid { Margin = new Thickness(14 + 18, 0), Children = { crumbs, right } };
        Children.Add(header);

        var content = new Border
        {
            Margin = new Thickness(14 + 24, 14, 14 + 24, 24),
            Child = new TextBlock
            {
                Text = "Tabs arrive in the next steps.", FontFamily = LT.Ui, FontSize = LT.TextSize(12),
                Foreground = new SolidColorBrush(LT.Faint), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        SetRow(content, 1);
        Children.Add(content);

        UpdateTime();
        DispatcherTimer.Run(() => { UpdateTime(); return true; }, TimeSpan.FromSeconds(15));
    }

    private void UpdateTime() => _time.Text = DateTime.Now.ToString("HH:mm");

    /// The "more" menu glyph: three dots.
    private sealed class Dots : Control
    {
        public override void Render(DrawingContext ctx)
        {
            var b = new SolidColorBrush(LT.Muted);
            double cy = Bounds.Height / 2, cx = Bounds.Width / 2;
            for (int i = -1; i <= 1; i++) ctx.DrawEllipse(b, null, new Point(cx + i * 4.2, cy), 1.4, 1.4);
        }
    }
}
