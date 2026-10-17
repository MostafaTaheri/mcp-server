using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Infrastructure.ExternalServices.Exceptions;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

public sealed class ElasticUnauthorizedException : ElasticAuthenticationException
{
    public ElasticUnauthorizedException(List<string>? details = null) : base(code: "401", message: "Elasticsearch rejected the supplied credentials. Check the Elastic authentication settings.", details: details ?? []) { }

    public override UpstreamFailureKind Kind => UpstreamFailureKind.Unauthorized;
}

public sealed class ElasticForbiddenException : ElasticAuthenticationException
{
    public ElasticForbiddenException(List<string>? details = null) : base(code: "403", message: "Elasticsearch authenticated the credentials but denied access to the cluster info endpoint.", details: details ?? []) { }

    public override UpstreamFailureKind Kind => UpstreamFailureKind.Forbidden;
}

public class ElasticAuthenticationException : ThirdPartyServiceException
{
    public ElasticAuthenticationException(string code, string? message = null, List<string>? details = null) : base(code: code, message: message ?? "Elasticsearch connection check failed.", details: details ?? []) { }
}

public sealed class ElasticRateLimitedException : ThirdPartyServiceException
{
    public ElasticRateLimitedException() : base(code: "429", message: "Elasticsearch is rate limiting requests.", details: []) { }

    public override UpstreamFailureKind Kind => UpstreamFailureKind.RateLimited;
}

public sealed class ElasticQueryTimeoutException : ThirdPartyServiceException
{
    public ElasticQueryTimeoutException() : base(code: "408", message: "Elasticsearch did not finish the query within the server-side time limit.", details: []) { }

    public override UpstreamFailureKind Kind => UpstreamFailureKind.Timeout;
}
