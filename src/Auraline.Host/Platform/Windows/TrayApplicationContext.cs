using System.Drawing;
using Auraline.Host.Lifecycle;
using Auraline.Host.Providers;

namespace Auraline.Host.Platform.Windows;

/// <summary>Windows Forms tray shell for the Windows Host executable.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    internal ContextMenuStrip Menu => _trayIcon.ContextMenuStrip!;

    public TrayApplicationContext(Uri webUi, IBrowserLauncher browser, ProviderManager providers)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Auraline", null, (_, _) => browser.Open(webUi));
        menu.Items.Add("Reconnect Providers", null, async (_, _) =>
        {
            foreach (var provider in providers.GetStatuses().Where(p => p.Enabled)) await providers.ReconnectAsync(provider.Id);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Auraline Host",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => browser.Open(webUi);
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }
}
