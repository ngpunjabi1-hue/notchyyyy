using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Theme;
using Notchyyyy.Today;

namespace Notchyyyy.Notch;

/// <summary>
/// The Today tab (the Mac app's TodayView): on the left the day at a glance (weekday, date, how much is done, what's
/// up next with a countdown); on the right a composer ("Call mom at 5pm" understands the time) and the list.
/// </summary>
public sealed class TodayView : Grid
{
    private readonly Tasks _tasks;
    private readonly IClock _clock;
    private readonly TextBlock _weekday, _date, _progressText, _nextTitle, _nextWhen;
    private readonly ProgressBar5 _bar;
    private readonly Border _next;
    private readonly TextBox _input;
    private readonly TimeField _time;
    private readonly ClockToggle _clockToggle;
    private readonly IconView _plus;
    private readonly Border _composer;
    private readonly TaskList _list;
    private DispatcherTimer? _timer;

    public TodayView(NotchModel m, IClock clock)
    {
        _tasks = m.Tasks; _clock = clock;
        ColumnDefinitions = new ColumnDefinitions("190,22,1,22,*");

        // ---- left: the day at a glance ----
        _weekday = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(18), FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(LT.Text) };
        _date = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(12), Foreground = new SolidColorBrush(LT.Muted) };
        _progressText = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(11), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted) };
        _bar = new ProgressBar5(clock) { Height = 5 };
        _nextTitle = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Text), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        _nextWhen = new TextBlock { FontFamily = LT.Mono, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.AccentColor) };
        _next = new Border
        {
            Padding = new Thickness(9), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(LT.Surface),
            BorderBrush = new SolidColorBrush(LT.Border), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel
            {
                Spacing = 3,
                Children = { new TextBlock { Text = "Up next", FontFamily = LT.Ui, FontSize = LT.TextSize(10), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Faint) }, _nextTitle, _nextWhen },
            },
        };
        var summary = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,10,Auto,*"),
            Children =
            {
                new StackPanel { Spacing = 1, Children = { _weekday, _date } },
            },
        };
        var progress = new StackPanel { Spacing = 5, Children = { _progressText, _bar } };
        Grid.SetRow(progress, 2); summary.Children.Add(progress);
        Grid.SetRow(_next, 3); summary.Children.Add(_next);
        Children.Add(summary);
        var divider = new Border { Background = new SolidColorBrush(LT.Border), Width = 1 };
        SetColumn(divider, 2);
        Children.Add(divider);

        // ---- right: add + list ----
        _plus = new IconView(IconKind.PlusCircle, 14, LT.Faint) { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        _input = new TextBox
        {
            PlaceholderText = "Add a task… try “Call mom at 5pm”", FontFamily = LT.Ui, FontSize = LT.TextSize(12.5),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 20,
            VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Plain(_input);
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddFromInput(); e.Handled = true; } };
        _input.GotFocus += (_, _) => FocusChanged();
        _input.LostFocus += (_, _) => FocusChanged();
        _time = new TimeField(clock) { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _clockToggle = new ClockToggle(clock, () => SetUseTime(!_time.IsVisible)) { VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto,8,Auto"), Margin = new Thickness(12, 0) };
        row.Children.Add(_plus);
        SetColumn(_input, 2); row.Children.Add(_input);
        SetColumn(_time, 4); row.Children.Add(_time);
        SetColumn(_clockToggle, 6); row.Children.Add(_clockToggle);
        _composer = new Border
        {
            Height = 38, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(LT.Surface),
            BorderBrush = new SolidColorBrush(LT.Border), BorderThickness = new Thickness(1), Child = row, Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        _composer.PointerPressed += (_, _) => _input.Focus();
        _list = new TaskList(_tasks, clock);
        var right = new Grid { RowDefinitions = new RowDefinitions("38,12,*"), Children = { _composer } };
        SetRow(_list, 2); right.Children.Add(_list);
        SetColumn(right, 4);
        Children.Add(right);
        Refresh(false);
    }

    public TextBox Input => _input;
    public TimeField Time => _time;
    public ClockToggle ClockButton => _clockToggle;
    public TaskList List => _list;
    public string Progress => _progressText.Text ?? "";
    public bool ShowingNext => _next.IsVisible;
    public string NextText => $"{_nextTitle.Text} | {_nextWhen.Text}";

    /// A text box with no chrome of its own: the composer is the field.
    private static void Plain(TextBox t)
    {
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) t.Resources[k] = Brushes.Transparent;
        foreach (var k in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" }) t.Resources[k] = Brushes.Transparent;
        foreach (var k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" }) t.Resources[k] = new SolidColorBrush(LT.Text);
        foreach (var k in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" }) t.Resources[k] = new SolidColorBrush(LT.Faint);
        t.Resources["TextControlSelectionHighlightColor"] = new SolidColorBrush(LT.AccentColor.WithOpacity(0.35));
        t.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
    }

    private void FocusChanged()
    {
        bool f = _input.IsFocused;
        _composer.Background = new SolidColorBrush(f ? LT.SurfaceHi : LT.Surface);
        _composer.BorderBrush = new SolidColorBrush(f ? LT.BorderHi : LT.Border);
        _plus.Color = f ? LT.AccentColor : LT.Faint;
        _plus.InvalidateVisual();
    }

    public void SetUseTime(bool on)
    {
        if (on) _time.Value = (Wall.Now().AddHours(1)).TimeOfDay;
        _time.IsVisible = on;
        _clockToggle.On = on;
    }

    /// Enter in the composer: add the task (with the time field's time if it's showing), then clear.
    public void AddFromInput()
    {
        var text = _input.Text ?? "";
        if (text.Trim().Length == 0) return;
        _tasks.Add(text, _time.IsVisible ? Wall.Now().Date + _time.Value : null);
        _input.Text = "";
        SetUseTime(false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tasks.Changed += OnChanged;
        Refresh(false);
        // the countdown and "overdue" change with the minutes
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Refresh(true));
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tasks.Changed -= OnChanged;
        _timer?.Stop(); _timer = null;
    }

    private void OnChanged(string what, DayTask? t) { if (what == "Changed") Refresh(true); }

    private void Refresh(bool animate)
    {
        var now = Wall.Now();
        _weekday.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        _date.Text = now.ToString("d MMMM", CultureInfo.CurrentCulture);
        int total = _tasks.Today.Count, done = _tasks.DoneCount;
        _progressText.Text = total == 0 ? "Nothing planned" : $"{done} of {total} done";
        _bar.IsVisible = total > 0;
        _bar.Set(total > 0 ? (double)done / total : 0, animate);
        var next = _tasks.NextUp;
        _next.IsVisible = next is not null;
        if (next is { Time: { } t }) { _nextTitle.Text = next.Title; _nextWhen.Text = Tasks.Countdown(t); }
        _list.Sync(animate);
    }
}

/// The 5-pt progress capsule: accent, green when everything's done; it fills on the Mac's liquid spring.
public sealed class ProgressBar5 : SpringControl
{
    private readonly SpringValue _v;
    public ProgressBar5(IClock clock) : base(clock) { _v = Spring(0, 0.001); }
    public double Value => _v.Target;
    public void Set(double v, bool animate) { if (animate) Go(_v, v, LT.Liquid); else { _v.Snap(v); InvalidateVisual(); } }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, f = Math.Clamp(_v.Value(Now), 0, 1);
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.08)), null, new Rect(0, 0, w, 5), 2.5, 2.5);
        var fill = _v.Target >= 0.999 ? LT.Green : LT.AccentColor;
        ctx.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, Math.Max(5, w * f), 5), 2.5, 2.5);
    }
}

/// The composer's clock button: shows the time field; accent while it's showing.
public sealed class ClockToggle : SpringControl
{
    private readonly Action _action;
    private readonly SpringValue _hover;
    private bool _on;
    public ClockToggle(IClock clock, Action action) : base(clock)
    {
        _action = action; _hover = Spring(0);
        Width = 22; Height = 22; Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Set a time");
    }
    public bool On { get => _on; set { _on = value; InvalidateVisual(); } }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _action(); }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        var c = _on ? LT.AccentColor : Mix(LT.Muted, LT.Text, h);
        var centre = new Point(11, 11);
        if (_on) ctx.DrawEllipse(new SolidColorBrush(c), null, centre, 7, 7);
        else ctx.DrawEllipse(null, new Pen(new SolidColorBrush(c), 1.5), centre, 6.5, 6.5);
        var hand = new Pen(new SolidColorBrush(_on ? Colors.White : c), 1.5, lineCap: PenLineCap.Round);
        ctx.DrawLine(hand, centre, centre + new Vector(0, -3.8));
        ctx.DrawLine(hand, centre, centre + new Vector(2.8, 1.4));
    }
}

/// <summary>
/// A compact time field ("17:30"): click the hours or the minutes, then the arrow keys or the wheel change them
/// (minutes in fives); digits can be typed too.
/// </summary>
public sealed class TimeField : SpringControl
{
    private TimeSpan _value = TimeSpan.FromHours(9);
    private int _part;          // 0 hours, 1 minutes
    private string _typed = "";

    public TimeField(IClock clock) : base(clock)
    {
        Width = 64; Height = 24; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Click the hours or minutes, then use the arrow keys or the wheel");
    }

    public TimeSpan Value { get => _value; set { _value = new TimeSpan(((value.Hours % 24) + 24) % 24, value.Minutes, 0); InvalidateVisual(); } }
    public int Part => _part;

    /// One step up or down: an hour, or five minutes (wrapping within the hour, as a clock field does).
    public void Step(int dir)
    {
        _typed = "";
        if (_part == 0) Value = new TimeSpan((_value.Hours + dir + 24) % 24, _value.Minutes, 0);
        else
        {
            int m = (_value.Minutes / 5 + dir) * 5;
            Value = new TimeSpan(_value.Hours, (m % 60 + 60) % 60, 0);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _part = e.GetPosition(this).X < Width / 2 ? 0 : 1;
        _typed = "";
        Focus();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { base.OnPointerWheelChanged(e); Step(e.Delta.Y > 0 ? 1 : -1); e.Handled = true; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Up: Step(1); e.Handled = true; break;
            case Key.Down: Step(-1); e.Handled = true; break;
            case Key.Left: _part = 0; _typed = ""; InvalidateVisual(); e.Handled = true; break;
            case Key.Right: _part = 1; _typed = ""; InvalidateVisual(); e.Handled = true; break;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Text is not { Length: 1 } t || !char.IsDigit(t[0])) return;
        _typed += t;
        int n = int.Parse(_typed);
        if (_part == 0) { if (n < 24) Value = new TimeSpan(n, _value.Minutes, 0); if (_typed.Length == 2 || n > 2) { _part = 1; _typed = ""; } }
        else { if (n < 60) Value = new TimeSpan(_value.Hours, n, 0); if (_typed.Length == 2) _typed = ""; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFocusedProperty) InvalidateVisual();   // the accent edge and the selected part
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05)), new Pen(new SolidColorBrush(IsFocused ? LT.AccentColor : LT.Border), 1), new Rect(0.5, 0.5, Width - 1, Height - 1), 6, 6);
        FormattedText hh = Mono(_value.Hours.ToString("00")), mm = Mono(_value.Minutes.ToString("00")), colon = Mono(":");
        double total = hh.Width + colon.Width + mm.Width, x = Width / 2 - total / 2, y = Height / 2 - hh.Height / 2;
        if (IsFocused)
        {
            var sel = _part == 0 ? new Rect(x - 2, 4, hh.Width + 4, Height - 8) : new Rect(x + hh.Width + colon.Width - 2, 4, mm.Width + 4, Height - 8);
            ctx.DrawRectangle(new SolidColorBrush(LT.AccentColor.WithOpacity(0.25)), null, sel, 3, 3);
        }
        ctx.DrawText(hh, new Point(x, y));
        ctx.DrawText(colon, new Point(x + hh.Width, y));
        ctx.DrawText(mm, new Point(x + hh.Width + colon.Width, y));
    }

    private static FormattedText Mono(string s) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Mono), LT.TextSize(11), new SolidColorBrush(s == ":" ? LT.Muted : LT.Text));
}

/// <summary>
/// The list: carried-over tasks first ("from Fri"), then today's, then "Clear completed". Rows glide to their
/// places on the snappy spring and fade in and out (the Mac's blurFade without the blur); it scrolls on a spring.
/// </summary>
public sealed class TaskList : Panel
{
    public const double RowH = 36, Gap = 2;
    private readonly Tasks _tasks;
    private readonly IClock _clock;
    private readonly Dictionary<Guid, TaskRow> _rows = new();
    private readonly List<TaskRow> _leaving = new();
    private readonly SpringValue _scroll = new(0, 0.05);
    private readonly StackPanel _empty;
    private readonly LinkButton _clear;
    private readonly SpringValue _clearY = new(0, 0.05);
    private bool _ticking;

    public TaskList(Tasks tasks, IClock clock)
    {
        _tasks = tasks; _clock = clock;
        ClipToBounds = true; Background = Brushes.Transparent;
        _empty = new StackPanel
        {
            Spacing = 6, Height = 110, VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new Control { Height = 14 },
                new IconView(IconKind.Today, 20, LT.Faint) { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Plan your day", FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Tasks with a time remind you when they're due", FontFamily = LT.Ui, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Faint), HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        _clear = new LinkButton(clock, "Clear completed", () => _tasks.ClearDone());
        Children.Add(_empty);
        Children.Add(_clear);
    }

    public IEnumerable<TaskRow> Rows => Order().Select(t => _rows[t.Id]);
    public TaskRow? RowFor(DayTask t) => _rows.GetValueOrDefault(t.Id);
    public bool ShowingEmpty => _empty.IsVisible;
    public LinkButton ClearButton => _clear;

    private IEnumerable<DayTask> Order() => _tasks.Carried.Concat(_tasks.Today);

    public void Sync(bool animate)
    {
        double now = _clock.Now;
        var list = Order().ToList();
        var live = list.Select(t => t.Id).ToHashSet();
        foreach (var (id, row) in _rows.Where(kv => !live.Contains(kv.Key)).ToList())
        {
            _rows.Remove(id);
            if (animate) { row.Leave(now); _leaving.Add(row); } else Children.Remove(row);
        }
        var carried = _tasks.Carried.Select(t => t.Id).ToHashSet();
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            double y = i * (RowH + Gap);
            if (!_rows.TryGetValue(t.Id, out var row))
            {
                row = new TaskRow(t, _tasks, _clock, () => Animate());
                _rows[t.Id] = row;
                Children.Add(row);
                row.Place(y, now, animate: false);
                if (animate) row.Arrive(now);
            }
            else row.Place(y, now, animate);
            row.Carried = carried.Contains(t.Id);
            row.Refresh(animate);
        }
        _empty.IsVisible = list.Count == 0;
        _clear.IsVisible = _tasks.DoneCount > 0;
        double cy = list.Count * (RowH + Gap);
        if (animate) _clearY.AnimateTo(cy, LT.Snappy, now); else _clearY.Snap(cy);
        Animate();
    }

    private double ContentHeight => Order().Count() * (RowH + Gap) + (_tasks.DoneCount > 0 ? 24 : 0);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _scroll.AnimateTo(Math.Clamp(_scroll.Target - e.Delta.Y * 60, 0, Math.Max(0, ContentHeight - Bounds.Height)), SpringSpec.Response(0.3, 1.0), _clock.Now);
        Animate();
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 389 : available.Width;
        foreach (var c in Children) c.Measure(new Size(w, c is TaskRow ? RowH : double.PositiveInfinity));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in Children)
        {
            if (c is TaskRow) c.Arrange(new Rect(0, 0, final.Width, RowH));
            else if (c == _clear) c.Arrange(new Rect(final.Width - c.DesiredSize.Width, 0, c.DesiredSize.Width, 22));
            else c.Arrange(new Rect(0, 0, final.Width, c.DesiredSize.Height));
        }
        Place();
        return final;
    }

    private void Place()
    {
        double now = _clock.Now, s = _scroll.Value(now);
        foreach (var r in Children.OfType<TaskRow>()) r.Apply(now, -s);
        _clear.RenderTransform = new TranslateTransform(0, _clearY.Value(now) + 2 - s);
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
            foreach (var gone in _leaving.Where(r => r.Gone(now)).ToList()) { _leaving.Remove(gone); Children.Remove(gone); }
            Place();
            if (!_scroll.IsSettled(now) || !_clearY.IsSettled(now) || Children.OfType<TaskRow>().Any(r => r.Moving(now))) Animate();
        });
    }
}

/// <summary>
/// One task (the Mac's TaskRow): a round check (accent, with a tick, once done), the title (struck through and faint
/// once done), "from Fri" if carried over, its time (red once overdue), and on hover: move to today, delete.
/// </summary>
public sealed class TaskRow : Control, Avalonia.Rendering.ICustomHitTest
{
    private readonly DayTask _t;
    private readonly Tasks _tasks;
    private readonly IClock _clock;
    private readonly Action _animate;
    private readonly SpringValue _y = new(0, 0.05), _appear = new(1, 0.002), _hover = new(0, 0.002), _done = new(0, 0.002), _hotMove = new(0, 0.002), _hotDelete = new(0, 0.002);
    private bool _leaving;
    private string? _down;

    public TaskRow(DayTask t, Tasks tasks, IClock clock, Action animate)
    {
        _t = t; _tasks = tasks; _clock = clock; _animate = animate;
        Height = TaskList.RowH; Cursor = new Cursor(StandardCursorType.Hand);
        _done.Snap(t.Done ? 1 : 0);
    }

    public DayTask Task => _t;
    public bool Carried { get; set; }
    public double Y => _y.Value(_clock.Now);
    public bool HitTest(Point p) => !_leaving && new Rect(Bounds.Size).Contains(p);

    public void Place(double y, double now, bool animate) { if (animate) _y.AnimateTo(y, LT.Snappy, now); else _y.Snap(y); }
    public void Arrive(double now) { _appear.Snap(0); _appear.AnimateTo(1, LT.Snappy, now); }
    public void Leave(double now) { _leaving = true; IsHitTestVisible = false; _appear.AnimateTo(0, LT.Snappy, now); }
    public bool Gone(double now) => _leaving && _appear.Value(now) < 0.02;
    public bool Moving(double now) => !_y.IsSettled(now) || !_appear.IsSettled(now) || !_hover.IsSettled(now) || !_done.IsSettled(now) || !_hotMove.IsSettled(now) || !_hotDelete.IsSettled(now);
    public void Refresh(bool animate) { if (animate) _done.AnimateTo(_t.Done ? 1 : 0, LT.Snappy, _clock.Now); else _done.Snap(_t.Done ? 1 : 0); InvalidateVisual(); }

    public void Apply(double now, double scroll)
    {
        double a = Math.Clamp(_appear.Value(now), 0, 1);
        Opacity = a;
        RenderTransform = new TranslateTransform(0, _y.Value(now) + scroll + (1 - a) * 4);
        if (Moving(now)) InvalidateVisual();
    }

    // zones: the check circle, the two hover actions, the rest
    private Point CheckCentre => new(12 + 8, Bounds.Height / 2);
    private Rect DeleteRect => new(Bounds.Width - 12 - 22, Bounds.Height / 2 - 10, 22, 20);
    private Rect MoveRect => new(Bounds.Width - 12 - 22 - 2 - 22, Bounds.Height / 2 - 10, 22, 20);
    private string Zone(Point p) =>
        _hover.Target > 0.5 && DeleteRect.Inflate(2).Contains(p) ? "delete" :
        _hover.Target > 0.5 && Carried && MoveRect.Inflate(2).Contains(p) ? "move" :
        Vector.Distance(p, CheckCentre) <= 12 ? "check" : "row";

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover.AnimateTo(1, LT.Hover, _clock.Now); _animate(); }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover.AnimateTo(0, LT.Hover, _clock.Now); _hotMove.AnimateTo(0, LT.Hover, _clock.Now); _hotDelete.AnimateTo(0, LT.Hover, _clock.Now); _animate();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var z = Zone(e.GetPosition(this));
        _hotMove.AnimateTo(z == "move" ? 1 : 0, LT.Hover, _clock.Now);
        _hotDelete.AnimateTo(z == "delete" ? 1 : 0, LT.Hover, _clock.Now);
        _animate();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); _down = Zone(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var z = Zone(e.GetPosition(this));
        if (z == _down) Click(z);
        _down = null;
    }

    /// "check" (or anywhere on the row) ticks it; "move" brings a carried task to today; "delete" removes it.
    public void Click(string zone)
    {
        switch (zone)
        {
            case "delete": _tasks.Delete(_t); break;
            case "move": _tasks.MoveToToday(_t); break;
            default: _tasks.Toggle(_t); break;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double now = _clock.Now, h = Math.Clamp(_hover.Value(now), 0, 1), d = Math.Clamp(_done.Value(now), 0, 1);
        double w = Bounds.Width, cy = Bounds.Height / 2;
        if (h > 0.005) ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05 * h)), null, new Rect(Bounds.Size), 8, 8);
        // the check: a ring, filling with the accent and a tick once done
        double r = 8 * Prefs.Shared.IconScale;
        var c = CheckCentre;
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Mix(LT.BorderHi, LT.AccentColor, d)), 1.5), c, r - 0.75, r - 0.75);
        if (d > 0.01)
        {
            ctx.DrawEllipse(new SolidColorBrush(LT.AccentColor.WithOpacity(d)), null, c, (r - 3) * (0.6 + 0.4 * d), (r - 3) * (0.6 + 0.4 * d));
            using (ctx.PushOpacity(d)) Icons.Draw(ctx, IconKind.Check, c, 9 * (0.7 + 0.3 * d), Colors.White);
        }
        // the right side: "from Fri", the time, the hover actions
        double right = w - 12;
        if (h > 0.01)
        {
            using (ctx.PushOpacity(h))
            {
                double hd = Math.Clamp(_hotDelete.Value(now), 0, 1);
                ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.08 * hd)), null, DeleteRect, 4, 4);
                Icons.Draw(ctx, IconKind.Trash, DeleteRect.Center, 11, Mix(LT.Muted, LT.Text, hd));
                if (Carried)
                {
                    double hm = Math.Clamp(_hotMove.Value(now), 0, 1);
                    ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.08 * hm)), null, MoveRect, 4, 4);
                    Icons.Draw(ctx, IconKind.Chevron, MoveRect.Center, 10, Mix(LT.Muted, LT.Text, hm));
                }
            }
            right -= h * (Carried ? 46 : 22) + h * 6;
        }
        if (_t.Time is { } time)
        {
            bool overdue = !_t.Done && time < Wall.Now();
            var tt = new FormattedText(Tasks.ShortTime(time), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Mono), LT.TextSize(11), new SolidColorBrush(overdue ? LT.Red : LT.Muted));
            right -= tt.Width;
            ctx.DrawText(tt, new Point(right, cy - tt.Height / 2));
            right -= 6;
        }
        if (Carried)
        {
            var from = new FormattedText($"from {_t.Day:ddd}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.Medium), LT.TextSize(10), new SolidColorBrush(LT.Faint));
            right -= from.Width;
            ctx.DrawText(from, new Point(right, cy - from.Height / 2));
            right -= 6;
        }
        // the title: faint and struck through once done
        double x = c.X + r + 10;
        var title = new FormattedText(_t.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui), LT.TextSize(12.5), new SolidColorBrush(Mix(LT.Text, LT.Faint, d)))
        { MaxTextWidth = Math.Max(10, right - x), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        ctx.DrawText(title, new Point(x, cy - title.Height / 2));
        if (d > 0.01)
        {
            double len = title.WidthIncludingTrailingWhitespace * d;
            ctx.DrawLine(new Pen(new SolidColorBrush(LT.Faint), 1), new Point(x, cy + 0.5), new Point(x + len, cy + 0.5));
        }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

/// A quiet text button ("Clear completed"): faint, brighter on hover.
public sealed class LinkButton : SpringControl
{
    private readonly string _title;
    private readonly Action _action;
    private readonly SpringValue _hover;
    public LinkButton(IClock clock, string title, Action action) : base(clock)
    {
        _title = title; _action = action; _hover = Spring(0);
        Height = 22; Cursor = new Cursor(StandardCursorType.Hand);
    }
    protected override Size MeasureOverride(Size available) => new(Text(_title, 10.5, LT.Faint).Width + 4, 22);
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _action(); }
    public override void Render(DrawingContext ctx)
    {
        var t = Text(_title, 10.5, Mix(LT.Faint, LT.Muted, V(_hover.Value(Now))));
        ctx.DrawText(t, new Point(2, 11 - t.Height / 2));
    }
}
