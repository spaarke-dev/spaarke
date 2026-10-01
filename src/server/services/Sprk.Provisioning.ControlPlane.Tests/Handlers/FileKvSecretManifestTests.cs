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
//   T226 (2026-09-30): from-shared-service retired; from-topology-constants
//   (SPE-ContainerTypeId, task 214) now parses — before T226 the reader rejected
//   it, so EVERY ReadAsync against the real manifest returned Failure. The keys
//   the owner removed from the process must not be served.
//
//   A38a (task 205a, 2026-08-25 — secret-free served-entry filter; target set
//   reduced to BFF-API-ClientSecret by T226):
//   A38a-1  RequireSecretFreeIdentity=true EXCLUDES the omit target
//           from served entries (count shrinks by exactly 1); manifest.yaml
//           rows unchanged (the raw document still parses them — proven by
//           the default-branch tests above against the SAME embedded yaml).
//   A38a-2  Default options (false) INCLUDE the target.
//   A38a-3  Q3 Path A rollback (both flags true) re-INCLUDES the target.
//   A38a-4  Dataverse-ClientSecret served under BOTH branches (§6.5 record).
//   A38a-5  :151 BINDING invariant still fires on synthetic yaml MISSING
//           BFF-API-ClientSecret / with never_delete=false — EVEN WITH the
//           secret-free filter active (filter is DOWNSTREAM of the
//           invariant; regression protection for the invariant's location).
//   A38a-6  Filter + invariant ordering: synthetic yaml WITH the required
//           rows + secret-free active → Success (invariant passed against
//           raw yaml) with the target absent from SERVED entries.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Models;
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
    // Task 245a: TenantId comes from the intake tenantId; the BFF app's id + audience are committed by
    // H3 itself (it runs after H4). All three used to wait on RunParameters.Secrets refs nobody supplied.
    [InlineData("TenantId", KvSecretValueSource.FromIntakeParameter)]
    [InlineData("BFF-API-ClientId", KvSecretValueSource.WrittenByEntraAppReg)]
    [InlineData("BFF-API-Audience", KvSecretValueSource.WrittenByEntraAppReg)]
    // T226: Redis is written by customer.bicep from the customer's own cache.
    [InlineData("Redis-ConnectionString", KvSecretValueSource.FromBicepOutput)]
    // T226: task 214's from-topology-constants — the reader rejected it before T226.
    [InlineData("SPE-ContainerTypeId", KvSecretValueSource.FromTopologyConstants)]
    // T226 / owner D13: interim key from the customer's own Document Intelligence (T243 removes it).
    [InlineData("DocumentIntelligence-ApiKey", KvSecretValueSource.FromBicepOutput)]
    [InlineData("Communication-Webhook-SigningKey", KvSecretValueSource.Generated)]
    // T245b: Spaarke-shared vendor keys (owner D5) are copied from the Spaarke platform vault.
    [InlineData("BingSearch-ApiKey", KvSecretValueSource.FromPlatformVault)]
    [InlineData("LlamaParse-ApiKey", KvSecretValueSource.FromPlatformVault)]
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

    // T226 (owner 2026-09-30): these keys were removed from the process — the BFF
    // reaches Service Bus, AI Search and OpenAI with the stamp UAMI, nothing reads
    // the Storage connection string, and Prompt Flow is retired
    // (D5). If one reappears in the catalog, H4b would emit a KV reference for it and
    // an unresolvable reference reaches the BFF as a literal "key".
    [Theory]
    [InlineData("AiSearch--AdminKey")]
    [InlineData("ServiceBus-ConnectionString")]
    [InlineData("Storage-ConnectionString")]
    [InlineData("AzureOpenAI-ApiKey")]
    [InlineData("PromptFlow-Endpoint")]
    [InlineData("PromptFlow-Key")]
    public async Task ReadAsync_RealEmbeddedManifest_DoesNotServeKeyRemovedFromTheProcess(string canonicalName)
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().NotContain(e => e.CanonicalName == canonicalName);
    }

    // T226: H4 projects topology constants from run parameters through a fixed map. A
    // from-topology-constants entry with no mapping fails on every run; a mapping with no
    // entry is dead code.
    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_IntakeSourcedEntriesMatchH4ParameterMap()
    {
        // Every entry H4 fills from an intake value (from-topology-constants, task 245a's
        // from-intake-parameter) has a parameter-key mapping in H4, and the map names nothing else.
        var result = await NewManifest().ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var intakeSourced = success.Entries
            .Where(e => e.ValueSource is KvSecretValueSource.FromTopologyConstants or KvSecretValueSource.FromIntakeParameter)
            .Select(e => e.CanonicalName);
        H4KvSecretsPopulationHandler.IntakeValueParameterKeys.Keys.Should().BeEquivalentTo(intakeSourced);
        H4KvSecretsPopulationHandler.IntakeValueParameterKeys.Values
            .Should().OnlyContain(key => IntakeParameterCatalog.IsKnown(key), "each maps to an accepted intake key");
    }

    // T226: from-shared-service is no longer a value_source. A manifest that still
    // carries one must be refused, not served with the entry silently skipped.
    [Fact]
    public void ParseYaml_UnrecognizedValueSource_IsRefusedNamingTheEntry()
    {
        const string yaml = """
            secrets:
              - canonical_name: "Dataverse-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "BFF-API-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "Redis-ConnectionString"
                never_delete: false
                value_source: "from-shared-service"
            """;

        var result = NewManifest().ParseYamlForTest(yaml);

        var failure = result.Should().BeOfType<KvSecretManifestReadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Redis-ConnectionString");
        failure.Diagnostic.Should().Contain("unrecognized value_source 'from-shared-service'");
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
    };

    [Fact]
    public void A38a_OmitTargetSet_ContainsExactlyBffApiClientSecret_AndNeverDataverseClientSecret()
    {
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().HaveCount(1);
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().Contain(A38aOmitTargets);
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().NotContain("Dataverse-ClientSecret",
            "Q3 Path A rollback copy stays unconditional until the 2026-11-23 sunset (§6.5 record 2026-08-25)");
    }

    [Fact]
    public async Task A38a_SecretFreeTrue_ExcludesTarget_CountShrinksByExactlyOne()
    {
        var baseline = await NewManifest().ReadAsync(CancellationToken.None);
        var baselineSuccess = baseline.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        baselineSuccess.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "the manifest.yaml rows themselves are UNCHANGED — the default branch serves the target");

        var filtered = await NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ReadAsync(CancellationToken.None);

        var filteredSuccess = filtered.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        filteredSuccess.Entries.Select(e => e.CanonicalName).Should().NotContain(A38aOmitTargets,
            "auth-v4 §9.1 — OMIT is the signal on secret-free environments");
        filteredSuccess.Entries.Count.Should().Be(baselineSuccess.Entries.Count - 1,
            "exactly the A38a target is filtered — nothing else");
    }

    [Fact]
    public async Task A38a_SecretFreeFalse_Default_IncludesTarget()
    {
        var result = await NewManifest().ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "default (client-secret) environments are unchanged by A38a");
    }

    [Fact]
    public async Task A38a_Q3PathARollback_ReIncludesTarget()
    {
        var result = await NewManifest(new KvSecretsPopulationOptions
        {
            RequireSecretFreeIdentity = true,
            SecretFreeIdentityRollback = true,
        }).ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "Q3 Path A rollback re-includes the A38a target (regression path)");
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
        // yaml) and THEN the filter removes the target from the
        // SERVED list — the exact "omit is a served-entry filter, NOT a yaml
        // row deletion" contract from the peer escalation record.
        var result = NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ParseYamlForTest(SyntheticYamlWithBothNeverDeleteRows);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().BeEquivalentTo(
            new[] { "Dataverse-ClientSecret", "Some-Other-Secret" },
            "BFF-API-ClientSecret is filtered from SERVED entries; Dataverse-ClientSecret + " +
            "unrelated entries stay");
    }

    // ---- Task 245b: the manifest's content version is H4's (and H4b's) secretsVer ----

    [Fact]
    public void ContentVersion_SameYamlSameVersion_ChangedYamlNewVersion()
    {
        var a = (KvSecretManifestReadResult.Success)NewManifest().ParseYamlForTest(SyntheticYamlWithBothNeverDeleteRows);
        var again = (KvSecretManifestReadResult.Success)NewManifest().ParseYamlForTest(SyntheticYamlWithBothNeverDeleteRows);
        var edited = (KvSecretManifestReadResult.Success)NewManifest().ParseYamlForTest(
            SyntheticYamlWithBothNeverDeleteRows.Replace("Some-Other-Secret", "Some-Renamed-Secret"));

        a.ContentVersion.Should().MatchRegex("^[0-9a-f]{64}$");
        again.ContentVersion.Should().Be(a.ContentVersion);
        edited.ContentVersion.Should().NotBe(a.ContentVersion);
    }

    [Fact]
    public async Task ContentVersion_H4AndH4bReadTheSameManifest_SoTheyAgreeOnSecretsVer()
    {
        var h4 = (KvSecretManifestReadResult.Success)await NewManifest().ReadAsync(CancellationToken.None);
        var h4b = (Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.PerEnvSettingsManifestReadResult.Success)
            await new Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.FilePerEnvSettingsManifest(
                NullLogger<Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.FilePerEnvSettingsManifest>.Instance)
                .ReadAsync(CancellationToken.None);

        h4b.ContentVersion.Should().Be(h4.ContentVersion,
            "both readers hash the one embedded scripts/canonical-secret-catalog/manifest.yaml (ArtifactVersion)");
    }
}
