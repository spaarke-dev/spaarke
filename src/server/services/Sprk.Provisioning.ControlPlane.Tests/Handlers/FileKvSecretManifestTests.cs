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
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class FileKvSecretManifestTests
{
    private static FileKvSecretManifest NewManifest() => new(NullLogger<FileKvSecretManifest>.Instance);

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
    [InlineData("AiSearch--AdminKey", KvSecretValueSource.FromBicepOutput)]
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

    [Fact]
    public async Task ReadAsync_RealEmbeddedManifest_EntriesSortedAlphabeticallyByCanonicalName()
    {
        var manifest = NewManifest();

        var result = await manifest.ReadAsync(CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject;
        var names = success.Entries.Select(e => e.CanonicalName).ToList();
        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
