using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// Ontology task 168 fix round (review finding 3): the commit route's <see cref="DecisionCommitFollowUps"/> has two constructors, and only
/// the longer one takes <see cref="AssignTaskNotifier"/>. Built from the REAL host composition (<see cref="CustomWebAppFactory"/>), so a
/// registration that moves behind a flag, or a notifier dependency that stops resolving, fails here instead of silently stopping every
/// Assign Task email. The system-mailbox job handler and the D-199 recipient gate are resolved the same way.
/// </summary>
public sealed class AssignTaskNotifierRegistrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AssignTaskNotifierRegistrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void TheRealContainer_GivesDecisionCommitFollowUpsTheNotifier_AndTheNotifierTheGate_AndRegistersTheJobHandler()
    {
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<AssignTaskNotifier>();
        var gate = scope.ServiceProvider.GetRequiredService<AssignTaskRecipientGate>();

        var followUps = scope.ServiceProvider.GetRequiredService<DecisionCommitFollowUps>();

        FieldOf<AssignTaskNotifier>(followUps).Should().BeSameAs(notifier, "DI must take the constructor that carries the notifier");
        FieldOf<AssignTaskRecipientGate>(notifier).Should().BeSameAs(gate, "one gate per request, asked by the notifier");
        scope.ServiceProvider.GetServices<IJobHandler>().OfType<AssignTaskEmailJobHandler>()
            .Should().ContainSingle().Which.JobType.Should().Be(AssignTaskNotifier.JobTypeName);
    }

    [Fact]
    public void WithScopeValidation_TheNotifierAndItsJobHandler_ResolveInAScope_NoCaptiveScopedDependency()
    {
        using var validating = _factory.WithWebHostBuilder(builder =>
            builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));
        using var scope = validating.Services.CreateScope();

        var followUps = scope.ServiceProvider.GetRequiredService<DecisionCommitFollowUps>();

        FieldOf<AssignTaskNotifier>(followUps).Should().NotBeNull();
        scope.ServiceProvider.GetServices<IJobHandler>().OfType<AssignTaskEmailJobHandler>().Should().ContainSingle();
    }

    private static object? FieldOf<T>(object consumer) => consumer.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(T))
        .GetValue(consumer);
}
