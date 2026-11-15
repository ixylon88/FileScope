using System.Windows;
using System.Windows.Threading;

namespace FileScope.Ui.Tests;

public sealed partial class UiTests
{
    [TestMethod]
    public Task HostProvidesStaDispatcherForAnIndividualTest() =>
        WpfTestHost.RunAsync((app, strings) =>
        {
            Assert.AreSame(app, Application.Current);
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            Assert.IsInstanceOfType<DispatcherSynchronizationContext>(SynchronizationContext.Current);

            var currentThread = Environment.CurrentManagedThreadId;
            var dispatchedThread = 0;
            app.Dispatcher.BeginInvoke(new Action(() => dispatchedThread = Environment.CurrentManagedThreadId));
            PumpUntil(() => dispatchedThread != 0);

            Assert.AreEqual(currentThread, dispatchedThread, "dispatcher callbacks stay on the test's STA thread");
        });

    [TestMethod]
    public async Task HostCleansUpAfterAFailedScenario()
    {
        Application? originalApplication = null;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WpfTestHost.RunAsync((app, strings) =>
        {
            originalApplication = app;
            var window = new Window { ShowActivated = false };
            window.Show();
            strings.SetCulture("de");
            app.Resources["FailedScenarioResource"] = new object();
#pragma warning disable WPF0001 // Deliberately leave a different native theme behind.
            app.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001

            throw new InvalidOperationException("Deliberate scenario failure.");
        }));

        await WpfTestHost.RunAsync((app, strings) =>
        {
            Assert.AreSame(originalApplication, app, "WPF keeps its one Application per process");
            Assert.AreEqual(0, app.Windows.Count, "failed scenarios cannot leak windows into the next test");
            Assert.AreEqual("en", strings.Culture.Name);
            Assert.IsFalse(app.Resources.Contains("FailedScenarioResource"));
#pragma warning disable WPF0001 // The next scenario must start with the production default.
            Assert.AreEqual(ThemeMode.Light, app.ThemeMode);
#pragma warning restore WPF0001
        });
    }
}
