using Ametrin.Avalonia.Reactor;
using Avalonia.Controls;
using Avalonia.Interactivity;
using static Ametrin.Avalonia.Reactor.UI;

namespace ReactorTests;

public sealed class ComponentTests
{
    [Test]
    public async Task ClicksUpdateStateThroughTheLatestHandler()
    {
        var (content, sameControl) = await Ui.Run(() =>
        {
            var host = Ui.Mount(Component<Counter>());
            var button = (Button)host.Child!;
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.Flush();
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Ui.Flush();
            return (button.Content, ReferenceEquals(button, host.Child));
        });

        await Assert.That(content).IsEqualTo("Count 2");
        await Assert.That(sameControl).IsTrue();
    }

    [Test]
    public async Task ComponentWithEqualPropsIsNotRenderedAgain()
    {
        var renders = await Ui.Run(() =>
        {
            var log = new RenderLog();
            var host = Ui.Mount(Component<Logged, LoggedProps>(new(log, "a")));
            host.Render(Component<Logged, LoggedProps>(new(log, "a")));
            var afterEqual = log.Renders;
            host.Render(Component<Logged, LoggedProps>(new(log, "b")));
            return (afterEqual, log.Renders);
        });

        await Assert.That(renders).IsEqualTo((1, 2));
    }

    [Test]
    public async Task EffectsRunAfterCommitAndCleanUpOnChangeAndUnmount()
    {
        var steps = await Ui.Run(() =>
        {
            var log = new List<string>();
            var host = Ui.Mount(Component<Effectful, EffectProps>(new(log, 1)));
            host.Render(Component<Effectful, EffectProps>(new(log, 2)));
            host.Render(Component<Effectful, EffectProps>(new(log, 3)));
            log.Add("committed 3");
            Ui.Flush();
            var window = (Window)host.Parent!;
            window.Content = null;
            Ui.Flush();
            host.Render(Component<Effectful, EffectProps>(new(log, 4)));
            Ui.Flush();
            log.Add("detached");
            window.Content = host;
            Ui.Flush();
            var remounted = TopLevel.GetTopLevel(host.Child) is not null;
            host.Render(TextBlock("gone"));
            return (string.Join(",", log), remounted);
        });

        await Assert.That(steps).IsEqualTo(("run 1,committed 3,cleanup 1,run 3,cleanup 3,detached,run 4,cleanup 4", true));
    }

    [Test]
    public async Task StateChangeThatSwapsTheRootControlUpdatesTheParent()
    {
        var childType = await Ui.Run(() =>
        {
            var remote = new Remote();
            var host = Ui.Mount(StackPanel(TextBlock("before"), Component<Switch, Remote>(remote)));
            remote.Set!(true);
            Ui.Flush();
            return ((StackPanel)host.Child!).Children[1].GetType();
        });

        await Assert.That(childType).IsEqualTo(typeof(Button));
    }

    [Test]
    public async Task ControlledTextBoxRevertsWhenStateIsNotUpdated()
    {
        var text = await Ui.Run(() =>
        {
            var host = Ui.Mount(TextBox().Text("fixed", _ => { }));
            var textBox = (TextBox)host.Child!;
            textBox.Text = "typed";
            Ui.Flush();
            return textBox.Text;
        });

        await Assert.That(text).IsEqualTo("fixed");
    }

    [Test]
    public async Task ControlledTextBoxFeedsStateBack()
    {
        var mirrored = await Ui.Run(() =>
        {
            var host = Ui.Mount(Component<Mirror>());
            var panel = (StackPanel)host.Child!;
            ((TextBox)panel.Children[0]).Text = "abc";
            Ui.Flush();
            return (((TextBox)panel.Children[0]).Text, ((TextBlock)panel.Children[1]).Text);
        });

        await Assert.That(mirrored).IsEqualTo(("abc", "abc"));
    }

    private sealed class Counter : Component
    {
        protected override Element Render()
        {
            var (count, setCount) = UseState(0);
            return Button($"Count {count}").OnClick(() => setCount(count + 1));
        }
    }

    private sealed class RenderLog
    {
        public int Renders { get; set; }
    }

    private sealed record LoggedProps(RenderLog Log, string Text);

    private sealed class Logged : Component<LoggedProps>
    {
        protected override Element Render()
        {
            Props.Log.Renders++;
            return TextBlock(Props.Text);
        }
    }

    private sealed record EffectProps(List<string> Log, int Dependency);

    private sealed class Effectful : Component<EffectProps>
    {
        protected override Element Render()
        {
            var (log, dependency) = Props;
            UseEffect(() =>
            {
                log.Add($"run {dependency}");
                return () => log.Add($"cleanup {dependency}");
            }, dependency);
            return TextBlock($"{dependency}");
        }
    }

    private sealed class Remote
    {
        public Action<bool>? Set { get; set; }
    }

    private sealed class Switch : Component<Remote>
    {
        protected override Element Render()
        {
            var (on, setOn) = UseState(false);
            Props.Set = setOn;
            return on ? Button("on") : TextBlock("off");
        }
    }

    private sealed class Mirror : Component
    {
        protected override Element Render()
        {
            var (text, setText) = UseState("");
            return StackPanel(TextBox().Text(text, value => setText(value ?? "")), TextBlock(text));
        }
    }
}
