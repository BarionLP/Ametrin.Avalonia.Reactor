using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ametrin.Avalonia.Reactor;

internal sealed class Node(Element element, Node? parent, ReactorRoot root)
{
    private ChildHost? host;

    public Element Element { get; set; } = element;
    public Node? Parent { get; } = parent;
    public ReactorRoot Root { get; } = root;
    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;
    public List<Node> Children { get; set; } = [];
    public Component? Component { get; set; }
    public Control? OwnControl { get; set; }
    public Dictionary<RoutedEvent, Action<RoutedEventArgs>>? Handlers { get; set; }
    public HashSet<RoutedEvent>? AttachedEvents { get; set; }
    public Dictionary<AvaloniaProperty, Action<object?>>? PropertyHandlers { get; set; }
    public bool ListensToProperties { get; set; }
    public bool Dirty { get; set; }
    public bool Unmounted { get; set; }

    public Control Control => OwnControl ?? Children[0].Control;
    public ChildHost Host => host ??= ChildHost.For(OwnControl!);
}
