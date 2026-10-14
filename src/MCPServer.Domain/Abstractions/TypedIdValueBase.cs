namespace MCPServer.Domain.Abstractions;

public abstract class TypedIdValueBase : IEquatable<TypedIdValueBase>
{
    protected TypedIdValueBase(Guid value)
    {
        if (value == Guid.Empty) throw new InvalidOperationException("Id value cannot be empty!");

        Value = value;
    }

    public Guid Value { get; }

    public bool Equals(TypedIdValueBase? other)
    {
        return Value == other?.Value;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;

        return obj is TypedIdValueBase other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(TypedIdValueBase obj1, TypedIdValueBase obj2)
    {
        return obj1?.Equals(obj2) ?? Equals(obj2, null);
    }

    public static bool operator !=(TypedIdValueBase x, TypedIdValueBase y)
    {
        return !(x == y);
    }
}