using MCPServer.Infrastructure.ExternalServices.Exceptions;

namespace MCPServer.Infrastructure.ExternalServices.Jira;

public sealed class JiraUnauthorizedException : JiraAuthenticationException
{
    public JiraUnauthorizedException(List<string>? details = null) : base(code: "401", message: "Jira rejected the supplied credentials. Check Jira:Email and Jira:ApiToken.", details: details ?? []) { }
}

public sealed class JiraForbiddenException : JiraAuthenticationException
{
    public JiraForbiddenException(List<string>? details = null) : base(code: "403", message: "Jira authenticated the credentials but denied access to the identity endpoint.", details: details ?? []) { }
}

public class JiraAuthenticationException : ThirdPartyServiceException
{
    public JiraAuthenticationException(string code, string? message = null, List<string>? details = null) : base(code: code, message: message ?? "Jira identity check failed.", details: details ?? []) { }
}
