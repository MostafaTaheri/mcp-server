using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Exceptions;

public abstract class ThirdPartyServiceException : BaseException, IUpstreamFailure
{
    public ThirdPartyServiceException(string code, string message, List<string> details) : base(
            code: $"Infrastructure-{code}",
            message: message,
            details: details)
    { }

    public virtual UpstreamFailureKind Kind => UpstreamFailureKind.Unavailable;
}
