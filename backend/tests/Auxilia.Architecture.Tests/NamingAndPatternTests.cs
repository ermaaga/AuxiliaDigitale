using NetArchTest.Rules;

namespace Auxilia.Architecture.Tests;

/// <summary>Application patterns of ADR 0004 (Managers + QueryServices + IOperationRunner, no CQRS handlers).</summary>
public sealed class NamingAndPatternTests
{
    [Fact]
    public void Managers_AreSealedAndNotPublic()
    {
        var result = Types.InAssembly(Solution.Load(Solution.Application))
            .That().AreClasses().And().HaveNameEndingWith("Manager")
            .Should().BeSealed().And().NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void QueryServices_AreSealedAndNotPublic()
    {
        var result = Types.InAssembly(Solution.Load(Solution.Application))
            .That().AreClasses().And().HaveNameEndingWith("QueryService")
            .Should().BeSealed().And().NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void Interfaces_StartWithI()
    {
        foreach (var assembly in Solution.ProductionProjects)
        {
            var result = Types.InAssembly(Solution.Load(assembly))
                .That().AreInterfaces()
                .Should().HaveNameStartingWith("I")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
        }
    }

    [Fact]
    public void NoCqrsHandlerTypes_Exist()
    {
        foreach (var assembly in Solution.ProductionProjects)
        {
            var result = Types.InAssembly(Solution.Load(assembly))
                .ShouldNot().HaveNameEndingWith("CommandHandler")
                .And().NotHaveNameEndingWith("QueryHandler")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
        }
    }

    [Fact]
    public void Worker_HasNoTimerBasedBackgroundServices()
    {
        // ADR 0007: the Worker only consumes queues; recurring logic is an IRecurringJob run manually.
        var result = Types.InAssembly(Solution.Load(Solution.Worker))
            .ShouldNot().Inherit(typeof(Microsoft.Extensions.Hosting.BackgroundService))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayerDependencyTests.Describe(result));
    }
}
