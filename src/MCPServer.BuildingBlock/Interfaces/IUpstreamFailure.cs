namespace MCPServer.BuildingBlock.Interfaces;

public enum UpstreamFailureKind
{
    Unavailable,
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    Timeout
}

/// <summary>Implemented by exceptions from external services so the Application layer can classify them without knowing Infrastructure types.</summary>
public interface IUpstreamFailure
{
    UpstreamFailureKind Kind { get; }
}
