using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileScope.App.Localization;

namespace FileScope.Ui.Tests;

/// <summary>
/// Owns the one WPF Application and its STA dispatcher for this test process.
/// The runner may select any scenario; none relies on another test's startup.
/// </summary>
internal static class WpfTestHost
{
    private sealed record Host(Application Application, Task Stopped);

    private static readonly Lock StartupLock = new();
    private static Task<Host>? _shared;

    private static Task<Host> GetHostAsync()
    {
        // Starting the background thread never waits for its dispatcher.
        lock (StartupLock) return _shared ??= StartAsync();
    }

    public static async Task RunAsync(Action<Application, LocalizationService> scenario)
    {
        var host = await GetHostAsync().ConfigureAwait(false);

        await host.Application.Dispatcher.InvokeAsync(() =>
        {
            var app = host.Application;
            var strings = LocalizationService.Instance;
            strings.SetCulture("en");
            System.Globalization.CultureInfo.CurrentCulture = strings.Culture;
            System.Globalization.CultureInfo.CurrentUICulture = strings.Culture;
            ResetResources(app);

            try
            {
                scenario(app, strings);
            }
            finally
            {
                // Also close windows whose scenario failed before its own cleanup.
                foreach (var window in app.Windows.Cast<Window>().ToArray()) window.Close();
                app.MainWindow = null;
                strings.SetCulture("en");
                ResetResources(app);
            }
        }).Task.ConfigureAwait(false);
    }

    public static async Task ShutdownAsync()
    {
        Task<Host>? pending;
        lock (StartupLock) pending = _shared;
        if (pending is null) return;

        var host = await pending.ConfigureAwait(false);
        // A broken shutdown must fail assembly cleanup instead of keeping the
        // final test and the CI job waiting indefinitely.
        await host.Application.Dispatcher.InvokeAsync(host.Application.Shutdown).Task
            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await host.Stopped.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    private static Task<Host> StartAsync()
    {
        var ready = new TaskCompletionSource<Host>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                // A plain Application loads production resources without invoking
                // App.OnStartup or opening the production composition root.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(app.Dispatcher));

                ready.SetResult(new Host(app, stopped.Task));
                // Application.Shutdown only stops the dispatcher when Application
                // started it. A bare Dispatcher.Run would leave Stopped pending.
                app.Run();
                stopped.SetResult();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                stopped.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "FileScope WPF tests" };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    private static void ResetResources(Application app)
    {
        // Replacing Resources removes Fluent's dictionaries and resets ThemeMode
        // to None. Select Light afterwards, before resolving BasedOn styles.
        app.Resources = new ResourceDictionary();
#pragma warning disable WPF0001 // Match the native theme selected by App.xaml.
        app.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FileScope.App;component/Styles/Controls.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FileScope.App;component/Styles/Themes/LightThemePalette.xaml", UriKind.Relative)
        });
        app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
    }
}

[TestClass]
public sealed class WpfTestLifetime
{
    [AssemblyCleanup]
    public static Task CleanupAsync() => WpfTestHost.ShutdownAsync();
}
