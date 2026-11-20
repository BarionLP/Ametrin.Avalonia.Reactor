using Ametrin.Avalonia.Reactor;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using static Ametrin.Avalonia.Reactor.UI;

namespace ReactorTests;

public sealed class ReconcilerTests
{
    [Test]
    public async Task RemovedPropIsClearedInsteadOfDefaulted()
    {
        var (widthBefore, isSetAfter) = await Ui.Run(() =>
        {
            var host = Ui.Mount(Border().Width(100));
            var border = (Border)host.Child!;
            var before = border.Width;
            host.Render(Border());
            return (before, border.IsSet(Layoutable.WidthProperty));
        });

        await Assert.That(widthBefore).IsEqualTo(100);
        await Assert.That(isSetAfter).IsFalse();
    }

    [Test]
    public async Task KeyedChildrenKeepTheirControlsWhenReordered()
    {
        var (sameInstances, texts) = await Ui.Run(() =>
        {
            var host = Ui.Mount(StackPanel(TextBlock("a").WithKey("a"), TextBlock("b").WithKey("b"), TextBlock("c").WithKey("c")));
            var panel = (StackPanel)host.Child!;
            var before = panel.Children.ToArray();
            host.Render(StackPanel(TextBlock("c").WithKey("c"), TextBlock("a").WithKey("a")));
            var after = panel.Children.ToArray();
            return (ReferenceEquals(after[0], before[2]) && ReferenceEquals(after[1], before[0]), string.Join(",", after.Cast<TextBlock>().Select(t => t.Text)));
        });

        await Assert.That(sameInstances).IsTrue();
        await Assert.That(texts).IsEqualTo("c,a");
    }

    [Test]
    public async Task ChangedControlTypeReplacesTheControl()
    {
        var (sameType, childType) = await Ui.Run(() =>
        {
            var host = Ui.Mount(StackPanel(TextBlock("a"), TextBlock("b")));
            var panel = (StackPanel)host.Child!;
            var second = panel.Children[1];
            host.Render(StackPanel(TextBlock("a"), Button("b")));
            return (ReferenceEquals(second, panel.Children[1]), panel.Children[1].GetType());
        });

        await Assert.That(sameType).IsFalse();
        await Assert.That(childType).IsEqualTo(typeof(Button));
    }

    [Test]
    public async Task ItemTemplateResetsStateButKeepsControlsWhenTheItemChanges()
    {
        var result = await Ui.Run(() =>
        {
            var host = Ui.Mount(ListBox().ItemsSource(new[] { 2, 3 }).ItemTemplate(ItemTemplate<Square, int>()));
            var blocks = host.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var texts = string.Join(",", blocks.Select(t => t.Text));
            ((Control)blocks[0].Parent!).DataContext = 5;
            return (texts, blocks[0].Text, ReferenceEquals(host.GetVisualDescendants().OfType<TextBlock>().First(), blocks[0]));
        });

        await Assert.That(result).IsEqualTo(("4,9", "25", true));
    }

    private sealed class Square : Component<int>
    {
        protected override Element Render() => Component<InitialSquare, int>(Props);
    }

    // Shows the square of its first props, so the text only changes when its state resets.
    private sealed class InitialSquare : Component<int>
    {
        protected override Element Render()
        {
            var (initial, _) = UseState(Props);
            return TextBlock($"{initial * initial}");
        }
    }
}
