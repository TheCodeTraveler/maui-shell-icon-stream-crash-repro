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
        var homePage = new ContentPage { Title = "Home", Content = new Label { Text = scenario.Name } };
        shell.Items.Add(new ShellContent { Title = "Home", Route = "home", Content = homePage });

        switch (scenario.Placement)
        {
            case IconPlacement.FlyoutIcon:
                shell.FlyoutIcon = scenario.CreateIconSource();
                break;
            case IconPlacement.BackButtonIconOverride:
                DetailPage.IconSource = scenario.CreateIconSource();
                break;
            case IconPlacement.ImageAndToolbarItem:
                var sharedSource = scenario.CreateIconSource();
                homePage.Content = new VerticalStackLayout { Children = { new Label { Text = scenario.Name }, new Image { Source = sharedSource, HeightRequest = 24, WidthRequest = 24 } } };
                homePage.ToolbarItems.Add(new ToolbarItem { IconImageSource = sharedSource });
                break;
        }

        _ = DriveScenarioAsync(scenario, shell);
        return new Window(shell);
    }

    static async Task DriveScenarioAsync(Scenario scenario, Shell shell)
    {
        if (scenario.ChangesForegroundColor)
        {
            // Waits for the first icon load instead of a fixed delay, so the second update always lands while it is pending
            await Task.WhenAny(scenario.FirstStreamRequested, Task.Delay(TimeSpan.FromSeconds(20)));
            Log.Line("SECOND_UPDATE", scenario.Name);
            await MainThread.InvokeOnMainThreadAsync(() => Shell.SetForegroundColor(shell, Colors.Red));
        }
        else if (scenario.Placement == IconPlacement.BackButtonIconOverride)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
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
