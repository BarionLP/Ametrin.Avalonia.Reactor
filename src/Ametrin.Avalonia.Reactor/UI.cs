using Avalonia.Controls.Templates;

[assembly: Ametrin.Avalonia.Reactor.GenerateElements("Avalonia.Controls")]

namespace Ametrin.Avalonia.Reactor;

public static partial class UI
{
    public static ComponentElement Component<TComponent>() where TComponent : Component, new() => new(typeof(TComponent), static () => new TComponent(), null);

    public static ComponentElement Component<TComponent, TProps>(TProps props) where TComponent : Component<TProps>, new() => new(typeof(TComponent), static () => new TComponent(), props);

    public static IDataTemplate ItemTemplate<TComponent, TItem>() where TComponent : Component<TItem>, new() => ItemTemplateCache<TComponent, TItem>.Instance;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class GenerateElementsAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
