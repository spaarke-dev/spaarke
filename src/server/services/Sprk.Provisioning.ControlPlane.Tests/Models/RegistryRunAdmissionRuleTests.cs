// -----------------------------------------------------------------------------
// RegistryRunAdmissionRuleTests.cs — ISS-031 (#2926). Every branch of the REG-07 admission rule: which registry rows
// POST /api/runs admits a run against. An admitted row is never moved.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class RegistryRunAdmissionRuleTests
{
    private static readonly DateTimeOffset Provisioned = new(2026, 11, 23, 14, 1, 48, TimeSpan.Zero);

    // --- never provisioned (first provisioning) ---

    [Fact]
    public void FirstProvisioning_InProgress_NoAcknowledgement_Admits()
        => RegistryRunAdmissionRule.Evaluate("InProgress", null, null)
            .Should().Be(new RegistryRunAdmission.Admit());

    [Theory]
    [InlineData("Ready")]
    [InlineData("Issue")]
    [InlineData("NotStarted")]
    public void NeverProvisioned_NotInProgress_IsRefused(string status)
        => RegistryRunAdmissionRule.Evaluate(status, null, null)
            .Should().BeOfType<RegistryRunAdmission.NotInProgress>()
            .Which.Detail.Should().Contain($"setupStatus='{status}'");

    [Fact]
    public void NeverProvisioned_WithAcknowledgement_IsRefused()
        => RegistryRunAdmissionRule.Evaluate("InProgress", null, "2026-11-23T14:01:48Z")
            .Should().BeOfType<RegistryRunAdmission.UpgradeMismatch>()
            .Which.Detail.Should().Contain("never been provisioned");

    // --- existing stamp ---

    [Theory]
    [InlineData("Ready")]
    [InlineData("InProgress")]
    public void ExistingStamp_WithoutAcknowledgement_IsRefused_NamingTheValue(string status)
        => RegistryRunAdmissionRule.Evaluate(status, Provisioned, "  ")
            .Should().BeOfType<RegistryRunAdmission.UpgradeNotAcknowledged>()
            .Which.Detail.Should().Contain("upgradeOfProvisionedOn']='2026-11-23T14:01:48Z'");

    [Theory]
    [InlineData("Ready")]
    [InlineData("InProgress")]
    public void ExistingStamp_MatchingAcknowledgement_Admits(string status)
        => RegistryRunAdmissionRule.Evaluate(status, Provisioned, "2026-11-23T14:01:48Z")
            .Should().Be(new RegistryRunAdmission.Admit());

    [Theory]
    [InlineData("2026-11-23T14:01:48.000Z")]
    [InlineData("2026-11-23T14:01:48.4Z")]          // sub-second noise from a client
    [InlineData("2026-11-23T09:01:48-05:00")]       // same instant, another offset
    [InlineData("2026-11-23 14:01:48")]             // no zone: read as UTC, as Dataverse writes it
    public void ExistingStamp_MatchingAcknowledgement_InAnotherSpelling_Admits(string acknowledgement)
        => RegistryRunAdmissionRule.Evaluate("ready", Provisioned, acknowledgement)
            .Should().Be(new RegistryRunAdmission.Admit());

    [Theory]
    [InlineData("2026-11-23T14:01:49Z")]   // one second off
    [InlineData("2026-10-20T09:00:00Z")]   // the original provisioning time, overwritten by the last completed run
    [InlineData("yes")]                    // not a timestamp
    public void ExistingStamp_MismatchedAcknowledgement_IsRefused(string acknowledgement)
        => RegistryRunAdmissionRule.Evaluate("Ready", Provisioned, acknowledgement)
            .Should().BeOfType<RegistryRunAdmission.UpgradeMismatch>()
            .Which.Detail.Should().Contain("2026-11-23T14:01:48Z");

    [Fact]
    public void ExistingStamp_IssueStatus_MatchingAcknowledgement_IsRefused()
        => RegistryRunAdmissionRule.Evaluate("Issue", Provisioned, "2026-11-23T14:01:48Z")
            .Should().BeOfType<RegistryRunAdmission.NotInProgress>();
}
