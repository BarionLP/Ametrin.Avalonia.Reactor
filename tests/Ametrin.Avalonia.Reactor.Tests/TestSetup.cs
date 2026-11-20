using Ametrin.Avalonia.Reactor;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace ReactorTests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal static class Ui
{
    private static readonly HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestApp), AvaloniaTestIsolationLevel.PerAssembly);

    public static Task<T> Run<T>(Func<T> action) => session.Dispatch(action, CancellationToken.None);

    public static ReactorHost Mount(Element element)
    {
        var host = new ReactorHost(element);
        var window = new Window { Content = host, Width = 400, Height = 400 };
        window.Show();
        Flush();
        window.UpdateLayout();
        return host;
    }

    public static void Flush() => Dispatcher.UIThread.RunJobs();
}
