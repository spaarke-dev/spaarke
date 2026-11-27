using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// #3276 (DI-178): <see cref="BillingDecisionRecorder"/> (a singleton, <c>FinanceModule</c>) shares a secure Decision Record inline only when it
/// was given the host's <see cref="IServiceScopeFactory"/>; its constructor parameter is optional so that unit-test compositions compile
/// unchanged, which means a host that failed to supply it would silently lose the fix. Built from the REAL host composition
/// (<see cref="CustomWebAppFactory"/>), with scope validation ON, so a missing factory, a captive scoped dependency or a synchronizer that no
/// scope can resolve fails here instead of in production.
/// <para><b>Why a field read.</b> A behavioural proof (a secure write through the resolved recorder reaching the synchronizer) needs the host's
/// real Dataverse writer, which this factory replaces with network-bound doubles; the share path itself is proven behaviourally, against the real
/// synchronizer, by <c>BillingDecisionRecorderInlineShareTests</c> (data-mutation). This test pins the one thing that test cannot: the host's
/// recorder carries a working scope factory, and a scope from THAT factory resolves the scoped synchronizer.</para>
/// </summary>
public sealed class BillingDecisionRecorderRegistrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public BillingDecisionRecorderRegistrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact(DisplayName = "3276 T7: the host's BillingDecisionRecorder holds a scope factory, and a scope from it resolves the secure-child synchronizer")]
    public void TheRealContainer_GivesTheRecorderAScopeFactory_ThatResolvesTheScopedSynchronizer()
    {
        using var validating = _factory.WithWebHostBuilder(builder =>
            builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));

        var recorder = validating.Services.GetRequiredService<BillingDecisionRecorder>(); // a singleton: resolves from the root, no captive scoped dependency

        var scopes = typeof(BillingDecisionRecorder)
            .GetField("_scopes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(recorder).Should().BeAssignableTo<IServiceScopeFactory>().Subject;
        using var scope = scopes.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>();
        using var other = scopes.CreateScope();
        other.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>().Should().NotBeSameAs(first, "scoped: a fresh instance per scope");
    }
}
