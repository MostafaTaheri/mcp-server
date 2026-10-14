using Microsoft.EntityFrameworkCore;
using MCPServer.Domain.Users;
using MCPServer.Domain.Users.ValueObjects;
using MCPServer.Infrastructure.Data;

namespace MCPServer.Infrastructure.UMS.Users;

public class UserRepository : IUserRepository
{
    private readonly MCPServerDbContext _context;

    public UserRepository(MCPServerDbContext context)
    {
        _context = context;
    }

    public async Task<User> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByPhoneNumberAsync(PhoneNumber phoneNumber, CancellationToken cancellationToken = default)
    {
        return await _context.Users.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        return Task.CompletedTask;
    }
}