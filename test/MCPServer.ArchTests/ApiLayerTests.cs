using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace MCPServer.ArchTests;

public class ApiLayerTests
{
    [Fact]
    public void Controllers_ShouldHave_ControllerSuffix()
    {
        var result = Types.InAssembly(AssemblyReference.Api)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("Controller")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All controllers must have the 'Controller' suffix. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_ShouldInherit_ControllerBase()
    {
        var result = Types.InAssembly(AssemblyReference.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(typeof(ControllerBase))
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All controllers must inherit from ControllerBase. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_ShouldReside_InControllersNamespace()
    {
        var result = Types.InAssembly(AssemblyReference.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .AreNotAbstract()
            .Should()
            .ResideInNamespace("MCPServer.Api.Controllers")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All controllers must reside in the 'MCPServer.Api.Controllers' namespace. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Controllers_ShouldHave_ApiControllerAttribute()
    {
        var result = Types.InAssembly(AssemblyReference.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .AreNotAbstract()
            .Should()
            .HaveCustomAttribute(typeof(ApiControllerAttribute))
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All controllers must be decorated with [ApiController]. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
