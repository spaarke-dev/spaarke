// -----------------------------------------------------------------------------
// FileKvSecretManifestTests.cs
//
// L2 CONTROL-PLANE unit tests for FileKvSecretManifest (task 126, Wave G-2
// Batch G-2C — the "task-084 canonical manifest DI-swap (C2.2)" half of this
// task). Exercises the REAL embedded-resource + YamlDotNet parse path against
// the REAL scripts/canonical-secret-catalog/manifest.yaml content (embedded
// at build time) — not a hand-rolled fake manifest string — so a drift
// between this reader's expectations and the actual manifest.yaml schema is
// caught here, not silently at runtime.
//
// COVERAGE:
//   T1  ReadAsync against the real embedded manifest.yaml succeeds and
//       returns > 0 entries (proves the embedded resource + YAML parse
//       pipeline works end-to-end).
//   T2  Dataverse-ClientSecret + BFF-API-ClientSecret are both present
//       (BINDING invariant — spec.md MUST rule).
//   T3  Every entry's Operation is Upsert (manifest.yaml never declares
//       Delete — alias-collapse is a manual, pre-checked action).
//   T4  value_source strings map to the correct KvSecretValueSource enum
//       members for a sample of known entries (from-existing-kv,
//       from-bicep-output, from-run-parameter, generated).
//   T5  Two consecutive ReadAsync calls return the SAME entry count
//       (Lazy-cached — Singleton lifetime contract from IKvSecretManifest.cs).
//   T6  Entries are sorted alphabetically by CanonicalName (ordinal —
//       determinism contract).
//
//   A38a (task 205a, 2026-08-25 — secret-free served-entry filter):
//   A38a-1  RequireSecretFreeIdentity=true EXCLUDES the three omit targets
//           from served entries (count shrinks by exactly 3); manifest.yaml
//           rows unchanged (the raw document still parses them — proven by
//           the default-branch tests above against the SAME embedded yaml).
//   A38a-2  Default options (false) INCLUDE all three targets.
//   A38a-3  Q3 Path A rollback (both flags true) re-INCLUDES all three.
//   A38a-4  Dataverse-ClientSecret served under BOTH branches (§6.5 record).
//   A38a-5  :151 BINDING invariant still fires on synthetic yaml MISSING
//           BFF-API-ClientSecret / with never_delete=false — EVEN WITH the
//           secret-free filter active (filter is DOWNSTREAM of the
//           invariant; regression protection for the invariant's location).
//   A38a-6  Filter + invariant ordering: synthetic yaml WITH the required
//           rows + secret-free active → Success (invariant passed against
//           raw yaml) with the three targets absent from SERVED entries.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class FileKvSecretManifestTests
{
    private static FileKvSecretManifest NewManifest(KvSecretsPopulationOptions? options = null) => new(
        NullLogger<FileKvSecretManifest>.Instance,
        Options.Create(options ?? new KvSecretsPopulationOptions()));

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ReturnsPopulatedSuccess()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().NotBeEmpty();
        success.Entries.Count.Should().BeGreaterThanOrEqualTo(20,
            "manifest.yaml (task 084) declared 26 entries as of 2026-08-19 — a drastically smaller count would indicate a parse regression");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ContainsBothBindingNeverDeleteSecrets()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().Contain(e => e.CanonicalName == "Dataverse-ClientSecret");
        success.Entries.Should().Contain(e => e.CanonicalName == "BFF-API-ClientSecret");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_EveryEntryIsUpsert()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().OnlyContain(e => e.Operation == KvSecretOperation.Upsert,
            "manifest.yaml never declares a Delete op — alias-collapse is a manual, pre-checked action (task 085 pattern)");
    }

    [Theory]
    [InlineData("Dataverse-ClientSecret", KvSecretValueSource.FromExistingKvSecret)]
    [InlineData("TenantId", KvSecretValueSource.FromRunParameters)]
    // Task 200: AiSearch--AdminKey flipped from-bicep-output → from-shared-service
    // when the F19 automation manifest additions landed (Phase A of task 200).
    // Kept as an inline case here because it exercises the FromSharedService
    // parser mapping end-to-end against the real embedded manifest.
    [InlineData("AiSearch--AdminKey", KvSecretValueSource.FromSharedService)]
    [InlineData("SPE-ContainerTypeId", KvSecretValueSource.FromBicepOutput)]
    [InlineData("Communication-Webhook-SigningKey", KvSecretValueSource.Generated)]
    public async Task ReadAsync_RealEmbeddedManifest_MapsValueSourceCorrectly(string canonicalName, KvSecretValueSource expected)
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var entry = success.Entries.Should().ContainSingle(e => e.CanonicalName == canonicalName).Subject;
        entry.ValueSource.Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_CalledTwice_ReturnsSameEntryCount()
    {
        var manifest = NewManifest();

        var first = await manifest.ReadAsync(CancellationToken.None);
        var second = await manifest.ReadAsync(CancellationToken.None);

        var firstSuccess = first.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var secondSuccess = second.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        secondSuccess.Entries.Count.Should().Be(firstSuccess.Entries.Count);
    }

    // Task 200: from-shared-service entries MUST carry a non-empty ServiceRef
    // (parser enforces conditional-required; downstream H4-shared handler
    // parses it as '<type>:<az-resource-name>'). Non-shared-service entries
    // MUST leave ServiceRef null (no leakage of the shared-only field into
    // the per-tenant flow).
    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_FromSharedServiceEntries_CarryServiceRef()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var sharedEntries = success.Entries
            .Where(e => e.ValueSource == KvSecretValueSource.FromSharedService)
            .ToList();
        sharedEntries.Should().NotBeEmpty("Phase A of task 200 added 6 shared-service entries");
        sharedEntries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.ServiceRef));
        sharedEntries.Should().OnlyContain(e => e.ServiceRef!.Contains(':'),
            "service_ref format is '<type>:<az-resource-name>'");

        var nonSharedEntries = success.Entries
            .Where(e => e.ValueSource != KvSecretValueSource.FromSharedService);
        nonSharedEntries.Should().OnlyContain(e => e.ServiceRef == null,
            "ServiceRef is scoped to from-shared-service entries only — no leakage");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_EntriesSortedAlphabeticallyByCanonicalName()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var names = success.Entries.Select(e => e.CanonicalName).ToList();
        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    // =========================================================================
    // Row A38a (task 205a, 2026-08-25) — secret-free served-entry filter
    // =========================================================================

    private static readonly string[] A38aOmitTargets =
    {
        "BFF-API-ClientSecret",
        "ServiceBus-ConnectionString",
        "AiSearch--AdminKey",
    };

    [Fact]
    public void A38a_OmitTargetSet_ContainsExactlyTheThreeTargets_AndNeverDataverseClientSecret()
    {
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().HaveCount(3);
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().Contain(A38aOmitTargets);
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().NotContain("Dataverse-ClientSecret",
            "Q3 Path A rollback copy stays unconditional until the 2026-11-23 sunset (§6.5 record 2026-08-25)");
    }

    [Fact]
    public async Task A38a_SecretFreeTrue_ExcludesThreeTargets_CountShrinksByExactlyThree()
    {
        var baseline = await NewManifest().ReadAsync(CancellationToken.None);
        var baselineSuccess = baseline.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        baselineSuccess.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "the manifest.yaml rows themselves are UNCHANGED — the default branch serves all three");

        var filtered = await NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ReadAsync(CancellationToken.None);

        var filteredSuccess = filtered.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        filteredSuccess.Entries.Select(e => e.CanonicalName).Should().NotContain(A38aOmitTargets,
            "auth-v4 §9.1 — OMIT is the signal on secret-free environments");
        filteredSuccess.Entries.Count.Should().Be(baselineSuccess.Entries.Count - 3,
            "exactly the three A38a targets are filtered — nothing else");
    }

    [Fact]
    public async Task A38a_SecretFreeFalse_Default_IncludesAllThreeTargets()
    {
        var result = await NewManifest().ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "default (client-secret) environments are unchanged by A38a");
    }

    [Fact]
    public async Task A38a_Q3PathARollback_ReIncludesAllThreeTargets()
    {
        var result = await NewManifest(new KvSecretsPopulationOptions
        {
            RequireSecretFreeIdentity = true,
            SecretFreeIdentityRollback = true,
        }).ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "Q3 Path A rollback re-includes ONLY the three A38a targets (regression path)");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A38a_DataverseClientSecret_ServedUnderEveryBranch(bool secretFree, bool rollback)
    {
        var result = await NewManifest(new KvSecretsPopulationOptions
        {
            RequireSecretFreeIdentity = secretFree,
            SecretFreeIdentityRollback = rollback,
        }).ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().Contain(e => e.CanonicalName == "Dataverse-ClientSecret",
            "Dataverse-ClientSecret is the Q3 Path A rollback copy — unconditional until 2026-11-23 (§6.5 record)");
    }

    // ---- A38a-5/6: BINDING invariant + filter ordering on synthetic yaml ----

    private const string SyntheticYamlWithBothNeverDeleteRows = """
        secrets:
          - canonical_name: "Dataverse-ClientSecret"
            never_delete: true
            value_source: "from-existing-kv"
          - canonical_name: "BFF-API-ClientSecret"
            never_delete: true
            value_source: "from-existing-kv"
          - canonical_name: "ServiceBus-ConnectionString"
            never_delete: false
            value_source: "from-shared-service"
            service_ref: "servicebus:sprksharedprod-servicebus"
          - canonical_name: "AiSearch--AdminKey"
            never_delete: false
            value_source: "from-shared-service"
            service_ref: "search:sprksharedprod-search"
          - canonical_name: "Some-Other-Secret"
            never_delete: false
            value_source: "generated"
        """;

    [Fact]
    public void A38a_BindingInvariant_StillFires_WhenBffApiClientSecretRowMissing_EvenWithFilterActive()
    {
        // The :151 BINDING invariant runs against the RAW yaml document —
        // the A38a filter is DOWNSTREAM and MUST NOT weaken it. A yaml
        // missing BFF-API-ClientSecret is refused ENTIRELY, filter or not.
        const string yamlMissingBff = """
            secrets:
              - canonical_name: "Dataverse-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "Some-Other-Secret"
                never_delete: false
                value_source: "generated"
            """;

        foreach (var options in new[]
        {
            new KvSecretsPopulationOptions(),
            new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true },
        })
        {
            var result = NewManifest(options).ParseYamlForTest(yamlMissingBff);

            var failure = result.Should().BeOfType<KvSecretManifestReadResult.Failure>().Subject;
            failure.Diagnostic.Should().Contain("BINDING never-delete invariant violated");
            failure.Diagnostic.Should().Contain("BFF-API-ClientSecret");
            failure.Diagnostic.Should().Contain("MISSING");
        }
    }

    [Fact]
    public void A38a_BindingInvariant_StillFires_WhenNeverDeleteFalse()
    {
        const string yamlNeverDeleteFalse = """
            secrets:
              - canonical_name: "Dataverse-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "BFF-API-ClientSecret"
                never_delete: false
                value_source: "from-existing-kv"
            """;

        var result = NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ParseYamlForTest(yamlNeverDeleteFalse);

        var failure = result.Should().BeOfType<KvSecretManifestReadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("never_delete=false");
    }

    [Fact]
    public void A38a_FilterIsDownstreamOfInvariant_YamlRowsPresent_ServedEntriesFiltered()
    {
        // The invariant PASSES (both never-delete rows present in the raw
        // yaml) and THEN the filter removes the three targets from the
        // SERVED list — the exact "omit is a served-entry filter, NOT a yaml
        // row deletion" contract from the peer escalation record.
        var result = NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ParseYamlForTest(SyntheticYamlWithBothNeverDeleteRows);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().BeEquivalentTo(
            new[] { "Dataverse-ClientSecret", "Some-Other-Secret" },
            "BFF-API-ClientSecret + ServiceBus-ConnectionString + AiSearch--AdminKey are filtered from " +
            "SERVED entries; Dataverse-ClientSecret + unrelated entries stay");
    }
}
