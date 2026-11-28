using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Notchyyyy.Notch;
using Notchyyyy.Theme;

namespace Notchyyyy.Platform;

/// <summary>
/// `--win-selftest --out DIR`: the overlay checked on real Windows (run on GitHub's Windows runners).
/// Moves the real cursor, asks Windows which window is under a point, captures the screen, and writes
/// PASS/FAIL lines to DIR/selftest.txt and screenshots to DIR/*.png. Exits 1 if anything failed.
/// </summary>
public static class WinSelfTest
{
    // ---- Win32 ----
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; }

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }

    /// A real left click where the cursor is (goes through Windows' input queue like a trackpad click).
    static void Click()
    {
        var down = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = 0x0002 } } };
        var up = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = 0x0004 } } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    /// Types text as real Unicode key presses.
    static void Type(string text)
    {
        var list = new List<INPUT>();
        foreach (char ch in text)
        {
            list.Add(new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { scan = ch, flags = 0x0004 } } });
            list.Add(new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { scan = ch, flags = 0x0004 | 0x0002 } } });
        }
        SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// Real key presses: each virtual key down in order, then all up in reverse.
    static void Keys(params ushort[] vks)
    {
        var list = new List<INPUT>();
        foreach (var vk in vks) list.Add(new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { vk = vk } } });
        foreach (var vk in Enumerable.Reverse(vks)) list.Add(new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { vk = vk, flags = 0x0002 } } });
        SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000;

    sealed record Shot(int W, int H, byte[] Bgra)
    {
        public (int r, int g, int b) At(int x, int y) { int i = (y * W + x) * 4; return (Bgra[i + 2], Bgra[i + 1], Bgra[i]); }
    }

    static Shot Capture()
    {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, 0, 0, 0x00CC0020 | 0x40000000);   // SRCCOPY | CAPTUREBLT (includes layered windows)
        var bi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var buf = new byte[w * h * 4];
        GetDIBits(mem, bmp, 0, (uint)h, buf, ref bi, 0);
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        return new Shot(w, h, buf);
    }

    static void Save(Shot s, string path)
    {
        using var wb = new WriteableBitmap(new PixelSize(s.W, s.H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = wb.Lock())
        {
            for (int y = 0; y < s.H; y++)
                Marshal.Copy(s.Bgra, y * s.W * 4, fb.Address + y * fb.RowBytes, s.W * 4);
        }
        wb.Save(path, new PngBitmapEncoderOptions());
    }

    public static async void Run(Shell sh)
    {
        var dir = Theme.Debug.Value("--out") ?? Path.Combine(Path.GetTempPath(), "notchyyyy-winselftest");
        Directory.CreateDirectory(dir);
        var theme = LT.Light ? "light" : "dark";
        var lines = new List<string>();
        void Log(string l) { lines.Add(l); File.WriteAllLines(Path.Combine(dir, $"selftest-{theme}.txt"), lines); Console.WriteLine(l); }
        void Check(string name, bool ok, string detail = "") => Log((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
        Task Wait(double s) => Task.Delay(TimeSpan.FromSeconds(s));

        try
        {
            if (!OperatingSystem.IsWindows()) { Log("SKIP not Windows"); return; }
            await Wait(1.5);
            var w = sh.Window;
            var hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            Check("has a window handle", hwnd != IntPtr.Zero);
            double s = w.RenderScaling;
            int sw = GetSystemMetrics(0), shh = GetSystemMetrics(1);
            GetWindowRect(hwnd, out var rc);
            Log($"INFO screen {sw}x{shh}, scaling {s}, window rect {rc.L},{rc.T} → {rc.R},{rc.B}");

            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            Check("always on top (WS_EX_TOPMOST)", (ex & WS_EX_TOPMOST) != 0);
            Check("off the taskbar and Alt+Tab (WS_EX_TOOLWINDOW, no WS_EX_APPWINDOW)", (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0);
            Check("layered", (ex & WS_EX_LAYERED) != 0);
            Check("click-through while closed (WS_EX_TRANSPARENT)", (ex & WS_EX_TRANSPARENT) != 0);
            Check("visible", IsWindowVisible(hwnd));
            int cx = (rc.L + rc.R) / 2;
            Check("centred horizontally on the screen", Math.Abs(cx - sw / 2) <= 1, $"window centre {cx}, screen centre {sw / 2}");
            Check("flush with the top of the screen", rc.T == 0, $"top {rc.T}");

            int Px(double dip) => (int)Math.Round(dip * s);
            var pill = new POINT { X = cx, Y = rc.T + Px(12) };
            var away = new POINT { X = cx, Y = shh - Px(120) };
            var body = new POINT { X = cx + Px(200), Y = rc.T + Px(200) };
            // a spot of plain background in the open body: below Home's cards, clear of the rounded corner
            var plain = new POINT { X = cx + Px(250), Y = rc.T + Px(288) };

            // closed: the pill is drawn, and clicks pass straight through it
            SetCursorPos(away.X, away.Y);
            await Wait(0.8);
            Check("starts closed", !sh.Model.Expanded);
            var under = GetAncestor(WindowFromPoint(pill), 2);
            Check("closed: a click on the pill goes to the window behind", under != hwnd, $"hwnd under pill {under}, ours {hwnd}");
            var closed = Capture(); Save(closed, Path.Combine(dir, $"{theme}-1-closed.png"));
            var (pr, pg, pb) = closed.At(cx, rc.T + Px(3));
            Check("closed pill is drawn in black", pr + pg + pb <= 6, $"pixel {pr},{pg},{pb}");

            // transparency: with the window hidden, everything outside the pill must look exactly the same
            ShowWindow(hwnd, 0);
            await Wait(0.6);
            var hidden = Capture(); Save(hidden, Path.Combine(dir, $"{theme}-0-without-app.png"));
            ShowWindow(hwnd, 8);   // SW_SHOWNA: show without activating
            await Wait(0.6);
            var shown = Capture();
            int diff = 0, compared = 0;
            for (int y = rc.T; y < Math.Min(rc.B, shh); y += 2)
            for (int x = Math.Max(rc.L, 0); x < Math.Min(rc.R, sw); x += 2)
            {
                bool inPill = Math.Abs(x - cx) <= Px(160) && y - rc.T <= Px(40);
                if (inPill) continue;
                compared++;
                var a = shown.At(x, y); var b = hidden.At(x, y);
                if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 6) diff++;
            }
            Check("transparent: nothing drawn outside the pill (no box, no shadow)", diff == 0, $"{diff} of {compared} pixels differ");

            // real hover: the cursor onto the pill swells it, then it opens
            SetCursorPos(pill.X, pill.Y);
            await Wait(0.06);
            bool hovering = sh.Model.Hovering || sh.Model.Expanded;
            Check("real cursor on the pill is noticed (hover)", hovering);
            await Wait(0.6);
            Check("opens on hover", sh.Model.Expanded);
            await Wait(0.6);
            var open = Capture(); Save(open, Path.Combine(dir, $"{theme}-2-open.png"));
            long ex2 = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            Check("open: takes clicks (no WS_EX_TRANSPARENT)", (ex2 & WS_EX_TRANSPARENT) == 0);
            var underBody = GetAncestor(WindowFromPoint(body), 2);
            Check("open: a click on the body reaches the notch", underBody == hwnd, $"hwnd under body {underBody}");
            var (br, bg, bb) = open.At(plain.X, plain.Y);
            int expect = LT.Light ? 249 : 0;
            Check("open body drawn in the theme's background", Math.Abs(br - expect) <= 3 && Math.Abs(bg - expect) <= 3 && Math.Abs(bb - expect) <= 3, $"pixel {br},{bg},{bb}");

            // text is smoothed in grayscale: no red/blue sub-pixel fringes on the neutral "Home" label
            int fringe = 0;
            for (int y = rc.T + Px(8); y < rc.T + Px(24); y++)
            for (int x = rc.L + Px(14 + 18 + 70); x < rc.L + Px(14 + 18 + 115); x++)
            {
                var (r, g, b) = open.At(x, y);
                fringe = Math.Max(fringe, Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)));
            }
            Check("text has no colour fringes (grayscale smoothing)", fringe <= 16, $"largest channel spread in the Home label: {fringe}");

            // moving inside keeps it open; leaving closes it
            SetCursorPos(body.X, body.Y);
            await Wait(0.8);
            Check("stays open with the cursor inside", sh.Model.Expanded);
            SetCursorPos(away.X, away.Y);
            await Wait(0.25);
            Check("doesn't close the instant the cursor leaves", sh.Model.Expanded);
            await Wait(0.8);
            Check("closes after the cursor leaves", !sh.Model.Expanded);
            long ex3 = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            Check("closed again: click-through restored", (ex3 & WS_EX_TRANSPARENT) != 0);
            await Wait(0.5);
            var after = Capture(); Save(after, Path.Combine(dir, $"{theme}-3-closed-again.png"));
            int diff2 = 0;
            for (int y = rc.T; y < Math.Min(rc.B, shh); y += 2)
            for (int x = Math.Max(rc.L, 0); x < Math.Min(rc.R, sw); x += 2)
            {
                var a = after.At(x, y); var b = closed.At(x, y);
                if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 30) diff2++;
            }
            // the music bars move, so allow a few pixels around them
            Check("closed again looks like before it opened (nothing left behind)", diff2 < 60, $"{diff2} pixels differ");

            // the cursor position is read correctly at the edge of the hot zone (DPI mapping)
            SetCursorPos(cx + Px(140), rc.T + Px(10));
            await Wait(0.06);
            Check("hover zone reaches 140 pt right of centre", sh.Model.Hovering || sh.Model.Expanded);
            SetCursorPos(away.X, away.Y);
            await Wait(1.2);
            SetCursorPos(cx + Px(152), rc.T + Px(10));
            await Wait(0.5);
            Check("and stops before 152 pt (zone is ±146)", !sh.Model.Hovering && !sh.Model.Expanded);
            SetCursorPos(away.X, away.Y);
            await Wait(1.0);

            // ---- step 3: the tab bar with the real mouse and keyboard ----
            var before = GetForegroundWindow();
            Log($"INFO foreground before opening: {before}");
            SetCursorPos(pill.X, pill.Y);
            await Wait(0.8);
            Check("re-opens on hover", sh.Model.Expanded);
            await Wait(0.4);
            var st = sh.Stage;
            POINT Btn(double cxDip) => new() { X = rc.L + Px(st.PillRect.X + cxDip), Y = rc.T + Px(st.PillRect.Y + TabPill.RowHeight / 2) };
            var tabs = st.Pill.Tabs;
            var media = Btn(st.Pill.ButtonCentre(1));
            SetCursorPos(media.X, media.Y);
            await Wait(0.3);
            Check("the cursor on the tab bar keeps the notch open", sh.Model.Expanded);
            Click();
            await Wait(0.6);
            Check("clicking a tab selects it", sh.Model.Tab == tabs[1], $"tab {sh.Model.Tab}");
            Check("a click inside gives the notch the keyboard", GetForegroundWindow() == hwnd);
            var mediaShot = Capture(); Save(mediaShot, Path.Combine(dir, $"{theme}-4-tab-media.png"));
            // whatever is behind the tab bar (here the runner's terminal text) must not show through it
            int rowY = rc.T + Px(st.PillRect.Y + TabPill.RowHeight / 2 - TabPill.Button / 2 - 2.5);
            int lo = 255, hi = 0;
            for (int x = rc.L + Px(st.PillRect.X + 24); x < rc.L + Px(st.PillRect.X + st.Pill.GlassWidth - 24); x++)
            {
                var (r, _, _) = mediaShot.At(x, rowY);
                lo = Math.Min(lo, r); hi = Math.Max(hi, r);
            }
            Check("nothing behind the tab bar shows through it", hi - lo <= 2, $"{hi - lo} levels of variation along the capsule");
            Keys(0x11, 0x33);   // Ctrl+3
            await Wait(0.6);
            Check("Ctrl+3 switches to the third tab", sh.Model.Tab == tabs[2], $"tab {sh.Model.Tab}");
            var sel = Btn(st.Pill.ButtonCentre(7));
            SetCursorPos(sel.X, sel.Y);
            await Wait(0.2);
            Click();
            await Wait(0.6);
            Check("clicking the last tab selects it", sh.Model.Tab == tabs[7], $"tab {sh.Model.Tab}");
            var lastShot = Capture(); Save(lastShot, Path.Combine(dir, $"{theme}-5-tab-settings.png"));
            Keys(0x1B);         // Esc
            await Wait(0.3);
            Check("Esc closes", !sh.Model.Expanded);
            await Wait(0.6);
            Check("after closing, the keyboard goes back to the app you were in", GetForegroundWindow() == before, $"foreground {GetForegroundWindow()}, expected {before}");
            // the close button
            SetCursorPos(away.X, away.Y);
            await Wait(0.6);
            SetCursorPos(pill.X, pill.Y);
            await Wait(0.8);
            var close = Btn(st.Pill.CloseCentre);
            SetCursorPos(close.X, close.Y);
            await Wait(0.3);
            Click();
            await Wait(0.4);
            Check("the close button closes", !sh.Model.Expanded);
            SetCursorPos(away.X, away.Y);
            await Wait(0.8);

            // ---- step 5: Settings › About, typing your name with the real keyboard; launch at login ----
            sh.Model.Tab = Tab.Settings;
            SetCursorPos(pill.X, pill.Y);
            await Wait(0.9);
            Check("re-opens on Settings", sh.Model.Expanded && sh.Model.Tab == Tab.Settings);
            await Wait(0.5);
            // go to About the way a person would: click it in the sidebar
            if (sh.Stage.Body.CurrentSettings is { } settings)
            {
                var sp = settings.Sidebar.TranslatePoint(new Point(70, Notch.SettingsSidebar.Y((int)Notch.SettingsPage.About) + 16), sh.Window)!.Value;
                SetCursorPos(rc.L + Px(sp.X), rc.T + Px(sp.Y));
                await Wait(0.2);
                Click();
                await Wait(0.7);
                Check("clicking About in the sidebar opens it", Notch.SettingsView.LastPage == Notch.SettingsPage.About);
            }
            var box = sh.Stage.Body.CurrentSettings?.CurrentPage?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            Check("About shows the name field", box is not null);
            if (box is not null)
            {
                var bp = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), sh.Window)!.Value;
                SetCursorPos(rc.L + Px(bp.X), rc.T + Px(bp.Y));
                await Wait(0.2);
                Click();
                await Wait(0.4);
                Check("clicking the field gives it the keyboard", box.IsFocused && GetForegroundWindow() == hwnd);
                Keys(0x11, 0x41);           // Ctrl+A, then type over it
                Type("Robin");
                Keys(0x0D);                 // Enter
                await Wait(0.4);
                Check("typing a name and pressing Enter saves it", sh.Model.UserName == "Robin", $"name '{sh.Model.UserName}'");
                Save(Capture(), Path.Combine(dir, $"{theme}-6-settings-about.png"));
            }
            Keys(0x1B);
            await Wait(0.8);
            bool wasOn = Startup.IsOn;
            Startup.Set(true);
            Check("launch at login: on adds Notchyyyy to Windows' startup list", Startup.IsOn);
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Check("launch at login: it starts this exe", (k?.GetValue("Notchyyyy") as string) == Startup.Command, $"{k?.GetValue("Notchyyyy")}");
            Startup.Set(false);
            Check("launch at login: off removes it", !Startup.IsOn);
            if (wasOn) Startup.Set(true);
#if WINDOWS
            // ---- step 6: a real media session, published the way Spotify or a browser does (SMTC) ----
            Windows.Media.Playback.MediaPlayer? mp = null;
            try { mp = new Windows.Media.Playback.MediaPlayer(); }
            catch (Exception e) { Log($"INFO can't make a media session on this machine: {e.Message}"); }
            if (sh.Model.Media.Unavailable is { } why) Log($"INFO media sessions unavailable here: {why}");
            else if (mp is not null)
            {
                const string Tune = "Notchyyyy self-test tune";
                mp.CommandManager.IsEnabled = false;
                var smtc = mp.SystemMediaTransportControls;
                smtc.IsEnabled = true; smtc.IsPlayEnabled = true; smtc.IsPauseEnabled = true;
                smtc.IsNextEnabled = true; smtc.IsPreviousEnabled = true;
                var presses = new System.Collections.Concurrent.ConcurrentQueue<string>();
                smtc.ButtonPressed += (_, a) => presses.Enqueue(a.Button.ToString());
                smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
                smtc.DisplayUpdater.MusicProperties.Title = Tune;
                smtc.DisplayUpdater.MusicProperties.Artist = "Sprout";
                smtc.DisplayUpdater.Update();
                smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Playing;
                var player = sh.Model.Media;
                for (int i = 0; i < 60 && !(player.Title == Tune && player.IsPlaying); i++) await Wait(0.1);
                Check("Media sees a playing media session", player.Title == Tune, $"title '{player.Title}', app '{player.AppName}' ({player.AppId})");
                Check("it shows as playing, with its artist", player.IsPlaying && player.Artist == "Sprout");
                Check("the closed pill shows music bars", sh.Model.LiveActivity);

                sh.Model.Tab = Tab.Media;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.2);
                var mv = sh.Stage.Body.CurrentMedia;
                Check("opens on the Media tab, showing the player", mv is { ShowingPlayer: true });
                Save(Capture(), Path.Combine(dir, $"{theme}-7-media-playing.png"));
                if (mv is not null)
                {
                    var pp = mv.Play.TranslatePoint(new Point(mv.Play.Bounds.Width / 2, mv.Play.Bounds.Height / 2), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(pp.X), rc.T + Px(pp.Y));
                    await Wait(0.3);
                    Click();
                    for (int i = 0; i < 30 && !presses.Contains("Pause"); i++) await Wait(0.1);
                    Check("clicking pause reaches the playing app", presses.Contains("Pause"), string.Join(",", presses));
                    // the app pauses: the notch follows
                    smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Paused;
                    for (int i = 0; i < 30 && player.IsPlaying; i++) await Wait(0.1);
                    await Wait(0.9);   // past the moment the click's own state is kept
                    Check("the app pausing shows as paused", !player.IsPlaying && !mv.Play.ShowsPause);
                    var np = mv.NextButton.TranslatePoint(new Point(mv.NextButton.Bounds.Width / 2, mv.NextButton.Bounds.Height / 2), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(np.X), rc.T + Px(np.Y));
                    await Wait(0.3);
                    Click();
                    for (int i = 0; i < 30 && !presses.Contains("Next"); i++) await Wait(0.1);
                    Check("clicking next reaches the playing app", presses.Contains("Next"), string.Join(",", presses));
                    Save(Capture(), Path.Combine(dir, $"{theme}-8-media-paused.png"));
                }
                // the app goes away: back to "Nothing playing"
                smtc.IsEnabled = false;
                mp.Dispose();
                for (int i = 0; i < 50 && player.HasTrack; i++) await Wait(0.1);
                Check("when the app's session ends, it shows 'Nothing playing'", !player.HasTrack);
                Keys(0x1B);
                await Wait(0.8);
            }
#endif
            // ---- step 7: the clipboard, copied to the way another app copies ----
            {
                var other = new WinClipboard(() => hwnd);   // another writer: the app only ignores its own copies
                var clips = sh.Model.Clips;
                async Task<bool> Until(Func<bool> ok) { for (int i = 0; i < 30 && !ok(); i++) await Wait(0.1); return ok(); }
                other.WriteText("Notchyyyy self-test text");
                Check("a copied text shows up first", await Until(() => clips.Items.FirstOrDefault()?.Text == "Notchyyyy self-test text"));
                other.WriteText("#E5484D");
                Check("a copied colour code becomes a colour card", await Until(() => clips.Items.FirstOrDefault()?.Color?.Hex == "#E5484D"));
                using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(120, 80)))
                {
                    surface.Canvas.Clear(new SkiaSharp.SKColor(48, 164, 108));
                    using var snap = surface.Snapshot();
                    using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    other.WritePng(data.ToArray());
                }
                Check("a copied image becomes an image card", await Until(() => clips.Items.FirstOrDefault()?.Kind == Clipboard.ClipKind.Image));
                int kept = clips.Items.Count;
                other.WritePrivateText("hunter2-password");
                await Wait(1.2);
                Check("a password manager's copy is never kept", clips.Items.Count == kept && !clips.Items.Any(c => c.Text.Contains("hunter2")));

                sh.Model.Tab = Tab.Clipboard;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.2);
                var cv = sh.Stage.Body.CurrentClipboard;
                Check("opens on the Clipboard tab with the cards", cv is not null && cv.Strip.Cards.Count() >= 3);
                Save(Capture(), Path.Combine(dir, $"{theme}-9-clipboard.png"));
                if (cv is not null && cv.Strip.Cards.FirstOrDefault(c => c.Item.Color is not null) is { } colour)
                {
                    // the RGB chip under the swatch
                    double cw = (Notch.ClipCard.W - 12 - 8) / 3;
                    var cp = colour.TranslatePoint(new Point(6 + cw + 4 + cw / 2, Notch.ClipCard.BodyH - 15), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(cp.X), rc.T + Px(cp.Y));
                    await Wait(0.3);
                    int count = clips.Items.Count;
                    Click();
                    await Wait(0.8);
                    Check("clicking RGB copies the colour as rgb()", WinClipboard.Read()?.Text == "rgb(229, 72, 77)", $"clipboard '{WinClipboard.Read()?.Text}'");
                    Check("copying back doesn't add a duplicate card", clips.Items.Count == count);
                    Save(Capture(), Path.Combine(dir, $"{theme}-10-clipboard-copied.png"));
                }
                Keys(0x1B);
                await Wait(0.8);
            }
            // ---- step 8: apps from the real Start menu ----
            {
                var apps = sh.Model.Apps;
                for (int i = 0; i < 150 && !apps.Scanned; i++) await Wait(0.1);
                Check("reads the Start menu's apps", apps.Scanned && apps.Installed.Count >= 5, $"{apps.Installed.Count} apps: {string.Join(", ", apps.Installed.Take(12).Select(a => a.Name))}");
                Check("pins some of Windows' own apps by default", apps.Ids.Count >= 2, string.Join(", ", apps.Ids.Select(apps.Name)));
                var catalog = new WinApps();
                var notepad = apps.Installed.FirstOrDefault(a => a.Name.Equals("Notepad", StringComparison.OrdinalIgnoreCase));
                if (notepad is not null)
                {
                    using var img = catalog.Icon(notepad.Id, 64);
                    int opaque = 0;
                    if (img is not null)
                    {
                        using var bmp = SkiaSharp.SKBitmap.FromImage(img);
                        for (int y = 0; y < bmp.Height; y += 2) for (int x = 0; x < bmp.Width; x += 2) if (bmp.GetPixel(x, y).Alpha > 128) opaque++;
                    }
                    Check("Notepad's icon comes from the shell, not blank", img is not null && opaque > 100, $"{img?.Width}x{img?.Height}, {opaque} opaque samples");
                    if (!apps.IsPinned(notepad.Id)) apps.Add(notepad.Id);
                }
                sh.Model.Tab = Tab.Apps;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.2);
                var av = sh.Stage.Body.CurrentApps;
                Check("opens on the Apps tab with the pinned apps", av is not null && av.Grid.Tiles.Any());
                for (int i = 0; i < 30 && av is not null && av.Grid.Tiles.Any(t => apps.Icon(t.Id!) is null); i++) await Wait(0.1);
                Save(Capture(), Path.Combine(dir, $"{theme}-11-apps.png"));
                if (av is not null && notepad is not null && av.Grid.TileFor(notepad.Id) is { } tile)
                {
                    var running = System.Diagnostics.Process.GetProcessesByName("Notepad").Select(p => p.Id).ToHashSet();
                    var tp = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(tp.X), rc.T + Px(tp.Y));
                    await Wait(0.3);
                    Click();
                    System.Diagnostics.Process? opened = null;
                    for (int i = 0; i < 80 && opened is null; i++)
                    {
                        await Wait(0.1);
                        opened = System.Diagnostics.Process.GetProcessesByName("Notepad").FirstOrDefault(p => !running.Contains(p.Id));
                    }
                    Check("clicking Notepad opens it", opened is not null);
                    await Wait(0.6);
                    Check("and the notch closes", !sh.Model.Expanded);
                    try { opened?.Kill(); } catch { }
                }
                // the picker
                sh.Model.Tab = Tab.Apps;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.2);
                av = sh.Stage.Body.CurrentApps;
                if (av is not null)
                {
                    var ap = av.AddButton.TranslatePoint(new Point(av.AddButton.Bounds.Width / 2, 13), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(ap.X), rc.T + Px(ap.Y));
                    await Wait(0.3);
                    Click();
                    await Wait(1.0);
                    Check("Add apps opens the picker with every app", av.ShowingPicker && av.Picker.List.Visible == apps.Installed.Count, $"{av.Picker.List.Visible} rows");
                    Check("the search field has the keyboard", av.Picker.Search.IsFocused);
                    Type("note");
                    await Wait(0.5);
                    Check("typing filters the list", av.Picker.List.Visible >= 1 && av.Picker.List.Shown.All(r => r.App.Name.Contains("note", StringComparison.OrdinalIgnoreCase)), string.Join(", ", av.Picker.List.Shown.Select(r => r.App.Name)));
                    await Wait(0.5);
                    Save(Capture(), Path.Combine(dir, $"{theme}-12-apps-picker.png"));
                    av.ShowPicker(false);
                }
                Keys(0x1B);
                await Wait(0.8);
            }
            // ---- step 9: Today and the stopwatch ----
            {
                sh.Model.Tab = Tab.Today;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.2);
                var tv = sh.Stage.Body.CurrentToday;
                Check("opens on the Today tab", tv is not null);
                if (tv is not null)
                {
                    var ip = tv.Input.TranslatePoint(new Point(40, tv.Input.Bounds.Height / 2), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(ip.X), rc.T + Px(ip.Y));
                    await Wait(0.2);
                    Click();
                    await Wait(0.4);
                    Check("clicking the composer gives it the keyboard", tv.Input.IsFocused);
                    Type("Self-test call at 11:59pm");
                    Keys(0x0D);
                    await Wait(0.6);
                    var added = sh.Model.Tasks.Today.FirstOrDefault(t => t.Title == "Self-test call");
                    Check("typing a task with a time adds it, time understood", added?.Time is { Hour: 23, Minute: 59 }, $"{added?.Title} {added?.Time}");
                    Save(Capture(), Path.Combine(dir, $"{theme}-13-today.png"));
#if WINDOWS
                    // a real Windows notification, as a due reminder sends it
                    var reminders = new WinReminders();
                    Check("Windows notifications are available to Notchyyyy", reminders.Available);
                    if (added is not null && reminders.Available)
                    {
                        reminders.Show(added);
                        await Wait(1.0);
                        var history = WinReminders.History();
                        Check("a reminder reaches Windows' notification centre", history.Contains(added.Id.ToString("N")[..16]), string.Join(",", history));
                        Save(Capture(), Path.Combine(dir, $"{theme}-14-reminder.png"));
                    }
#endif
                    if (added is not null) sh.Model.Tasks.Delete(added);
                }
                // the stopwatch, by its keys
                sh.Model.Tab = Tab.Stopwatch;
                await Wait(0.8);
                var sv = sh.Stage.Body.CurrentStopwatch;
                if (sv is not null)
                {
                    var bp = sv.RightButton.TranslatePoint(new Point(38, 18), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(bp.X), rc.T + Px(bp.Y));
                    await Wait(0.2);
                    Click();
                    await Wait(1.3);
                    Keys(0x4C);   // L
                    await Wait(0.7);
                    Keys(0x20);   // Space
                    await Wait(0.3);
                    var watch = sh.Model.Stopwatch;
                    Check("Start, L and Space run the stopwatch and take a lap", !watch.Running && watch.Laps.Count == 1 && watch.Elapsed() is > 1.6 and < 3.5, $"{watch.Elapsed():0.00} s, {watch.Laps.Count} laps");
                    Save(Capture(), Path.Combine(dir, $"{theme}-15-stopwatch.png"));
                    Keys(0x20);   // running again: the closed pill shows the time
                    Keys(0x1B);
                    await Wait(1.6);
                    Check("while it runs, the closed pill shows its time", watch.Running && sh.Model.Timing && !sh.Model.Expanded);
                    Save(Capture(), Path.Combine(dir, $"{theme}-16-pill-stopwatch.png"));
                    watch.Reset();
                }
                Keys(0x1B);
                await Wait(0.8);
            }
            // ---- step 10: the machine's own numbers ----
            {
                var sys = sh.Model.System;
                for (int i = 0; i < 40 && sys.Samples < 3; i++) await Wait(0.1);
                Check("reads CPU, memory and storage", sys.Samples >= 3 && sys.Cores.Length == Environment.ProcessorCount && sys.MemTotal > 1e9 && sys.DiskTotal > 1e9,
                    $"{sys.Samples} readings, {sys.Cores.Length} cores, CPU {sys.Cpu:P0}, memory {SystemInfo.SystemMonitor.Bytes(sys.MemUsed)} of {SystemInfo.SystemMonitor.Bytes(sys.MemTotal)}, " +
                    $"disk {SystemInfo.SystemMonitor.Bytes(sys.DiskUsed)} of {SystemInfo.SystemMonitor.Bytes(sys.DiskTotal)}, GPU {(sys.Gpu is { } g ? g.ToString("P0") : "none")} ({sys.GpuName}), chip '{sys.Chip}'");
                Log($"INFO chip '{sys.Chip}', graphics '{sys.GpuName}'");
                // busy every core for 3 s: the CPU reading must rise with it
                double calm = sys.Cpu;
                // (on threads of their own, below normal priority: busy work on the shared pool held up the reading's own
                // timer on a 4-core runner, so no new reading came — the test's fault, not the app's)
                using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3.2)))
                {
                    var spin = Enumerable.Range(0, Environment.ProcessorCount).Select(_ =>
                    {
                        var t = new Thread(() => { double x = 0; while (!stop.IsCancellationRequested) x += Math.Sqrt(x + 1); })
                            { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                        t.Start();
                        return t;
                    }).ToList();
                    int readingsBefore = sys.Samples;
                    await Wait(2.8);
                    double busy = sys.Cpu;
                    Check("the CPU reading rises when the machine gets busy", busy > 0.7 && busy > calm && sys.Samples > readingsBefore, $"calm {calm:P0}, busy {busy:P0}, {sys.Samples - readingsBefore} readings meanwhile");
                    foreach (var t in spin) t.Join();
                }
                sh.Model.Tab = Tab.System;
                SetCursorPos(pill.X, pill.Y);
                await Wait(1.5);
                Check("opens on the System tab", sh.Stage.Body.CurrentSystem is not null);
                Save(Capture(), Path.Combine(dir, $"{theme}-17-system.png"));
                Keys(0x1B);
                await Wait(0.8);
            }
            // ---- step 11: Sprout's senses, the water bubble, the setup ----
            {
                // typing: Shift tapped quickly (counted as key presses, and it types nothing anywhere)
                SetCursorPos(away.X, away.Y);
                await Wait(0.3);
                for (int i = 0; i < 10; i++) { Keys(0x10); await Wait(0.09); }
                await Wait(0.1);
                Check("quick key presses make Sprout run (typing sensed, no hook)", sh.Model.Buddy.Typing, $"senses running: {sh.Senses?.Running}");
                await Wait(1.3);
                Check("it stops once the keys stop", !sh.Model.Buddy.Typing);
#if WINDOWS
                var senses = new WinSenses();
                Check("raw keyboard input is available", senses.Available);
                double idle1 = senses.SecondsSinceInput;
                await Wait(1.6);
                double idle2 = senses.SecondsSinceInput;
                Check("idle time counts up without input", idle2 > idle1 + 1.2, $"{idle1:0.0} s → {idle2:0.0} s");
#endif
                // the water bubble
                sh.Remind();
                await Wait(1.2);
                Check("a reminder swells the closed notch into a bubble", sh.Model.Peeking == Notch.Peek.Hydrate && !sh.Model.Expanded && Math.Abs(sh.Stage.CurrentWidth - 420) < 1,
                    $"peek {sh.Model.Peeking}, width {sh.Stage.CurrentWidth:0}");
                Save(Capture(), Path.Combine(dir, $"{theme}-18-water-bubble.png"));
                var dp = sh.Stage.Peek.DrankButton.TranslatePoint(new Point(sh.Stage.Peek.DrankButton.Bounds.Width / 2, 14), sh.Window)!.Value;
                SetCursorPos(rc.L + Px(dp.X), rc.T + Px(dp.Y));
                await Wait(0.3);
                Check("the bubble takes clicks (only inside itself)", (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TRANSPARENT) == 0);
                int glasses = sh.Model.Water.Count;
                Click();
                await Wait(0.5);
                Check("Drank counts a glass and Sprout cheers", sh.Model.Water.Count == glasses + 1 && sh.Model.Peeking == Notch.Peek.Cheer, $"{sh.Model.Water.Count} glasses, {sh.Model.Peeking}");
                Save(Capture(), Path.Combine(dir, $"{theme}-19-water-cheer.png"));
                await Wait(2.4);
                Check("the bubble tucks itself away, and the notch doesn't open under the pointer", sh.Model.Peeking == Notch.Peek.None && !sh.Model.Expanded);
                Check("clicks pass through again", (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TRANSPARENT) != 0);
                SetCursorPos(away.X, away.Y);
                await Wait(0.6);

                // the setup, as on a first launch: a typed name and real clicks on each button
                Notch.OnboardingView.Saved = Notch.SetupStep.Name;
                sh.Model.Tab = Tab.Home;
                sh.Model.Pinned = true;
                sh.Model.Onboarded = false;
                sh.Tracker.Expand();
                await Wait(1.4);
                var setup = sh.Stage.Body.CurrentSetup;
                Check("setup opens in the notch, without the tab bar", setup is not null && sh.Stage.PillOpacity < 0.01);
                if (setup is not null)
                {
                    var np = setup.NameBox.TranslatePoint(new Point(60, 21), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(np.X), rc.T + Px(np.Y));
                    await Wait(0.2);
                    Click();
                    await Wait(0.3);
                    Keys(0x11, 0x41);
                    Type("Sam");
                    await Wait(0.3);
                    Save(Capture(), Path.Combine(dir, $"{theme}-20-setup-name.png"));
                    Keys(0x0D);
                    await Wait(0.8);
                    Check("a typed name and Enter move on", setup.Step == Notch.SetupStep.Look && sh.Model.UserName == "Sam", $"step {setup.Step}, name '{sh.Model.UserName}'");
                    for (int i = 0; i < 3; i++)
                    {
                        setup = sh.Stage.Body.CurrentSetup!;
                        var pp = setup.Primary.TranslatePoint(new Point(setup.Primary.Bounds.Width / 2, 16), sh.Window)!.Value;
                        SetCursorPos(rc.L + Px(pp.X), rc.T + Px(pp.Y));
                        await Wait(0.25);
                        Click();
                        await Wait(0.8);
                    }
                    setup = sh.Stage.Body.CurrentSetup!;
                    Check("Continue goes through every step", setup.Step == Notch.SetupStep.Done, $"step {setup.Step}");
                    Save(Capture(), Path.Combine(dir, $"{theme}-21-setup-done.png"));
                    var sp = setup.Primary.TranslatePoint(new Point(setup.Primary.Bounds.Width / 2, 16), sh.Window)!.Value;
                    SetCursorPos(rc.L + Px(sp.X), rc.T + Px(sp.Y));
                    await Wait(0.25);
                    Click();
                    await Wait(1.2);
                    Check("Start using Notchyyyy finishes: Home and the tab bar", sh.Model.Onboarded && !sh.Model.Pinned && sh.Stage.Body.CurrentHome is not null && sh.Stage.PillOpacity > 0.99);
                }
                Keys(0x1B);
                await Wait(0.8);
            }
            SetCursorPos(away.X, away.Y);
        }
        catch (Exception e) { Log("FAIL exception: " + e); }
        finally
        {
            bool ok = lines.Count > 0 && !lines.Any(l => l.StartsWith("FAIL"));
            Log(ok ? "ALL PASS" : "SOME FAILED");
            await Task.Delay(300);
            Environment.Exit(ok ? 0 : 1);
        }
    }
}
