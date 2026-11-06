using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// The readability proofs of the stores <see cref="ContactIdentityBinderFactory"/> builds are held by the factory, one per
/// (environment, table, column set), on its injected <see cref="TimeProvider"/>: a proof made through one binder's store
/// holds for the next binder of the same environment for <see cref="SecuredColumnReadProof.ProofLifetime"/>, and a failed
/// proof is made again at the next use. The stores are the real <see cref="DataverseContactIdentityStore"/> over a fake
/// Web API (<see cref="ReadProofHttp"/>), so "made again" is counted in requests.
/// </summary>
public class ContactIdentityBinderFactoryReadProofTests
{
    private const string Foreign = "https://spaarke-demo.crm.dynamics.com";
    private const string OtherForeign = "https://spaarke-test.crm.dynamics.com";

    private static readonly Func<CancellationToken, Task<string>> Token = _ => Task.FromResult("token");

    private readonly FakeTimeProvider _clock = new(IdentityBindingTestKit.Now);

    private ContactIdentityBinderFactory FactoryOver(ReadProofHttp http)
        => new(http, NullLoggerFactory.Instance, IdentityBindingTestKit.Tenants(IdentityBindingTestKit.CustomerTenant), _clock,
            IdentityBindingTestKit.FactoryConfiguration("https://spaarkedev1.crm.dynamics.com"));

    private static ReadProofHttp Granting()
    {
        var http = new ReadProofHttp();
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.IdentityColumns);
        return http;
    }

    [Fact]
    public async Task TwoStoresOfOneEnvironment_ShareOneProof()
    {
        var http = Granting();
        var factory = FactoryOver(http);

        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.NoMarkedRows);
        var proofReads = http.WhoAmICount;
        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.NoMarkedRows);

        proofReads.Should().Be(2, "one proof per column set: the binding columns and the collision columns");
        http.WhoAmICount.Should().Be(proofReads, "the second binder's store holds the proof the first one made");
    }

    [Fact]
    public async Task StoresOfDifferentEnvironments_DoNotShare()
    {
        var http = Granting();
        var factory = FactoryOver(http);

        await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None);
        var proofReads = http.WhoAmICount;
        await factory.CreateStore(OtherForeign, Token).ProbeBindingReadabilityAsync(CancellationToken.None);

        http.WhoAmICount.Should().Be(proofReads * 2, "a proof holds for the environment it was made in only");
    }

    [Fact]
    public async Task ASharedProof_ExpiresOnTheInjectedClock_AndIsMadeAgain()
    {
        var http = Granting();
        var factory = FactoryOver(http);

        await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None);
        var proofReads = http.WhoAmICount;
        _clock.Advance(SecuredColumnReadProof.ProofLifetime - TimeSpan.FromSeconds(1));
        await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None);
        http.WhoAmICount.Should().Be(proofReads, "within its lifetime the proof is held");

        // A grant withdrawn meanwhile is seen once the proof has expired.
        http.DirectGrants.Clear();
        _clock.Advance(TimeSpan.FromSeconds(1));
        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.Masked);
        http.WhoAmICount.Should().BeGreaterThan(proofReads, "an expired proof is made again");
    }

    [Fact]
    public async Task AFailedProof_IsNeverHeld_AcrossStores()
    {
        // The binding columns are granted, the collision columns are not: one proof holds, the other fails.
        var http = new ReadProofHttp();
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.BindingColumns);
        var factory = FactoryOver(http);

        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.Masked);
        var proofReads = http.WhoAmICount;
        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.Masked, "the failed proof was not held as a proven one");
        http.WhoAmICount.Should().Be(proofReads + 1,
            "the proven binding-columns proof is held; only the failed collision-columns proof is made again");

        // ... so a grant added afterwards is picked up at once.
        http.DirectGrants.UnionWith(DataverseContactIdentityStore.CollisionColumns);
        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.NoMarkedRows);
    }

    [Fact]
    public async Task AFaultedProof_IsNeverHeld_AcrossStores()
    {
        var http = Granting();
        http.ProofFaults = true;
        var factory = FactoryOver(http);

        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.Failed);
        http.ProofFaults = false;
        (await factory.CreateStore(Foreign, Token).ProbeBindingReadabilityAsync(CancellationToken.None))
            .Should().Be(BindingReadability.NoMarkedRows);
    }

    [Fact]
    public void TheSameColumns_InAnotherOrderOrSpelling_ShareOneProof_AndAnotherSetDoesNot()
    {
        var factory = FactoryOver(new ReadProofHttp());
        var source = new NoReads();

        var proof = factory.SharedReadProof(Foreign, source, "contact", new[] { "sprk_a", "sprk_b" }, NullLogger.Instance);

        factory.SharedReadProof(Foreign + "/", source, "Contact", new[] { "SPRK_B", "sprk_a" }, NullLogger.Instance)
            .Should().BeSameAs(proof);
        factory.SharedReadProof(Foreign, source, "contact", new[] { "sprk_a" }, NullLogger.Instance)
            .Should().NotBeSameAs(proof);
        factory.SharedReadProof(OtherForeign, source, "contact", new[] { "sprk_a", "sprk_b" }, NullLogger.Instance)
            .Should().NotBeSameAs(proof);
    }

    private sealed class NoReads : IColumnReadPermissionSource
    {
        public Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct) => throw new InvalidOperationException("no read expected");

        public Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct)
            => throw new InvalidOperationException("no read expected");

        public Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
            Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct)
            => throw new InvalidOperationException("no read expected");
    }
}
