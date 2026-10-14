namespace MCPServer.Api.Middlewares;

public interface ICorrelationIdProvider
{
    void SetCurrentCorrelationId(string correlationId);
    string? GetCurrentCorrelationId();
}