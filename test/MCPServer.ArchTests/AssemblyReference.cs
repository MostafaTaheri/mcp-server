using System.Reflection;
using MCPServer.Application.Abstractions;
using MCPServer.Api.Middlewares;

namespace MCPServer.ArchTests;

internal static class AssemblyReference
{
    public static readonly Assembly Application = typeof(BaseBusinessValidator).Assembly;
    public static readonly Assembly Api = typeof(ExceptionsMiddleware).Assembly;
}
