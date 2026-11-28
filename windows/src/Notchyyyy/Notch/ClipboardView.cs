using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Clipboard;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// The Clipboard tab (the Mac app's ClipboardView): "N recent · click a card to copy it again" and Clear, then a
/// row of cards, newest first, that scrolls sideways (the mouse wheel scrolls it too) and fades out on the right.
/// Cards slide to their new places when the list changes; a new one grows in, a removed one shrinks away.
/// </summary>
public sealed class ClipboardView : Grid
{
    private readonly Clips _clips;
    private readonly IClock _clock;
    private readonly TextBlock _count;
    private readonly PillButton _clear;
    private readonly CardStrip _strip;
    private readonly Control _empty;
    private DispatcherTimer? _timer;

    public ClipboardView(NotchModel m, IClock clock)
    {
        _clips = m.Clips; _clock = clock;
        RowDefinitions = new RowDefinitions("26,12,*");
        _count = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Muted), VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock
        {
            Text = "· click a card to copy it again", FontFamily = LT.Ui, FontSize = LT.TextSize(11),
            Foreground = new SolidColorBrush(LT.Faint), VerticalAlignment = VerticalAlignment.Center,
        };
        _clear = new PillButton(clock, "Clear", IconKind.Trash, () => _clips.Clear()) { HorizontalAlignment = HorizontalAlignment.Right };
        var header = new Grid { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _count, hint } }, _clear } };
        Children.Add(header);

        _strip = new CardStrip(_clips, clock);
        SetRow(_strip, 2);
        Children.Add(_strip);

        _empty = new StackPanel
        {
            Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new IconView(IconKind.Clipboard, 22, LT.Faint) { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Nothing copied yet", FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Text, images, colour codes and SVG icons you copy show up here", FontFamily = LT.Ui, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Faint), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center },
            },
        };
        SetRow(_empty, 2);
        Children.Add(_empty);
        Refresh(animate: false);
    }

    public CardStrip Strip => _strip;
    public PillButton ClearButton => _clear;
    public bool ShowingEmpty => _empty.IsVisible;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clips.Changed += OnChanged;
        Refresh(animate: false);
        // "now" becomes "1m", and so on
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => _strip.InvalidateCards());
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clips.Changed -= OnChanged;
        _timer?.Stop(); _timer = null;
    }

    private void OnChanged(string what, ClipItem? item)
    {
        Refresh(animate: true);
        _strip.InvalidateCards();   // ✓ Copied comes and goes; ages change
    }

    private void Refresh(bool animate)
    {
        int n = _clips.Items.Count;
        _count.Text = $"{n} recent";
        _clear.IsVisible = n > 0;
        _empty.IsVisible = n == 0;
        _strip.IsVisible = n > 0 || _strip.HasLeaving;
        _strip.Sync(animate);
    }
}

/// <summary>
/// The row of cards. Each card has its own springs for its place along the row and for arriving or leaving; the
/// row's scroll position follows the wheel on a critically damped spring (no overshoot, no stepping).
/// </summary>
public sealed class CardStrip : Panel
{
    public const double CardW = 156, Gap = 14;
    private readonly Clips _clips;
    private readonly IClock _clock;
    private readonly Dictionary<Guid, ClipCard> _cards = new();
    private readonly List<ClipCard> _leaving = new();
    private readonly SpringValue _scroll = new(0, 0.05);
    private bool _ticking;
    private static readonly SpringSpec Glide = SpringSpec.Response(0.3, 1.0);

    public CardStrip(Clips clips, IClock clock)
    {
        _clips = clips; _clock = clock;
        ClipToBounds = true;
        Background = Brushes.Transparent;   // the wheel works between cards too
        // a soft edge on the right says "there's more", instead of a card sliced in half
        OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.93), new GradientStop(Colors.Transparent, 1) },
        };
    }

    public bool HasLeaving => _leaving.Count > 0;
    public IEnumerable<ClipCard> Cards => _clips.Items.Where(i => _cards.ContainsKey(i.Id)).Select(i => _cards[i.Id]);
    public ClipCard? CardFor(ClipItem i) => _cards.GetValueOrDefault(i.Id);
    public double ScrollTarget => _scroll.Target;
    public double ScrollNow => _scroll.Value(_clock.Now);
    public double MaxScroll => Math.Max(0, _clips.Items.Count * (CardW + Gap) - Gap + 28 - Bounds.Width);

    /// Brings the cards in line with the list.
    public void Sync(bool animate)
    {
        double now = _clock.Now;
        var live = _clips.Items.Select(i => i.Id).ToHashSet();
        foreach (var (id, card) in _cards.Where(kv => !live.Contains(kv.Key)).ToList())
        {
            _cards.Remove(id);
            if (animate) { card.Leave(now); _leaving.Add(card); }
            else Children.Remove(card);
        }
        for (int i = 0; i < _clips.Items.Count; i++)
        {
            var item = _clips.Items[i];
            double x = i * (CardW + Gap);
            if (!_cards.TryGetValue(item.Id, out var card))
            {
                card = new ClipCard(item, _clips, _clock, () => Animate());
                _cards[item.Id] = card;
                Children.Add(card);
                card.Place(x, now, animate: false);
                if (animate) card.Arrive(now);
            }
            else card.Place(x, now, animate);
        }
        // a new copy at the front: glide back to the start so it's seen
        if (animate && _clips.Items.Count > 0 && _scroll.Target > 0 && _clips.Items[0].Date >= Wall.Now().AddSeconds(-2)) Go(0);
        Go(Math.Clamp(_scroll.Target, 0, MaxScroll), animate);
        Animate();
    }

    public void InvalidateCards() { foreach (var c in _cards.Values) c.Dirty(); }

    // back on screen (after the picker, another tab): pick up any motion that started while away; frames
    // can't be asked for while off screen, so a tile pinned meanwhile would otherwise stay invisible (seen on Windows)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Animate(); }



    private void Go(double target, bool animate = true)
    {
        if (animate) _scroll.AnimateTo(target, Glide, _clock.Now); else _scroll.Snap(target);
        Animate();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // a mouse wheel's notch is 1; a trackpad sends small fractions. Either axis scrolls the row.
        double d = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        Go(Math.Clamp(_scroll.Target - d * 90, 0, MaxScroll));
        e.Handled = true;
    }

    /// Scrolls so the card at <paramref name="index"/> is in view (tests; keyboard later).
    public void ScrollTo(double x, bool animate = true) => Go(Math.Clamp(x, 0, MaxScroll), animate);

    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(new Size(CardW, ClipCard.H));
        return new Size(0, ClipCard.H + 8);
    }

    protected override Size ArrangeOverride(Size final)
    {
        // cards are laid out at the left; their place along the row is a transform, so moving them never re-lays out
        foreach (var c in Children) c.Arrange(new Rect(0, 4, CardW, ClipCard.H));
        Place();
        return final;
    }

    private void Place()
    {
        double now = _clock.Now, s = _scroll.Value(now);
        foreach (var c in Children.OfType<ClipCard>()) c.Apply(now, -s);
    }

    private void Animate()
    {
        Place();
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            double now = _clock.Now;
            foreach (var gone in _leaving.Where(c => c.Gone(now)).ToList()) { _leaving.Remove(gone); Children.Remove(gone); }
            if (_leaving.Count == 0 && _clips.Items.Count == 0) IsVisible = false;
            Place();
            if (!_scroll.IsSettled(now) || Children.OfType<ClipCard>().Any(c => c.Moving(now))) Animate();
        });
    }
}

/// <summary>
/// One card (the Mac's ClipCard): the content, a hairline, and a footer with its kind and age. Hover lifts it a
/// little and shows a remove badge; clicking copies it again (✓ Copied, and it moves to the front). Colours have
/// HEX / RGB / HSL chips; SVG icons show Copy SVG / Copy PNG on hover.
/// </summary>
public sealed class ClipCard : Control, Avalonia.Rendering.ICustomHitTest
{
    public const double W = CardStrip.CardW, BodyH = 138, FooterH = 26, H = BodyH + 1 + FooterH;
    private readonly ClipItem _item;
    private readonly Clips _clips;
    private readonly IClock _clock;
    private readonly Action _animate;
    private readonly SpringValue _x = new(0, 0.05), _appear = new(1, 0.002), _hover = new(0, 0.002), _press = new(0, 0.002);
    private readonly Dictionary<string, SpringValue> _zones = new();
    private string? _hot, _down;
    private bool _leaving, _dirty = true;
    /// Redraw on the next frame (after a copy, the time changing).
    public void Dirty() { _dirty = true; InvalidateVisual(); }

    public ClipCard(ClipItem item, Clips clips, IClock clock, Action animate)
    {
        _item = item; _clips = clips; _clock = clock; _animate = animate;
        Width = W; Height = H;
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderTransformOrigin = RelativePoint.Center;
        ToolTip.SetTip(this, item.Kind == ClipKind.Text && item.Text.Length > 300 ? item.Text[..300] + "…" : null);
    }

    public ClipItem Item => _item;
    public double X => _x.Value(_clock.Now);
    public bool Hovered => _hover.Target > 0.5;
    public string? HotZone => _hot;

    public bool HitTest(Point p) => !_leaving && new Rect(0, 0, W, H).Contains(p);

    // ---- placement (driven by the strip) ----

    public void Place(double x, double now, bool animate)
    {
        if (animate) { if (Math.Abs(_x.Target - x) > 0.01) _x.AnimateTo(x, LT.Snappy, now); }
        else _x.Snap(x);
    }
    public void Arrive(double now) { _appear.Snap(0); _appear.AnimateTo(1, LT.Snappy, now); }
    public void Leave(double now) { _leaving = true; IsHitTestVisible = false; _appear.AnimateTo(0, LT.Snappy, now); }
    public bool Gone(double now) => _leaving && _appear.Value(now) < 0.02;
    public bool Moving(double now) => !_x.IsSettled(now) || !_appear.IsSettled(now) || !_hover.IsSettled(now) || !_press.IsSettled(now)
                                      || _zones.Values.Any(z => !z.IsSettled(now));

    /// Transform for this frame: its place along the row, and the Mac's scale (0.85 arriving, 1.02 hovered, 0.96 copied).
    public void Apply(double now, double offset)
    {
        double a = Math.Clamp(_appear.Value(now), 0, 1.2), h = Math.Clamp(_hover.Value(now), 0, 1.2), p = Math.Clamp(_press.Value(now), 0, 1.2);
        double scale = (0.85 + 0.15 * a) * (1 + 0.02 * h * (1 - p)) * (1 - 0.04 * p);
        Opacity = Math.Clamp(a, 0, 1);
        RenderTransform = new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(_x.Value(now) + offset, 0));
        // moving is only a transform; the card is redrawn only while its own look changes (hover, chips, badge)
        if (!_hover.IsSettled(now) || _zones.Values.Any(z => !z.IsSettled(now)) || _dirty) { _dirty = false; InvalidateVisual(); }
    }

    // ---- pointer ----

    private const double Badge = 18;
    private Rect BadgeRect => new(W - 7 - Badge, 7, Badge, Badge);
    private bool Copied(string? format = null) => _clips.JustCopied == (format is null ? _item.Id.ToString() : $"{_item.Id}:{format}");

    private IEnumerable<(string Zone, Rect R)> Zones()
    {
        if (_hover.Target > 0.5) yield return ("remove", BadgeRect.Inflate(3));
        if (_item.Kind == ClipKind.Color)
        {
            double cw = (W - 12 - 8) / 3;
            string[] f = { "hex", "rgb", "hsl" };
            for (int i = 0; i < 3; i++) yield return (f[i], new Rect(6 + i * (cw + 4), BodyH - 30 + 5, cw, 20));
        }
        if (_item.Kind == ClipKind.Svg && _hover.Target > 0.5)
        {
            bool png = _item.Picture is not null;
            double cw = png ? (W - 12 - 4) / 2 : W - 12;
            yield return ("svg", new Rect(6, BodyH - 6 - 22, cw, 22));
            if (png) yield return ("png", new Rect(6 + cw + 4, BodyH - 6 - 22, cw, 22));
        }
    }

    private string Zone(Point p) => Zones().FirstOrDefault(z => z.R.Contains(p)).Zone ?? "card";

    private SpringValue Z(string zone) => _zones.TryGetValue(zone, out var s) ? s : _zones[zone] = new SpringValue(0, 0.002);

    private void SetHot(string? zone)
    {
        if (zone == _hot) return;
        double now = _clock.Now;
        if (_hot is not null) Z(_hot).AnimateTo(0, LT.Hover, now);
        _hot = zone;
        if (zone is not null) Z(zone).AnimateTo(1, LT.Hover, now);
        _animate();
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover.AnimateTo(1, LT.Hover, _clock.Now); SetHot(Zone(e.GetPosition(this))); _animate(); }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); SetHot(Zone(e.GetPosition(this))); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover.AnimateTo(0, LT.Hover, _clock.Now); SetHot(null); _animate(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); _down = Zone(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var z = Zone(e.GetPosition(this));
        if (z == _down && new Rect(0, 0, W, H).Contains(e.GetPosition(this))) Click(z);
        _down = null;
    }

    /// What a click on <paramref name="zone"/> does ("card", "remove", "hex", "rgb", "hsl", "svg", "png").
    public void Click(string zone)
    {
        if (zone == "remove") { _clips.Remove(_item); return; }
        double now = _clock.Now;
        _press.AnimateTo(1, LT.Snappy, now);
        DispatcherTimer.RunOnce(() => { _press.AnimateTo(0, LT.Snappy, _clock.Now); _animate(); }, TimeSpan.FromSeconds(1.2));
        _clips.Copy(_item, zone == "card" ? null : zone);
        _animate();
    }

    // ---- drawing ----

    private static FormattedText T(string s, double size, Color c, FontWeight w = FontWeight.Normal, FontFamily? f = null) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(f ?? LT.Ui, FontStyle.Normal, w), LT.TextSize(size), new SolidColorBrush(c));

    public override void Render(DrawingContext ctx)
    {
        double now = _clock.Now, h = Math.Clamp(_hover.Value(now), 0, 1);
        var card = new Rect(0, 0, W, H);
        var shape = new RoundedRect(card, 10);
        ctx.DrawRectangle(new SolidColorBrush(Mix(LT.Surface, LT.SurfaceHi, h)), null, shape);
        using (ctx.PushClip(shape))
        {
            DrawBody(ctx, now, h);
            ctx.DrawRectangle(new SolidColorBrush(LT.Border), null, new Rect(0, BodyH, W, 1));
            DrawFooter(ctx);
        }
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Mix(LT.Border, LT.BorderHi, h)), 1), new RoundedRect(card.Deflate(0.5), 9.5));
        DrawBadge(ctx, now, h);
    }

    private void DrawBody(DrawingContext ctx, double now, double h)
    {
        var body = new Rect(0, 0, W, BodyH);
        switch (_item.Kind)
        {
            case ClipKind.Text:
            {
                var t = T(_item.Text, 11.5, LT.Text.WithOpacity(0.9));
                t.MaxTextWidth = W - 20; t.MaxTextHeight = BodyH - 20; t.MaxLineCount = 7; t.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawText(t, new Point(10, 10));
                break;
            }
            case ClipKind.Image:
                if (_item.Bitmap is { } bmp) Fill(ctx, bmp, body);
                break;
            case ClipKind.Color:
            {
                var c = _item.Color!;
                var sw = new Rect(0, 0, W, BodyH - 30);
                if (c.A < 1) Checker(ctx, sw, light: false);
                ctx.DrawRectangle(new SolidColorBrush(c.Color), null, sw);
                var ink = c.IsLight ? Colors.Black.WithOpacity(0.75) : Colors.White.WithOpacity(0.92);
                var hex = T(c.Hex, 12.5, ink, FontWeight.SemiBold, LT.Mono);
                ctx.DrawText(hex, new Point(10, sw.Bottom - 10 - hex.Height));
                double cw = (W - 12 - 8) / 3;
                string[] f = { "hex", "rgb", "hsl" }, titles = { "HEX", "RGB", "HSL" };
                for (int i = 0; i < 3; i++)
                {
                    double zh = Math.Clamp(_zones.TryGetValue(f[i], out var z) ? z.Value(now) : 0, 0, 1);
                    var r = new Rect(6 + i * (cw + 4), BodyH - 30 + 5, cw, 20);
                    ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05 + 0.05 * zh)), null, r, 5, 5);
                    bool done = Copied(f[i]);
                    var col = done ? LT.Green : Mix(LT.Muted, LT.Text, zh);
                    var tt = T(titles[i], 9.5, col, FontWeight.SemiBold, LT.Mono);
                    double tw = tt.Width + (done ? 11 : 0), x0 = r.Center.X - tw / 2;
                    if (done) Icons.Draw(ctx, IconKind.Check, new Point(x0 + 4, r.Center.Y), 9, col);
                    ctx.DrawText(tt, new Point(x0 + (done ? 11 : 0), r.Center.Y - tt.Height / 2));
                }
                break;
            }
            case ClipKind.Svg:
            {
                Checker(ctx, body, light: true);
                if (_item.Bitmap is { } icon)
                {
                    var box = new Rect(W / 2 - 29, BodyH / 2 - 29, 58, 58);
                    double k = Math.Min(58 / icon.Size.Width, 58 / icon.Size.Height);
                    var dst = new Rect(box.Center.X - icon.Size.Width * k / 2, box.Center.Y - icon.Size.Height * k / 2, icon.Size.Width * k, icon.Size.Height * k);
                    ctx.DrawImage(icon, new Rect(icon.Size), dst);
                }
                else
                {
                    var t = T("Can't preview this SVG", 10, Colors.Black.WithOpacity(0.5));
                    ctx.DrawText(t, new Point(W / 2 - t.Width / 2, BodyH / 2 - t.Height / 2));
                }
                // Copy SVG / Copy PNG rise from the bottom on hover
                if (h > 0.01)
                {
                    bool png = _item.Picture is not null;
                    double cw = png ? (W - 12 - 4) / 2 : W - 12, dy = (1 - h) * 14;
                    using (ctx.PushOpacity(h))
                        foreach (var (zone, title, x) in png ? new[] { ("svg", "Copy SVG", 6.0), ("png", "Copy PNG", 6 + cw + 4) } : new[] { ("svg", "Copy SVG", 6.0) })
                        {
                            double zh = Math.Clamp(_zones.TryGetValue(zone, out var z) ? z.Value(now) : 0, 0, 1);
                            bool done = Copied(zone);
                            var r = new Rect(x, BodyH - 6 - 22 + dy, cw, 22);
                            ctx.DrawRectangle(new SolidColorBrush(done ? LT.Green : Colors.Black.WithOpacity(0.7 + 0.15 * zh)), null, r, 6, 6);
                            var tt = T(done ? "Copied ✓" : title, 9.5, Colors.White, FontWeight.SemiBold);
                            ctx.DrawText(tt, new Point(r.Center.X - tt.Width / 2, r.Center.Y - tt.Height / 2));
                        }
                }
                break;
            }
        }
    }

    private void DrawFooter(DrawingContext ctx)
    {
        bool copied = Copied();
        var col = copied ? LT.Green : LT.Muted;
        double cy = BodyH + 1 + FooterH / 2;
        var icon = copied ? IconKind.Check : _item.Kind switch
        {
            ClipKind.Text => IconKind.TextLines, ClipKind.Image => IconKind.Photo, ClipKind.Color => IconKind.Palette, _ => IconKind.Shapes,
        };
        Icons.Draw(ctx, icon, new Point(9 + 5, cy), 11 * Prefs.Shared.IconScale, col);
        var label = T(copied ? "Copied" : _item.Label, 10, col, FontWeight.Medium);
        label.MaxTextWidth = W - 9 - 15 - 40; label.MaxLineCount = 1; label.Trimming = TextTrimming.CharacterEllipsis;
        ctx.DrawText(label, new Point(9 + 15, cy - label.Height / 2));
        var age = T(Clips.Relative(_item.Date), 10, LT.Faint, FontWeight.Normal, LT.Mono);
        ctx.DrawText(age, new Point(W - 9 - age.Width, cy - age.Height / 2));
    }

    /// The remove badge: grows in at the top right on hover, red under the pointer.
    private void DrawBadge(DrawingContext ctx, double now, double h)
    {
        if (h < 0.01) return;
        double zh = Math.Clamp(_zones.TryGetValue("remove", out var z) ? z.Value(now) : 0, 0, 1);
        var c = BadgeRect.Center;
        double r = Badge / 2 * (0.3 + 0.7 * h) * (1 + 0.12 * zh);
        using (ctx.PushOpacity(Math.Clamp(h, 0, 1)))
        {
            ctx.DrawEllipse(new SolidColorBrush(Colors.Black.WithOpacity(0.3)), null, c + new Vector(0, 1), r + 0.5, r + 0.5);
            ctx.DrawEllipse(new SolidColorBrush(Mix(Color.FromRgb(77, 77, 77), LT.Red, zh)), new Pen(new SolidColorBrush(Colors.White.WithOpacity(0.25)), 1), c, r - 0.5, r - 0.5);
            Icons.Draw(ctx, IconKind.Close, c, 9 * (r / (Badge / 2)), Colors.White);
        }
    }

    private static void Fill(DrawingContext ctx, Avalonia.Media.Imaging.Bitmap bmp, Rect r)
    {
        double k = Math.Max(r.Width / bmp.Size.Width, r.Height / bmp.Size.Height);
        double sw = r.Width / k, sh = r.Height / k;
        var src = new Rect((bmp.Size.Width - sw) / 2, (bmp.Size.Height - sh) / 2, sw, sh);
        ctx.DrawImage(bmp, src, r);
    }

    /// The transparency checkerboard (7-pt cells) behind SVGs and see-through colours.
    private static void Checker(DrawingContext ctx, Rect r, bool light)
    {
        ctx.DrawRectangle(new SolidColorBrush(Grey(light ? 0.97 : 0.2)), null, r);
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            const double cell = 7;
            int row = 0;
            for (double y = r.Top; y < r.Bottom; y += cell, row++)
            for (double x = r.Left + (row % 2 == 0 ? 0 : cell); x < r.Right; x += cell * 2)
            {
                s.BeginFigure(new Point(x, y), true);
                s.LineTo(new Point(Math.Min(x + cell, r.Right), y));
                s.LineTo(new Point(Math.Min(x + cell, r.Right), Math.Min(y + cell, r.Bottom)));
                s.LineTo(new Point(x, Math.Min(y + cell, r.Bottom)));
                s.EndFigure(true);
            }
        }
        ctx.DrawGeometry(new SolidColorBrush(Grey(light ? 0.9 : 0.27)), null, g);
    }

    private static Color Grey(double v) { byte b = (byte)Math.Round(v * 255); return Color.FromRgb(b, b, b); }
    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
