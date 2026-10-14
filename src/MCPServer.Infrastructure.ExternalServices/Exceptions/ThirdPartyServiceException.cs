namespace MCPServer.Infrastructure.ExternalServices.Exceptions;

public abstract class ThirdPartyServiceException : BaseException
{
    public ThirdPartyServiceException(string code, string message, List<string> details) : base(
            code: $"Infrastructure-{code}",
            message: message,
            details: details)
    { }
}