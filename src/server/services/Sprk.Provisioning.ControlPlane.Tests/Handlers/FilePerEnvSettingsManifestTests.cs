// -----------------------------------------------------------------------------
// FilePerEnvSettingsManifestTests.cs
//
// Task 205c / punch row A39 — unit tests for FilePerEnvSettingsManifest
// exercising the REAL embedded scripts/canonical-secret-catalog/manifest.yaml
// content (same pattern as FileKvSecretManifestTests for the sibling
// `secrets:` section) — not a hand-rolled fixture — so drift between the
// shipped manifest.yaml and the auth-v4 §10.2 live-contract 8-entry set is
// caught here, not silently at BFF boot time.
//
// COVERAGE (POML acceptance-criteria mapping):
//   T1  ReadAsync against the real embedded manifest.yaml succeeds.
//   T2  All 8 §10.2 entries are present with the expected sourcing shape.
//   T3  Zero required=false among the 8 §10.2 entries (SF-18 sweep).
//   T4  Exactly ONE Credentials__Order__* entry (Order__0) — no Order__1+.
//   T5  Graph__Credentials__Order__0 literal value is EXACTLY
//       "ManagedIdentityFederated".
//   T6  Graph__Credentials__RequireSecretFreeIdentity literal value is
//       EXACTLY "true".
//   T7  ManagedIdentity__ClientId sources uami_client_id (NOT an object-id-
//       shaped key) — SF-2 identifier-swap guard at the manifest level.
//   T8  The 3 ServiceBus FQNS entries (4/5/6) all source the SAME parameter
//       key (source-dedup convention) and are FromHandlerOutput, never a
//       literal (a literal would hardcode a namespace name across every
//       customer stamp).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class FilePerEnvSettingsManifestTests
{
    private static FilePerEnvSettingsManifest NewManifest()
        => new(NullLogger<FilePerEnvSettingsManifest>.Instance);

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ReturnsPopulatedSuccess()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<PerEnvSettingsManifestReadResult.Success>().Subject;
        success.Entries.Should().NotBeEmpty();
        success.Entries.Count.Should().BeGreaterThanOrEqualTo(15,
            "task 201 shipped 8 per_env_settings entries; task 205c / A39 added 7 more " +
            "(entry 3 already existed) — a smaller count would indicate a parse regression");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ContainsAllEightSection102Entries()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var expectedKeys = new[]
        {
            "Graph__Credentials__Order__0",
            "Graph__Credentials__RequireSecretFreeIdentity",
            "ManagedIdentity__ClientId",
            "ServiceBus__FullyQualifiedNamespace",
            "Membership__EventPublisher__ServiceBusNamespace",
            "Membership__JunctionUpdater__ServiceBusNamespace",
            "AiSearch__ManagedIdentity__Enabled",
            "AiSafety__ContentSafety__ManagedIdentity__Enabled",
        };

        foreach (var key in expectedKeys)
        {
            entries.Should().Contain(e => e.Key == key, $"auth-v4 §10.2 entry '{key}' must be present");
        }
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_AllEightSection102Entries_RequiredTrue()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var section102Keys = new HashSet<string>(StringComparer.Ordinal)
        {
            "Graph__Credentials__Order__0",
            "Graph__Credentials__RequireSecretFreeIdentity",
            "ManagedIdentity__ClientId",
            "ServiceBus__FullyQualifiedNamespace",
            "Membership__EventPublisher__ServiceBusNamespace",
            "Membership__JunctionUpdater__ServiceBusNamespace",
            "AiSearch__ManagedIdentity__Enabled",
            "AiSafety__ContentSafety__ManagedIdentity__Enabled",
        };

        var section102Entries = entries.Where(e => section102Keys.Contains(e.Key)).ToList();
        section102Entries.Should().HaveCount(8, "all 8 auth-v4 §10.2 entries must be present exactly once");
        section102Entries.Should().OnlyContain(e => e.Required,
            "H4b:286 silently skips missing optional entries -- SF-18 forbids required=false " +
            "on any of the 8 §10.2 live-contract entries");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ExactlyOneCredentialsOrderEntry()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var orderEntries = entries.Where(e => e.Key.StartsWith("Graph__Credentials__Order__", StringComparison.Ordinal)).ToList();

        orderEntries.Should().ContainSingle(
            "the ordered credential selector fails opaquely if any non-MI-FIC provider is " +
            "listed alongside RequireSecretFreeIdentity=true -- Order__0 must be the ONLY entry");
        orderEntries[0].Key.Should().Be("Graph__Credentials__Order__0");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_CredentialOrderZero_IsManagedIdentityFederatedLiteral()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var order0 = entries.Single(e => e.Key == "Graph__Credentials__Order__0");

        order0.PerEnvSource.Should().Be(PerEnvSettingSource.Literal);
        order0.LiteralValue.Should().Be("ManagedIdentityFederated");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_RequireSecretFreeIdentity_IsTrueLiteral()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var flag = entries.Single(e => e.Key == "Graph__Credentials__RequireSecretFreeIdentity");

        flag.PerEnvSource.Should().Be(PerEnvSettingSource.Literal);
        flag.LiteralValue.Should().Be("true");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ManagedIdentityClientId_SourcesUamiClientId_Sf2Guard()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var entry = entries.Single(e => e.Key == "ManagedIdentity__ClientId");

        entry.PerEnvSource.Should().Be(PerEnvSettingSource.FromHandlerOutput);
        // SF-2 identifier-swap trap: MUST be the clientId source (uami_client_id,
        // backed by InterStepState.MiClientId), NEVER an object-id-shaped source
        // (which would be InterStepState.MiObjectId -- the RBAC principalId).
        entry.ParameterKey.Should().Be("uami_client_id");
        entry.ParameterKey.Should().NotContain("object",
            "MiObjectId (principalId) is for RBAC assignments only -- sourcing it here " +
            "creates successfully and fails only at token exchange with AADSTS700213");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ServiceBusFqnsEntries_ShareOneFromHandlerOutputSource()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var fqnsKeys = new[]
        {
            "ServiceBus__FullyQualifiedNamespace",
            "Membership__EventPublisher__ServiceBusNamespace",
            "Membership__JunctionUpdater__ServiceBusNamespace",
        };
        var fqnsEntries = entries.Where(e => fqnsKeys.Contains(e.Key)).ToList();

        fqnsEntries.Should().HaveCount(3);
        fqnsEntries.Should().OnlyContain(e => e.PerEnvSource == PerEnvSettingSource.FromHandlerOutput,
            "a literal here would hardcode one namespace name across every customer stamp");
        fqnsEntries.Select(e => e.ParameterKey).Distinct().Should().ContainSingle(
            "all 3 settings share ONE source per the manifest's existing dedup convention " +
            "(mirrors ManagedIdentity__ClientId / Graph__ManagedIdentity__ClientId sharing uami_client_id)");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_AiSearchAndAiSafetyMiFlags_AreTrueLiterals()
    {
        var manifest = NewManifest();
        var result = await manifest.ReadAsync(CancellationToken.None);
        var entries = ((PerEnvSettingsManifestReadResult.Success)result).Entries;

        var aiSearch = entries.Single(e => e.Key == "AiSearch__ManagedIdentity__Enabled");
        var aiSafety = entries.Single(e => e.Key == "AiSafety__ContentSafety__ManagedIdentity__Enabled");

        aiSearch.PerEnvSource.Should().Be(PerEnvSettingSource.Literal);
        aiSearch.LiteralValue.Should().Be("true");
        aiSafety.PerEnvSource.Should().Be(PerEnvSettingSource.Literal);
        aiSafety.LiteralValue.Should().Be("true");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_TwoConsecutiveCalls_ReturnSameEntryCount()
    {
        var manifest = NewManifest();

        var first = ((PerEnvSettingsManifestReadResult.Success)await manifest.ReadAsync(CancellationToken.None)).Entries;
        var second = ((PerEnvSettingsManifestReadResult.Success)await manifest.ReadAsync(CancellationToken.None)).Entries;

        second.Count.Should().Be(first.Count, "Lazy-cached -- Singleton lifetime contract");
    }
}
