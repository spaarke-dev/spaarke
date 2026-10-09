// -----------------------------------------------------------------------------
// ArmAppServiceSettingsWriterTests.cs
//
// Task 253 (G38) — unit tests for ArmAppServiceSettingsWriter, the ARM write
// that replaced H4b's pwsh run of the generated Configure script. ADR-038
// path #1.
//
// The real Azure.ResourceManager.AppService client runs against the project's
// hand-rolled fake ARM transport (ArmSdkTestFakes — NOT Mock<HttpMessageHandler>,
// which testing.md bans). The wire is the only place that shows what the
// generated script's contract needs:
//   - both the production site AND the staging slot are written;
//   - each whole-dictionary PUT carries every setting already on the slot
//     (merge, never replace — an operator-set AiSpendLimit survives, T254);
//   - a slot that already holds every requested value gets no PUT (no restart);
//   - an ARM refusal is a Failure naming the slot, never a setting value.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmAppServiceSettingsWriterTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "spaarke-bff-acme";
    private const string KeyVaultRef = "@Microsoft.KeyVault(VaultName=sprk-acme-prod-kv;SecretName=AzureOpenAI-Endpoint)";
    private const string SecretLookingValue = "https://acme-cosmos.documents.azure.com/";

    private static readonly IReadOnlyDictionary<string, string> Requested = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AzureOpenAI__Endpoint"] = KeyVaultRef,
        ["CosmosPersistence__Endpoint"] = SecretLookingValue,
        ["Customer__Id"] = "acme",
    };

    [Fact]
    public async Task MergeAsync_FirstRun_WritesProductionThenStaging_KeepingEverySettingAlreadyThere()
    {
        // The operator set a spend limit with Set-AiSpendLimit.ps1; H4b does not request it this run (T254).
        var arm = new FakeAppServiceArm(
            production: new() { ["WEBSITE_RUN_FROM_PACKAGE"] = "1", ["AiSpendLimit__MonthlyLimitUsd"] = "250" },
            staging: new() { ["Scheduling__RunScheduledJobs"] = "false", ["AiSpendLimit__MonthlyLimitUsd"] = "250" });

        var result = await NewWriter(arm).MergeAsync(NewRequest(), CancellationToken.None);

        result.Should().BeEquivalentTo(new AppServiceSettingsWriteResult.Success(new[] { "production", "staging" }));
        arm.Writes.Select(w => w.Path).Should().Equal(
            $"{SitePath}/config/appsettings",
            $"{SitePath}/slots/staging/config/appsettings");

        arm.Writes[0].Settings.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["WEBSITE_RUN_FROM_PACKAGE"] = "1",
            ["AiSpendLimit__MonthlyLimitUsd"] = "250",
            ["AzureOpenAI__Endpoint"] = KeyVaultRef,
            ["CosmosPersistence__Endpoint"] = SecretLookingValue,
            ["Customer__Id"] = "acme",
        }, "the PUT replaces the whole dictionary — every existing production setting must be in it");
        arm.Writes[1].Settings.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Scheduling__RunScheduledJobs"] = "false",
            ["AiSpendLimit__MonthlyLimitUsd"] = "250",
            ["AzureOpenAI__Endpoint"] = KeyVaultRef,
            ["CosmosPersistence__Endpoint"] = SecretLookingValue,
            ["Customer__Id"] = "acme",
        }, "the staging slot gets the same settings so a swap cannot drop one, and keeps its own");
    }

    [Fact]
    public async Task MergeAsync_ASettingHoldsAnotherValue_OverwritesOnlyThatSetting()
    {
        var arm = new FakeAppServiceArm(
            production: new() { ["Customer__Id"] = "someone-else", ["AzureOpenAI__Endpoint"] = KeyVaultRef, ["CosmosPersistence__Endpoint"] = SecretLookingValue },
            staging: new(Requested));

        var result = await NewWriter(arm).MergeAsync(NewRequest(), CancellationToken.None);

        result.Should().BeEquivalentTo(new AppServiceSettingsWriteResult.Success(new[] { "production" }));
        arm.Writes.Should().ContainSingle().Which.Settings["Customer__Id"].Should().Be("acme");
    }

    // ---------- task 255: an exclusive list key — stale indices are removed ----------

    [Fact]
    public void MergeAppSettings_ExclusiveListKey_RemovesEveryOtherChildOfTheList_AndKeepsTheRest()
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkforceIdentity__CustomerTenantIds__0"] = "tenant-a",
            ["WorkforceIdentity__CustomerTenantIds__1"] = "tenant-dropped",
            ["workforceidentity__customertenantids__5"] = "case-variant",   // .NET binds it too
            ["WorkforceIdentity:CustomerTenantIds:2"] = "colon-form",       // and this
            ["WorkforceIdentity__CustomerTenantIds"] = "bare",              // and the bare key
            ["WorkforceIdentity__Other"] = "kept",                          // a sibling, not under the list
            ["WorkforceIdentity__CustomerTenantIdsExtra"] = "kept",         // a prefix match, not a child
            ["Unrelated"] = "kept",
        };
        var requested = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkforceIdentity__CustomerTenantIds__0"] = "tenant-a",
        };

        var (settings, changed) = ArmAppServiceSettingsWriter.MergeAppSettings(
            current, requested, ["WorkforceIdentity__CustomerTenantIds"]);

        settings.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["WorkforceIdentity__CustomerTenantIds__0"] = "tenant-a",
            ["WorkforceIdentity__Other"] = "kept",
            ["WorkforceIdentity__CustomerTenantIdsExtra"] = "kept",
            ["Unrelated"] = "kept",
        }, "a tenant dropped from the list must not keep admitting its employees from a stale index");
        changed.Should().BeEquivalentTo(
            "WorkforceIdentity__CustomerTenantIds__1", "workforceidentity__customertenantids__5",
            "WorkforceIdentity:CustomerTenantIds:2", "WorkforceIdentity__CustomerTenantIds");
    }

    [Fact]
    public void MergeAppSettings_ExclusiveListAlreadyExact_ChangesNothing()
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkforceIdentity__CustomerTenantIds__0"] = "tenant-a",
            ["Unrelated"] = "kept",
        };

        var (_, changed) = ArmAppServiceSettingsWriter.MergeAppSettings(
            current, new Dictionary<string, string> { ["WorkforceIdentity__CustomerTenantIds__0"] = "tenant-a" },
            ["WorkforceIdentity__CustomerTenantIds"]);

        changed.Should().BeEmpty("a re-run with the same list writes nothing — no restart");
    }

    [Fact]
    public void MergeAppSettings_NoExclusiveListKeys_NeverRemovesAnything()
    {
        var current = new Dictionary<string, string> { ["WorkforceIdentity__CustomerTenantIds__1"] = "x" };

        var (settings, changed) = ArmAppServiceSettingsWriter.MergeAppSettings(current, new Dictionary<string, string>());

        settings.Should().ContainKey("WorkforceIdentity__CustomerTenantIds__1");
        changed.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeAsync_ExclusiveListKey_RemovesStaleIndicesOnBothSlots()
    {
        var arm = new FakeAppServiceArm(
            production: new(Requested) { ["WorkforceIdentity__CustomerTenantIds__1"] = "dropped" },
            staging: new(Requested) { ["WorkforceIdentity__CustomerTenantIds__1"] = "dropped" });

        var result = await NewWriter(arm).MergeAsync(
            NewRequest() with { ExclusiveListKeys = ["WorkforceIdentity__CustomerTenantIds"] }, CancellationToken.None);

        result.Should().BeEquivalentTo(new AppServiceSettingsWriteResult.Success(new[] { "production", "staging" }));
        arm.Writes.Should().HaveCount(2).And.OnlyContain(w => !w.Settings.ContainsKey("WorkforceIdentity__CustomerTenantIds__1"));
    }

    [Fact]
    public async Task MergeAsync_BothSlotsAlreadyMatch_WritesNothing()
    {
        var arm = new FakeAppServiceArm(
            production: new(Requested) { ["Extra"] = "kept" },
            staging: new(Requested));

        var result = await NewWriter(arm).MergeAsync(NewRequest(), CancellationToken.None);

        result.Should().BeEquivalentTo(new AppServiceSettingsWriteResult.Success(Array.Empty<string>()));
        arm.Writes.Should().BeEmpty("a re-run changes nothing — no PUT, so no BFF restart");
    }

    [Fact]
    public async Task MergeAsync_StagingSlotMissing_FailsNamingTheSlot_AfterProductionWasWritten()
    {
        var arm = new FakeAppServiceArm(production: new(), staging: null);

        var result = await NewWriter(arm).MergeAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<AppServiceSettingsWriteResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("staging slot").And.Contain(AppServiceName).And.Contain("404")
            .And.Contain("Already written: production");
        arm.Writes.Should().ContainSingle("production was merged before the staging slot was found missing");
    }

    [Fact]
    public async Task MergeAsync_ArmRefusesTheWrite_FailsWithoutLeakingAValue()
    {
        var arm = new FakeAppServiceArm(production: new(), staging: new(), rejectWrites: true);

        var result = await NewWriter(arm).MergeAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<AppServiceSettingsWriteResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("production slot").And.Contain("AuthorizationFailed");
        failure.Diagnostic.Should().NotContain(SecretLookingValue, "setting values never reach a diagnostic");
    }

    // ---------- helpers ----------

    private static string SitePath =>
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.Web/sites/{AppServiceName}";

    private static AppServiceSettingsWriteRequest NewRequest() => new(SubscriptionId, ResourceGroupName, AppServiceName, Requested);

    private static ArmAppServiceSettingsWriter NewWriter(FakeAppServiceArm arm) => new(
        ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(arm.Respond)),
        NullLogger<ArmAppServiceSettingsWriter>.Instance);

    /// <summary>
    /// Canned ARM for one site and its staging slot (<c>null</c> = no slot): answers the calls the writer makes and
    /// records each PUT's path + settings, in order. Anything else is a 404 so an unexpected call surfaces.
    /// </summary>
    private sealed class FakeAppServiceArm
    {
        private readonly Dictionary<string, string> _production;
        private readonly Dictionary<string, string>? _staging;
        private readonly bool _rejectWrites;

        public FakeAppServiceArm(Dictionary<string, string> production, Dictionary<string, string>? staging, bool rejectWrites = false)
        {
            _production = production;
            _staging = staging;
            _rejectWrites = rejectWrites;
        }

        public List<(string Path, Dictionary<string, string> Settings)> Writes { get; } = new();

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var production = $"{SitePath}/config/appsettings";
            var staging = $"{SitePath}/slots/staging/config/appsettings";

            if (Is(path, production + "/list") && request.Method == HttpMethod.Post)
            {
                return Settings(production, _production);
            }
            if (Is(path, $"{SitePath}/slots/staging") && request.Method == HttpMethod.Get)
            {
                return _staging is null
                    ? ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody(
                        "ResourceNotFound", "The Resource 'Microsoft.Web/sites/slots/staging' was not found."))
                    : Json(new { id = $"{SitePath}/slots/staging", name = $"{AppServiceName}/staging", location = "westus2", properties = new { } });
            }
            if (Is(path, staging + "/list") && request.Method == HttpMethod.Post && _staging is not null)
            {
                return Settings(staging, _staging);
            }
            if ((Is(path, production) || Is(path, staging)) && request.Method == HttpMethod.Put)
            {
                if (_rejectWrites)
                {
                    return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody(
                        "AuthorizationFailed", "The client does not have authorization to perform action Microsoft.Web/sites/config/write."));
                }
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(body);
                var written = document.RootElement.GetProperty("properties").EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
                Writes.Add((path, written));
                return Settings(path, written);
            }

            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound,
                ArmSdkTestFakes.ArmErrorBody("NotFound", $"Unexpected {request.Method} {path}"));
        }

        private static bool Is(string path, string expected) => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase);

        private static HttpResponseMessage Settings(string resourceId, Dictionary<string, string> settings)
            => Json(new { id = resourceId, name = "appsettings", type = "Microsoft.Web/sites/config", properties = settings });

        private static HttpResponseMessage Json(object body)
            => ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(body));
    }
}
