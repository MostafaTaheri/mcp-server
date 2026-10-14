using NetArchTest.Rules;

namespace MCPServer.ArchTests;

public class LayerDependencyTests
{
    private const string DomainNamespace = "MCPServer.Domain";
    private const string ApplicationNamespace = "MCPServer.Application";
    private const string InfrastructureNamespace = "MCPServer.Infrastructure";
    private const string ApiNamespace = "MCPServer.Api";

    [Fact]
    public void Domain_ShouldNot_DependOn_ApplicationLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Domain)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain layer must not reference Application. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_ShouldNot_DependOn_InfrastructureLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Domain)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain layer must not reference Infrastructure. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_ShouldNot_DependOn_ApiLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Domain)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain layer must not reference Api. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

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

    [Fact]
    public void Infrastructure_ShouldNot_DependOn_ApplicationLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Infrastructure)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure layer must not reference Application. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_ShouldNot_DependOn_ApiLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Infrastructure)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure layer must not reference Api. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
