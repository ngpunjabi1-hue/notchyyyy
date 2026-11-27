using Avalonia;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// The state the notch is drawn from. Views listen to <see cref="Changed"/>.
public sealed class NotchModel
{
    public event Action<string>? Changed;

    private bool _expanded, _hovering, _musicPlaying;

    public bool Expanded { get => _expanded; set => Set(ref _expanded, value, nameof(Expanded)); }
    public bool Hovering { get => _hovering; set => Set(ref _hovering, value, nameof(Hovering)); }

    /// Stand-in for the media session until the Media step (plan step 6): `--demo` plays.
    public bool MusicPlaying { get => _musicPlaying; set => Set(ref _musicPlaying, value, nameof(MusicPlaying)); }

    /// Windows laptops have no notch: a slim pill centred at the top of the screen, like the Mac app on
    /// screens without one. Its height is also the open body's header row.
    public Size NotchSize { get; } = new(180, 32);
    public Size BodySize { get; } = new(700, 300);
    public double PillGap => 10;
    public double PillHeight => 42;

    /// While true the notch stays open (`--open`, onboarding, a dialog).
    public bool Pinned { get; set; }
    /// A menu is open: its items can sit outside the notch, so don't collapse.
    public bool MenuOpen { get; set; }

    public bool LiveActivity => Prefs.Shared.LiveActivity && MusicPlaying;
    /// something shows in the closed pill: a live activity, or Sprout
    public bool Activity => LiveActivity || Prefs.Shared.Buddy;
    /// room the live activity needs beside the pill's centre
    public double Extra => 92;

    /// The closed pill's size (it swells a little under the pointer).
    public Size ClosedSize => new(NotchSize.Width + 12 + (Activity ? Extra : 0) + (Hovering ? 16 : 0),
                                  NotchSize.Height + (Hovering ? 5 : 0));

    private void Set(ref bool field, bool value, string name)
    {
        if (field == value) return;
        field = value;
        Changed?.Invoke(name);
    }
}
