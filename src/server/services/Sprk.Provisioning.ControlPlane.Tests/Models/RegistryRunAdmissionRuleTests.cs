// -----------------------------------------------------------------------------
// RegistryRunAdmissionRuleTests.cs — ISS-031 (#2926). Every branch of the REG-07 admission rule: which registry rows
// POST /api/runs admits a run against, and when it moves a Ready row to InProgress.
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
    public void FirstProvisioning_InProgress_NoClaim_Admits_WithoutMove()
        => RegistryRunAdmissionRule.Evaluate("InProgress", null, null)
            .Should().Be(new RegistryRunAdmission.Admit(MoveToInProgress: false));

    [Theory]
    [InlineData("Ready")]
    [InlineData("Issue")]
    [InlineData("NotStarted")]
    public void NeverProvisioned_NotInProgress_IsRefused(string status)
        => RegistryRunAdmissionRule.Evaluate(status, null, null)
            .Should().BeOfType<RegistryRunAdmission.NotInProgress>()
            .Which.Detail.Should().Contain($"setupStatus='{status}'");

    [Fact]
    public void NeverProvisioned_WithUpgradeClaim_IsRefused()
        => RegistryRunAdmissionRule.Evaluate("InProgress", null, "2026-11-23T14:01:48Z")
            .Should().BeOfType<RegistryRunAdmission.UpgradeMismatch>()
            .Which.Detail.Should().Contain("never been provisioned");

    // --- existing stamp (upgrade) ---

    [Theory]
    [InlineData("Ready")]
    [InlineData("InProgress")]
    public void ExistingStamp_WithoutClaim_IsRefused_NamingTheValue(string status)
        => RegistryRunAdmissionRule.Evaluate(status, Provisioned, "  ")
            .Should().BeOfType<RegistryRunAdmission.UpgradeNotAcknowledged>()
            .Which.Detail.Should().Contain("'2026-11-23T14:01:48Z'");

    [Fact]
    public void ExistingStamp_Ready_MatchingClaim_Admits_AndMoves()
        => RegistryRunAdmissionRule.Evaluate("Ready", Provisioned, "2026-11-23T14:01:48Z")
            .Should().Be(new RegistryRunAdmission.Admit(MoveToInProgress: true));

    [Theory]
    [InlineData("2026-11-23T14:01:48.000Z")]
    [InlineData("2026-11-23T14:01:48.4Z")]          // sub-second noise from a client
    [InlineData("2026-11-23T09:01:48-05:00")]       // same instant, another offset
    [InlineData("2026-11-23 14:01:48")]             // no zone: read as UTC, as Dataverse writes it
    public void ExistingStamp_MatchingClaim_InAnotherSpelling_Admits(string claim)
        => RegistryRunAdmissionRule.Evaluate("ready", Provisioned, claim)
            .Should().Be(new RegistryRunAdmission.Admit(MoveToInProgress: true));

    [Fact]
    public void ExistingStamp_InProgress_MatchingClaim_Admits_WithoutMove()
        => RegistryRunAdmissionRule.Evaluate("InProgress", Provisioned, "2026-11-23T14:01:48Z")
            .Should().Be(new RegistryRunAdmission.Admit(MoveToInProgress: false));

    [Theory]
    [InlineData("2026-11-23T14:01:49Z")]   // one second off
    [InlineData("2026-10-20T09:00:00Z")]   // the original provisioning time, overwritten by the last completed run
    [InlineData("yes")]                    // not a timestamp
    public void ExistingStamp_MismatchedClaim_IsRefused(string claim)
        => RegistryRunAdmissionRule.Evaluate("Ready", Provisioned, claim)
            .Should().BeOfType<RegistryRunAdmission.UpgradeMismatch>()
            .Which.Detail.Should().Contain("2026-11-23T14:01:48Z");

    [Fact]
    public void ExistingStamp_IssueStatus_MatchingClaim_IsRefused()
        => RegistryRunAdmissionRule.Evaluate("Issue", Provisioned, "2026-11-23T14:01:48Z")
            .Should().BeOfType<RegistryRunAdmission.NotInProgress>();
}
