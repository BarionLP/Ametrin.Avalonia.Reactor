using Ametrin.Avalonia.Reactor;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using ReactorSample;
using static Ametrin.Avalonia.Reactor.UI;

return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window {
                Title = "Reactor on Avalonia",
                Width = 720,
                Height = 640,
                TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur],
                Background = Brushes.Transparent,
                Content = new ReactorHost(Component<Gallery>()),
            };
        base.OnFrameworkInitializationCompleted();
    }
}
