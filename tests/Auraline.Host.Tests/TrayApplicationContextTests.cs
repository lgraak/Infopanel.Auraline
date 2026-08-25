using Auraline.Host.Configuration;
using Auraline.Host.Lifecycle;
using Auraline.Host.Platform.Windows;
using Auraline.Host.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Forms;

namespace Auraline.Host.Tests;

public sealed class TrayApplicationContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuralineTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TrayProvidesRequiredCommandsAndExitDisposesResources()
    {
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var store = new ConfigurationStore(AuralinePaths.FromRoot(_root));
                store.LoadAsync().GetAwaiter().GetResult();
                store.UpdateAsync(current => current with { Providers = current.Providers.Select(p => p with { Enabled = false }).ToList() }).GetAwaiter().GetResult();
                using var manager = new ProviderManager(store, new UnusedConnector(), new SystemAsyncDelay(), NullLogger<ProviderManager>.Instance);
                var browser = new FakeBrowser();
                using var context = new TrayApplicationContext(new Uri("http://127.0.0.1:48481/"), browser, manager);

                var labels = context.Menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text ?? string.Empty).ToArray();
                Assert.Equal(["Open Auraline", "Reconnect Providers", "Exit"], labels);
                ((ToolStripMenuItem)context.Menu.Items[0]).PerformClick();
                Assert.Equal(1, browser.OpenCount);
                ((ToolStripMenuItem)context.Menu.Items[context.Menu.Items.Count - 1]).PerformClick();
                completion.TrySetResult(null);
            }
            catch (Exception ex) { completion.TrySetResult(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var error = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join(TimeSpan.FromSeconds(2));
        Assert.Null(error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeBrowser : IBrowserLauncher
    {
        public int OpenCount { get; private set; }
        public bool Open(Uri uri)
        {
            OpenCount++;
            return true;
        }
    }

    private sealed class UnusedConnector : IProviderConnector
    {
        public Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Disabled provider must not connect.");
    }
}
