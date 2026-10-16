using MCPServer.Infrastructure.ExternalServices.Exceptions;

namespace MCPServer.Infrastructure.ExternalServices.Kibana;

public sealed class KibanaUnauthorizedException : KibanaConnectionException
{
    public KibanaUnauthorizedException(List<string>? details = null) : base(code: "401", message: "Kibana rejected the supplied credentials. Check the Kibana (or Elastic) authentication settings.", details: details ?? []) { }
}

public sealed class KibanaForbiddenException : KibanaConnectionException
{
    public KibanaForbiddenException(List<string>? details = null) : base(code: "403", message: "Kibana authenticated the credentials but denied access to the status endpoint.", details: details ?? []) { }
}

public class KibanaConnectionException : ThirdPartyServiceException
{
    public KibanaConnectionException(string code, string? message = null, List<string>? details = null) : base(code: code, message: message ?? "Kibana connection check failed.", details: details ?? []) { }
}
