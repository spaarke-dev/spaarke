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
//   (SPE-ContainerTypeId, task 214; retired again with that secret by T227e) parsed — before T226 the reader rejected
//   it, so EVERY ReadAsync against the real manifest returned Failure. The keys
//   the owner removed from the process must not be served.
//
//   A38a (task 205a, 2026-08-25 — secret-free served-entry filter; target set
//   reduced to BFF-API-ClientSecret by T226, then BFF-API-ClientSecret +
//   Dataverse-ClientSecret by task 225b / G21, which also made secret-free the
//   default — the BINDING rule: never create either credential secret in a
//   secret-free environment):
//   A38a-1  RequireSecretFreeIdentity=true EXCLUDES both omit targets
//           from served entries (count shrinks by exactly 2) vs the explicit
//           legacy client-secret path (false), which serves them; manifest.yaml
//           rows unchanged.
//   A38a-2  Default options are secret-free: both targets omitted.
//   A38a-3  Q3 Path A rollback (both flags true) re-INCLUDES both targets.
//   A38a-4  Dataverse-ClientSecret follows the omit-target rule on every branch.
//   A38a-5  :151 BINDING invariant still fires on synthetic yaml MISSING
//           BFF-API-ClientSecret / with never_delete=false — EVEN WITH the
//           secret-free filter active (filter is DOWNSTREAM of the
//           invariant; regression protection for the invariant's location).
//   A38a-6  Filter + invariant ordering: synthetic yaml WITH the required
//           rows + secret-free active → Success (invariant passed against
//           raw yaml) with the targets absent from SERVED entries.
//
//   Task 225b (owner D18, 2026-10-02): the Spaarke-shared vendor keys and their
//   from-platform-vault source left the catalog — the keys are asserted absent and
//   the source is refused as unrecognized.
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

    /// <summary>
    /// The legacy client-secret path (task 225b made secret-free the default) — serves every
    /// manifest row, including the two credential secrets the secret-free filter omits.
    /// </summary>
    private static FileKvSecretManifest NewLegacyClientSecretManifest() =>
        NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = false });

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ReturnsPopulatedSuccess()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().NotBeEmpty();
        // The catalog shrinks on purpose as keys are removed (owner D13): 21 entries on 2026-10-04 after task 242
        // removed Redis-ConnectionString, of which the default secret-free reader serves 19. The floor guards
        // against a parse regression (a handful of rows), not against deliberate removals.
        success.Entries.Count.Should().BeGreaterThanOrEqualTo(15,
            "the default reader served 19 entries on 2026-10-04 — a drastically smaller count would indicate a parse regression");
    }

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_ContainsBothBindingNeverDeleteSecrets()
    {
        // The rows are in the catalog; only the legacy client-secret path serves them.
        var manifest = NewLegacyClientSecretManifest();

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
    // (Redis-ConnectionString removed by task 242: stamp Redis is Entra-only, no secret.)
    [InlineData("Communication-Webhook-SigningKey", KvSecretValueSource.Generated)]
    public async Task ReadAsync_RealEmbeddedManifest_MapsValueSourceCorrectly(string canonicalName, KvSecretValueSource expected)
    {
        // Legacy client-secret path so the Dataverse-ClientSecret row is served (task 225b default omits it).
        var manifest = NewLegacyClientSecretManifest();

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
    // Task 225b (owner D18, 2026-10-02): the Spaarke-shared vendor keys left every stamp —
    // Bing Search v7 was retired by Microsoft 2025-08-11; LlamaParse has no production caller.
    // T243 (owner D13, 2026-10-02): the BFF reaches Document Intelligence with the stamp UAMI.
    [Theory]
    [InlineData("AiSearch--AdminKey")]
    [InlineData("ServiceBus-ConnectionString")]
    [InlineData("Storage-ConnectionString")]
    [InlineData("AzureOpenAI-ApiKey")]
    [InlineData("PromptFlow-Endpoint")]
    [InlineData("PromptFlow-Key")]
    [InlineData("BingSearch-ApiKey")]
    [InlineData("LlamaParse-ApiKey")]
    [InlineData("DocumentIntelligence-ApiKey")]
    public async Task ReadAsync_RealEmbeddedManifest_DoesNotServeKeyRemovedFromTheProcess(string canonicalName)
    {
        // Legacy client-secret path = the widest served set; absent there means absent everywhere.
        var manifest = NewLegacyClientSecretManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Should().NotContain(e => e.CanonicalName == canonicalName);
    }

    // T226 / task 245a: H4 projects intake values from run parameters through a fixed map. A
    // from-intake-parameter entry with no mapping fails on every run; a mapping with no
    // entry is dead code.
    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_IntakeSourcedEntriesMatchH4ParameterMap()
    {
        // Every entry H4 fills from an intake value (task 245a's from-intake-parameter) has a
        // parameter-key mapping in H4, and the map names nothing else.
        var result = await NewManifest().ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var intakeSourced = success.Entries
            .Where(e => e.ValueSource is KvSecretValueSource.FromIntakeParameter)
            .Select(e => e.CanonicalName);
        H4KvSecretsPopulationHandler.IntakeValueParameterKeys.Keys.Should().BeEquivalentTo(intakeSourced);
        H4KvSecretsPopulationHandler.IntakeValueParameterKeys.Values
            .Should().OnlyContain(key => IntakeParameterCatalog.IsKnown(key), "each maps to an accepted intake key");
    }

    // T226: from-shared-service is no longer a value_source; task 225b (owner D18) removed
    // from-platform-vault; T227e removed from-topology-constants. A manifest that still carries
    // one must be refused, not served with the entry silently skipped.
    [Theory]
    [InlineData("from-shared-service")]
    [InlineData("from-platform-vault")]
    [InlineData("from-topology-constants")]
    public void ParseYaml_UnrecognizedValueSource_IsRefusedNamingTheEntry(string retiredValueSource)
    {
        var yaml = $"""
            secrets:
              - canonical_name: "Dataverse-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "BFF-API-ClientSecret"
                never_delete: true
                value_source: "from-existing-kv"
              - canonical_name: "Redis-ConnectionString"
                never_delete: false
                value_source: "{retiredValueSource}"
            """;

        var result = NewManifest().ParseYamlForTest(yaml);

        var failure = result.Should().BeOfType<KvSecretManifestReadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Redis-ConnectionString");
        failure.Diagnostic.Should().Contain($"unrecognized value_source '{retiredValueSource}'");
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
        "Dataverse-ClientSecret",   // task 225b (G21)
    };

    [Fact]
    public void A38a_OmitTargetSet_IsExactlyTheTwoCredentialSecrets()
    {
        FileKvSecretManifest.SecretFreeIdentityOmitTargets.Should().BeEquivalentTo(A38aOmitTargets,
            "BINDING rule (task 225b): neither credential secret is ever created in a secret-free environment");
    }

    [Fact]
    public async Task A38a_SecretFreeTrue_ExcludesTargets_CountShrinksByExactlyTwo()
    {
        var baseline = await NewLegacyClientSecretManifest().ReadAsync(CancellationToken.None);
        var baselineSuccess = baseline.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        baselineSuccess.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "the manifest.yaml rows themselves are UNCHANGED — the legacy client-secret path serves the targets");

        var filtered = await NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ReadAsync(CancellationToken.None);

        var filteredSuccess = filtered.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        filteredSuccess.Entries.Select(e => e.CanonicalName).Should().NotContain(A38aOmitTargets,
            "auth-v4 §9.1 — OMIT is the signal on secret-free environments");
        filteredSuccess.Entries.Count.Should().Be(baselineSuccess.Entries.Count - 2,
            "exactly the two A38a targets are filtered — nothing else");
    }

    [Fact]
    public async Task A38a_DefaultOptions_AreSecretFree_OmitBothTargets()
    {
        var result = await NewManifest().ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().NotContain(A38aOmitTargets,
            "task 225b (G21): RequireSecretFreeIdentity defaults to true — every new stamp runs MI-FIC");
    }

    [Fact]
    public async Task A38a_Q3PathARollback_ReIncludesTargets()
    {
        var result = await NewManifest(new KvSecretsPopulationOptions
        {
            RequireSecretFreeIdentity = true,
            SecretFreeIdentityRollback = true,
        }).ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().Contain(A38aOmitTargets,
            "Q3 Path A rollback re-includes both A38a targets (regression path)");
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task A38a_DataverseClientSecret_FollowsTheOmitTargetRule(bool secretFree, bool rollback, bool expectServed)
    {
        var result = await NewManifest(new KvSecretsPopulationOptions
        {
            RequireSecretFreeIdentity = secretFree,
            SecretFreeIdentityRollback = rollback,
        }).ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Any(e => e.CanonicalName == "Dataverse-ClientSecret").Should().Be(expectServed,
            "task 225b: omitted when secret-free, served on the legacy path and under Q3 Path A rollback");
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
            new KvSecretsPopulationOptions { RequireSecretFreeIdentity = false }, // legacy path (T225b: the default is now secret-free)
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
        // yaml) and THEN the filter removes the targets from the
        // SERVED list — the exact "omit is a served-entry filter, NOT a yaml
        // row deletion" contract from the peer escalation record.
        var result = NewManifest(new KvSecretsPopulationOptions { RequireSecretFreeIdentity = true })
            .ParseYamlForTest(SyntheticYamlWithBothNeverDeleteRows);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        success.Entries.Select(e => e.CanonicalName).Should().BeEquivalentTo(
            new[] { "Some-Other-Secret" },
            "BFF-API-ClientSecret + Dataverse-ClientSecret are filtered from SERVED entries (task 225b); " +
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
