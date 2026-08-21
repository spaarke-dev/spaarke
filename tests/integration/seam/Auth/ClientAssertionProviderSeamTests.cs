using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-B1 (auth-v4 task 020) — the MI-FIC client-assertion seam.
///
/// <para><b>What is worth asserting here, and what is not.</b> The one property of this provider that
/// is both load-bearing and deterministically testable is that <b>construction never touches the
/// network</b>. Everything else about it either requires a live managed identity or lives in private
/// state.</para>
///
/// <para>That property is not a detail. On a developer workstation there is no IMDS endpoint, and in
/// CI there is none either. If this constructor probed the identity endpoint, the BFF would fail at
/// <i>startup</i> everywhere except Azure — taking down local development, and destroying the ordered
/// credential selection task 021 builds, which depends on the MI attempt failing at <i>call</i> time
/// so it can fall through to the next credential. "Fails gracefully rather than throwing at startup"
/// is exactly this.</para>
///
/// <para><b>The failure path IS asserted</b> — see the last test. An earlier version of this file
/// skipped it, claiming a real mint attempt would "wait out a timeout". The task 020 code-review gate
/// <b>measured that claim and it was false</b>: the mint fails in ~80 ms with a catchable
/// <c>MsalServiceException</c>, because an unroutable IMDS address fails fast rather than hanging.
/// The property matters more than the cost — <i>fails at first call, not at construction, with a
/// branchable ErrorCode</i> is the entire contract task 021's ordered credential selection rests on,
/// and without this test the construction tests above prove only that a constructor did not throw,
/// which is indistinguishable from one that silently succeeded on the network.</para>
///
/// <para><b>Not asserted: "resolving the provider twice returns the same singleton."</b> That is the
/// authored acceptance criterion, and it is an ADR-038 ban <b>B3</b> DI-registration test — the second
/// time this project's authored criteria have specified a banned shape (task 011 hit the same wall).
/// The registration is <c>AddSingleton</c> in <c>AuthorizationModule</c>; what actually makes singleton
/// scope load-bearing is that <c>ManagedIdentityClientAssertion</c> caches the signed assertion until
/// expiry, so a per-request instance would re-mint on every token acquisition.
///
/// <para>That is a structural property and belongs in source analysis, so it is booked onto task 060
/// as an explicit acceptance criterion — <i>"ManagedIdentityClientAssertion is constructed ONLY in a
/// constructor or static initializer, never inside a method body"</i>, plus a negative control. Booked
/// rather than gestured at: the quality gate for task 020 correctly objected that an earlier draft
/// deferred to 060 without 060 accepting the obligation.</para>
/// </summary>
public class ClientAssertionProviderSeamTests
{
    private const string UamiClientId = "5967251e-0000-0000-0000-000000000000";

    private static IConfiguration Config(params (string Key, string? Value)[] settings)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in settings) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ManagedIdentityAssertionProvider Create(IConfiguration config)
        => new(config, NullLogger<ManagedIdentityAssertionProvider>.Instance);

    [Fact]
    public void Provider_WithUamiConfigured_ConstructsWithoutThrowing()
    {
        var act = () => Create(Config(("Graph:ManagedIdentity:ClientId", UamiClientId)));

        act.Should().NotThrow(
            "construction must be network-free — the identity endpoint is unreachable off-Azure, and " +
            "probing it here would fail BFF startup on every developer workstation");
    }

    [Fact]
    public void Provider_WithNoManagedIdentityConfigured_StillConstructsWithoutThrowing()
    {
        // The local-dev shape, and the shape task 021's ordered selection depends on: no identity
        // configured at all must NOT be a startup failure. The provider becomes one that will fail
        // when called, which is what lets the selector fall through to the next credential.
        var act = () => Create(Config());

        act.Should().NotThrow(
            "an absent managed identity is a fall-through condition, not a startup error");
    }

    [Fact]
    public void ResolveUamiClientId_PrefersCanonicalKey_FallsBackToLegacy_NullWhenAbsent()
    {
        // ADR-028 A4 requires the credential come from the single shared provider rather than each
        // call site rolling its own lookup. Asserted through the resolver's observable contract: both
        // the canonical Spaarke Auth v2 key and the legacy ExternalAccess-style key resolve, with the
        // canonical one winning — so an environment configured either way gets the same identity, and
        // the provider cannot drift from every app-only consumer.
        ManagedIdentityCredentialFactory
            .ResolveUamiClientId(Config(("Graph:ManagedIdentity:ClientId", UamiClientId)))
            .Should().Be(UamiClientId, "the canonical Spaarke Auth v2 key resolves");

        ManagedIdentityCredentialFactory
            .ResolveUamiClientId(Config(("ManagedIdentity:ClientId", UamiClientId)))
            .Should().Be(UamiClientId, "the legacy ExternalAccess-style key still resolves");

        ManagedIdentityCredentialFactory
            .ResolveUamiClientId(Config(
                ("Graph:ManagedIdentity:ClientId", UamiClientId),
                ("ManagedIdentity:ClientId", "legacy-value-that-must-lose")))
            .Should().Be(UamiClientId, "the canonical key wins when both are present");

        ManagedIdentityCredentialFactory
            .ResolveUamiClientId(Config())
            .Should().BeNull("no identity configured resolves to null, not to a fabricated default");
    }

    [Fact]
    public void SharedLibrary_ConstructsWithABffSuppliedAssertionProvider()
    {
        // The seam's whole point: Spaarke.Dataverse is the base layer and cannot reference the BFF,
        // so it takes the contract by dependency inversion. This asserts the half that is NOT
        // compiler-enforced — that a BFF-supplied implementation is accepted and does not disturb
        // construction.
        //
        // The other half (that it still constructs WITHOUT one, NFR-04) is deliberately NOT asserted
        // here: that the nullable parameter has a null default is proven by this file compiling at all,
        // and asserting it at runtime would be ADR-038 ban B11 (testing what the compiler enforces).
        // The real evidence for NFR-04 is the 10,553 existing fixtures passing unchanged.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com",
            ["TENANT_ID"] = Guid.NewGuid().ToString(),
            ["API_APP_ID"] = Guid.NewGuid().ToString(),
            ["API_CLIENT_SECRET"] = "not-a-real-secret",
            ["Graph:ManagedIdentity:Enabled"] = "true",
        }).Build();

        IClientAssertionProvider provider = Create(Config(("Graph:ManagedIdentity:ClientId", UamiClientId)));

        var withProvider = () => new DataverseAccessDataSource(
            Moq.Mock.Of<IDataverseService>(), new System.Net.Http.HttpClient(), config,
            NullLogger<DataverseAccessDataSource>.Instance, credential: null, assertion: provider);

        withProvider.Should().NotThrow("the shared library accepts a BFF-supplied assertion provider");
    }

    [Fact]
    public async Task Provider_WhenNoManagedIdentityIsReachable_FailsAtFirstCall_WithACatchableMsalError()
    {
        // THE contract task 021 depends on. Construction succeeded (tests above); the failure must
        // arrive here, as a typed exception carrying a code the ordered selector can branch on.
        var provider = Create(Config(("Graph:ManagedIdentity:ClientId", UamiClientId)));

        var ex = await Assert.ThrowsAsync<MsalServiceException>(() => provider.GetAssertionAsync());

        // Assert the TYPE plus a SET of codes, never a single code. Which one is thrown depends on the
        // host: a developer workstation cannot route to 169.254.169.254 at all, whereas GitHub-hosted
        // runners are Azure VMs where IMDS *is* reachable but carries no matching identity. Pinning one
        // code would make this test pass locally and fail in CI, or vice versa.
        ex.ErrorCode.Should().BeOneOf(
            MsalError.ManagedIdentityUnreachableNetwork,     // no route to IMDS (workstation)
            MsalError.ManagedIdentityRequestFailed,          // IMDS reachable, identity absent/wrong
            MsalError.ManagedIdentityAllSourcesUnavailable); // no MI source at all
    }
}
