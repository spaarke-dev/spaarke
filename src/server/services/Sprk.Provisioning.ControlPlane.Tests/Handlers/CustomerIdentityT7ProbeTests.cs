// -----------------------------------------------------------------------------
// CustomerIdentityT7ProbeTests.cs
//
// Task 238 (D-14) — H13 trap T7: both App Service slots carry Customer__Id equal
// to the run's customerId. ADR-038 path #1.
//
// The real Azure.ResourceManager.AppService client runs against the project's
// hand-rolled FakeArmHttpMessageHandler (ArmSdkTestFakes — not
// Mock<HttpMessageHandler>, which .claude/constraints/testing.md bans), so the
// tests prove the two list calls the probe actually issues: production
// config/appsettings/list and staging slots/staging/config/appsettings/list.
//
// Scope (POML 238 test-scope clause): Passed; staging missing; blank; mismatch
// incl. a case-only difference; ARM read fault; missing request field. Beyond it,
// one test: names differing only in case with different values are ambiguous
// (a code-review finding — the probe must not pass on whichever name it saw first).
//
// Task 255 (INCOMING-141): the same reads also prove the customer workforce tenant
// list — missing on a slot, a stale extra index, an unparseable value, Spaarke's
// tenant on a Model 1 stamp, and a run without the list all fail; order does not
// matter; a case-variant or ':' name counts (the BFF binds it).
//
// Task 264 (#2001 from SPA-r3 ISS-013): a third clause — the BFF app registration has
// authenticationBehaviors.removeUnverifiedEmailClaim == true. The real Microsoft.Graph client runs against a hand-rolled
// fake HTTP handler (the same ArmSdkTestFakes.NewHandler transport), so the tests prove the one v1.0 read the probe issues
// (applications(appId='...'), tenant-scoped via the factory). false / null / absent / 404 fail; a read error is an
// InfraFault, never a Passed. The decision itself is the pure EvaluateUnverifiedEmailClaim.
//
// Task 265 (#2265): a fourth clause on the same two slot reads — ExternalAccess__CustomerBusinessUnitId parses as a GUID
// equal to the run's InterStepState.CustomerBusinessUnitId (H10). Missing on a slot, blank, non-GUID, a different GUID, two
// case-variant names with different values, and a run that carries no unit all fail; the same GUID in another hex case
// passes. The decision itself is the pure CheckCustomerBusinessUnit.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CustomerIdentityT7ProbeTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "spaarke-acme-prod-api";
    private const string CustomerId = "acme";
    private const string KeyVaultRef = "@Microsoft.KeyVault(SecretUri=https://sprk-acme-prod-kv.vault.azure.net/secrets/Redis-ConnectionString/)";
    private const string RunTenantId = "11111111-1111-1111-1111-111111111111";   // Model 1: Spaarke's tenant = AzureAd__TenantId
    private const string BffAppRegId = "33333333-3333-3333-3333-333333333333";
    private const string WorkforceA = "d0e0c0a0-0000-4000-8000-000000000003";
    private const string WorkforceB = "e1e1e1e1-0000-4000-8000-000000000004";
    private const string CustomerBu = "a1b2c3d4-0000-4000-8000-0000000000aa";   // H10's CustomerBusinessUnitId ("D" format)
    private const string OtherBu = "b2c3d4e5-0000-4000-8000-0000000000bb";
    private const string BuSetting = "ExternalAccess__CustomerBusinessUnitId";

    [Fact]
    public async Task ProbeAsync_BothSlotsCarryRunCustomerId_ReturnsPassed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T7CustomerIdentityExplicit);
        arm.Reads.Should().HaveCount(3, "production list, staging slot GET, staging list");
    }

    [Fact]
    public async Task ProbeAsync_StagingSlotLacksSetting_ReturnsFailedNamingSlot()
    {
        // customer.bicep sets it on production only — the shape H4b exists to prevent.
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(customerId: null));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("staging [MISSING");
        failed.Diagnostic.Should().Contain("production [OK]");
        failed.Diagnostic.Should().NotContain(KeyVaultRef, "no app setting other than Customer__Id is ever reported");
    }

    [Fact]
    public async Task ProbeAsync_SettingBlank_ReturnsFailed()
    {
        var arm = new FakeSlotSettingsArm(Settings("  "), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("production [BLANK");
    }

    [Theory]
    [InlineData("nwind")]   // another customer's id
    [InlineData("Acme")]    // case-only difference — the value is canonical and never normalised
    public async Task ProbeAsync_SettingHasDifferentValue_ReturnsFailed(string observed)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(observed));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain($"staging [MISMATCH — Customer__Id='{observed}']");
    }

    [Fact]
    public async Task ProbeAsync_ArmListForbidden_ReturnsInfraFault()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId), forbidProductionList: true);

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 403");
    }

    [Theory]
    [InlineData(nameof(TrapVerificationRequest.CustomerId))]
    [InlineData(nameof(TrapVerificationRequest.AppServiceName))]
    public async Task ProbeAsync_RequestFieldMissing_ReturnsInfraFaultWithoutArmCall(string field)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));
        var request = field == nameof(TrapVerificationRequest.CustomerId)
            ? Request() with { CustomerId = "" }
            : Request() with { AppServiceName = "" };

        var outcome = await NewProbe(arm).ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Which.Diagnostic.Should().Contain(field);
        arm.Reads.Should().BeEmpty();
    }

    // ---------- task 255: the customer workforce tenant list ----------

    [Fact]
    public async Task ProbeAsync_StagingLacksTheWorkforceList_ReturnsFailedNamingSlot()
    {
        var staging = Settings(CustomerId);
        staging.Remove("WorkforceIdentity__CustomerTenantIds__0");
        staging.Remove("WorkforceIdentity__CustomerTenantIds__1");
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), staging);

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("staging [MISSING — no WorkforceIdentity__CustomerTenantIds__N");
        failed.Diagnostic.Should().NotContain("Customer__Id='", "Customer__Id is fine on both slots");
    }

    [Fact]
    public async Task ProbeAsync_WorkforceListInAnotherOrder_ReturnsPassed()
    {
        var production = Settings(CustomerId);
        production["WorkforceIdentity__CustomerTenantIds__0"] = WorkforceB;
        production["WorkforceIdentity__CustomerTenantIds__1"] = WorkforceA;
        var arm = new FakeSlotSettingsArm(production, Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>("the BFF's member test is a set");
    }

    [Theory]
    [InlineData("WorkforceIdentity__CustomerTenantIds__2", "f2f2f2f2-0000-4000-8000-000000000005", "[MISMATCH")]   // a stale extra index
    [InlineData("workforceidentity__customertenantids__7", "f2f2f2f2-0000-4000-8000-000000000005", "[MISMATCH")]   // case-variant name still binds
    [InlineData("WorkforceIdentity:CustomerTenantIds:2", "f2f2f2f2-0000-4000-8000-000000000005", "[MISMATCH")]     // ':' form still binds
    [InlineData("WorkforceIdentity__CustomerTenantIds__2", WorkforceA, "[MISMATCH")]                              // a duplicate
    [InlineData("WorkforceIdentity__CustomerTenantIds__2", "not-a-guid", "[UNPARSEABLE")]
    [InlineData("WorkforceIdentity__CustomerTenantIds__2", RunTenantId, "[SPAARKE TENANT")]                       // Model 1: AzureAd__TenantId listed
    public async Task ProbeAsync_ProductionWorkforceListDiffers_ReturnsFailed(string extraKey, string extraValue, string expected)
    {
        var production = Settings(CustomerId);
        production[extraKey] = extraValue;
        var arm = new FakeSlotSettingsArm(production, Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain($"production {expected}").And.Contain("staging [OK]");
    }

    [Fact]
    public async Task ProbeAsync_RunCarriesNoWorkforceList_ReturnsFailed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request() with { CustomerWorkforceTenantIds = null }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("predates T255");
    }

    [Fact]
    public async Task ProbeAsync_Model2_WorkforceTenantMayEqualTheRegistrationTenant()
    {
        // Model 2: the registration lives in the customer's tenant, so the two coincide (hand-off §3).
        var production = Settings(CustomerId);
        var staging = Settings(CustomerId);
        foreach (var slot in new[] { production, staging })
        {
            slot["WorkforceIdentity__CustomerTenantIds__0"] = RunTenantId;
            slot.Remove("WorkforceIdentity__CustomerTenantIds__1");
        }
        var arm = new FakeSlotSettingsArm(production, staging);

        var outcome = await NewProbe(arm).ProbeAsync(
            Request() with { TenancyModel = "Model2", CustomerWorkforceTenantIds = [RunTenantId] }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    [Theory]
    [InlineData("kv", "[UNRESOLVABLE")]       // ISS-021: a Key Vault reference was a silent pass
    [InlineData("blank", "[UNRESOLVABLE")]
    [InlineData("zero", "[UNRESOLVABLE")]
    [InlineData("missing", "[UNRESOLVABLE")]
    [InlineData("ambiguous", "[UNRESOLVABLE")]
    public async Task ProbeAsync_Model1_RegistrationTenantUnreadable_FailsClosed_NotASilentPass(string shape, string expected)
    {
        var production = Settings(CustomerId);
        switch (shape)
        {
            case "kv": production["AzureAd__TenantId"] = KeyVaultRef; break;
            case "blank": production["AzureAd__TenantId"] = " "; break;
            case "zero": production["AzureAd__TenantId"] = Guid.Empty.ToString(); break;
            case "missing": production.Remove("AzureAd__TenantId"); break;
            default: production["azuread__tenantid"] = "22222222-2222-2222-2222-222222222222"; break;
        }
        var arm = new FakeSlotSettingsArm(production, Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain($"production {expected}").And.Contain("staging [OK]");
    }

    [Fact]
    public async Task ProbeAsync_Model2_RegistrationTenantAKeyVaultReference_StillPasses()
    {
        // Model 2: the Spaarke-tenant guard does not apply, so an unreadable AzureAd__TenantId is not a defect here.
        var production = Settings(CustomerId);
        production["AzureAd__TenantId"] = KeyVaultRef;
        var arm = new FakeSlotSettingsArm(production, Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request() with { TenancyModel = "Model2" }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    [Fact]
    public void FindSetting_CaseVariantNamesWithDifferentValues_IsAmbiguous()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Customer__Id"] = CustomerId,
            ["customer__id"] = "nwind",
        };

        CustomerIdentityT7Probe.FindSetting(settings).Should().Be(
            new CustomerIdentityT7Probe.SettingLookup(null, Ambiguous: true));
    }

    // ---------- task 264: the BFF app registration strips unverified emails ----------

    [Fact]
    public async Task ProbeAsync_BffAppFlagTrue_ReadsTheRunsAppInTheRunsTenant_ReturnsPassed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));
        var graph = FakeGraph.WithFlag(true);

        var outcome = await NewProbe(arm, graph).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
        graph.Tenants.Should().Equal(new[] { RunTenantId }, "I5: the Graph client is built for the run's tenant, never an ambient one");
        var read = graph.Requests.Should().ContainSingle().Subject;
        read.AbsoluteUri.Should().StartWith("https://graph.microsoft.com/v1.0/applications(appId='" + BffAppRegId + "')");
        read.Query.Should().Contain("authenticationBehaviors");
        read.AbsoluteUri.Should().NotContain("/beta/");
    }

    [Theory]
    [InlineData(false, "removeUnverifiedEmailClaim = false")]
    [InlineData(null, "no authenticationBehaviors")]
    public async Task ProbeAsync_BffAppFlagNotTrue_ReturnsFailedNamingTheApp(bool? flag, string expected)
    {
        // flag == null: Graph returns authenticationBehaviors without the flag only when other values are set, so the
        // "absent" body is covered by EvaluateUnverifiedEmailClaim_FlagNull; here a missing object is the ACME shape.
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));
        var graph = flag is null
            ? FakeGraph.WithBody($$"""{ "id": "obj", "appId": "{{BffAppRegId}}", "authenticationBehaviors": null }""")
            : FakeGraph.WithFlag(flag);

        var outcome = await NewProbe(arm, graph).ProbeAsync(Request(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain(BffAppRegId).And.Contain(expected).And.Contain("Set it by hand").And.Contain("a resume does not re-run H3");
        failed.Diagnostic.Should().NotContain("Customer__Id=", "the settings clauses are fine");
    }

    [Fact]
    public async Task ProbeAsync_BffAppAuthenticationBehaviorsWithoutTheFlag_ReturnsFailed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm, FakeGraph.WithFlag(null)).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("removeUnverifiedEmailClaim = absent");
    }

    [Fact]
    public async Task ProbeAsync_BffAppNotFound_ReturnsFailed_H3sAppIsMissing()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm, FakeGraph.WithStatus(HttpStatusCode.NotFound, "Request_ResourceNotFound"))
            .ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("was not found").And.Contain("Start a new run");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Authorization_RequestDenied", "HTTP 403")]
    [InlineData(HttpStatusCode.InternalServerError, "generalException", "HTTP 500")]
    public async Task ProbeAsync_GraphReadFails_ReturnsInfraFault_NeverPassed(HttpStatusCode status, string code, string expected)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm, FakeGraph.WithStatus(status, code)).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Which.Diagnostic.Should().Contain(expected);
    }

    [Fact]
    public async Task ProbeAsync_GraphReadFailsButSettingsWrong_ReturnsFailedNotingTheUnreadClause()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(customerId: null));

        var outcome = await NewProbe(arm, FakeGraph.WithStatus(HttpStatusCode.Forbidden, "Authorization_RequestDenied"))
            .ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("staging [MISSING").And.Contain("clause was not read").And.Contain("HTTP 403");
    }

    [Fact]
    public async Task ProbeAsync_GraphClientFactoryThrows_ReturnsInfraFault()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));
        var probe = new CustomerIdentityT7Probe(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(arm.Respond)),
            Options.Create(new H13AcceptanceOptions()),
            NullLogger<CustomerIdentityT7Probe>.Instance,
            _ => throw new InvalidOperationException("no credential"));

        var outcome = await probe.ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Which.Diagnostic.Should().Contain("InvalidOperationException");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-guid")]
    public async Task ProbeAsync_BffAppRegIdBlankOrUnparseable_ReturnsInfraFaultWithoutAnyRead(string appRegId)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));
        var graph = FakeGraph.WithFlag(true);

        var outcome = await NewProbe(arm, graph).ProbeAsync(Request() with { BffAppRegId = appRegId }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain(nameof(TrapVerificationRequest.BffAppRegId));
        arm.Reads.Should().BeEmpty();
        graph.Tenants.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_SettingsAndAppRegistrationBothWrong_ReportsBothClauses()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(customerId: null));

        var outcome = await NewProbe(arm, FakeGraph.WithFlag(false)).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("staging [MISSING").And.Contain("removeUnverifiedEmailClaim = false");
    }

    [Fact]
    public void EvaluateUnverifiedEmailClaim_FlagTrue_ReturnsNoProblem()
    {
        CustomerIdentityT7Probe.EvaluateUnverifiedEmailClaim(
            new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = true }).Should().BeNull();
    }

    [Fact]
    public void EvaluateUnverifiedEmailClaim_FlagFalse_ReturnsAProblem()
    {
        CustomerIdentityT7Probe.EvaluateUnverifiedEmailClaim(
            new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = false }).Should().Contain("= false");
    }

    [Fact]
    public void EvaluateUnverifiedEmailClaim_NullBehaviors_ReturnsAProblem()
    {
        CustomerIdentityT7Probe.EvaluateUnverifiedEmailClaim(null).Should().Contain("no authenticationBehaviors");
    }

    [Fact]
    public void EvaluateUnverifiedEmailClaim_FlagNull_ReturnsAProblem()
    {
        CustomerIdentityT7Probe.EvaluateUnverifiedEmailClaim(
            new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = null, BlockAzureADGraphAccess = true })
            .Should().Contain("= absent");
    }

    // ---------- task 265: the customer business unit ----------

    [Fact]
    public async Task ProbeAsync_BothSlotsCarryTheRunsCustomerBusinessUnit_ReturnsPassed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
        arm.Reads.Should().HaveCount(3, "the unit rides on the same production + staging reads");
    }

    [Fact]
    public async Task ProbeAsync_StagingLacksTheCustomerBusinessUnit_ReturnsFailedNamingStaging()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId, businessUnit: null));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain($"staging [MISSING — no {BuSetting} setting]")
            .And.Contain("production [OK]")
            .And.Contain(CustomerBu, "the expected unit is a public identifier")
            .And.Contain("registration_owner_unconfigured")
            .And.Contain("#1673")
            .And.Contain("start a new run (a resume does not re-run a completed H4b)").And.Contain("az webapp config appsettings set");
        failed.Diagnostic.Should().NotContain("Customer__Id='", "Customer__Id and the workforce list are fine")
            .And.NotContain(KeyVaultRef, "no other app setting is ever reported");
    }

    [Fact]
    public async Task ProbeAsync_ProductionLacksTheCustomerBusinessUnit_ReturnsFailedNamingProduction()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId, businessUnit: null), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("production [MISSING").And.Contain("staging [OK]");
    }

    [Theory]
    [InlineData("   ", "[BLANK")]
    [InlineData("", "[BLANK")]
    [InlineData("not-a-guid", "[UNPARSEABLE — " + BuSetting + "='not-a-guid'")]
    [InlineData(OtherBu, "[MISMATCH — " + BuSetting + "='" + OtherBu + "'")]
    [InlineData("00000000-0000-0000-0000-000000000000", "[MISMATCH")]
    public async Task ProbeAsync_CustomerBusinessUnitBlankUnparseableOrDifferent_ReturnsFailed(string observed, string expected)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId, businessUnit: observed));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain($"staging {expected}").And.Contain("production [OK]");
    }

    [Fact]
    public async Task ProbeAsync_CustomerBusinessUnitInAnotherHexCase_ReturnsPassed()
    {
        var arm = new FakeSlotSettingsArm(
            Settings(CustomerId, businessUnit: CustomerBu.ToUpperInvariant()),
            Settings(CustomerId, businessUnit: "{" + CustomerBu + "}"));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>("the clause compares GUIDs, not strings");
    }

    [Fact]
    public async Task ProbeAsync_RunCarriesUppercaseUnitAndSlotsLowercase_ReturnsPassed()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(
            Request() with { CustomerBusinessUnitId = CustomerBu.ToUpperInvariant() }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ProbeAsync_RunCarriesNoCustomerBusinessUnit_ReturnsFailedEvenWhenSlotsCarryOne(string runValue)
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(
            Request() with { CustomerBusinessUnitId = runValue }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("no InterStepState.CustomerBusinessUnitId").And.Contain("start a new run");
    }

    [Fact]
    public async Task ProbeAsync_CaseVariantUnitNamesWithDifferentValues_ReturnsFailedAmbiguous()
    {
        var production = Settings(CustomerId);
        production["externalaccess__customerbusinessunitid"] = OtherBu;   // the BFF binds one of the two; which cannot be told
        var arm = new FakeSlotSettingsArm(production, Settings(CustomerId));

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("production [AMBIGUOUS").And.Contain("staging [OK]");
    }

    [Fact]
    public async Task ProbeAsync_UnitSettingNameInAnotherCase_ReturnsPassed()
    {
        var staging = Settings(CustomerId, businessUnit: null);
        staging["EXTERNALACCESS__CUSTOMERBUSINESSUNITID"] = CustomerBu;   // names bind case-insensitively, like Customer__Id
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), staging);

        var outcome = await NewProbe(arm).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    [Fact]
    public async Task ProbeAsync_UnitWrongAndGraphReadFails_ReturnsFailedNotInfraFault()
    {
        // The unit clause is a settings clause: SettingsVerdictOrDeferral must still report it when the app-registration read fails.
        var arm = new FakeSlotSettingsArm(Settings(CustomerId), Settings(CustomerId, businessUnit: OtherBu));

        var outcome = await NewProbe(arm, FakeGraph.WithStatus(HttpStatusCode.Forbidden, "Authorization_RequestDenied"))
            .ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("staging [MISMATCH — " + BuSetting).And.Contain("clause was not read");
    }

    [Fact]
    public async Task ProbeAsync_UnitWrongAndAppRegistrationWrong_ReportsBothClauses()
    {
        var arm = new FakeSlotSettingsArm(Settings(CustomerId, businessUnit: null), Settings(CustomerId));

        var outcome = await NewProbe(arm, FakeGraph.WithFlag(false)).ProbeAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("production [MISSING — no " + BuSetting).And.Contain("removeUnverifiedEmailClaim = false");
    }

    [Fact]
    public void CheckCustomerBusinessUnit_MatchingValue_ReturnsNull()
    {
        CustomerIdentityT7Probe.CheckCustomerBusinessUnit(
            new Dictionary<string, string> { [BuSetting] = " " + CustomerBu.ToUpperInvariant() + " " }, CustomerBu).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    public void CheckCustomerBusinessUnit_NoExpectedUnit_ReturnsAProblemWhateverTheSettingHolds(string? expected)
    {
        CustomerIdentityT7Probe.CheckCustomerBusinessUnit(
            new Dictionary<string, string> { [BuSetting] = CustomerBu }, expected).Should().StartWith("[NO EXPECTED UNIT");
    }

    [Fact]
    public void CheckCustomerBusinessUnit_NullSettings_ReturnsMissing()
    {
        CustomerIdentityT7Probe.CheckCustomerBusinessUnit(null, CustomerBu).Should().StartWith("[MISSING");
    }

    // ---------- helpers ----------

    private static Dictionary<string, string> Settings(string? customerId, string? businessUnit = CustomerBu)
    {
        var settings = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__Redis"] = KeyVaultRef,
        };
        if (customerId is not null)
        {
            settings["Customer__Id"] = customerId;
        }
        // T255: what H4b writes — the run's workforce tenants, and H4b's AzureAd__TenantId (the run's tenantId).
        settings["AzureAd__TenantId"] = RunTenantId;
        settings["WorkforceIdentity__CustomerTenantIds__0"] = WorkforceA;
        settings["WorkforceIdentity__CustomerTenantIds__1"] = WorkforceB;
        // T265: what H4b writes from H10's output (null → the setting is absent on the slot).
        if (businessUnit is not null)
        {
            settings[BuSetting] = businessUnit;
        }
        return settings;
    }

    private static TrapVerificationRequest Request() => new(
        CustomerId: CustomerId,
        RunId: "run-t7",
        TenantId: "11111111-1111-1111-1111-111111111111",
        SubscriptionId: SubscriptionId,
        DataverseUrl: "https://org.crm.dynamics.com",
        BffAppRegId: BffAppRegId,
        UamiClientId: "44444444-4444-4444-4444-444444444444",
        KeyVaultName: "sprk-acme-prod-kv",
        AppServiceName: AppServiceName,
        ResourceGroupName: ResourceGroupName,
        CustomerWorkforceTenantIds: [WorkforceA, WorkforceB],
        TenancyModel: "Model1",
        CustomerBusinessUnitId: CustomerBu);

    private static CustomerIdentityT7Probe NewProbe(FakeSlotSettingsArm arm, FakeGraph? graph = null) => new(
        ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(arm.Respond)),
        Options.Create(new H13AcceptanceOptions()),
        NullLogger<CustomerIdentityT7Probe>.Instance,
        (graph ?? FakeGraph.WithFlag(true)).Factory);

    /// <summary>Canned Microsoft Graph for the one application read; the real SDK client runs over a fake HTTP handler.</summary>
    private sealed class FakeGraph
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        private FakeGraph(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<string> Tenants { get; } = new();
        public List<Uri> Requests { get; } = new();

        public static FakeGraph WithFlag(bool? flag) => WithBody(flag is null
            ? $$"""{ "id": "obj", "appId": "{{BffAppRegId}}", "authenticationBehaviors": { "blockAzureADGraphAccess": true } }"""
            : $$"""{ "id": "obj", "appId": "{{BffAppRegId}}", "authenticationBehaviors": { "removeUnverifiedEmailClaim": {{(flag.Value ? "true" : "false")}} } }""");

        public static FakeGraph WithBody(string json) => new(_ => ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, json));

        public static FakeGraph WithStatus(HttpStatusCode status, string code) => new(_ => ArmSdkTestFakes.JsonResponse(
            status, $$"""{ "error": { "code": "{{code}}", "message": "canned" } }"""));

        public GraphServiceClient Factory(string tenantId)
        {
            Tenants.Add(tenantId);
            var handler = ArmSdkTestFakes.NewHandler(request =>
            {
                Requests.Add(request.RequestUri!);
                return _respond(request);
            });
            return new GraphServiceClient(new HttpClient(handler));
        }
    }

    /// <summary>Canned ARM for one site + its staging slot; anything else is a 404 so a stray call fails.</summary>
    private sealed class FakeSlotSettingsArm
    {
        private readonly Dictionary<string, string> _production;
        private readonly Dictionary<string, string> _staging;
        private readonly bool _forbidProductionList;

        public FakeSlotSettingsArm(
            Dictionary<string, string> production, Dictionary<string, string> staging, bool forbidProductionList = false)
        {
            _production = production;
            _staging = staging;
            _forbidProductionList = forbidProductionList;
        }

        public List<string> Reads { get; } = new();

        private static string SiteId =>
            $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.Web/sites/{AppServiceName}";

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith($"/sites/{AppServiceName}/config/appsettings/list", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Post)
            {
                Reads.Add(path);
                if (_forbidProductionList)
                {
                    return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody(
                        "AuthorizationFailed",
                        "The client does not have authorization to perform action Microsoft.Web/sites/config/list/action."));
                }
                return Settings(SiteId + "/config/appsettings", "Microsoft.Web/sites/config", _production);
            }

            if (path.EndsWith("/slots/staging", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Get)
            {
                Reads.Add(path);
                return Json(new { id = SiteId + "/slots/staging", name = $"{AppServiceName}/staging", location = "westus2", properties = new { } });
            }

            if (path.EndsWith("/slots/staging/config/appsettings/list", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Post)
            {
                Reads.Add(path);
                return Settings(SiteId + "/slots/staging/config/appsettings", "Microsoft.Web/sites/slots/config", _staging);
            }

            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound,
                ArmSdkTestFakes.ArmErrorBody("NotFound", $"Unexpected {request.Method} {path}"));
        }

        private static HttpResponseMessage Settings(string id, string type, Dictionary<string, string> properties)
            => Json(new { id, name = "appsettings", type, properties });

        private static HttpResponseMessage Json(object body)
            => ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(body));
    }
}
