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
            Notch.SettingsView.LastPage = Notch.SettingsPage.About;
            sh.Model.Tab = Tab.Settings;
            SetCursorPos(pill.X, pill.Y);
            await Wait(0.9);
            Check("re-opens on Settings", sh.Model.Expanded && sh.Model.Tab == Tab.Settings);
            await Wait(0.5);
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
