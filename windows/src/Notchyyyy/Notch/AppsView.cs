using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Apps;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// The Apps tab (the Mac app's AppsView): a shelf of pinned apps (click one to open it), Edit to remove or reorder
/// them (they wiggle, each with a ×, and can be dragged), and Add apps for a picker of every Start-menu app.
/// </summary>
public sealed class AppsView : Panel
{
    /// Closes the notch after an app is opened (set by the shell).
    public static Action? Collapse { get; set; }

    private readonly Favorites _fav;
    private readonly IClock _clock;
    private readonly BlurSwap _swap = new();
    private readonly Grid _shelf;
    private readonly AppPicker _picker;
    private readonly AppGrid _grid;
    private readonly TextBlock _label;
    private readonly PillButton _add, _edit;
    private bool _editing = Debug.Has("--apps-edit"), _ticking;

    public AppsView(NotchModel m, IClock clock)
    {
        _fav = m.Apps; _clock = clock;
        _label = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Muted), VerticalAlignment = VerticalAlignment.Center };
        _add = new PillButton(clock, "Add apps", IconKind.Plus, () => ShowPicker(true));
        _edit = new PillButton(clock, "Edit", IconKind.Pencil, () => SetEditing(!_editing));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _add, _edit } };
        _grid = new AppGrid(_fav, clock, () => ShowPicker(true));
        _shelf = new Grid { RowDefinitions = new RowDefinitions("26,12,*"), Children = { _label, buttons } };
        Avalonia.Controls.Grid.SetRow(_grid, 2);
        _shelf.Children.Add(_grid);
        _picker = new AppPicker(_fav, clock, () => ShowPicker(false));
        Children.Add(_swap);
        bool picking = Debug.Has("--apps-picker");
        _swap.Show(picking ? _picker : _shelf, clock.Now, animate: false);
        if (picking) _fav.ScanInBackground();
        SetEditing(_editing, animate: false);
    }

    public AppGrid Grid => _grid;
    public AppPicker Picker => _picker;
    public PillButton AddButton => _add;
    public PillButton EditButton => _edit;
    public bool Editing => _editing;
    public bool ShowingPicker => ReferenceEquals(_swap.Current, _picker);
    public string Label => _label.Text ?? "";

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _fav.Changed += OnChanged;
        Refresh(false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _fav.Changed -= OnChanged;
        if (_editing) SetEditing(false, animate: false);   // leaving the tab ends editing, as on the Mac
    }

    private void OnChanged(string what, string? id)
    {
        if (what == "Icon") { _grid.IconLoaded(id); _picker.IconLoaded(id); return; }
        if (what == "Scanned") _picker.Rebuild();
        Refresh(true);
    }

    private void Refresh(bool animate)
    {
        if (_fav.Ids.Count == 0 && _editing) SetEditing(false, animate);
        _label.Text = _editing ? "Click × to remove · drag to reorder" : $"{_fav.Ids.Count} pinned";
        _grid.Sync(animate);
        _picker.RefreshMarks();
    }

    public void SetEditing(bool on, bool animate = true)
    {
        _editing = on;
        _add.IsVisible = !on;
        _edit.Prominent = on;
        _edit.SetTitle(on ? "Done" : "Edit", on ? IconKind.Check : IconKind.Pencil);
        _grid.SetEditing(on, animate);
        _label.Text = on ? "Click × to remove · drag to reorder" : $"{_fav.Ids.Count} pinned";
    }

    public void ShowPicker(bool on)
    {
        if (on == ShowingPicker) return;
        if (on) { SetEditing(false); _fav.ScanInBackground(); _picker.Reset(); }
        _swap.Show(on ? _picker : _shelf, _clock.Now, animate: true);
        Tick();
    }

    private void Tick()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            double now = _clock.Now;
            _swap.Update(now);
            if (_swap.Animating(now)) Tick();
            else if (ShowingPicker) _picker.FocusSearch();
        });
    }
}

/// <summary>
/// The pinned apps in a grid (fixed 66-pt tiles, as many columns as fit, centred). Tiles move to their places on the
/// snappy spring, grow in and shrink away; the grid scrolls on a spring with the wheel. While editing, a tile can be
/// dragged to a new place, the others making room as it goes.
/// </summary>
public sealed class AppGrid : Panel
{
    public const double Spacing = 16, RowSpacing = 14, Top = 8;
    private readonly Favorites _fav;
    private readonly IClock _clock;
    private readonly Action _openPicker;
    private readonly Dictionary<string, AppTile> _tiles = new();
    private readonly List<AppTile> _leaving = new();
    private readonly AppTile _addTile;
    private readonly SpringValue _scroll = new(0, 0.05);
    private bool _ticking, _editing;
    private AppTile? _dragging;
    private Point _dragFrom;
    private Vector _dragOffset;

    public AppGrid(Favorites fav, IClock clock, Action openPicker)
    {
        _fav = fav; _clock = clock; _openPicker = openPicker;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        _addTile = new AppTile(null, fav, clock, () => Animate()) { Action = openPicker };
        Children.Add(_addTile);
    }

    public static double TileSize => 66 * Prefs.Shared.IconScale;
    public static double TileHeight => TileSize * 1.15;
    public int Columns => Math.Max(4, (int)((624 + Spacing) / (TileSize + Spacing)));
    public IEnumerable<AppTile> Tiles => _fav.Ids.Where(_tiles.ContainsKey).Select(id => _tiles[id]);
    public AppTile AddTile => _addTile;
    public AppTile? TileFor(string id) => _tiles.GetValueOrDefault(id);
    public double ScrollNow => _scroll.Value(_clock.Now);
    public bool Dragging => _dragging is not null;

    /// Where the tile at <paramref name="i"/> sits (before scrolling), centred as SwiftUI centres a fixed-column grid.
    public Point Slot(int i)
    {
        int cols = Columns;
        double width = cols * TileSize + (cols - 1) * Spacing;
        double x0 = Math.Max(0, (Bounds.Width > 0 ? Bounds.Width : 624) - width) / 2;
        return new Point(x0 + i % cols * (TileSize + Spacing), Top + i / cols * (TileHeight + RowSpacing));
    }

    private double ContentHeight => Top + ((_fav.Ids.Count + (_editing ? 0 : 1) + Columns - 1) / Columns) * (TileHeight + RowSpacing);
    private double MaxScroll => Math.Max(0, ContentHeight - Bounds.Height);

    public void Sync(bool animate)
    {
        double now = _clock.Now;
        var live = _fav.Ids.ToHashSet();
        foreach (var (id, tile) in _tiles.Where(kv => !live.Contains(kv.Key)).ToList())
        {
            _tiles.Remove(id);
            if (animate) { tile.Leave(now); _leaving.Add(tile); } else Children.Remove(tile);
        }
        for (int i = 0; i < _fav.Ids.Count; i++)
        {
            var id = _fav.Ids[i];
            if (!_tiles.TryGetValue(id, out var tile))
            {
                tile = new AppTile(id, _fav, _clock, () => Animate());
                tile.Action = () => Open(id);
                tile.SetEditing(_editing, animate: false);
                _tiles[id] = tile;
                Children.Add(tile);
                tile.Place(Slot(i), now, animate: false);
                if (animate) tile.Arrive(now);
            }
            else if (tile != _dragging) tile.Place(Slot(i), now, animate);
        }
        _addTile.Place(Slot(_fav.Ids.Count), now, animate);
        _addTile.Show(!_editing, now, animate);
        if (animate) _scroll.AnimateTo(Math.Clamp(_scroll.Target, 0, MaxScroll), LT.Snappy, now); else _scroll.Snap(Math.Clamp(_scroll.Target, 0, MaxScroll));
        Animate();
    }

    public void SetEditing(bool on, bool animate)
    {
        _editing = on;
        foreach (var t in _tiles.Values) t.SetEditing(on, animate);
        Sync(animate);
    }

    public void IconLoaded(string? id) { if (id is not null && _tiles.TryGetValue(id, out var t)) t.InvalidateVisual(); }

    private (string Id, double At)? _opening;

    /// A quick press (0.12 s, on the app's clock), then the app opens and the notch closes.
    private void Open(string id)
    {
        _opening = (id, _clock.Now + 0.12);
        Animate();
    }

    private void OpenIfDue(double now)
    {
        if (_opening is not { } o || now < o.At) return;
        _opening = null;
        _fav.Launch(o.Id);
        AppsView.Collapse?.Invoke();
    }

    // ---- dragging to reorder (editing only) ----

    internal void BeginDrag(AppTile tile, PointerPressedEventArgs e)
    {
        if (!_editing || tile.Id is null) return;
        _dragging = tile;
        _dragFrom = e.GetPosition(this);
        _dragOffset = default;
        e.Pointer.Capture(tile);
        tile.Lift(true, _clock.Now);
        Animate();
    }

    internal void DragTo(AppTile tile, PointerEventArgs e)
    {
        if (_dragging != tile) return;
        var p = e.GetPosition(this);
        _dragOffset = p - _dragFrom;
        // the slot under the tile's centre
        int index = _fav.Ids.ToList().IndexOf(tile.Id!);
        var home = Slot(index);
        var centre = new Point(home.X + TileSize / 2 + _dragOffset.X, home.Y + TileHeight / 2 + _dragOffset.Y + _scroll.Value(_clock.Now));
        int cols = Columns;
        var s0 = Slot(0);
        int col = Math.Clamp((int)Math.Floor((centre.X - s0.X + Spacing / 2) / (TileSize + Spacing)), 0, cols - 1);
        int row = Math.Max(0, (int)Math.Floor((centre.Y - Top + RowSpacing / 2) / (TileHeight + RowSpacing)));
        int target = Math.Clamp(row * cols + col, 0, _fav.Ids.Count - 1);
        if (target != index)
        {
            // keep the tile under the pointer: its home slot moves, so its offset from that slot changes
            var newHome = Slot(target);
            _dragFrom += newHome - home;
            _dragOffset = p - _dragFrom;
            _fav.Move(tile.Id!, target);
        }
        tile.DragOffset = _dragOffset;
        Animate();
    }

    internal void EndDrag(AppTile tile, PointerReleasedEventArgs e)
    {
        if (_dragging != tile) return;
        e.Pointer.Capture(null);
        _dragging = null;
        int index = _fav.Ids.ToList().IndexOf(tile.Id!);
        // drop: from where it is now into its slot, on the snappy spring
        var at = tile.Current(_clock.Now);
        tile.DragOffset = default;
        tile.Place(at, _clock.Now, animate: false);
        tile.Place(Slot(index), _clock.Now, animate: true);
        tile.Lift(false, _clock.Now);
        Animate();
    }

    // ---- layout and frames ----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _scroll.AnimateTo(Math.Clamp(_scroll.Target - e.Delta.Y * 60, 0, MaxScroll), SpringSpec.Response(0.3, 1.0), _clock.Now);
        Animate();
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(new Size(TileSize, TileHeight));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in Children) c.Arrange(new Rect(0, 0, TileSize, TileHeight));
        // the first layout: the slots are only known now
        double now = _clock.Now;
        for (int i = 0; i < _fav.Ids.Count; i++) if (_tiles.TryGetValue(_fav.Ids[i], out var t) && !t.Placed) t.Place(Slot(i), now, animate: false);
        if (!_addTile.Placed) _addTile.Place(Slot(_fav.Ids.Count), now, animate: false);
        Place();
        return final;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); Sync(animate: false); }

    private void Place()
    {
        double now = _clock.Now, s = _scroll.Value(now);
        foreach (var t in Children.OfType<AppTile>()) t.Apply(now, -s);
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
            foreach (var gone in _leaving.Where(t => t.Gone(now)).ToList()) { _leaving.Remove(gone); Children.Remove(gone); }
            OpenIfDue(now);
            Place();
            if (_editing || _opening is not null || !_scroll.IsSettled(now) || Children.OfType<AppTile>().Any(t => t.Moving(now))) Animate();
        });
    }
}

/// <summary>
/// One app on the shelf (the Mac's AppTile), or the dashed Add tile. Hover lifts the icon (1.1×, 2 pt up) on a soft
/// wash; a press dips it (0.88×) before the app opens. While editing it wiggles, wears a × to remove it, and drags.
/// </summary>
public sealed class AppTile : Control, Avalonia.Rendering.ICustomHitTest
{
    private readonly Favorites _fav;
    private readonly IClock _clock;
    private readonly Action _animate;
    private readonly SpringValue _x = new(0, 0.05), _y = new(0, 0.05), _appear = new(1, 0.002), _hover = new(0, 0.002),
        _press = new(0, 0.002), _edit = new(0, 0.002), _badgeHover = new(0, 0.002), _shown = new(1, 0.002), _lift = new(0, 0.002);
    private readonly double _seed = Random.Shared.NextDouble();
    private bool _editing, _leaving, _downOnBadge;

    public AppTile(string? id, Favorites fav, IClock clock, Action animate)
    {
        Id = id; _fav = fav; _clock = clock; _animate = animate;
        Width = AppGrid.TileSize; Height = AppGrid.TileHeight;
        Cursor = new Cursor(StandardCursorType.Hand);
        if (id is not null) ToolTip.SetTip(this, fav.Name(id));
    }

    public string? Id { get; }
    public Action? Action { get; set; }
    public bool Placed { get; private set; }
    public Vector DragOffset { get; set; }
    public bool Hovered => _hover.Target > 0.5;
    public int Opens { get; private set; }

    private double Size => AppGrid.TileSize;
    private double IconSize => Size * 0.71;
    private Rect IconRect => new((Size - IconSize) / 2, (AppGrid.TileHeight - (IconSize + 6 + 12)) / 2, IconSize, IconSize);
    private Point BadgeCentre => new(IconRect.Left - 5 + 9, IconRect.Top - 5 + 9);
    private bool OnBadge(Point p) => _editing && Id is not null && Vector.Distance(p, BadgeCentre) <= 12;

    public bool HitTest(Point p) => !_leaving && _shown.Target > 0.5 && (new Rect(0, 0, Size, AppGrid.TileHeight).Contains(p) || OnBadge(p));

    // ---- placement ----

    public void Place(Point slot, double now, bool animate)
    {
        Placed = true;
        if (animate) { _x.AnimateTo(slot.X, LT.Snappy, now); _y.AnimateTo(slot.Y, LT.Snappy, now); }
        else { _x.Snap(slot.X); _y.Snap(slot.Y); }
    }
    public Point Current(double now) => new(_x.Value(now) + DragOffset.X, _y.Value(now) + DragOffset.Y);
    public void Arrive(double now) { _appear.Snap(0); _appear.AnimateTo(1, LT.Snappy, now); }
    public void Leave(double now) { _leaving = true; IsHitTestVisible = false; _appear.AnimateTo(0, LT.Snappy, now); }
    public bool Gone(double now) => _leaving && _appear.Value(now) < 0.02;
    public void Show(bool on, double now, bool animate) { IsHitTestVisible = on; if (animate) _shown.AnimateTo(on ? 1 : 0, LT.Snappy, now); else _shown.Snap(on ? 1 : 0); }
    public void SetEditing(bool on, bool animate)
    {
        _editing = on;
        if (animate) _edit.AnimateTo(on ? 1 : 0, LT.Snappy, _clock.Now); else _edit.Snap(on ? 1 : 0);
        if (on) _hover.Snap(0);
    }
    public void Lift(bool on, double now) => _lift.AnimateTo(on ? 1 : 0, LT.Snappy, now);

    public bool Moving(double now) => !_x.IsSettled(now) || !_y.IsSettled(now) || !_appear.IsSettled(now) || !_hover.IsSettled(now)
        || !_press.IsSettled(now) || !_edit.IsSettled(now) || !_badgeHover.IsSettled(now) || !_shown.IsSettled(now) || !_lift.IsSettled(now);

    /// This frame's transform: its place (plus a drag), the scale of arriving / leaving / lifting, and the wiggle.
    public void Apply(double now, double scroll)
    {
        double a = Math.Clamp(_appear.Value(now), 0, 1.2) * Math.Clamp(_shown.Value(now), 0, 1.2);
        double s = (0.6 + 0.4 * a) * (1 + 0.08 * _lift.Value(now));
        double e = Math.Clamp(_edit.Value(now), 0, 1);
        double wiggle = e * Math.Sin(now * 14 + _seed * 6) * 1.8 * (1 - Math.Clamp(_lift.Value(now), 0, 1));
        var c = new Point(Size / 2, AppGrid.TileHeight / 2);
        RenderTransform = new MatrixTransform(
            Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(wiggle * Math.PI / 180) * Matrix.CreateScale(s, s)
            * Matrix.CreateTranslation(c.X + _x.Value(now) + DragOffset.X, c.Y + _y.Value(now) + DragOffset.Y + scroll));
        Opacity = Math.Clamp(a, 0, 1);
        ZIndex = _lift.Target > 0.5 ? 10 : 0;
        if (Moving(now) || _editing) InvalidateVisual();
    }

    // ---- pointer ----

    private AppGrid? Grid => Parent as AppGrid;

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); if (!_editing) _hover.AnimateTo(1, LT.Hover, _clock.Now); _animate(); }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover.AnimateTo(0, LT.Hover, _clock.Now); _badgeHover.AnimateTo(0, LT.Hover, _clock.Now); _animate();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _badgeHover.AnimateTo(OnBadge(p - DragOffset) ? 1 : 0, LT.Hover, _clock.Now);
        Grid?.DragTo(this, e);
        _animate();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        _downOnBadge = OnBadge(e.GetPosition(this));
        if (_editing && !_downOnBadge) { Grid?.BeginDrag(this, e); return; }
        if (!_editing) { _press.AnimateTo(1, LT.Hover, _clock.Now); _animate(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        if (Grid?.Dragging == true) { Grid.EndDrag(this, e); return; }
        if (_editing)
        {
            if (_downOnBadge && OnBadge(p) && Id is not null) _fav.Remove(Id);
            return;
        }
        _press.AnimateTo(0, LT.Hover, _clock.Now);
        _animate();
        if (new Rect(0, 0, Size, AppGrid.TileHeight).Contains(p)) { Opens++; Action?.Invoke(); }
    }

    // ---- drawing ----

    public override void Render(DrawingContext ctx)
    {
        double now = _clock.Now, h = Math.Clamp(_hover.Value(now), 0, 1.1), p = Math.Clamp(_press.Value(now), 0, 1.1);
        double e = Math.Clamp(_edit.Value(now), 0, 1);
        double hv = h * (1 - e);
        if (hv > 0.005) ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05 * Math.Min(1, hv))), null, new Rect(0, 0, Size, AppGrid.TileHeight), 10, 10);
        var icon = IconRect;
        double scale = (1 + 0.1 * hv) * (1 - 0.12 * p);
        var c = icon.Center + new Vector(0, -2 * hv);
        var r = new Rect(c.X - icon.Width * scale / 2, c.Y - icon.Height * scale / 2, icon.Width * scale, icon.Height * scale);
        if (Id is null)
        {
            // the Add tile: a dashed rounded square with a plus
            var dash = new Pen(new SolidColorBrush(Mix(LT.Border, LT.BorderHi, Math.Min(1, h))), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            ctx.DrawRectangle(null, dash, new RoundedRect(r.Deflate(0.5), 12));
            Icons.Draw(ctx, IconKind.Plus, r.Center, 15 * Prefs.Shared.IconScale, Mix(LT.Muted, LT.Text, Math.Min(1, h)));
        }
        else if (_fav.Icon(Id) is { } bmp) ctx.DrawImage(bmp, new Rect(bmp.Size), r);
        else ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.06)), null, r, 10, 10);   // until its icon is in
        var name = new FormattedText(Id is null ? "Add" : _fav.Name(Id), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(LT.Ui), LT.TextSize(10), new SolidColorBrush(Id is null ? LT.Muted : Mix(LT.Muted, LT.Text, Math.Min(1, hv))))
        { MaxTextWidth = Size, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        ctx.DrawText(name, new Point(0, icon.Bottom + 6));
        // the remove badge, growing in at the icon's top-left corner while editing
        if (Id is not null && e > 0.01)
        {
            double bh = Math.Clamp(_badgeHover.Value(now), 0, 1), br = 9 * (0.3 + 0.7 * e) * (1 + 0.12 * bh);
            var bc = BadgeCentre;
            using (ctx.PushOpacity(e))
            {
                ctx.DrawEllipse(new SolidColorBrush(Colors.Black.WithOpacity(0.3)), null, bc + new Vector(0, 1), br + 0.5, br + 0.5);
                ctx.DrawEllipse(new SolidColorBrush(Mix(Color.FromRgb(77, 77, 77), LT.Red, bh)), new Pen(new SolidColorBrush(Colors.White.WithOpacity(0.25)), 1), bc, br - 0.5, br - 0.5);
                Icons.Draw(ctx, IconKind.Close, bc, 9 * br / 9, Colors.White);
            }
        }
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

/// <summary>
/// The in-notch app picker (the Mac's AppPicker): a search field and Done, then every Start-menu app in four columns.
/// Click one to pin it (✓, a soft accent wash) or unpin it.
/// </summary>
public sealed class AppPicker : Grid
{
    private readonly Favorites _fav;
    private readonly IClock _clock;
    private readonly TextBox _search;
    private readonly PickerList _list;
    private readonly StackPanel _none;
    private readonly TextBlock _noneText;
    private readonly LiveCanvas _spinner;

    public AppPicker(Favorites fav, IClock clock, Action done)
    {
        _fav = fav; _clock = clock;
        RowDefinitions = new RowDefinitions("32,12,*");
        _search = Fields.Quiet(new TextBox
        {
            PlaceholderText = "Search apps", Height = 32, MinHeight = 32, FontFamily = LT.Ui, FontSize = LT.TextSize(12.5),
            Foreground = new SolidColorBrush(LT.Text), Background = new SolidColorBrush(LT.Surface), BorderBrush = new SolidColorBrush(LT.Border),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(32, 6, 12, 6), VerticalContentAlignment = VerticalAlignment.Center,
        });
        _search.TextChanged += (_, _) => _list?.Filter(_search.Text ?? "");
        _search.KeyDown += (_, e) => { if (e.Key == Key.Escape && (_search.Text ?? "").Length > 0) { _search.Text = ""; e.Handled = true; } };
        var glass = new IconView(IconKind.Search, 12, LT.Faint) { Width = 14, Height = 14, Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        var doneButton = new PillButton(clock, "Done", IconKind.Check, done) { Prominent = true, Margin = new Thickness(10, 0, 0, 0) };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var field = new Panel { Children = { _search, glass } };
        top.Children.Add(field);
        Grid.SetColumn(doneButton, 1);
        top.Children.Add(doneButton);
        Children.Add(top);

        _list = new PickerList(fav, clock);
        SetRow(_list, 2);
        Children.Add(_list);
        _noneText = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted), HorizontalAlignment = HorizontalAlignment.Center };
        _none = new StackPanel
        {
            Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false,
            Children = { new IconView(IconKind.Search, 20, LT.Faint) { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center }, _noneText },
        };
        SetRow(_none, 2);
        Children.Add(_none);
        // a small spinner while the Start menu is read (on the render thread, like everything that keeps moving)
        _spinner = new LiveCanvas(clock, (c, size, now) =>
        {
            using var p = new SkiaSharp.SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 2, StrokeCap = SkiaSharp.SKStrokeCap.Round, Color = Sk.C(LT.Muted) };
            float r = (float)size.Width / 2 - 2;
            using var path = new SkiaSharp.SKPath();
            path.AddArc(new SkiaSharp.SKRect(2, 2, 2 + 2 * r, 2 + 2 * r), (float)(now * 360 % 360), 260);
            c.DrawPath(path, p);
        }) { Width = 16, Height = 16, Animating = _ => !_fav.Scanned, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SetRow(_spinner, 2);
        Children.Add(_spinner);
        Rebuild();
    }

    public TextBox Search => _search;
    public PickerList List => _list;
    public bool ShowingNoMatch => _none.IsVisible;

    public void Reset() { _search.Text = ""; _list.Filter(""); _list.ScrollToTop(); }
    public void FocusSearch() { if (!_search.IsFocused) _search.Focus(); }

    public void Rebuild()
    {
        _spinner.IsVisible = !_fav.Scanned;
        _spinner.Kick();
        _list.Rebuild();
        _list.Filter(_search.Text ?? "");
        UpdateNone();
    }

    public void RefreshMarks() => _list.InvalidateRows();
    public void IconLoaded(string? id) => _list.IconLoaded(id);

    private void UpdateNone()
    {
        var q = (_search.Text ?? "").Trim();
        _none.IsVisible = _fav.Scanned && _list.Visible == 0;
        _noneText.Text = q.Length > 0 ? $"No apps match “{q}”" : "No apps found";
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _list.Filtered += UpdateNone; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _list.Filtered -= UpdateNone; }
}

/// <summary>Every app, four to a row, scrolled on a spring; rows load their icons as they come into view.</summary>
public sealed class PickerList : Panel
{
    public const double RowH = 34, Gap = 6, ColGap = 8;
    private readonly Favorites _fav;
    private readonly IClock _clock;
    private readonly Dictionary<string, PickerRow> _rows = new();
    private List<PickerRow> _shown = new();
    private readonly SpringValue _scroll = new(0, 0.05);
    private bool _ticking;

    public PickerList(Favorites fav, IClock clock) { _fav = fav; _clock = clock; ClipToBounds = true; Background = Brushes.Transparent; }

    public event Action? Filtered;
    public int Visible => _shown.Count;
    public IReadOnlyList<PickerRow> Shown => _shown;
    public double ScrollNow => _scroll.Value(_clock.Now);

    public void Rebuild()
    {
        foreach (var a in _fav.Installed)
            if (!_rows.ContainsKey(a.Id)) { var r = new PickerRow(a, _fav, _clock); _rows[a.Id] = r; Children.Add(r); }
    }

    public void Filter(string query)
    {
        var q = query.Trim();
        _shown = _fav.Installed.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                               .Select(a => _rows[a.Id]).ToList();
        var set = _shown.ToHashSet();
        foreach (var r in _rows.Values) r.IsVisible = set.Contains(r);
        _scroll.Snap(0);
        InvalidateArrange();
        Filtered?.Invoke();
    }

    public void ScrollToTop() => _scroll.Snap(0);
    public void InvalidateRows() { foreach (var r in _rows.Values) r.InvalidateVisual(); }
    public void IconLoaded(string? id) { if (id is not null && _rows.TryGetValue(id, out var r)) r.InvalidateVisual(); }

    private double ColW => (Bounds.Width - 3 * ColGap) / 4;
    private double MaxScroll => Math.Max(0, (_shown.Count + 3) / 4 * (RowH + Gap) - Gap - Bounds.Height);

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 624 : available.Width;
        foreach (var r in _shown) r.Measure(new Size((w - 3 * ColGap) / 4, RowH));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double cw = (final.Width - 3 * ColGap) / 4;
        for (int i = 0; i < _shown.Count; i++)
            _shown[i].Arrange(new Rect(i % 4 * (cw + ColGap), i / 4 * (RowH + Gap), cw, RowH));
        Place();
        return final;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _scroll.AnimateTo(Math.Clamp(_scroll.Target - e.Delta.Y * 60, 0, MaxScroll), SpringSpec.Response(0.3, 1.0), _clock.Now);
        Animate();
        e.Handled = true;
    }

    private void Place()
    {
        double s = _scroll.Value(_clock.Now);
        var t = new TranslateTransform(0, -s);
        foreach (var r in _shown)
        {
            r.RenderTransform = t;
            // only rows in view ask for their icon
            double y = r.Bounds.Y - s;
            r.InView = y > -RowH && y < Bounds.Height;
        }
    }

    private void Animate()
    {
        Place();
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ => { _ticking = false; Place(); if (!_scroll.IsSettled(_clock.Now)) Animate(); });
    }
}

/// <summary>One app in the picker: its icon and name, and a + (or a ✓ on an accent wash once pinned).</summary>
public sealed class PickerRow : SpringControl
{
    private readonly AppEntry _app;
    private readonly Favorites _fav;
    private readonly SpringValue _hover;
    private bool _inView;

    public PickerRow(AppEntry app, Favorites fav, IClock clock) : base(clock)
    {
        _app = app; _fav = fav; _hover = Spring(0);
        Height = PickerList.RowH;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public AppEntry App => _app;
    public bool InView { get => _inView; set { if (_inView == value) return; _inView = value; if (value) InvalidateVisual(); } }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); ToolTip.SetTip(this, (_fav.IsPinned(_app.Id) ? "Unpin " : "Pin ") + _app.Name); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) { _fav.Toggle(_app.Id); InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        bool pinned = _fav.IsPinned(_app.Id);
        var bg = pinned ? LT.AccentColor.WithOpacity(0.1) : LT.Wash.WithOpacity(0.05 * h);
        ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(Bounds.Size), 8, 8);
        double s = 22 * Prefs.Shared.IconScale;
        var icon = new Rect(8, (Bounds.Height - s) / 2, s, s);
        if (_inView && _fav.Icon(_app.Id) is { } bmp) ctx.DrawImage(bmp, new Rect(bmp.Size), icon);
        else ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.06)), null, icon, 5, 5);
        var name = Text(_app.Name, 11.5, LT.Text);
        name.MaxTextWidth = Math.Max(10, Bounds.Width - 8 - s - 8 - 8 - 18); name.MaxLineCount = 1; name.Trimming = TextTrimming.CharacterEllipsis;
        ctx.DrawText(name, new Point(8 + s + 8, Bounds.Height / 2 - name.Height / 2));
        var mark = pinned ? IconKind.CheckCircle : IconKind.PlusCircle;
        Icons.Draw(ctx, mark, new Point(Bounds.Width - 8 - 7, Bounds.Height / 2), 14, pinned ? LT.AccentColor : Mix(LT.Faint, LT.Text, h));
    }
}
