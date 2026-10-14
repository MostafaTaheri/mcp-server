using MCPServer.Domain.Abstractions;
using MCPServer.Domain.Users;

namespace MCPServer.Domain;

/// <summary>
/// Application-level unit-of-work marker.
/// Inherits all transactional operations from <see cref="IUnitOfWork"/>.
/// </summary>
public interface IMCPServerUnitOfWork : IUnitOfWork
{
    IUserRepository Users { get; }
}
