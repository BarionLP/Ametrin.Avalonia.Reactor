using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Ametrin.Avalonia.Reactor.Generator;

internal sealed class ModelBuilder
{
    private const string AttributeName = "Ametrin.Avalonia.Reactor.GenerateElementsAttribute";
    private const string ElementType = "global::Ametrin.Avalonia.Reactor.ControlElement";
    private static readonly HashSet<string> ReservedNames = ["Set", "On", "OnChanged", "Children", "WithKey", "Key", "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Component"];
    private static readonly HashSet<string> SkippedAttributes = ["System.ObsoleteAttribute", "System.Diagnostics.CodeAnalysis.ExperimentalAttribute", "Avalonia.Metadata.UnstableAttribute"];
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly INamedTypeSymbol control;
    private readonly INamedTypeSymbol topLevel;
    private readonly INamedTypeSymbol panel;
    private readonly INamedTypeSymbol contentControl;
    private readonly INamedTypeSymbol decorator;
    private readonly INamedTypeSymbol styledProperty;
    private readonly INamedTypeSymbol directProperty;
    private readonly INamedTypeSymbol attachedProperty;
    private readonly INamedTypeSymbol routedEvent;
    private readonly CancellationToken token;

    private ModelBuilder(Compilation compilation, CancellationToken token)
    {
        control = compilation.GetTypeByMetadataName("Avalonia.Controls.Control")!;
        topLevel = compilation.GetTypeByMetadataName("Avalonia.Controls.TopLevel")!;
        panel = compilation.GetTypeByMetadataName("Avalonia.Controls.Panel")!;
        contentControl = compilation.GetTypeByMetadataName("Avalonia.Controls.ContentControl")!;
        decorator = compilation.GetTypeByMetadataName("Avalonia.Controls.Decorator")!;
        styledProperty = compilation.GetTypeByMetadataName("Avalonia.StyledProperty`1")!;
        directProperty = compilation.GetTypeByMetadataName("Avalonia.DirectProperty`2")!;
        attachedProperty = compilation.GetTypeByMetadataName("Avalonia.AttachedProperty`1")!;
        routedEvent = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent`1")!;
        this.token = token;
    }

    public static GenerationModel Build(Compilation compilation, CancellationToken token)
    {
        var assemblyNames = compilation.Assembly.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == AttributeName && a.ConstructorArguments is [{ Value: string }])
            .Select(a => (string)a.ConstructorArguments[0].Value!)
            .ToHashSet();
        if (assemblyNames.Count == 0) return GenerationModel.Empty;

        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols.Where(a => assemblyNames.Contains(a.Name)).ToImmutableArray();
        if (assemblies.IsEmpty) return GenerationModel.Empty;
        if (Cache.TryGetValue(assemblies[0], out var cached) && cached.Assemblies.SequenceEqual(assemblies, SymbolEqualityComparer.Default.Equals)) return cached.Model;

        var builder = new ModelBuilder(compilation, token);
        if (builder.control is null) return GenerationModel.Empty;

        var types = assemblies
            .SelectMany(a => TopLevelTypes(a.GlobalNamespace))
            .Where(t => t is { DeclaredAccessibility: Accessibility.Public, IsGenericType: false, TypeKind: TypeKind.Class } && !IsSkipped(t))
            .OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal)
            .ToImmutableArray();

        var model = builder.Build(types);
        Cache.AddOrUpdate(assemblies[0], new(assemblies, model));
        return model;
    }

    // Roslyn reuses metadata assembly symbols across compilations while the references stay the same, so edits to source skip the scan.
    private static readonly ConditionalWeakTable<IAssemblySymbol, CachedModel> Cache = new();

    private sealed record CachedModel(ImmutableArray<IAssemblySymbol> Assemblies, GenerationModel Model);

    private GenerationModel Build(ImmutableArray<INamedTypeSymbol> types)
    {
        var controls = types.Where(IsMountableControl).GroupBy(t => t.Name).Select(g => g.First()).ToImmutableArray();

        var owners = controls
            .SelectMany(Chain)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(t => Chain(t).Count())
            .ThenBy(t => t.ToDisplayString(), StringComparer.Ordinal)
            .ToImmutableArray();

        var generatedNames = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
        var classNames = new HashSet<string>();
        var ownerModels = ImmutableArray.CreateBuilder<OwnerModel>();

        foreach (var owner in owners)
        {
            token.ThrowIfCancellationRequested();
            var inherited = new HashSet<string>(Chain(owner).Skip(1).SelectMany(t => generatedNames.TryGetValue(t, out var names) ? names : []));
            var names = new HashSet<string>();
            var properties = ImmutableArray.CreateBuilder<MemberModel>();
            var events = ImmutableArray.CreateBuilder<MemberModel>();

            foreach (var field in PublicStaticFields(owner))
            {
                if (TryProperty(field, owner) is { } property && !inherited.Contains(property.Name) && names.Add(property.Name)) properties.Add(property);
            }
            foreach (var field in PublicStaticFields(owner))
            {
                if (TryEvent(field) is { } evt && !inherited.Contains(evt.Name) && names.Add(evt.Name)) events.Add(evt);
            }

            generatedNames[owner] = names;
            if (properties.Count == 0 && events.Count == 0) continue;

            var (receiver, typeParameters, constraint) = Receiver(owner);
            ownerModels.Add(new(UniqueClassName(classNames, owner.Name + "ElementExtensions"), receiver, typeParameters, constraint, properties.ToImmutable(), events.ToImmutable()));
        }

        var attachedNames = new HashSet<string>();
        foreach (var owner in types)
        {
            token.ThrowIfCancellationRequested();
            var attached = ImmutableArray.CreateBuilder<MemberModel>();
            foreach (var field in PublicStaticFields(owner))
            {
                if (field.Type is not INamedTypeSymbol { IsGenericType: true } type || !Is(type, attachedProperty) || !field.Name.EndsWith("Property", StringComparison.Ordinal)) continue;
                var valueType = type.TypeArguments[0];
                var name = owner.Name + field.Name[..^"Property".Length];
                if (!IsPublic(valueType) || ReservedNames.Contains(name) || !attachedNames.Add(name)) continue;
                attached.Add(new(name, FieldAccess(field), valueType.ToDisplayString(TypeFormat)));
            }
            if (attached.Count == 0) continue;
            ownerModels.Add(new(UniqueClassName(classNames, owner.Name + "AttachedElementExtensions"), $"{ElementType}<T>", "<T>", $" where T : {control.ToDisplayString(TypeFormat)}, new()", attached.ToImmutable(), ImmutableArray<MemberModel>.Empty));
        }

        var controlModels = controls
            .Where(t => !ReservedNames.Contains(t.Name))
            .Select(t => new ControlModel(t.Name, t.ToDisplayString(TypeFormat), KindOf(t), TextPropertyOf(t)))
            .ToImmutableArray();

        return new(controlModels, ownerModels.ToImmutable());
    }

    private MemberModel? TryProperty(IFieldSymbol field, INamedTypeSymbol owner)
    {
        if (field.Type is not INamedTypeSymbol { IsGenericType: true } type || !field.Name.EndsWith("Property", StringComparison.Ordinal)) return null;
        var name = field.Name[..^"Property".Length];
        if (ReservedNames.Contains(name)) return null;

        ITypeSymbol valueType;
        if (Is(type, styledProperty)) valueType = type.TypeArguments[0];
        else if (Is(type, directProperty) && HasPublicSetter(owner, name)) valueType = type.TypeArguments[1];
        else return null;

        return IsPublic(valueType) ? new(name, FieldAccess(field), valueType.ToDisplayString(TypeFormat)) : null;
    }

    private MemberModel? TryEvent(IFieldSymbol field)
    {
        if (field.Type is not INamedTypeSymbol { IsGenericType: true } type || !Is(type, routedEvent) || !field.Name.EndsWith("Event", StringComparison.Ordinal)) return null;
        var argsType = type.TypeArguments[0];
        var name = "On" + field.Name[..^"Event".Length];
        return IsPublic(argsType) && !ReservedNames.Contains(name) ? new(name, FieldAccess(field), argsType.ToDisplayString(TypeFormat)) : null;
    }

    private (string Receiver, string TypeParameters, string Constraint) Receiver(INamedTypeSymbol owner)
    {
        if (owner.IsSealed) return ($"{ElementType}<{owner.ToDisplayString(TypeFormat)}>", "", "");
        var bound = Inherits(owner, control) ? owner : control;
        return ($"{ElementType}<T>", "<T>", $" where T : {bound.ToDisplayString(TypeFormat)}, new()");
    }

    private bool IsMountableControl(INamedTypeSymbol type) => !type.IsAbstract && Inherits(type, control) && !Inherits(type, topLevel) && type.InstanceConstructors.Any(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);

    private ChildKind KindOf(INamedTypeSymbol type)
    {
        if (Inherits(type, panel)) return ChildKind.Panel;
        if (Inherits(type, contentControl)) return ChildKind.Content;
        if (Inherits(type, decorator)) return ChildKind.Decorator;
        return ChildKind.None;
    }

    private string? TextPropertyOf(INamedTypeSymbol type)
    {
        foreach (var owner in Chain(type))
        {
            foreach (var field in PublicStaticFields(owner))
            {
                if (field.Name == "TextProperty" && field.Type is INamedTypeSymbol { IsGenericType: true } fieldType && Is(fieldType, styledProperty) && fieldType.TypeArguments[0].SpecialType == SpecialType.System_String)
                    return FieldAccess(field);
            }
        }
        return null;
    }

    private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol type)
    {
        for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
            yield return current;
    }

    private static IEnumerable<IFieldSymbol> PublicStaticFields(INamedTypeSymbol type) => type.GetMembers().OfType<IFieldSymbol>().Where(f => f is { IsStatic: true, IsReadOnly: true, DeclaredAccessibility: Accessibility.Public } && !IsSkipped(f));

    private static bool HasPublicSetter(INamedTypeSymbol owner, string name)
    {
        foreach (var type in Chain(owner))
        {
            if (type.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
                return property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false } && !IsSkipped(property);
        }
        return false;
    }

    private static bool Is(INamedTypeSymbol type, INamedTypeSymbol definition) => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition);

    private static bool Inherits(INamedTypeSymbol type, INamedTypeSymbol baseType) => Chain(type).Any(t => SymbolEqualityComparer.Default.Equals(t, baseType));

    private static bool IsPublic(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsPublic(array.ElementType),
        INamedTypeSymbol named => IsAccessibleChain(named) && named.TypeArguments.All(IsPublic),
        _ => false,
    };

    private static bool IsAccessibleChain(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public || IsSkipped(current)) return false;
        return true;
    }

    private static bool IsSkipped(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass is { } attribute && SkippedAttributes.Contains(attribute.ToDisplayString()));

    private static string FieldAccess(IFieldSymbol field) => $"{field.ContainingType.ToDisplayString(TypeFormat)}.{field.Name}";

    private static string UniqueClassName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++) candidate = name + i;
        return candidate;
    }

    private static IEnumerable<INamedTypeSymbol> TopLevelTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers()) yield return type;
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in TopLevelTypes(child))
                yield return type;
    }
}
