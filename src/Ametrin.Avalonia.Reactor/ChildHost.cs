using Avalonia.Controls;

namespace Ametrin.Avalonia.Reactor;

internal abstract class ChildHost
{
    public abstract int Count { get; }
    public abstract Control Get(int index);
    public abstract void Insert(int index, Control child);
    public abstract void RemoveAt(int index);
    public abstract void Move(int from, int to);

    public int IndexOf(Control child)
    {
        for (var i = 0; i < Count; i++)
            if (Get(i) == child) return i;
        return -1;
    }

    public static ChildHost For(Control control) => control switch
    {
        Panel panel => new PanelHost(panel),
        ContentControl content => new SingleHost(control, () => content.Content as Control, child => content.Content = child),
        Decorator decorator => new SingleHost(control, () => decorator.Child, child => decorator.Child = child),
        _ => throw new NotSupportedException($"{control.GetType().Name} cannot have child elements"),
    };
}

internal sealed class PanelHost(Panel panel) : ChildHost
{
    public override int Count => panel.Children.Count;
    public override Control Get(int index) => panel.Children[index];
    public override void Insert(int index, Control child) => panel.Children.Insert(index, child);
    public override void RemoveAt(int index) => panel.Children.RemoveAt(index);
    public override void Move(int from, int to) => panel.Children.Move(from, to);
}

internal sealed class SingleHost(Control owner, Func<Control?> get, Action<Control?> set) : ChildHost
{
    public override int Count => get() is null ? 0 : 1;
    public override Control Get(int index) => index == 0 && get() is { } child ? child : throw new ArgumentOutOfRangeException(nameof(index));

    public override void Insert(int index, Control child)
    {
        if (index != 0 || Count != 0) throw new InvalidOperationException($"{owner.GetType().Name} accepts a single child element");
        set(child);
    }

    public override void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
        set(null);
    }

    public override void Move(int from, int to) => throw new InvalidOperationException($"{owner.GetType().Name} accepts a single child element");
}
