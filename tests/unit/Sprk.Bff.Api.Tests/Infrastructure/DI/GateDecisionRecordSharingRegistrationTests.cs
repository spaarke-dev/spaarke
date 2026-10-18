using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// Ontology task 042b round 2 (D-169, uac-r2): <see cref="GateDecisionRecordSharing"/> is registered unconditionally and is a REQUIRED dependency
/// of <see cref="DirectThreadAccessService"/> and <see cref="DecisionRecordWriter"/> (singletons) and <see cref="SecureChildReconciler"/> (scoped). Built from the REAL host
/// composition (<see cref="CustomWebAppFactory"/>, which replaces only the network-bound services), so a missing registration or a captive
/// scoped dependency fails here instead of at the first request.
/// </summary>
public sealed class GateDecisionRecordSharingRegistrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public GateDecisionRecordSharingRegistrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void TheRealContainer_ResolvesDirectThreadAccessService_AndSecureChildReconciler_EachWithTheGateRecordHelper()
    {
        using var scope = _factory.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<GateDecisionRecordSharing>();

        var threads = scope.ServiceProvider.GetRequiredService<IDirectThreadAccessService>()
            .Should().BeOfType<DirectThreadAccessService>().Subject;
        var reconciler = scope.ServiceProvider.GetRequiredService<SecureChildReconciler>();
        var writer = scope.ServiceProvider.GetRequiredService<DecisionRecordWriter>();

        GateOf(threads).Should().BeSameAs(gate, "one helper for the host");
        GateOf(reconciler).Should().BeSameAs(gate);
        GateOf(writer).Should().BeSameAs(gate, "042b round 3: the writer's share-at-write takes it as a required dependency too");
    }

    [Fact]
    public void WithScopeValidation_TheHelperAndItsSingletonConsumer_ResolveFromTheRoot_NoCaptiveScopedDependency()
    {
        using var validating = _factory.WithWebHostBuilder(builder =>
            builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));

        var gate = validating.Services.GetRequiredService<GateDecisionRecordSharing>();
        var threads = validating.Services.GetRequiredService<IDirectThreadAccessService>();
        using var scope = validating.Services.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<SecureChildReconciler>();
        var writer = validating.Services.GetRequiredService<DecisionRecordWriter>();

        GateOf(threads).Should().BeSameAs(gate);
        GateOf(reconciler).Should().BeSameAs(gate);
        GateOf(writer).Should().BeSameAs(gate);
    }

    private static object? GateOf(object consumer) => consumer.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(GateDecisionRecordSharing))
        .GetValue(consumer);
}
