using MCPServer.Domain.Exceptions;

namespace MCPServer.Domain.Abstractions;

/// <summary>
/// Base class for business rules with automatic multi-language support
/// </summary>
public abstract class BaseBusinessRule : IBusinessRule
{
    public abstract BaseException GetRelatedException();
    public abstract bool IsBroken();
    public virtual Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default) 
        => Task.FromResult(IsBroken());
}
