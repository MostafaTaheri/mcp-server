using NetArchTest.Rules;

namespace MCPServer.ArchTests;

public class LayerDependencyTests
{
    private const string ApplicationNamespace = "MCPServer.Application";
    private const string InfrastructureNamespace = "MCPServer.Infrastructure";
    private const string ApiNamespace = "MCPServer.Api";

    [Fact]
    public void Application_ShouldNot_DependOn_InfrastructureLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Application layer must not reference Infrastructure. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNot_DependOn_ApiLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Application layer must not reference Api. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
