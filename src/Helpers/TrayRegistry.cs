using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GHelper.Linux.Helpers;

/// <summary>
/// Keeps every <see cref="TrayIcon"/> in the application-level
/// <c>TrayIcon.Icons</c> collection.
///
/// Since Avalonia 12.1.4 (AvaloniaUI/Avalonia#22118) a <c>TrayIcon</c> no
/// longer creates its platform implementation in the constructor. It is
/// created in <c>Attach()</c>, which only runs when the icon is added to
/// <c>TrayIcon.Icons</c> on the <see cref="Application"/>. A standalone
/// <c>new TrayIcon()</c> is inert: no StatusNotifierItem is exported on
/// D-Bus and the panel shows nothing. Removing an icon from the collection
/// detaches it, which disposes the platform implementation (menu exporter,
/// watcher name owner) and releases its D-Bus name.
///
/// UI thread only, like every other <see cref="TrayIcon"/> member.
/// </summary>
public static class TrayRegistry
{
    /// <summary>Attach <paramref name="icon"/> to the panel. Idempotent.</summary>
    public static void Register(TrayIcon icon)
    {
        Dispatcher.UIThread.VerifyAccess();

        Application? app = Application.Current;
        if (app == null)
        {
            Logger.WriteLine("TrayRegistry: no Application, tray icon not registered");
            return;
        }

        TrayIcons? icons = TrayIcon.GetIcons(app);
        if (icons == null)
        {
            icons = new TrayIcons();
            TrayIcon.SetIcons(app, icons);
        }

        if (!icons.Contains(icon))
            icons.Add(icon);
    }

    /// <summary>Detach <paramref name="icon"/> from the panel. Disposes the native icon.</summary>
    public static void Unregister(TrayIcon icon)
    {
        Dispatcher.UIThread.VerifyAccess();

        Application? app = Application.Current;
        if (app == null)
            return;

        TrayIcon.GetIcons(app)?.Remove(icon);
    }
}
