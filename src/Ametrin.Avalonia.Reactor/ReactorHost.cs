using System.Reflection.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

[assembly: MetadataUpdateHandler(typeof(Ametrin.Avalonia.Reactor.HotReload))]

namespace Ametrin.Avalonia.Reactor;

public sealed class ReactorHost : Decorator
{
    private readonly ReactorRoot root;

    public ReactorHost(Element content)
    {
        root = new ReactorRoot(this);
        root.Render(content);
    }

    public void Render(Element content) => root.Render(content);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        root.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        root.Detach();
    }
}

internal sealed class ReactorItemHost<TComponent, TItem> : Decorator where TComponent : Component<TItem>, new()
{
    private readonly ReactorRoot root;

    public ReactorItemHost() => root = new ReactorRoot(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TItem item) root.Render(UI.Component<TComponent, TItem>(item), resetState: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        root.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        root.Detach();
    }
}

internal static class ItemTemplateCache<TComponent, TItem> where TComponent : Component<TItem>, new()
{
    public static readonly IDataTemplate Instance = new FuncDataTemplate<TItem>((_, _) => new ReactorItemHost<TComponent, TItem>(), supportsRecycling: true);
}

internal static class HotReload
{
    public static void UpdateApplication(Type[]? updatedTypes) => ReactorRoot.InvalidateAll();
}
