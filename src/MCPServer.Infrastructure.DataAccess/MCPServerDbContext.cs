using MCPServer.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace MCPServer.Infrastructure.Data;

public class MCPServerDbContext : DbContext
{
    public MCPServerDbContext(DbContextOptions<MCPServerDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MCPServerDbContext).Assembly);
    }
}