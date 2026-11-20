using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ametrin.Avalonia.Reactor;

internal static class Reconciler
{
    public static Node Reconcile(Node? old, Element next, Node parent)
    {
        if (old is not null && CanReuse(old.Element, next))
        {
            Update(old, next);
            return old;
        }
        if (old is not null) Unmount(old);
        return Mount(next, parent);
    }

    public static void RenderComponent(Node node)
    {
        node.Dirty = false;
        var element = node.Component!.RunRender(node.Root.ResetHooksOnMismatch);
        if (node.Children.Count == 0) node.Children.Add(Reconcile(null, element, node));
        else node.Children[0] = Reconcile(node.Children[0], element, node);
    }

    public static void ReconcileChildren(Node parent, List<Element> next)
    {
        var old = parent.Children;
        if (next.Count == 0 && old.Count == 0) return;

        Dictionary<object, Node>? keyed = null;
        foreach (var child in old)
            if (child.Element.Key is { } key) (keyed ??= []).Add(key, child);

        HashSet<object>? keys = null;
        var cursor = 0;
        var result = new List<Node>(next.Count);
        foreach (var element in next)
        {
            Node? match = null;
            if (element.Key is not { } key) match = NextUnkeyed(old, ref cursor);
            else if (!(keys ??= []).Add(key)) throw new InvalidOperationException($"Duplicate key '{key}' among the children of {parent.Element.GetType().Name}");
            else keyed?.Remove(key, out match);
            result.Add(Reconcile(match, element, parent));
        }

        if (keyed is not null) foreach (var stale in keyed.Values) Unmount(stale);
        while (NextUnkeyed(old, ref cursor) is { } stale) Unmount(stale);

        parent.Children = result;
        SyncHost(parent.Host, result);
    }

    public static void ReplaceInParent(Node node, Control previous)
    {
        var owner = node.Parent;
        while (owner is { OwnControl: null }) owner = owner.Parent;
        var host = owner!.Host;
        var index = host.IndexOf(previous);
        host.RemoveAt(index);
        host.Insert(index, node.Control);
    }

    public static void Unmount(Node node)
    {
        node.Unmounted = true;
        foreach (var child in node.Children) Unmount(child);
        node.Component?.Unmount();
    }

    public static void RestoreControlledValues(Node node)
    {
        if (node.Unmounted || node.Element is not ControlElement { PropertyHandlers: { } handlers } element) return;
        var control = node.OwnControl!;
        foreach (var (property, value) in element.Props)
            if (handlers.ContainsKey(property) && !Equals(control.GetValue(property), value))
                control.SetValue(property, value);
    }

    private static bool CanReuse(Element old, Element next) => Equals(old.Key, next.Key) && (old, next) switch
    {
        (ControlElement a, ControlElement b) => a.ControlType == b.ControlType,
        (ComponentElement a, ComponentElement b) => a.ComponentType == b.ComponentType,
        _ => false,
    };

    private static Node? NextUnkeyed(List<Node> nodes, ref int cursor)
    {
        while (cursor < nodes.Count)
        {
            var node = nodes[cursor++];
            if (node.Element.Key is null) return node;
        }
        return null;
    }

    private static Node Mount(Element element, Node parent)
    {
        var node = new Node(element, parent, parent.Root);
        switch (element)
        {
            case ControlElement control:
                node.OwnControl = control.Create();
                UpdateProps(node.OwnControl, null, control);
                UpdatePlainProps(node.OwnControl, null, control.PlainProps);
                UpdateEvents(node, control);
                UpdatePropertyHandlers(node, control);
                ReconcileChildren(node, control.ChildElements);
                break;
            case ComponentElement component:
                MountComponent(node, component);
                break;
            default:
                throw new NotSupportedException($"Cannot mount {element.GetType().Name}");
        }
        return node;
    }

    private static void MountComponent(Node node, ComponentElement element)
    {
        node.Component = element.Create();
        node.Component.Attach(node, element.Props);
        RenderComponent(node);
    }

    private static void Update(Node node, Element next)
    {
        var previous = node.Element;
        var reset = node.Root.ResettingState;
        if (ReferenceEquals(previous, next) && !node.Dirty && !reset) return;
        node.Element = next;
        switch (next)
        {
            case ControlElement control:
                var old = (ControlElement)previous;
                UpdateProps(node.OwnControl!, old.Props, control);
                UpdatePlainProps(node.OwnControl!, old.PlainProps, control.PlainProps);
                UpdateEvents(node, control);
                UpdatePropertyHandlers(node, control);
                ReconcileChildren(node, control.ChildElements);
                break;
            case ComponentElement component when reset:
                node.Component!.Unmount();
                MountComponent(node, component);
                break;
            case ComponentElement component when node.Dirty || !Equals(((ComponentElement)previous).Props, component.Props):
                node.Component!.SetProps(component.Props);
                RenderComponent(node);
                break;
        }
    }

    // Controlled props compare against the live value because the control may have changed it since the last render.
    private static void UpdateProps(Control control, List<PropValue<AvaloniaProperty>>? previous, ControlElement next)
    {
        foreach (var (property, value) in next.Props)
        {
            var controlled = next.PropertyHandlers?.ContainsKey(property) ?? false;
            var changed = controlled ? !Equals(control.GetValue(property), value) : !TryFind(previous, property, out var old) || !Equals(old, value);
            if (changed) control.SetValue(property, value);
        }
        if (previous is null) return;
        foreach (var (property, _) in previous)
            if (!TryFind(next.Props, property, out _)) control.ClearValue(property);
    }

    private static void UpdatePlainProps(Control control, List<PropValue<PlainProperty>>? previous, List<PropValue<PlainProperty>>? next)
    {
        if (next is not null)
            foreach (var (property, value) in next)
                if (!TryFind(previous, property, out var old) || !Equals(old, value)) property.Apply(control, value);
        if (previous is not null)
            foreach (var (property, _) in previous)
                if (!TryFind(next, property, out _)) property.Reset(control);
    }

    private static bool TryFind<TProperty>(List<PropValue<TProperty>>? props, TProperty property, out object? value) where TProperty : class
    {
        if (props is not null)
        {
            foreach (var prop in props)
            {
                if (prop.Property != property) continue;
                value = prop.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static void UpdateEvents(Node node, ControlElement next)
    {
        if (next.Events is { } events)
        {
            foreach (var routedEvent in events.Keys)
            {
                if (!(node.AttachedEvents ??= []).Add(routedEvent)) continue;
                node.OwnControl!.AddHandler(routedEvent, (EventHandler<RoutedEventArgs>)((_, args) =>
                {
                    if (node.Handlers is { } handlers && handlers.TryGetValue(routedEvent, out var handler)) handler(args);
                }));
            }
        }
        node.Handlers = next.Events;
    }

    private static void UpdatePropertyHandlers(Node node, ControlElement next)
    {
        if (next.PropertyHandlers is not null && !node.ListensToProperties)
        {
            node.ListensToProperties = true;
            node.OwnControl!.PropertyChanged += (_, args) => OnPropertyChanged(node, args);
        }
        node.PropertyHandlers = next.PropertyHandlers;
    }

    private static void OnPropertyChanged(Node node, AvaloniaPropertyChangedEventArgs args)
    {
        if (node.Root.IsCommitting || node.Unmounted || node.PropertyHandlers is not { } handlers || !handlers.TryGetValue(args.Property, out var handler)) return;
        handler(args.NewValue);
        node.Root.QueueControlledCheck(node);
    }

    private static void SyncHost(ChildHost host, List<Node> nodes)
    {
        if (InSync(host, nodes)) return;

        var wanted = new HashSet<Control>(nodes.Count);
        foreach (var node in nodes) wanted.Add(node.Control);

        for (var i = host.Count - 1; i >= 0; i--)
            if (!wanted.Contains(host.Get(i))) host.RemoveAt(i);

        for (var i = 0; i < nodes.Count; i++)
        {
            var control = nodes[i].Control;
            if (i < host.Count && host.Get(i) == control) continue;
            var index = host.IndexOf(control);
            if (index == -1) host.Insert(i, control);
            else host.Move(index, i);
        }
    }

    private static bool InSync(ChildHost host, List<Node> nodes)
    {
        if (host.Count != nodes.Count) return false;
        for (var i = 0; i < nodes.Count; i++)
            if (host.Get(i) != nodes[i].Control) return false;
        return true;
    }
}
