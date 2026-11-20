using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ametrin.Avalonia.Reactor;

public abstract class Element
{
    public object? Key { get; internal set; }
}

internal readonly record struct PropValue<TProperty>(TProperty Property, object? Value);

public abstract class PlainProperty
{
    private protected PlainProperty() { }

    internal abstract void Apply(Control control, object? value);
    internal abstract void Reset(Control control);
}

// For CLR properties that are not Avalonia properties. Values are compared with Equals, so prefer values that compare by content.
public sealed class PlainProperty<TControl, TValue>(Action<TControl, TValue> apply, Action<TControl> reset) : PlainProperty where TControl : Control
{
    internal override void Apply(Control control, object? value) => apply((TControl)control, (TValue)value!);
    internal override void Reset(Control control) => reset((TControl)control);
}

public abstract class ControlElement : Element
{
    internal List<PropValue<AvaloniaProperty>> Props { get; } = [];
    internal List<PropValue<PlainProperty>>? PlainProps { get; private set; }
    internal Dictionary<RoutedEvent, Action<RoutedEventArgs>>? Events { get; private protected set; }
    internal Dictionary<AvaloniaProperty, Action<object?>>? PropertyHandlers { get; private protected set; }
    internal List<Element> ChildElements { get; } = [];
    internal abstract Type ControlType { get; }
    internal abstract Control Create();

    internal void SetPlain(PlainProperty property, object? value) => Put(PlainProps ??= [], property, value);

    private protected static void Put<TProperty>(List<PropValue<TProperty>> props, TProperty property, object? value) where TProperty : class
    {
        for (var i = 0; i < props.Count; i++)
        {
            if (props[i].Property != property) continue;
            props[i] = new(property, value);
            return;
        }
        props.Add(new(property, value));
    }
}

public sealed class ControlElement<T> : ControlElement where T : Control, new()
{
    internal override Type ControlType => typeof(T);
    internal override Control Create() => new T();

    public ControlElement<T> Set<TValue>(AvaloniaProperty<TValue> property, TValue value)
    {
        Put(Props, property, value);
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

    public static ControlElement<T> Set<T, TControl, TValue>(this ControlElement<T> element, PlainProperty<TControl, TValue> property, TValue value) where T : TControl, new() where TControl : Control
    {
        element.SetPlain(property, value);
        return element;
    }
}
