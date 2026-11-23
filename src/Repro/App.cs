namespace Repro;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var scenario = Scenario.Parse(Scenario.CaseName);
        Log.Line("START", scenario.Name, ("maui", typeof(Shell).Assembly.GetName().Version?.ToString() ?? "?"), ("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription));
        ObserveUnhandledExceptions(scenario);

        Routing.RegisterRoute("detail", typeof(DetailPage));
        var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Flyout };
        shell.Items.Add(new ShellContent { Title = "Home", Route = "home", Content = new ContentPage { Title = "Home", Content = new Label { Text = scenario.Name } } });

        if (scenario.Placement == IconPlacement.FlyoutIcon)
            shell.FlyoutIcon = scenario.CreateIconSource();
        else
            DetailPage.IconSource = scenario.CreateIconSource();

        _ = DriveScenarioAsync(scenario, shell);
        return new Window(shell);
    }

    static async Task DriveScenarioAsync(Scenario scenario, Shell shell)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        if (scenario.Placement == IconPlacement.FlyoutIcon)
        {
            // A second UpdateLeftBarButtonItem while the first icon load is pending: MAUI cancels its own first load
            await MainThread.InvokeOnMainThreadAsync(() => Shell.SetForegroundColor(shell, Colors.Red));
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() => shell.GoToAsync("detail"));
        }

        await Task.Delay(TimeSpan.FromSeconds(10));
        Log.Line("ALIVE", scenario.Name);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Log.Line("DONE", scenario.Name);
    }

    static void ObserveUnhandledExceptions(Scenario scenario)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Line("UNHANDLED", scenario.Name, ("source", "AppDomain"), ("exception", e.ExceptionObject.ToString() ?? "?"));
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            Log.Line("UNHANDLED", scenario.Name, ("source", "AndroidEnvironment"), ("exception", e.Exception.ToString()));
    }
}

sealed class DetailPage : ContentPage
{
    public static ImageSource? IconSource { get; set; }

    public DetailPage()
    {
        Title = "Detail";
        Content = new Label { Text = "Detail" };
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { IconOverride = IconSource });
    }
}
