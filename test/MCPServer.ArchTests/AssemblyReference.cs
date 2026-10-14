using System.Reflection;
using MCPServer.Application.Abstractions;
using MCPServer.Api.Controllers;
using MCPServer.Domain.Abstractions;
using MCPServer.Infrastructure.Data;

namespace MCPServer.ArchTests;

internal static class AssemblyReference
{
    public static readonly Assembly Domain = typeof(Entity<>).Assembly;
    public static readonly Assembly Application = typeof(BaseBusinessValidator).Assembly;
    public static readonly Assembly Infrastructure = typeof(ApplicationUnitOfWork).Assembly;
    public static readonly Assembly Api = typeof(UserController).Assembly;
}
