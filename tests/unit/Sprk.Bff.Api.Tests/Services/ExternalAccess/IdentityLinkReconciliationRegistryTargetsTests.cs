// customer-provisioning-orchestration-r1 T186: the identity-link reconciliation job reconciles only registry rows whose
// setup has finished. The ACME placeholder (sprk_isactive = true, sprk_setupstatus = InProgress) failed the job every
// 5 minutes on spaarke-bff-dev: the environment had no identity-binding schema and no BFF access yet.

using FluentAssertions;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.ExternalAccess;

public class IdentityLinkReconciliationRegistryTargetsTests
{
    private static DataverseEnvironmentRecord Row(string url, EnvironmentSetupStatus? status)
        => new() { DataverseUrl = url, SetupStatus = status };

    [Fact]
    public void ReadyRows_AreReconciled()
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(new[] { Row("https://ready.crm.dynamics.com", EnvironmentSetupStatus.Ready) })
            .Should().Equal("https://ready.crm.dynamics.com");

    [Theory]
    [InlineData(EnvironmentSetupStatus.NotStarted)]
    [InlineData(EnvironmentSetupStatus.InProgress)]
    [InlineData(EnvironmentSetupStatus.Issue)]
    public void RowsWhoseSetupHasNotFinished_AreSkipped(EnvironmentSetupStatus status)
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(new[] { Row("https://provisioning.crm.dynamics.com", status) })
            .Should().BeEmpty();

    [Fact]
    public void RowsWithoutAStatus_PredateTheColumn_AndAreKept()
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(new[] { Row("https://legacy.crm.dynamics.com", null) })
            .Should().Equal("https://legacy.crm.dynamics.com");

    [Fact]
    public void TheT186Registry_YieldsOnlyTheReadyAndLegacyRows()
    {
        var registry = new[]
        {
            Row("https://spaarke-acme.crm.dynamics.com/", EnvironmentSetupStatus.InProgress),
            Row("https://spaarkedev1.crm.dynamics.com", EnvironmentSetupStatus.Ready),
            Row("https://legacy.crm.dynamics.com", null),
        };

        var targets = IdentityLinkReconciliationJob.ProvisioningTargetUrls(
            ownEnvironment: "https://own.crm.dynamics.com",
            registrationEnvironment: null,
            registryEnvironments: IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry));

        targets.Should().Equal("https://spaarkedev1.crm.dynamics.com", "https://legacy.crm.dynamics.com");
    }

    [Fact]
    public void SkippedRows_AreCountedByStatus_ReadyAndLegacyAreNotSkipped()
    {
        var registry = new[]
        {
            Row("https://a.crm.dynamics.com", EnvironmentSetupStatus.InProgress),
            Row("https://b.crm.dynamics.com", EnvironmentSetupStatus.Issue),
            Row("https://c.crm.dynamics.com", EnvironmentSetupStatus.InProgress),
            Row("https://d.crm.dynamics.com", EnvironmentSetupStatus.Ready),
            Row("https://e.crm.dynamics.com", null),
        };

        IdentityLinkReconciliationJob.SkippedRegistryRowsByStatus(registry).Should().BeEquivalentTo(
            new Dictionary<EnvironmentSetupStatus, int>
            {
                [EnvironmentSetupStatus.InProgress] = 2,
                [EnvironmentSetupStatus.Issue] = 1,
            });
    }

    [Fact]
    public void NothingSkipped_WhenEveryRowIsReadyOrLegacy()
        => IdentityLinkReconciliationJob.SkippedRegistryRowsByStatus(new[]
            {
                Row("https://d.crm.dynamics.com", EnvironmentSetupStatus.Ready),
                Row("https://e.crm.dynamics.com", null),
            })
            .Should().BeEmpty();
}
