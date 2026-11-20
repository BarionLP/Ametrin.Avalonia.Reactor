using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ametrin.Avalonia.Reactor;

public abstract class Element
{
    public object? Key { get; internal set; }
}

internal readonly record struct PropValue(AvaloniaProperty Property, object? Value);

public abstract class ControlElement : Element
{
    internal List<PropValue> Props { get; } = [];
    internal Dictionary<RoutedEvent, Action<RoutedEventArgs>>? Events { get; private protected set; }
    internal Dictionary<AvaloniaProperty, Action<object?>>? PropertyHandlers { get; private protected set; }
    internal List<Element> ChildElements { get; } = [];
    internal abstract Type ControlType { get; }
    internal abstract Control Create();
}

public sealed class ControlElement<T> : ControlElement where T : Control, new()
{
    internal override Type ControlType => typeof(T);
    internal override Control Create() => new T();

    public ControlElement<T> Set<TValue>(AvaloniaProperty<TValue> property, TValue value)
    {
        for (var i = 0; i < Props.Count; i++)
        {
            if (Props[i].Property != property) continue;
            Props[i] = new(property, value);
            return this;
        }
        Props.Add(new(property, value));
        return this;
    }

    public ControlElement<T> On<TArgs>(RoutedEvent<TArgs> routedEvent, Action<TArgs> handler) where TArgs : RoutedEventArgs
    {
        (Events ??= [])[routedEvent] = args => handler((TArgs)args);
        return this;
    }

    public ControlElement<T> OnChanged<TValue>(AvaloniaProperty<TValue> property, Action<TValue> handler)
    {
        (PropertyHandlers ??= [])[property] = value => handler((TValue)value!);
        return this;
    }

    public ControlElement<T> Children(params ReadOnlySpan<Element> children)
    {
        ChildElements.AddRange(children);
        return this;
    }
}

public sealed class ComponentElement : Element
{
    internal ComponentElement(Type componentType, Func<Component> create, object? props)
    {
        ComponentType = componentType;
        Create = create;
        Props = props;
    }

    internal Type ComponentType { get; }
    internal Func<Component> Create { get; }
    internal object? Props { get; }
}

internal sealed class RootElement : Element
{
    public static RootElement Instance { get; } = new();
}

public static class ElementExtensions
{
    public static TElement WithKey<TElement>(this TElement element, object key) where TElement : Element
    {
        element.Key = key;
        return element;
    }
}
