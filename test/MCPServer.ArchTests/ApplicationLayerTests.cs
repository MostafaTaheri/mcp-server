using MCPServer.Application.Abstractions;
using MCPServer.Application.Exceptions;
using NetArchTest.Rules;

namespace MCPServer.ArchTests;

public class ApplicationLayerTests
{
    [Fact]
    public void Validators_ShouldInherit_BaseBusinessValidator()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .That()
            .HaveNameEndingWith("Validator")
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(typeof(BaseBusinessValidator))
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All validators must inherit from BaseBusinessValidator. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Commands_ShouldHave_CommandSuffix()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .That()
            .ResideInNamespaceContaining(".Features.")
            .And()
            .HaveNameEndingWith("Command")
            .Should()
            .ResideInNamespaceContaining("MCPServer.Application")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All commands must reside in the Application layer. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Services_ShouldHave_ServiceSuffix()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .That()
            .HaveNameEndingWith("Service")
            .And()
            .AreNotInterfaces()
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("Service")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All service implementations must have the 'Service' suffix. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void ServiceInterfaces_ShouldHave_IServiceNaming()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .That()
            .AreInterfaces()
            .And()
            .HaveNameEndingWith("Service")
            .Should()
            .HaveNameStartingWith("I")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All service interfaces must start with 'I'. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void ApplicationExceptions_ShouldInherit_ApplicationBaseException()
    {
        var result = Types.InAssembly(AssemblyReference.Application)
            .That()
            .ResideInNamespace("MCPServer.Application.Exceptions")
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(typeof(BaseException))
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All concrete application exceptions must inherit from BaseException. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
