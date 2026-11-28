#if WINDOWS
using System.Runtime.InteropServices;
using System.Security;
using Notchyyyy.Today;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Notchyyyy.Platform;

/// <summary>
/// Task reminders as Windows notifications (the Mac sends them through its notification centre). An app that isn't
/// installed from the Store needs an identity Windows can show notifications for: Notchyyyy registers its own
/// (HKCU\Software\Classes\AppUserModelId\Notchyyyy, a name and an icon), as unpackaged apps do — no administrator
/// rights, nothing outside the user's own registry.
/// </summary>
public sealed class WinReminders : IReminders
{
    public const string Aumid = "Notchyyyy.Notchyyyy";
    private ToastNotifier? _notifier;

    public WinReminders()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}"))
            {
                k.SetValue("DisplayName", "Notchyyyy");
                if (IconFile() is { } icon) k.SetValue("IconUri", icon);
            }
            SetCurrentProcessExplicitAppUserModelID(Aumid);
            _notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
        }
        catch (Exception e) { Theme.Debug.Log($"notifications unavailable: {e.Message}"); }
    }

    public bool Available => _notifier is not null;

    public void Show(DayTask task)
    {
        if (_notifier is null) return;
        try
        {
            string when = task.Time is { } t ? "Scheduled for " + Tasks.ShortTime(t) : "";
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast launch="notchyyyy:today">
                  <visual><binding template="ToastGeneric">
                    <text>{SecurityElement.Escape(task.Title)}</text>
                    <text>{SecurityElement.Escape(when)}</text>
                  </binding></visual>
                  <audio src="ms-winsoundevent:Notification.Reminder"/>
                </toast>
                """);
            _notifier.Show(new ToastNotification(xml) { Tag = task.Id.ToString("N")[..16], Group = "tasks" });
        }
        catch (Exception e) { Theme.Debug.Log($"reminder not shown: {e.Message}"); }
    }

    /// The tags of reminders Windows is holding in the notification centre (for the self-test).
    public static IReadOnlyList<string> History()
    {
        try { return ToastNotificationManager.History.GetHistory(Aumid).Select(n => n.Tag).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    /// The app's icon as a PNG Windows can show beside its notifications (written once, next to the settings).
    private static string? IconFile()
    {
        try
        {
            var path = Path.Combine(Theme.Prefs.Folder, "notchyyyy.png");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Theme.Prefs.Folder);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(128, 128));
                Sprout.SproutPainter.Draw(surface.Canvas, new Avalonia.Rect(8, 8, 112, 112), Sprout.SproutPose.For(Sprout.SproutMood.Happy, 0), 0);
                using var img = surface.Snapshot();
                using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            return path;
        }
        catch { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
#endif
