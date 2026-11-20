using System.Diagnostics.CodeAnalysis;

namespace Ametrin.Avalonia.Reactor;

public abstract class Component
{
    private readonly List<object> hooks = [];
    private Node? node;
    private int hookIndex;

    protected abstract Element Render();

    protected (T Value, Action<T> Set) UseState<T>(T initial)
    {
        if (!NextHook(out StateHook<T>? hook)) hook = AddHook(new StateHook<T>(initial, this));
        return (hook.Value, hook.Set);
    }

    protected T UseMemo<T>(Func<T> factory, params object?[] dependencies)
    {
        if (!NextHook(out MemoHook<T>? hook)) return AddHook(new MemoHook<T>(factory(), dependencies)).Value;
        if (!hook.Dependencies.SequenceEqual(dependencies))
        {
            hook.Value = factory();
            hook.Dependencies = dependencies;
        }
        return hook.Value;
    }

    protected void UseEffect(Func<Action?> effect, params object?[] dependencies)
    {
        if (!NextHook(out EffectHook? hook)) hook = AddHook(new EffectHook());
        else if (hook.Dependencies!.SequenceEqual(dependencies)) return;
        hook.Dependencies = dependencies;
        node!.Root.QueueEffect(hook, effect);
    }

    protected void UseEffect(Action effect, params object?[] dependencies) => UseEffect(() =>
    {
        effect();
        return null;
    }, dependencies);

    internal virtual void SetProps(object? props) { }

    internal void Attach(Node owner, object? props)
    {
        node = owner;
        SetProps(props);
    }

    internal Element RunRender(bool resetOnHookMismatch)
    {
        try
        {
            return RenderWithHooks();
        }
        catch (HookOrderException) when (resetOnHookMismatch)
        {
            Unmount();
            hooks.Clear();
            return RenderWithHooks();
        }
    }

    internal void Unmount()
    {
        foreach (var hook in hooks)
        {
            if (hook is not EffectHook effect) continue;
            var cleanup = effect.Cleanup;
            effect.Pending = null;
            effect.Cleanup = null;
            cleanup?.Invoke();
        }
    }

    internal void Invalidate()
    {
        if (node is { Unmounted: false }) node.Root.Invalidate(node);
    }

    internal void Dispatch(Action action)
    {
        if (node is null || node.Root.Dispatcher.CheckAccess()) action();
        else node.Root.Dispatcher.Post(action);
    }

    private Element RenderWithHooks()
    {
        hookIndex = 0;
        var element = Render();
        if (hookIndex != hooks.Count) throw new HookOrderException(GetType());
        return element;
    }

    private bool NextHook<THook>([NotNullWhen(true)] out THook? hook) where THook : class
    {
        if (hookIndex == hooks.Count)
        {
            hook = null;
            return false;
        }
        hook = hooks[hookIndex++] as THook ?? throw new HookOrderException(GetType());
        return true;
    }

    private THook AddHook<THook>(THook hook) where THook : class
    {
        hooks.Add(hook);
        hookIndex++;
        return hook;
    }
}

public abstract class Component<TProps> : Component
{
    public TProps Props { get; private set; } = default!;

    internal override void SetProps(object? props) => Props = (TProps)props!;
}

internal sealed class HookOrderException(Type component) : InvalidOperationException($"Hooks in {component.Name} must be called in the same order on every render");

internal sealed class StateHook<T>
{
    public StateHook(T initial, Component owner)
    {
        Value = initial;
        Set = value => owner.Dispatch(() =>
        {
            if (EqualityComparer<T>.Default.Equals(Value, value)) return;
            Value = value;
            owner.Invalidate();
        });
    }

    public T Value { get; private set; }
    public Action<T> Set { get; }
}

internal sealed class MemoHook<T>(T value, object?[] dependencies)
{
    public T Value { get; set; } = value;
    public object?[] Dependencies { get; set; } = dependencies;
}

internal sealed class EffectHook
{
    public object?[]? Dependencies { get; set; }
    public Func<Action?>? Pending { get; set; }
    public Action? Cleanup { get; set; }
}
