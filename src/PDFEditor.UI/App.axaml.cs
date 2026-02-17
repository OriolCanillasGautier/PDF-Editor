using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NLog;
using PDFEditor.UI.ViewModels;
using ReactiveUI;

namespace PDFEditor.UI;

public partial class App : Application
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // ReactiveUI global exception handler — prevents crashes from unhandled
        // exceptions in reactive pipelines / commands
        RxApp.DefaultExceptionHandler = new GlobalExceptionHandler();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = new ViewModels.MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Catches all unhandled ReactiveUI/command exceptions and logs them
/// instead of crashing the app.
/// </summary>
internal class GlobalExceptionHandler : IObserver<Exception>
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public void OnNext(Exception ex)
    {
        Log.Error(ex, "Unhandled ReactiveUI exception");
    }

    public void OnError(Exception ex)
    {
        Log.Fatal(ex, "Fatal ReactiveUI exception");
    }

    public void OnCompleted() { }
}
