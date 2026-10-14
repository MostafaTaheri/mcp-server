using MCPServer.Infrastructure.ExternalServices.Exceptions;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

/// <summary>Raised when Atlassian rejects or fails an OAuth call. Messages are fixed and never include response bodies.</summary>
public sealed class JiraOAuthException(string code, string message)
    : ThirdPartyServiceException(code, message, []);
