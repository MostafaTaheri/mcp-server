using MCPServer.Domain;
using MCPServer.Domain.Users;
using MCPServer.Infrastructure.UMS.Users;

namespace MCPServer.Infrastructure.Data;

public class ApplicationUnitOfWork(MCPServerDbContext dbContext) : BaseUnitOfWork(dbContext), IMCPServerUnitOfWork, IDisposable
{
    private readonly MCPServerDbContext _dbContext = dbContext;
    private IUserRepository _userRepository;
    public IUserRepository Users => _userRepository ??= new UserRepository(_dbContext);
}