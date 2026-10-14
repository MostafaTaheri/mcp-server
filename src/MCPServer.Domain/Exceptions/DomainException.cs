namespace MCPServer.Domain.Exceptions;

public abstract class DomainException : BaseException
{
    public DomainException(string code, string message, List<string> details) : base(
            code: $"Domain-{code}",
            message: message,
            details: details){}
}