// -----------------------------------------------------------------------------
// SecretFreeMarkerTests.cs
//
// Row A38a (task 205a, 2026-08-25) — unit tests over the positive
// secret-free migration marker components:
//   1. SecretFreeMarkerConsistencyDetector — Model 2 fleet-consistency
//      (remediation plan §5.3): MIXED tag state across N per-customer vaults
//      is the failure record; uniform (all/none) is healthy.
//   2. ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied — the pure
//      check-then-apply idempotency decision ("2nd invocation is a no-op").
//   3. DataverseEnvironmentRegistryClient.BuildCredentialModePatchBody —
//      wire-shape guard: sprk_credentialmode ships as a JSON STRING (single-
//      line-of-text column), unlike sprk_setupstatus's option-set integer.
//
// ADR-038 CATEGORY: Path #1 pure C# unit tests — no ARM / Dataverse / HTTP.
// The live tag/PATCH paths are seam-tested per the registry client's
// established env-guarded posture; CI covers the DECISION logic as pure
// functions (same rationale as BuildPatchBody / ParseSnapshot).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SecretFreeMarkerConsistencyDetectorTests
{
    private static (SecretFreeMarkerConsistencyDetector detector, List<string> warnings) NewDetector()
    {
        var warnings = new List<string>();
        return (new SecretFreeMarkerConsistencyDetector(new CapturingLogger(warnings)), warnings);
    }

    [Fact]
    public void MixedState_TwoTaggedOneNot_ReturnsMixedFailureRecord_AndLogsWarning()
    {
        var (detector, warnings) = NewDetector();
        var observations = new List<VaultMarkerObservation>
        {
            new("kv-acme-v1", HasSecretFreeTag: true),
            new("kv-globex-v1", HasSecretFreeTag: true),
            new("kv-initech-v1", HasSecretFreeTag: false),
        };

        var result = detector.Evaluate(observations);

        var mixed = result.Should().BeOfType<SecretFreeMarkerConsistencyResult.Mixed>().Subject;
        mixed.TaggedVaultIds.Should().BeEquivalentTo(new[] { "kv-acme-v1", "kv-globex-v1" });
        mixed.UntaggedVaultIds.Should().BeEquivalentTo(new[] { "kv-initech-v1" });

        warnings.Should().ContainSingle(w => w.Contains("MIXED") && w.Contains("kv-initech-v1"),
            "the detector logs Warning naming the untagged vaults — detector, not fatal gate");
    }

    [Fact]
    public void AllTagged_ReturnsUniformAllTagged_NoWarning()
    {
        var (detector, warnings) = NewDetector();
        var result = detector.Evaluate(new List<VaultMarkerObservation>
        {
            new("kv-a", true), new("kv-b", true), new("kv-c", true),
        });

        var uniform = result.Should().BeOfType<SecretFreeMarkerConsistencyResult.Uniform>().Subject;
        uniform.AllTagged.Should().BeTrue();
        uniform.VaultCount.Should().Be(3);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void NoneTagged_PreMigrationFleet_ReturnsUniformNotTagged_NoWarning()
    {
        var (detector, warnings) = NewDetector();
        var result = detector.Evaluate(new List<VaultMarkerObservation>
        {
            new("kv-a", false), new("kv-b", false),
        });

        var uniform = result.Should().BeOfType<SecretFreeMarkerConsistencyResult.Uniform>().Subject;
        uniform.AllTagged.Should().BeFalse();
        warnings.Should().BeEmpty("a uniformly pre-migration fleet is healthy, not mixed");
    }

    [Fact]
    public void EmptyFleet_VacuouslyUniform()
    {
        var (detector, _) = NewDetector();
        var result = detector.Evaluate(Array.Empty<VaultMarkerObservation>());
        result.Should().BeOfType<SecretFreeMarkerConsistencyResult.Uniform>()
            .Which.VaultCount.Should().Be(0);
    }

    /// <summary>Captures Warning-level formatted messages only.</summary>
    private sealed class CapturingLogger : ILogger<SecretFreeMarkerConsistencyDetector>
    {
        private readonly List<string> _warnings;
        public CapturingLogger(List<string> warnings) => _warnings = warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}

public sealed class ArmSecretFreeMarkerApplierPureTests
{
    [Fact]
    public void IsVaultTagAlreadyApplied_TagPresentTrue_IsIdempotentNoOp()
    {
        var tags = new Dictionary<string, string>
        {
            ["spaarke-secret-free-identity"] = "true",
            ["unrelated"] = "x",
        };
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(tags).Should().BeTrue(
            "check-then-apply: a 2nd invocation reads the tag and issues NO ARM write");
    }

    [Fact]
    public void IsVaultTagAlreadyApplied_CaseInsensitiveValue()
    {
        var tags = new Dictionary<string, string> { ["spaarke-secret-free-identity"] = "True" };
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(tags).Should().BeTrue();
    }

    [Fact]
    public void IsVaultTagAlreadyApplied_TagAbsentOrWrongValueOrNull_RequiresApply()
    {
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(null).Should().BeFalse();
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(new Dictionary<string, string>()).Should().BeFalse();
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(
            new Dictionary<string, string> { ["spaarke-secret-free-identity"] = "false" }).Should().BeFalse();
        ArmSecretFreeMarkerApplier.IsVaultTagAlreadyApplied(
            new Dictionary<string, string> { ["other-tag"] = "true" }).Should().BeFalse();
    }

    [Fact]
    public void MarkerConstants_MatchTheA38aContract()
    {
        // Grep-stable contract consumed by A38c operator-script gates +
        // infra scans — renaming silently breaks their pre-checks.
        SecretFreeMarker.VaultTagName.Should().Be("spaarke-secret-free-identity");
        SecretFreeMarker.VaultTagValue.Should().Be("true");
        SecretFreeMarker.CredentialModeSecretFree.Should().Be("secret-free");
    }
}

public sealed class RegistryCredentialModePatchBodyTests
{
    [Fact]
    public void BuildCredentialModePatchBody_ShipsStringValue_NotOptionSetInteger()
    {
        var body = DataverseEnvironmentRegistryClient.BuildCredentialModePatchBody("secret-free");
        body.Should().Be("{\"sprk_credentialmode\":\"secret-free\"}",
            "sprk_credentialmode is a single-line-of-text column — JSON string on the wire " +
            "(unlike sprk_setupstatus's option-set integer)");
    }

    [Fact]
    public void BuildCredentialModePatchBody_EmptyValue_Throws()
    {
        var act = () => DataverseEnvironmentRegistryClient.BuildCredentialModePatchBody(" ");
        act.Should().Throw<ArgumentException>();
    }
}
