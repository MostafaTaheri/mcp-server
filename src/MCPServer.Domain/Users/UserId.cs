using MCPServer.Domain.Abstractions;

namespace MCPServer.Domain.Users;

public class UserId : TypedIdValueBase
{
    public UserId(Guid value) : base(value)
    {
    }

    public static UserId New() => new(SequentialGuidGenerator.Generate());
}
