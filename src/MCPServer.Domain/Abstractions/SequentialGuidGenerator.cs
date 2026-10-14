namespace MCPServer.Domain.Abstractions;

public static class SequentialGuidGenerator
{
    public static Guid Generate()
    {
        return Guid.CreateVersion7(DateTimeOffset.UtcNow);
    }
}