using System.Reflection.Metadata;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Ametrin.Avalonia.Reactor;

internal sealed class ReactorRoot
{
    private static readonly Lock rootsLock = new();
    private static readonly List<WeakReference<ReactorRoot>> roots = [];

    private readonly HashSet<Node> dirty = [];
    private readonly List<EffectHook> effects = [];
    private readonly HashSet<Node> controlledChecks = [];
    private bool flushPosted;
    private bool attached;
    private bool resetPending;
    private Element? content;

    public ReactorRoot(Control host)
    {
        Dispatcher = host.Dispatcher;
        Node = new Node(RootElement.Instance, null, this) { OwnControl = host };
        if (!MetadataUpdater.IsSupported) return;
        lock (rootsLock)
        {
            roots.RemoveAll(r => !r.TryGetTarget(out _));
            roots.Add(new(this));
        }
    }

    public Dispatcher Dispatcher { get; }
    public Node Node { get; }
    public bool IsCommitting { get; private set; }
    public bool ResetHooksOnMismatch { get; private set; }
    public bool ResettingState { get; private set; }

    // resetState replaces every component instance in the tree while still reusing matching controls.
    public void Render(Element element, bool resetState = false)
    {
        content = element;
        resetPending |= resetState;
        if (attached) Commit([element]);
    }

    public void Attach()
    {
        attached = true;
        if (content is not null) Commit([content]);
    }

    // Deferred so moving the host to another parent within one dispatcher job keeps its state.
    public void Detach()
    {
        attached = false;
        Dispatcher.Post(() =>
        {
            if (!attached) Commit([]);
        });
    }

    public void Invalidate(Node node)
    {
        node.Dirty = true;
        dirty.Add(node);
        PostFlush();
    }

    public void QueueEffect(EffectHook hook, Func<Action?> effect)
    {
        if (hook.Pending is null) effects.Add(hook);
        hook.Pending = effect;
        PostFlush();
    }

    public void QueueControlledCheck(Node node)
    {
        controlledChecks.Add(node);
        PostFlush();
    }

    public static void InvalidateAll()
    {
        ReactorRoot[] live;
        lock (rootsLock)
            live = [.. roots.Select(r => r.TryGetTarget(out var root) ? root : null).OfType<ReactorRoot>()];
        foreach (var root in live)
            root.Dispatcher.Post(root.InvalidateTree);
    }

    private void InvalidateTree()
    {
        ResetHooksOnMismatch = true;
        Visit(Node);

        void Visit(Node node)
        {
            if (node.Component is not null) Invalidate(node);
            foreach (var child in node.Children) Visit(child);
        }
    }

    private void Commit(List<Element> elements)
    {
        IsCommitting = true;
        ResettingState = resetPending;
        resetPending = false;
        try
        {
            Reconciler.ReconcileChildren(Node, elements);
        }
        finally
        {
            IsCommitting = false;
            ResettingState = false;
        }
    }

    private void PostFlush()
    {
        if (flushPosted) return;
        flushPosted = true;
        Dispatcher.Post(Flush);
    }

    private void Flush()
    {
        flushPosted = false;
        IsCommitting = true;
        try
        {
            RenderDirty();
            foreach (var node in controlledChecks) Reconciler.RestoreControlledValues(node);
            controlledChecks.Clear();
        }
        finally
        {
            IsCommitting = false;
            ResetHooksOnMismatch = false;
        }
        RunEffects();
    }

    private void RenderDirty()
    {
        if (dirty.Count == 0) return;
        var batch = dirty.ToArray();
        dirty.Clear();
        Array.Sort(batch, static (a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var node in batch)
        {
            if (!node.Dirty || node.Unmounted) continue;
            var previous = node.Control;
            Reconciler.RenderComponent(node);
            if (node.Control != previous) Reconciler.ReplaceInParent(node, previous);
        }
    }

    private void RunEffects()
    {
        if (effects.Count == 0) return;
        var pending = effects.ToArray();
        effects.Clear();
        foreach (var hook in pending)
        {
            if (hook.Pending is not { } effect) continue;
            hook.Pending = null;
            hook.Cleanup?.Invoke();
            hook.Cleanup = effect();
        }
    }
}
