// customer-provisioning-orchestration-r1 T186: the identity-link reconciliation job reconciles only registry rows whose
// setup has finished. The ACME placeholder (sprk_isactive = true, sprk_setupstatus = InProgress) failed the job every
// 5 minutes on spaarke-bff-dev: the environment had no identity-binding schema and no BFF access yet.
//
// #2761: and only rows this BFF serves. A row naming another BFF's App Service (sprk_appservicename) is a customer stamp,
// reconciled by its own BFF; the dev BFF's identity has no seat there, so WhoAmI answered 403 0x80072560 every run.

using FluentAssertions;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.ExternalAccess;

public class IdentityLinkReconciliationRegistryTargetsTests
{
    private const string OwnAppService = "spaarke-bff-dev";
    private const string StampAppService = "sprk-acme-prod-api";

    private static DataverseEnvironmentRecord Row(
        string url, EnvironmentSetupStatus? status, string? appService = null, string? customerId = null)
        => new() { DataverseUrl = url, SetupStatus = status, AppServiceName = appService, CustomerId = customerId };

    [Fact]
    public void ReadyRows_AreReconciled()
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(
                new[] { Row("https://ready.crm.dynamics.com", EnvironmentSetupStatus.Ready) }, OwnAppService)
            .Should().Equal("https://ready.crm.dynamics.com");

    [Theory]
    [InlineData(EnvironmentSetupStatus.NotStarted)]
    [InlineData(EnvironmentSetupStatus.InProgress)]
    [InlineData(EnvironmentSetupStatus.Issue)]
    public void RowsWhoseSetupHasNotFinished_AreSkipped(EnvironmentSetupStatus status)
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(
                new[] { Row("https://provisioning.crm.dynamics.com", status) }, OwnAppService)
            .Should().BeEmpty();

    [Fact]
    public void RowsWithoutAStatus_PredateTheColumn_AndAreKept()
        => IdentityLinkReconciliationJob.ReconcilableRegistryUrls(
                new[] { Row("https://legacy.crm.dynamics.com", null) }, OwnAppService)
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
            registryEnvironments: IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry, OwnAppService));

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

        IdentityLinkReconciliationJob.SkippedRegistryRowsByStatus(registry, OwnAppService).Should().BeEquivalentTo(
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
                }, OwnAppService)
            .Should().BeEmpty();

    // ── #2761: which BFF serves a row ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARowNamingNoAppService_IsServedByThisBff(string? appService)
        => IdentityLinkReconciliationJob.IsServedByThisBff(
                Row("https://demo.crm.dynamics.com", EnvironmentSetupStatus.Ready, appService), OwnAppService)
            .Should().BeTrue("a demo or dev row has no BFF of its own: this BFF provisions into it");

    [Theory]
    [InlineData("spaarke-bff-dev")]
    [InlineData("SPAARKE-BFF-DEV")]
    [InlineData(" spaarke-bff-dev ")]
    public void ARowNamingThisBffsAppService_IsServed_CaseInsensitively(string appService)
        => IdentityLinkReconciliationJob.IsServedByThisBff(
                Row("https://own-stamp.crm.dynamics.com", EnvironmentSetupStatus.Ready, appService), OwnAppService)
            .Should().BeTrue("Azure site names are case-insensitive");

    [Fact]
    public void ARowNamingAnotherAppService_IsNotServed()
        => IdentityLinkReconciliationJob.IsServedByThisBff(
                Row("https://spaarke-acme.crm.dynamics.com/", EnvironmentSetupStatus.Ready, StampAppService), OwnAppService)
            .Should().BeFalse("the stamp's own BFF reconciles its own environment (#2761)");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WhenThisBffsNameIsUnknown_ARowNamingAnyAppService_IsNotServed(string? ownAppService)
        => IdentityLinkReconciliationJob.IsServedByThisBff(
                Row("https://spaarke-acme.crm.dynamics.com/", EnvironmentSetupStatus.Ready, StampAppService), ownAppService)
            .Should().BeFalse("off App Service this host cannot be the BFF a stamp row names");

    [Fact]
    public void TheDevRegistryOf2026_11_13_YieldsTheDevRowOnly_NotTheAcmeStamp()
    {
        // The active rows on dev when #2761 was diagnosed: the ACME stamp (Ready, names its own BFF), the Dev row (Ready,
        // no App Service) and a trial row with no URL.
        var registry = new[]
        {
            Row("https://spaarke-acme.crm.dynamics.com/", EnvironmentSetupStatus.Ready, StampAppService, "acme"),
            Row("https://spaarkedev1.crm.dynamics.com", EnvironmentSetupStatus.Ready),
            new DataverseEnvironmentRecord { DataverseUrl = null },
        };

        var targets = IdentityLinkReconciliationJob.ProvisioningTargetUrls(
            ownEnvironment: "https://spaarkedev1.crm.dynamics.com",
            registrationEnvironment: "https://spaarkedev1.crm.dynamics.com",
            registryEnvironments: IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry, OwnAppService));

        targets.Should().BeEmpty("the Dev row is the own environment and the ACME row is served by sprk-acme-prod-api");
        IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry, StampAppService)
            .Should().Contain("https://spaarke-acme.crm.dynamics.com/", "the ACME BFF itself still serves its row");
    }

    [Fact]
    public void RowsServedByAnotherBff_AreCountedByAppService_AndNotAlsoByStatus()
    {
        var registry = new[]
        {
            Row("https://acme.crm.dynamics.com", EnvironmentSetupStatus.Ready, StampAppService),
            Row("https://acme2.crm.dynamics.com", EnvironmentSetupStatus.InProgress, "SPRK-ACME-PROD-API"),
            Row("https://beta.crm.dynamics.com", EnvironmentSetupStatus.Ready, "sprk-beta-prod-api"),
            Row("https://own.crm.dynamics.com", EnvironmentSetupStatus.Ready, OwnAppService),
            Row("https://demo.crm.dynamics.com", EnvironmentSetupStatus.Issue),
        };

        IdentityLinkReconciliationJob.RegistryRowsServedByAnotherBff(registry, OwnAppService).Should().BeEquivalentTo(
            new Dictionary<string, int> { [StampAppService] = 2, ["sprk-beta-prod-api"] = 1 });
        IdentityLinkReconciliationJob.SkippedRegistryRowsByStatus(registry, OwnAppService).Should().BeEquivalentTo(
            new Dictionary<EnvironmentSetupStatus, int> { [EnvironmentSetupStatus.Issue] = 1 },
            "the InProgress stamp row is another BFF's, so it is not counted twice");
        IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry, OwnAppService)
            .Should().Equal("https://own.crm.dynamics.com");
    }

    [Fact]
    public void CustomerRowsWithoutAnAppService_AreNamedForTheStaleRegistryWarning()
    {
        var registry = new[]
        {
            Row("https://stale.crm.dynamics.com", EnvironmentSetupStatus.Ready, appService: null, customerId: "beta"),
            Row("https://stale2.crm.dynamics.com", EnvironmentSetupStatus.Ready, appService: " ", customerId: "BETA"),
            Row("https://acme.crm.dynamics.com", EnvironmentSetupStatus.Ready, StampAppService, "acme"),
            Row("https://demo.crm.dynamics.com", EnvironmentSetupStatus.Ready),
        };

        IdentityLinkReconciliationJob.CustomerRowsWithoutAnAppService(registry).Should().Equal("beta");
        IdentityLinkReconciliationJob.ReconcilableRegistryUrls(registry, OwnAppService).Should().Contain(
            "https://stale.crm.dynamics.com", "a blank App Service keeps the row, as before; the warning makes it visible");
    }

    [Theory]
    [InlineData(EnvironmentSetupStatus.NotStarted)]
    [InlineData(EnvironmentSetupStatus.InProgress)]
    [InlineData(EnvironmentSetupStatus.Issue)]
    public void ACustomerRowStillBeingProvisioned_IsNotWarnedAbout(EnvironmentSetupStatus status)
        // Provisioning writes sprk_appservicename only when it completes, so an in-flight or failed customer row has none and is
        // not reconciled either: warning about it every run would be wrong and noisy (the #2761 review's finding).
        => IdentityLinkReconciliationJob.CustomerRowsWithoutAnAppService(new[]
            {
                Row("https://provisioning.crm.dynamics.com", status, appService: null, customerId: "gamma"),
            })
            .Should().BeEmpty();
}
