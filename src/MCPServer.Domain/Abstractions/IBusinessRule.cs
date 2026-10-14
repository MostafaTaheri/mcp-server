using MCPServer.Domain.Exceptions;

namespace MCPServer.Domain.Abstractions;

public interface IBusinessRule
{
    bool IsBroken();
    Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsBroken());
    BaseException GetRelatedException();
}