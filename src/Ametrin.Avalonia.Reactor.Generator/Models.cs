using System.Collections;
using System.Collections.Immutable;

namespace Ametrin.Avalonia.Reactor.Generator;

internal enum ChildKind { None, Panel, Content, Decorator }

internal sealed record ControlModel(string Name, string TypeName, ChildKind Kind, string? TextProperty);

internal sealed record MemberModel(string Name, string Field, string ValueType);

internal sealed record OwnerModel(string ClassName, string Receiver, string TypeParameters, string Constraint, EquatableArray<MemberModel> Properties, EquatableArray<MemberModel> Events);

internal sealed record GenerationModel(EquatableArray<ControlModel> Controls, EquatableArray<OwnerModel> Owners)
{
    public static GenerationModel Empty { get; } = new(ImmutableArray<ControlModel>.Empty, ImmutableArray<OwnerModel>.Empty);
}

internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
    private readonly ImmutableArray<T> items = items;

    public bool IsEmpty => items.IsDefaultOrEmpty;

    public bool Equals(EquatableArray<T> other) => items.AsSpan().SequenceEqual(other.items.AsSpan());
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items.AsSpan()) hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(items.IsDefault ? [] : items)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);
}
