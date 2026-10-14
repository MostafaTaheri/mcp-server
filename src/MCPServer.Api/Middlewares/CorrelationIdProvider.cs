namespace MCPServer.Api.Middlewares;

public class CorrelationIdProvider : ICorrelationIdProvider
{
    private string? _currentCorrelationId;

    public void SetCurrentCorrelationId(string correlationId)
    {
        _currentCorrelationId = correlationId;
    }

    public string? GetCurrentCorrelationId()
    {
        return _currentCorrelationId;
    }
}