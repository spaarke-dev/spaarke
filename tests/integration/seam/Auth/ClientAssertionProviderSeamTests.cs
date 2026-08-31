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
    public void SharedLibrary_ConstructsWithABffSuppliedCredentialProvider()
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

        // Task 022 swapped the parameter this asserts. Task 020 threaded in an IClientAssertionProvider
        // as a placeholder; the seam the base layer actually needs is the CLIENT-level one, because
        // ordered selection spans assertion / certificate / secret and only the first of those IS an
        // assertion. The assertion contract still mints the MI-FIC credential — one level down, inside
        // the provider, which is what the rest of this file exercises.
        IConfidentialClientProvider provider = new OrderedCredentialClientProvider(
            Microsoft.Extensions.Options.Options.Create(new Sprk.Bff.Api.Configuration.CredentialSelectionOptions
            {
                Order = new List<string> { nameof(Sprk.Bff.Api.Configuration.CredentialKind.ClientSecret) },
            }),
            config,
            NullLogger<OrderedCredentialClientProvider>.Instance);

        var withProvider = () => new DataverseAccessDataSource(
            Moq.Mock.Of<IDataverseService>(), new System.Net.Http.HttpClient(), config,
            NullLogger<DataverseAccessDataSource>.Instance, credential: null, confidentialClients: provider);

        withProvider.Should().NotThrow("the shared library accepts a BFF-supplied credential provider");
    }

    [Fact]
    public async Task Provider_WhenNoManagedIdentityIsReachable_FailsAtFirstCall_WithACatchableMsalError()
    {
        // THE contract task 021 depends on. Construction succeeded (tests above); the failure must
        // arrive here, as a typed exception carrying a code the ordered selector can branch on.
        var provider = Create(Config(("Graph:ManagedIdentity:ClientId", UamiClientId)));

        // Assert the BASE type. An earlier version demanded MsalServiceException specifically and was
        // WRONG in a way that mattered: MSAL throws MsalClientException for
        // managed_identity_all_sources_unavailable, so this test failed intermittently — roughly half of
        // full-suite runs, while passing in isolation. The intermittency is real and explainable rather
        // than mysterious: MSAL caches the selected managed-identity source process-statically, so which
        // probe path runs first (and therefore which exception shape surfaces) depends on which test
        // touches it first under parallel collections.
        //
        // Chasing that down on 2026-08-21 found a genuine defect in task 021's ordered selection, not
        // just a bad assertion here: its fall-through predicate and catch clause were also typed to
        // MsalServiceException, so the commonest fall-through case — no IMDS on this host — would have
        // propagated instead of falling through to the secret. This test is what surfaced it. Keeping
        // the assertion at the base type is what keeps it able to.
        // ThrowsAnyAsync, not ThrowsAsync: the latter demands an EXACT type match and would reject both
        // concrete subclasses, which is the whole point here. Observed on this machine within minutes of
        // each other — one run MsalClientException, the next MsalServiceException, same code path.
        var ex = await Assert.ThrowsAnyAsync<MsalException>(() => provider.GetAssertionAsync());

        // Assert a SET of codes, never a single one. Which is thrown depends on the host: a developer
        // workstation cannot route to 169.254.169.254 at all, whereas GitHub-hosted runners are Azure
        // VMs where IMDS *is* reachable but carries no matching identity. Pinning one code would make
        // this pass locally and fail in CI, or vice versa.
        ex.ErrorCode.Should().BeOneOf(
            MsalError.ManagedIdentityUnreachableNetwork,     // no route to IMDS (workstation)
            MsalError.ManagedIdentityRequestFailed,          // IMDS reachable, identity absent/wrong
            MsalError.ManagedIdentityAllSourcesUnavailable); // no MI source at all
    }
}
