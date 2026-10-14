namespace MCPServer.Application.Exceptions;

public abstract class BusinessException : BaseException
{
    public BusinessException(string code, string message, List<string> details) : base(
            code: $"Business-{code}",
            message: message,
            details: details)
    { }
}


public sealed class NotFoundException : BusinessException
{
    public NotFoundException(List<string>? details = null) : base(code: "404", message: "Item not found.", details: details ?? []) { }
}

public sealed class PermissionDeniedException : BusinessException
{
    public PermissionDeniedException(List<string>? details = null) : base(code: "403", message: "Permission Denied.", details: details ?? []) { }
}

public sealed class ServiceAuthenticationException : BusinessException
{
    public ServiceAuthenticationException(List<string>? details = null) : base(code: "401", message: "Authentication Failed.", details: details ?? []) { }
}