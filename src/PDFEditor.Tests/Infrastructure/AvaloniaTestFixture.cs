using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using ReactiveUI;
using System.Reactive.Concurrency;
using Xunit;

namespace PDFEditor.Tests.Infrastructure;

/// <summary>
/// Initialises the Avalonia headless platform once for the entire test session.
/// All tests that need Avalonia (ViewModels, rendering) must belong to the
/// "AvaloniaTests" collection so they share this single fixture.
/// </summary>
public class AvaloniaTestFixture : IDisposable
{
    static AvaloniaTestFixture()
    {
        // Boot the headless Avalonia platform (no window is shown)
        // UseReactiveUI() sets RxApp.MainThreadScheduler = AvaloniaScheduler.Instance
        AppBuilder.Configure(() => new Application())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions()) // UseHeadlessDrawing defaults to true
            .UseReactiveUI()
            .SetupWithoutStarting();

        // Override back to a synchronous scheduler so tests are deterministic
        // (AvaloniaScheduler.Instance is still valid for WhenAnyValue / ObserveOn)
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
    }

    /// <summary>
    /// Runs an action on the UI thread and flushes pending work afterward.
    /// Call this from test methods that dispatch to Dispatcher.UIThread.
    /// </summary>
    public static void RunOnUIThread(Action action)
    {
        Dispatcher.UIThread.Post(action);
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose() { }
}

/// <summary>Collection marker shared by all headless-Avalonia tests.</summary>
[CollectionDefinition("AvaloniaTests")]
public class AvaloniaTestCollection : ICollectionFixture<AvaloniaTestFixture> { }
