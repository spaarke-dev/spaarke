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
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private const string WorkforceA = "d0e0c0a0-0000-4000-8000-000000000003";
    private const string WorkforceB = "e1e1e1e1-0000-4000-8000-000000000004";

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

    // ---------- helpers ----------

    private static Dictionary<string, string> Settings(string? customerId)
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
        return settings;
    }

    private static TrapVerificationRequest Request() => new(
        CustomerId: CustomerId,
        RunId: "run-t7",
        TenantId: "11111111-1111-1111-1111-111111111111",
        SubscriptionId: SubscriptionId,
        DataverseUrl: "https://org.crm.dynamics.com",
        BffAppRegId: "33333333-3333-3333-3333-333333333333",
        UamiClientId: "44444444-4444-4444-4444-444444444444",
        KeyVaultName: "sprk-acme-prod-kv",
        AppServiceName: AppServiceName,
        ResourceGroupName: ResourceGroupName,
        CustomerWorkforceTenantIds: [WorkforceA, WorkforceB],
        TenancyModel: "Model1");

    private static CustomerIdentityT7Probe NewProbe(FakeSlotSettingsArm arm) => new(
        ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(arm.Respond)),
        Options.Create(new H13AcceptanceOptions()),
        NullLogger<CustomerIdentityT7Probe>.Instance);

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
