using Avalonia.Input;

namespace Notchyyyy.Notch;

/// <summary>
/// The notch's keyboard, while it's open: Esc closes, Ctrl+1…9 switch to the visible tabs (⌘1…9 on the Mac).
/// The overlay only has the keyboard after a click inside the open notch, as on the Mac.
/// </summary>
public static class NotchKeys
{
    /// True when the key was used.
    public static bool Handle(NotchModel m, Action collapse, Key key, KeyModifiers mods)
    {
        if (!m.Expanded) return false;
        if (key == Key.Escape) { collapse(); return true; }
        if (mods.HasFlag(KeyModifiers.Control) && key >= Key.D1 && key <= Key.D9)
        {
            int n = key - Key.D1;
            var tabs = m.VisibleTabs;
            if (n < tabs.Count) { m.Tab = tabs[n]; return true; }
        }
        return false;
    }
}
