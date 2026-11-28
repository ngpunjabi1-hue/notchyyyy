using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

public enum SetupStep { Name, Look, Tabs, Water, Done }

/// <summary>
/// First-run setup, inside the notch (the Mac app's OnboardingView): name, look, tabs, water, done. Sprout keeps you
/// company on the left, its mood following each step. Everything chosen applies at once and can be changed later
/// in Settings. Pages slide 18 pt and fade on a soft spring, forward or back.
/// </summary>
public sealed class OnboardingView : Canvas
{
    /// Survives the rebuild that a theme or colour change causes, so setup never jumps back to the start.
    public static SetupStep Saved { get; set; } = int.TryParse(Debug.Value("--onboarding-step"), out var s) ? (SetupStep)Math.Clamp(s, 0, 4) : SetupStep.Name;

    public const double SproutColumn = 190, Gap = 18;
    private readonly NotchModel _m;
    private readonly IClock _clock;
    private readonly SproutView _sprout;
    private readonly Progress _progress;
    private readonly PageSwap _pages;
    private readonly PrimaryButton _primary;
    private readonly BackButton _back;
    private readonly TextBlock _hint;
    private readonly TextBox _name;
    private SetupStep _step;
    private bool _ticking;

    /// Finishing setup (the shell closes the pin; tests watch it).
    public static event Action? Finished;

    public OnboardingView(NotchModel m, IClock clock)
    {
        _m = m; _clock = clock;
        _step = Saved;
        ClipToBounds = false;
        _sprout = new SproutView(clock, HomeView.SproutSize)
        {
            Interactive = true, HitDepth = 0.74,
            Source = t => (Mood, HomeView.HomeGaze, false, 1),
        };
        double canvas = HomeView.SproutSize * 1.3, rectTop = (HomeView.SproutSize - canvas) / 2 + canvas * 0.115, k = canvas * 0.77 / 100;
        SetLeft(_sprout, SproutColumn / 2 - HomeView.SproutSize / 2);
        SetTop(_sprout, m.BodySize.Height - (rectTop + 76 * k) - HomeView.ContentOrigin.Y);
        Children.Add(_sprout);

        _name = new TextBox
        {
            Text = m.UserName, PlaceholderText = "Your name", Width = 340, Height = 42, MinHeight = 42, FontFamily = LT.Ui,
            FontSize = LT.TextSize(15), FontWeight = FontWeight.Medium, CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 0),
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
        Fields.Quiet(_name);
        _name.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(LT.AccentColor.WithOpacity(0.6));
        _name.TextChanged += (_, _) => _primary?.SetEnabled(_step != SetupStep.Name || NameText.Length > 0);   // Sprout smiles once there's a name
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter && NameText.Length > 0) { Go(1); e.Handled = true; } };

        _progress = new Progress(clock);
        _pages = new PageSwap(clock);
        _back = new BackButton(clock, () => Go(-1));
        _hint = new TextBlock { Text = "You can change this later in Settings", FontFamily = LT.Ui, FontSize = LT.TextSize(10.5), Foreground = new SolidColorBrush(LT.Faint), VerticalAlignment = VerticalAlignment.Center };
        _primary = new PrimaryButton(clock, () => { if (_step == SetupStep.Done) Finish(); else Go(1); });
        var footer = new Grid { Height = 32, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,10,Auto") };
        footer.Children.Add(_back);
        Grid.SetColumn(_hint, 2); footer.Children.Add(_hint);
        Grid.SetColumn(_primary, 4); footer.Children.Add(_primary);
        double right = m.BodySize.Width - 2 * HomeView.ContentOrigin.X - SproutColumn - Gap - 6;
        var column = new Grid { Width = right, Height = m.BodySize.Height - HomeView.ContentOrigin.Y - 24, RowDefinitions = new RowDefinitions("Auto,16,*,Auto") };
        column.Children.Add(_progress);
        Grid.SetRow(_pages, 2); column.Children.Add(_pages);
        Grid.SetRow(footer, 3); column.Children.Add(footer);
        SetLeft(column, SproutColumn + Gap);
        Children.Add(column);

        _pages.Show(Page(_step), 0, clock.Now, animate: false);
        Chrome();
    }

    public SetupStep Step => _step;
    public TextBox NameBox => _name;
    public PrimaryButton Primary => _primary;
    public BackButton Back => _back;
    public SproutView Sprout => _sprout;
    public Control CurrentPage => _pages.Current!;
    private string NameText => (_name.Text ?? "").Trim();

    /// What Sprout feels on each page (the Mac's sproutMood).
    public SproutMood Mood => _step switch
    {
        SetupStep.Name => NameText.Length == 0 ? SproutMood.Idle : SproutMood.Happy,
        SetupStep.Look => SproutMood.Love,
        SetupStep.Water => Prefs.Shared.WaterReminders ? SproutMood.Thirsty : SproutMood.Idle,
        SetupStep.Done => SproutMood.Cheer,
        _ => SproutMood.Idle,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_step == SetupStep.Name) Avalonia.Threading.DispatcherTimer.RunOnce(() => _name.Focus(), TimeSpan.FromSeconds(0.35));
    }

    /// One step forward (1) or back (−1).
    public void Go(int d)
    {
        if (_step == SetupStep.Name && NameText.Length > 0)
        {
            _m.UserName = NameText;
            Prefs.Shared.UserName = NameText;
        }
        int next = (int)_step + d;
        if (next < 0 || next > (int)SetupStep.Done) return;
        if (_step == SetupStep.Name && NameText.Length == 0 && d > 0) return;
        _step = (SetupStep)next;
        Saved = _step;
        _pages.Show(Page(_step), d, _clock.Now, animate: true);
        Chrome();
        Tick();
        if (_step == SetupStep.Name) Avalonia.Threading.DispatcherTimer.RunOnce(() => _name.Focus(), TimeSpan.FromSeconds(0.3));
    }

    public void Finish()
    {
        Saved = SetupStep.Name;
        _m.Pinned = false;
        _m.Onboarded = true;
        Finished?.Invoke();
    }

    private void Chrome()
    {
        _progress.Set((int)_step, _clock.Now);
        _back.IsVisible = _step != SetupStep.Name && _step != SetupStep.Done;
        _hint.IsVisible = _step != SetupStep.Name && _step != SetupStep.Done;
        _primary.Set(_step == SetupStep.Done ? "Start using Notchyyyy" : "Continue", _step == SetupStep.Done ? IconKind.Sparkle : IconKind.Chevron);
        _primary.SetEnabled(_step != SetupStep.Name || NameText.Length > 0);
    }

    private void Tick()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            double now = _clock.Now;
            _pages.Update(now);
            if (_pages.Animating(now)) Tick();
        });
    }

    // ---- pages ----

    private Control Page(SetupStep s) => s switch
    {
        SetupStep.Name => Stack(18, Heading("Hi, I'm Sprout", "I live in your notch, with your music, clipboard, tasks and apps. What should I call you?"), Detach(_name)),
        SetupStep.Look => LookPage(),
        SetupStep.Tabs => TabsPage(),
        SetupStep.Water => WaterPage(),
        _ => Stack(16, Heading($"You're all set, {FirstName}", "A few things worth knowing:"),
            Stack(10, Tip(IconKind.Pointer, "Hover the notch to open it, move away to close"),
                      Tip(IconKind.Grid, "Ctrl+1…9 switch tabs, Esc closes"),
                      Tip(IconKind.Sparkle, "Poke Sprout. Gently."))),
    };

    private static Control Detach(Control c) { if (c.Parent is Panel p) p.Children.Remove(c); return c; }

    private string FirstName => (NameText.Length > 0 ? NameText : _m.UserName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    private static StackPanel Stack(double spacing, params Control[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    private static Control Heading(string title, string detail) => Stack(4,
        new TextBlock { Text = title, FontFamily = LT.Ui, FontSize = LT.TextSize(19), FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(LT.Text) },
        new TextBlock { Text = detail, FontFamily = LT.Ui, FontSize = LT.TextSize(12), Foreground = new SolidColorBrush(LT.Muted), TextWrapping = TextWrapping.Wrap });

    private Control LookPage()
    {
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var a in Enum.GetValues<Appearance>()) themes.Children.Add(new ThemeCard(_clock, a, () => Prefs.Shared.Appearance = a));
        var accent = Stack(8,
            new TextBlock { Text = "ACCENT", FontFamily = LT.Ui, FontSize = LT.TextSize(9), FontWeight = FontWeight.SemiBold, LetterSpacing = 1.3, Foreground = new SolidColorBrush(LT.Faint) },
            new Swatches(_clock, a => Prefs.Shared.Accent = a, gap: 3));
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 18,
            Children = { themes, new Border { Width = 1, Height = 46, Background = new SolidColorBrush(LT.Border), VerticalAlignment = VerticalAlignment.Center }, accent },
        };
        accent.VerticalAlignment = VerticalAlignment.Center;
        return Stack(16, Heading("Make it yours", "Pick a theme and a colour. Sprout and the whole notch follow along."), row);
    }

    private Control TabsPage()
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var t in Enum.GetValues<Tab>().Where(t => t.Hideable()))
            grid.Children.Add(new TabChoice(_clock, t) { Margin = new Thickness(0, 0, 7, 7) });
        return Stack(12, Heading("Choose your tabs", "Keep what you'll use. Home and Settings are always there."), grid);
    }

    private Control WaterPage()
    {
        var p = Prefs.Shared;
        var goalText = new TextBlock { Text = $"{p.WaterGoal} glasses", Width = 74, TextAlignment = TextAlignment.Center, FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Text), VerticalAlignment = VerticalAlignment.Center };
        var goal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        goal.Children.Add(new StepButton(_clock, IconKind.Minus, () => { p.WaterGoal -= 1; goalText.Text = $"{p.WaterGoal} glasses"; }));
        goal.Children.Add(goalText);
        goal.Children.Add(new StepButton(_clock, IconKind.Plus, () => { p.WaterGoal += 1; goalText.Text = $"{p.WaterGoal} glasses"; }));
        var interval = Row("Remind me every", new Segmented<WaterInterval>(_clock, Enum.GetValues<WaterInterval>(),
            i => (int)i < 60 ? $"{(int)i}m" : (int)i == 60 ? "1h" : "1.5h", () => p.WaterInterval, i => p.WaterInterval = i));
        var goalRow = Row("Daily goal", goal);
        void Dim() { interval.Opacity = goalRow.Opacity = p.WaterReminders ? 1 : 0.35; interval.IsHitTestVisible = goalRow.IsHitTestVisible = p.WaterReminders; }
        var toggle = Row("Water reminders", new Toggle(_clock, () => p.WaterReminders, v => { p.WaterReminders = v; Dim(); }));
        Dim();
        Border Line() => new() { Height = 1, Background = new SolidColorBrush(LT.Border) };
        return Stack(10, Heading("Stay hydrated", "Every so often the notch can nudge you to drink some water."), Stack(0, toggle, Line(), interval, Line(), goalRow));
    }

    private static Control Row(string label, Control control)
    {
        var g = new Grid { Height = 36, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = label, FontFamily = LT.Ui, FontSize = LT.TextSize(12.5), Foreground = new SolidColorBrush(LT.Text), VerticalAlignment = VerticalAlignment.Center });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static Control Tip(IconKind icon, string text)
    {
        var tile = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(LT.AccentColor.WithOpacity(0.12)), Child = new IconView(icon, 12, LT.AccentColor) };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { tile, new TextBlock { Text = text, FontFamily = LT.Ui, FontSize = LT.TextSize(12), Foreground = new SolidColorBrush(LT.Text), VerticalAlignment = VerticalAlignment.Center } },
        };
    }
}

/// <summary>Pages that slide 18 pt (left going forward, right going back) and fade, on the Mac's spring(0.42, 0.12).</summary>
public sealed class PageSwap : Panel
{
    private readonly IClock _clock;
    private readonly List<(Control View, SpringValue Amount, int Dir)> _items = new();
    private static readonly SpringSpec Soft = new(0.42, 0.12);
    public PageSwap(IClock clock) { _clock = clock; ClipToBounds = false; }
    public Control? Current => _items.Count > 0 ? _items[^1].View : null;

    /// <paramref name="dir"/>: 1 forward, −1 back.
    public void Show(Control view, int dir, double now, bool animate)
    {
        if (!animate)
        {
            Children.Clear(); _items.Clear();
            Children.Add(view); _items.Add((view, new SpringValue(0, 0.002), dir));
            Update(now);
            return;
        }
        for (int i = 0; i < _items.Count; i++)
        {
            var (v, a, _) = _items[i];
            a.AnimateTo(1, Soft, now);
            _items[i] = (v, a, dir);   // leaving: the other way
        }
        var amount = new SpringValue(1, 0.002);
        amount.AnimateTo(0, Soft, now);
        _items.Add((view, amount, -dir));
        Children.Add(view);
        Update(now);
    }

    public bool Animating(double now) => _items.Any(i => !i.Amount.IsSettled(now));

    public void Update(double now)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var (view, amount, dir) = _items[i];
            double a = amount.Value(now);
            bool leaving = i < _items.Count - 1;
            if (leaving && amount.IsSettled(now) && a > 0.99) { Children.Remove(view); _items.RemoveAt(i); continue; }
            view.Opacity = Math.Clamp(1 - Math.Abs(a), 0, 1);
            // arriving: from 18 pt on the side it comes from; leaving: 18 pt toward the other side
            view.RenderTransform = new TranslateTransform(leaving ? -dir * 18 * Math.Clamp(a, 0, 1.2) : -dir * 18 * a, 0);
            view.IsHitTestVisible = !leaving;
        }
    }
}

/// "STEP 2 OF 5" and five segments, the current one long; done ones in the accent.
public sealed class Progress : SpringControl
{
    private readonly SpringValue[] _w = new SpringValue[5];
    private int _step;
    public Progress(IClock clock) : base(clock) { for (int i = 0; i < 5; i++) _w[i] = Spring(i == 0 ? 22 : 10, 0.01); Height = 14; }
    public int Step => _step;
    public string Label => $"STEP {_step + 1} OF 5";
    public void Set(int step, double now)
    {
        _step = step;
        for (int i = 0; i < 5; i++) Go(_w[i], i == step ? 22 : 10, new SpringSpec(0.45, 0.2));
        InvalidateVisual();
    }
    public override void Render(DrawingContext ctx)
    {
        var t = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(9.5), new SolidColorBrush(LT.Faint));
        // letter-spaced by hand (1.3 pt), as the Mac's tracking
        double x = 0, cy = Bounds.Height / 2;
        foreach (var ch in Label)
        {
            var g = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(9.5), new SolidColorBrush(LT.Faint));
            ctx.DrawText(g, new Point(x, cy - t.Height / 2));
            x += g.WidthIncludingTrailingWhitespace + 1.3;
        }
        x += 12;
        for (int i = 0; i < 5; i++)
        {
            double w = _w[i].Value(Now);
            ctx.DrawRectangle(new SolidColorBrush(i <= _step ? LT.AccentColor : LT.Wash.WithOpacity(0.1)), null, new Rect(x, cy - 2, w, 4), 2, 2);
            x += w + 4;
        }
    }
}

/// The main action: an accent capsule; its arrow nudges forward on hover. Faint and inert until it can be used.
public sealed class PrimaryButton : SpringControl
{
    private readonly Action _action;
    private readonly SpringValue _hover, _enabled;
    private string _title = "Continue";
    private IconKind _icon = IconKind.Chevron;
    private bool _on = true;
    public PrimaryButton(IClock clock, Action action) : base(clock)
    {
        _action = action; _hover = Spring(0); _enabled = Spring(1);
        Height = 32; Cursor = new Cursor(StandardCursorType.Hand); HorizontalAlignment = HorizontalAlignment.Right;
    }
    public string Title => _title;
    public bool IsOn => _on;
    public void Set(string title, IconKind icon) { _title = title; _icon = icon; InvalidateMeasure(); InvalidateVisual(); }
    public void SetEnabled(bool on) { _on = on; Go(_enabled, on ? 1 : 0, LT.Hover); }
    private FormattedText T(Color c) => Text(_title, 12, c, FontWeight.SemiBold);
    protected override Size MeasureOverride(Size available) => new(16 + T(Colors.White).Width + 6 + 10 + 16, 32);
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (_on && new Rect(Bounds.Size).Contains(e.GetPosition(this))) _action(); }
    public void Press() { if (_on) _action(); }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now)) * V(_enabled.Value(Now)), en = V(_enabled.Value(Now));
        var bg = Mix(LT.Wash.WithOpacity(0.08), LT.AccentColor.WithOpacity(0.92 + 0.08 * h), en);
        ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(Bounds.Size), 16, 16);
        var ink = Mix(LT.Faint, Colors.White, en);
        var t = T(ink);
        ctx.DrawText(t, new Point(16, 16 - t.Height / 2));
        Icons.Draw(ctx, _icon, new Point(16 + t.Width + 6 + 5 + 2 * h, 16), 10, ink);
    }
}

/// "‹ Back", quiet.
public sealed class BackButton : SpringControl
{
    private readonly Action _action;
    private readonly SpringValue _hover;
    public BackButton(IClock clock, Action action) : base(clock) { _action = action; _hover = Spring(0); Height = 30; Width = 64; Cursor = new Cursor(StandardCursorType.Hand); VerticalAlignment = VerticalAlignment.Center; }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _action(); }
    public override void Render(DrawingContext ctx)
    {
        var c = Mix(LT.Muted, LT.Text, V(_hover.Value(Now)));
        // a chevron pointing back
        var g = new StreamGeometry();
        using (var s = g.Open()) { s.BeginFigure(new Point(16, 11), false); s.LineTo(new Point(12, 15)); s.LineTo(new Point(16, 19)); s.EndFigure(false); }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(c), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
        var t = Text("Back", 11.5, c, FontWeight.Medium);
        ctx.DrawText(t, new Point(24, 15 - t.Height / 2));
    }
}

/// A theme drawn as a tiny preview of the open notch (the Mac's ThemeCard); the chosen one has an accent edge.
public sealed class ThemeCard : SpringControl
{
    private readonly Appearance _a;
    private readonly Action _pick;
    private readonly SpringValue _hover;
    public ThemeCard(IClock clock, Appearance a, Action pick) : base(clock)
    {
        _a = a; _pick = pick; _hover = Spring(0);
        Width = 80; Height = 48 + 7 + 16; Cursor = new Cursor(StandardCursorType.Hand);
    }
    private bool Selected => Prefs.Shared.Appearance == _a;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _pick(); }
    public override void Render(DrawingContext ctx)
    {
        bool light = _a == Appearance.Light, sel = Selected;
        double h = sel ? 0 : V(_hover.Value(Now)), s = 1 + 0.03 * h;
        var r = new Rect(40 - 40 * s, 24 - 24 * s, 80 * s, 48 * s);
        var bg = light ? Color.FromRgb(249, 249, 250) : Color.FromRgb(15, 16, 18);
        var ink = light ? Colors.Black : Colors.White;
        ctx.DrawRectangle(new SolidColorBrush(bg), null, r, 8, 8);
        ctx.DrawRectangle(new SolidColorBrush(Colors.Black), null, new Rect(27, r.Top, 26, 6), 3, 3);
        ctx.DrawRectangle(new SolidColorBrush(ink.WithOpacity(0.75)), null, new Rect(10, r.Top + 10, 40, 3.5), 1.75, 1.75);
        ctx.DrawRectangle(new SolidColorBrush(ink.WithOpacity(0.25)), null, new Rect(10, r.Top + 17.5, 58, 3.5), 1.75, 1.75);
        ctx.DrawRectangle(new SolidColorBrush(ink.WithOpacity(0.12)), null, new Rect(10, r.Top + 25, 26, 8), 2, 2);
        ctx.DrawRectangle(new SolidColorBrush(LT.AccentColor), null, new Rect(40, r.Top + 25, 14, 8), 2, 2);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(sel ? LT.AccentColor : LT.Border), sel ? 2 : 1), r.Deflate(sel ? 1 : 0.5), 8, 8);
        var t = Text(light ? "Light" : "Dark", 11, sel ? LT.Text : LT.Muted, sel ? FontWeight.SemiBold : FontWeight.Normal);
        ctx.DrawText(t, new Point(40 - t.Width / 2, 48 + 7));
    }
}

/// One tab as a chip to keep or hide (the Mac's TabChoice): on, it's tinted with a tick in the accent.
public sealed class TabChoice : SpringControl
{
    private readonly Tab _tab;
    private readonly SpringValue _hover, _on;
    public TabChoice(IClock clock, Tab tab) : base(clock)
    {
        _tab = tab; _hover = Spring(0); _on = Spring(IsOn ? 1 : 0);
        Height = 30; Cursor = new Cursor(StandardCursorType.Hand);
    }
    public Tab Tab => _tab;
    public bool IsOn => !Prefs.Shared.HiddenTabs.Contains(_tab.Key());
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Flip(); }
    public void Flip() { Prefs.Shared.SetTabHidden(_tab.Key(), IsOn); Go(_on, IsOn ? 1 : 0, LT.Hover); }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now)), on = V(_on.Value(Now)), w = Bounds.Width;
        var r = new Rect(0.5, 0.5, w - 1, 29);
        ctx.DrawRectangle(new SolidColorBrush(Mix(LT.Wash.WithOpacity(0.025 + 0.025 * h), LT.AccentColor.WithOpacity(0.09), on)),
            new Pen(new SolidColorBrush(Mix(LT.Border, LT.AccentColor.WithOpacity(0.35), on)), 1), r, 9, 9);
        Icons.Draw(ctx, _tab.Icon(), new Point(11 + 8, 15), 12, Mix(LT.Faint, LT.AccentColor, on));
        var t = Text(_tab.Title(), 12, Mix(LT.Muted, LT.Text, on), FontWeight.Medium);
        t.MaxTextWidth = Math.Max(10, w - 11 - 16 - 8 - 15 - 11 - 4); t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis;
        ctx.DrawText(t, new Point(11 + 16 + 8, 15 - t.Height / 2));
        var c = new Point(w - 11 - 7.5, 15);
        if (on > 0.01)
        {
            ctx.DrawEllipse(new SolidColorBrush(LT.AccentColor.WithOpacity(on)), null, c, 7.5, 7.5);
            using (ctx.PushOpacity(on)) Icons.Draw(ctx, IconKind.Check, c, 9, Colors.White);
        }
        if (on < 0.99) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(LT.BorderHi.WithOpacity(1 - on)), 1.2), c, 6.9, 6.9);
    }
}
