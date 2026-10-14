using MCPServer.Domain.Users;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;

namespace MCPServer.ArchTests;

public class InfrastructureLayerTests
{
    [Fact]
    public void Repositories_ShouldHave_RepositorySuffix()
    {
        var result = Types.InAssembly(AssemblyReference.Infrastructure)
            .That()
            .ImplementInterface(typeof(IUserRepository))
            .And()
            .AreNotInterfaces()
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("Repository")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All repository implementations must have the 'Repository' suffix. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void EntityConfigurations_ShouldImplement_IEntityTypeConfiguration()
    {
        var result = Types.InAssembly(AssemblyReference.Infrastructure)
            .That()
            .HaveNameEndingWith("EntityConfiguration")
            .Should()
            .ImplementInterface(typeof(IEntityTypeConfiguration<>))
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All entity configurations must implement IEntityTypeConfiguration<>. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Repositories_ShouldReside_InInfrastructureLayer()
    {
        var result = Types.InAssembly(AssemblyReference.Infrastructure)
            .That()
            .HaveNameEndingWith("Repository")
            .And()
            .AreNotInterfaces()
            .Should()
            .ResideInNamespace("MCPServer.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"All repository implementations must reside in the Infrastructure layer. Violating types:{Environment.NewLine}" +
            string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
