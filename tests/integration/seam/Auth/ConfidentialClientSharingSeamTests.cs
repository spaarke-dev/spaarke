using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-A2 (task 011) → FR-B3 (task 022) — confidential-client sharing seam.
///
/// <para><b>MIGRATED, not rewritten from scratch (task 022).</b> These assertions used to be made
/// against three per-class static caches — one on <c>DataverseAccessDataSource</c>, one on
/// <c>DataverseUserClient</c>, one on <c>AgentTokenService</c> — each keyed
/// <c>(tenant|client|secret-fingerprint)</c>. That shape was itself the defect ADR-028 A4 forbids: one
/// process could hold three confidential clients, and three OBO token caches, for the SAME identity.
/// Task 011 booked it as a time-boxed A4 exception that <b>expired at task 022</b>. The caches are gone;
/// <see cref="OrderedCredentialClientProvider"/> owns the one cache, so the same three questions are now
/// asked of it. <c>tests/integration/seam/**</c> is an ADR-038 KEEP path — this file is migrated in
/// place rather than deleted and replaced.</para>
///
/// <para><b>What got STRONGER in the migration, and it is worth stating.</b> The old test could only
/// count builds: construct five instances of one type, observe one build. It could not say anything
/// about whether two <i>different</i> consumers shared a client, because each type had its own cache and
/// they provably did not. The assertions below check reference identity of the returned client, which is
/// the actual property the consolidation buys — every consumer of one identity gets one object, hence
/// one MSAL OBO token cache.</para>
///
/// <para><b>What is still deferred to task 060.</b> These prove the provider shares correctly under a
/// correctly-scoped key. They cannot prove some FUTURE call site builds its own client instead of asking
/// — a bypassing site simply would not touch this seam. That guard is source analysis over
/// <c>ConfidentialClientApplicationBuilder.Create</c> call sites, the shape ADR-038 sanctions and which
/// task 060 already builds.</para>
/// </summary>
[Collection(DataverseCredentialSeamCollection.Name)]
public class ConfidentialClientSharingSeamTests
{
    private const string Secret = "test-secret-value";

    // ---------------------------------------------------------------------------------------------
    // One identity → one confidential client, shared by every consumer
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ManyRequestsForOneIdentity_BuildExactlyOneConfidentialClient()
    {
        // Unique key per run, so this measures only what this test asks for.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();
        var provider = Build();

        // Five acquisitions stands in for five requests across the four migrated call sites.
        for (var i = 0; i < 5; i++)
        {
            (await provider.GetClientAsync(tenantId, clientId)).Should().NotBeNull();
        }

        provider.BuildCountFor(tenantId, clientId, CredentialKind.ClientSecret)
            .Should().Be(1,
                "five acquisitions under one identity must build ONE confidential client — one per "
                + "call would discard MSAL's OBO token cache on every request");
    }

    [Fact]
    public async Task TwoDifferentConsumers_AskingForTheSameIdentity_GetTheSameClientInstance()
    {
        // THE property task 022 exists to establish, and the one the pre-migration test could not
        // express: GraphClientFactory, DataverseAccessDataSource, DataverseUserClient and
        // AgentTokenService all resolve the BFF's identity through this one provider, so they hold ONE
        // confidential client between them rather than three. MSAL's OBO token cache lives on the
        // client, so sharing the object is what shares the cache.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();
        var provider = Build();

        var first = await provider.GetClientAsync(tenantId, clientId);
        var second = await provider.GetClientAsync(tenantId, clientId);

        second.Should().BeSameAs(first,
            "one identity must map to one confidential client object for every consumer in the process");
    }

    [Fact]
    public async Task DifferentTenants_DoNotShareAConfidentialClient()
    {
        // The negative half: sharing must be KEYED, not global. A client shared across tenants would be
        // a cross-tenant token-cache leak — a far worse defect than the one this seam fixes.
        var clientId = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();
        var provider = Build();

        var a = await provider.GetClientAsync(tenantA, clientId);
        var b = await provider.GetClientAsync(tenantB, clientId);

        b.Should().NotBeSameAs(a, "token caches must not cross tenants");
        provider.BuildCountFor(tenantA, clientId, CredentialKind.ClientSecret).Should().Be(1);
        provider.BuildCountFor(tenantB, clientId, CredentialKind.ClientSecret).Should().Be(1);
    }

    [Fact]
    public async Task RotatedSecret_BuildsANewConfidentialClient_NotTheStaleOne()
    {
        // MSAL binds the credential at Build() and holds it for the client's lifetime. If the cache key
        // omitted the secret, a rotation would silently keep handing back a client built with the OLD
        // secret — presenting as AADSTS7000215 on OBO while the app-only path kept working, and "fixed"
        // by a restart nobody could explain. Task 011 code-review W-1, preserved through the migration.
        //
        // Rotated on ONE provider through a mutable configuration, deliberately. Building a second
        // provider would have made this assertion vacuous — two providers hold two caches, so their
        // clients differ whatever the key is, and the test would pass even if the fingerprint had been
        // dropped from the key. Only rotating underneath a single cache proves the key moved with the
        // secret.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();

        var config = new MutableConfiguration { ["API_CLIENT_SECRET"] = "secret-v1" };
        var provider = Build(config);

        var stale = await provider.GetClientAsync(tenantId, clientId);

        config["API_CLIENT_SECRET"] = "secret-v2";
        var fresh = await provider.GetClientAsync(tenantId, clientId);

        fresh.Should().NotBeSameAs(stale, "a rotated secret must produce a NEW client, never the stale one");
        provider.BuildCountFor(tenantId, clientId, CredentialKind.ClientSecret)
            .Should().Be(1, "the fresh client is keyed by the NEW fingerprint, so exactly one build stands under it");
    }

    // ---------------------------------------------------------------------------------------------

    private static OrderedCredentialClientProvider Build(IConfiguration? configuration = null) =>
        new(
            Options.Create(new CredentialSelectionOptions
            {
                // ClientSecret only: this file is about CACHING, and the secret branch is the one that
                // needs no network. Credential SELECTION has its own suite (CredentialOrderingSeamTests).
                Order = new List<string> { nameof(CredentialKind.ClientSecret) },
            }),
            configuration ?? new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["API_CLIENT_SECRET"] = Secret,
            }).Build(),
            NullLogger<OrderedCredentialClientProvider>.Instance);

    /// <summary>
    /// Minimal writable <see cref="IConfiguration"/>. <c>AddInMemoryCollection</c> snapshots at Build(),
    /// so it cannot express a rotation; the provider re-reads the secret when it describes a credential,
    /// which is exactly the behaviour the rotation test needs to drive.
    /// </summary>
    private sealed class MutableConfiguration : IConfiguration
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => new ConfigurationBuilder().Build().GetSection(key);
    }
}

/// <summary>
/// Serialises the seam tests that exercise confidential-client caching.
///
/// <para><b>No longer load-bearing.</b> Since task 011's code-review pass the sharing assertions count
/// builds PER KEY, and since task 022 each test builds its own provider instance, so concurrent
/// construction elsewhere cannot perturb them. Retained as defence in depth.</para>
/// </summary>
[CollectionDefinition(Name)]
public class DataverseCredentialSeamCollection
{
    public const string Name = "DataverseCredentialSeam";
}
