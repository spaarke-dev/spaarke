// -----------------------------------------------------------------------------
// ArmSlotStickyAppSettingWriterTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmSlotStickyAppSettingWriter — the ARM
// write behind H9's ADR-036 A1 rule 2 scheduled-jobs slot guard (GitHub #987)
// and its production-settings mirror (T186 L46). ADR-038 path #1.
//
// TWO LAYERS:
//   1. The BUILD RULES are tested as pure functions (BuildStagingSettings /
//      MergeStickyAppSettingNames): the slot carries production's non-sticky
//      settings, keeps its own sticky ones, gets the guard, and a re-run
//      reports no change.
//   2. The SDK PATH runs the real Azure.ResourceManager.AppService client
//      against the project's hand-rolled FakeArmHttpMessageHandler (the same
//      fake transport ArmSlotSwapperTests uses — NOT Mock<HttpMessageHandler>,
//      which .claude/constraints/testing.md bans). It proves what only the
//      wire can: the two whole-object PUTs carry everything they must (so
//      nothing is wiped), the sticky name is written BEFORE the value, and an
//      already-prepared slot gets no write at all. slotConfigNames goes as raw
//      JSON over the same pipeline (T186 L43); the fake also answers with the
//      live new-site shape ("id": null, null name lists) that broke the typed GET.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmSlotStickyAppSettingWriterTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "spaarke-bff-acme";
    private const string SlotName = "staging";
    private const string GuardName = "Scheduling__RunScheduledJobs";
    private const string GuardValue = "false";
    private const string KeyVaultRef = "@Microsoft.KeyVault(SecretUri=https://kv-acme.vault.azure.net/secrets/Redis/)";

    // ---------- pure build: the staging slot's settings (T186 L46) ----------

    [Fact]
    public void BuildStagingSettings_CarriesProductionNonSticky_KeepsSlotSticky_DropsSlotOnly_AddsGuard()
    {
        var production = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Redis__Enabled"] = "true",
            ["Redis__ConnectionString"] = KeyVaultRef,
        };
        var slot = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Redis__ConnectionString"] = KeyVaultRef,
            ["LeftoverOnSlotOnly"] = "x",
        };

        var (settings, changed) = ArmSlotStickyAppSettingWriter.BuildStagingSettings(
            production, slot, new[] { "ASPNETCORE_ENVIRONMENT", GuardName }, GuardName, GuardValue);

        settings.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Redis__Enabled"] = "true",
            ["Redis__ConnectionString"] = KeyVaultRef,
            [GuardName] = GuardValue,
        }, "a swap carries the slot's non-sticky settings into production, so they must be production's own");
        changed.Should().Equal(new[] { "LeftoverOnSlotOnly", "Redis__Enabled", GuardName }.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void BuildStagingSettings_ProductionStickySetting_IsNeverCopied()
    {
        var production = new Dictionary<string, string> { ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "prod-ai" };

        var (settings, _) = ArmSlotStickyAppSettingWriter.BuildStagingSettings(
            production, new Dictionary<string, string>(),
            new[] { "APPLICATIONINSIGHTS_CONNECTION_STRING", GuardName }, GuardName, GuardValue);

        settings.Should().NotContainKey("APPLICATIONINSIGHTS_CONNECTION_STRING");
    }

    [Fact]
    public void BuildStagingSettings_SlotAlreadyPrepared_ReportsNoChange()
    {
        var production = new Dictionary<string, string> { ["Redis__Enabled"] = "true" };
        var slot = new Dictionary<string, string> { ["Redis__Enabled"] = "true", [GuardName] = GuardValue };

        var (_, changed) = ArmSlotStickyAppSettingWriter.BuildStagingSettings(
            production, slot, new[] { GuardName }, GuardName, GuardValue);

        changed.Should().BeEmpty("a re-run must write nothing (and so not restart the slot)");
    }

    [Fact]
    public void BuildStagingSettings_GuardHoldsAnotherValue_IsCorrected()
    {
        var (settings, changed) = ArmSlotStickyAppSettingWriter.BuildStagingSettings(
            new Dictionary<string, string>(), new Dictionary<string, string> { [GuardName] = "true" },
            new[] { GuardName }, GuardName, GuardValue);

        settings[GuardName].Should().Be(GuardValue);
        changed.Should().Equal(GuardName);
    }

    // ---------- pure merge: the site's sticky app-setting names ----------

    [Fact]
    public void MergeStickyAppSettingNames_NameAbsent_AppendsIt_KeepingExistingNamesInOrder()
    {
        var current = new[] { "ASPNETCORE_ENVIRONMENT", "WEBSITE_SWAP_WARMUP_PING_PATH" };

        var (names, changed) = ArmSlotStickyAppSettingWriter.MergeStickyAppSettingNames(current, GuardName);

        changed.Should().BeTrue();
        names.Should().Equal("ASPNETCORE_ENVIRONMENT", "WEBSITE_SWAP_WARMUP_PING_PATH", GuardName);
    }

    [Fact]
    public void MergeStickyAppSettingNames_NameAlreadySticky_ReportsNoChange()
    {
        var current = new[] { "ASPNETCORE_ENVIRONMENT", GuardName };

        var (names, changed) = ArmSlotStickyAppSettingWriter.MergeStickyAppSettingNames(current, GuardName);

        changed.Should().BeFalse();
        names.Should().Equal(current);
    }

    // ---------- SDK path over the fake ARM transport ----------

    [Fact]
    public async Task EnsureAsync_FirstRun_MakesNameStickyThenWritesSlotSettings_FromProduction()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { "ASPNETCORE_ENVIRONMENT", "WEBSITE_SWAP_WARMUP_PING_PATH" },
            stickyConnectionStringNames: new[] { "SqlDb" },
            productionSettings: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["Redis__ConnectionString"] = KeyVaultRef,
                ["Redis__Enabled"] = "true",
            },
            slotSettings: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                ["Redis__ConnectionString"] = KeyVaultRef,
            });

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        result.Should().Be(new SlotStickyAppSettingResult.Success(StickyNameAdded: true, SettingWritten: true));
        arm.Writes.Should().HaveCount(2);

        var namesWrite = arm.Writes[0];
        namesWrite.Path.Should().EndWithEquivalentOf("/config/slotConfigNames",
            "the name is made sticky BEFORE the value exists on the slot, so no intermediate state lets a swap carry it");
        StringArray(namesWrite.Properties, "appSettingNames").Should().Equal(
            "ASPNETCORE_ENVIRONMENT", "WEBSITE_SWAP_WARMUP_PING_PATH", GuardName);
        StringArray(namesWrite.Properties, "connectionStringNames").Should().Equal(new[] { "SqlDb" },
            "the connection-string sticky list is written back untouched");

        var settingsWrite = arm.Writes[1];
        settingsWrite.Path.Should().EndWithEquivalentOf($"/slots/{SlotName}/config/appsettings");
        settingsWrite.Properties.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString())
            .Should().BeEquivalentTo(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                ["Redis__ConnectionString"] = KeyVaultRef,
                ["Redis__Enabled"] = "true",
                [GuardName] = GuardValue,
            }, "the PUT replaces the whole dictionary: production's non-sticky settings, the slot's sticky ones, the guard");
    }

    [Fact]
    public async Task EnsureAsync_NewSite_ArmAnswersNullIdAndNullNames_StillGuards_AndMirrorsProduction()
    {
        // T186 L43 — the exact live answer for sprk-acme-prod-api, a site that has never had a sticky name:
        // "id": null and every name list null. The typed SlotConfigNamesResource GET threw
        // ArgumentNullException('resourceId') on it. T186 L46 — production held Redis__Enabled (written by
        // customer.bicep to the production site only); the slot did not, and the BFF aborted there at startup.
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: null,
            stickyConnectionStringNames: null,
            productionSettings: new Dictionary<string, string> { ["Redis__Enabled"] = "true" },
            slotSettings: new Dictionary<string, string>(),
            liveNewSiteShape: true);

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        result.Should().Be(new SlotStickyAppSettingResult.Success(StickyNameAdded: true, SettingWritten: true));
        arm.Writes.Should().HaveCount(2);
        StringArray(arm.Writes[0].Properties, "appSettingNames").Should().Equal(GuardName);
        arm.Writes[0].Properties.GetProperty("connectionStringNames").ValueKind.Should().Be(JsonValueKind.Null,
            "a property ARM returned is written back as read");
        arm.Writes[1].Properties.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString())
            .Should().BeEquivalentTo(new Dictionary<string, string?> { ["Redis__Enabled"] = "true", [GuardName] = GuardValue });
    }

    [Fact]
    public async Task EnsureAsync_ArmRejectsStickyNamesWrite_ReturnsFailure_BeforeAnySlotWrite()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: Array.Empty<string>(),
            stickyConnectionStringNames: Array.Empty<string>(),
            productionSettings: new Dictionary<string, string>(),
            slotSettings: new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Staging" },
            rejectNamesWrite: true);

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<SlotStickyAppSettingResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("AuthorizationFailed").And.Contain("403");
        arm.Writes.Should().BeEmpty("no value reaches the slot unless its name is sticky first");
    }

    [Fact]
    public async Task EnsureAsync_AlreadyPrepared_MakesNoWrite()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { "ASPNETCORE_ENVIRONMENT", GuardName },
            stickyConnectionStringNames: Array.Empty<string>(),
            productionSettings: new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production", ["Redis__Enabled"] = "true" },
            slotSettings: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                ["Redis__Enabled"] = "true",
                [GuardName] = GuardValue,
            });

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        result.Should().Be(new SlotStickyAppSettingResult.Success(StickyNameAdded: false, SettingWritten: false));
        arm.Writes.Should().BeEmpty("a re-run changes nothing — no PUT, so no slot restart");
        arm.Reads.Should().HaveCount(4, "sticky names, production's settings, the slot, and its settings are read to decide that");
    }

    [Fact]
    public async Task EnsureAsync_ArmRejectsSettingsWrite_ReturnsFailure_DoesNotThrow()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { GuardName },
            stickyConnectionStringNames: Array.Empty<string>(),
            productionSettings: new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production" },
            slotSettings: new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Staging" },
            rejectSettingsWrite: true);

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<SlotStickyAppSettingResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("AuthorizationFailed");
        failure.Diagnostic.Should().Contain(SlotName);
        failure.Diagnostic.Should().NotContain("Staging", "app-setting values never reach a diagnostic")
            .And.NotContain("Production");
    }

    // ---------- helpers ----------

    private static SlotStickyAppSettingRequest NewRequest() => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        AppServiceName: AppServiceName,
        SlotName: SlotName,
        SettingName: GuardName,
        SettingValue: GuardValue);

    private static ArmSlotStickyAppSettingWriter NewWriter(FakeAppServiceArm arm) => new(
        ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(arm.Respond)),
        Options.Create(new BffDeployOptions()),
        NullLogger<ArmSlotStickyAppSettingWriter>.Instance);

    private static List<string?> StringArray(JsonElement properties, string name)
        => properties.GetProperty(name).EnumerateArray().Select(e => e.GetString()).ToList();

    /// <summary>
    /// Canned ARM for one site + slot: answers the five calls the writer makes
    /// and records every write's path + <c>properties</c> body, in order.
    /// Anything else gets a 404 so an unexpected call surfaces as a failure.
    /// </summary>
    private sealed class FakeAppServiceArm
    {
        private readonly string[]? _stickyAppSettingNames;
        private readonly string[]? _stickyConnectionStringNames;
        private readonly Dictionary<string, string> _productionSettings;
        private readonly Dictionary<string, string> _slotSettings;
        private readonly bool _rejectSettingsWrite;
        private readonly bool _rejectNamesWrite;
        private readonly bool _liveNewSiteShape;

        public FakeAppServiceArm(
            string[]? stickyAppSettingNames,
            string[]? stickyConnectionStringNames,
            Dictionary<string, string> productionSettings,
            Dictionary<string, string> slotSettings,
            bool rejectSettingsWrite = false,
            bool rejectNamesWrite = false,
            bool liveNewSiteShape = false)
        {
            _stickyAppSettingNames = stickyAppSettingNames;
            _stickyConnectionStringNames = stickyConnectionStringNames;
            _productionSettings = productionSettings;
            _slotSettings = slotSettings;
            _rejectSettingsWrite = rejectSettingsWrite;
            _rejectNamesWrite = rejectNamesWrite;
            _liveNewSiteShape = liveNewSiteShape;
        }

        public List<(string Path, JsonElement Properties)> Writes { get; } = new();
        public List<string> Reads { get; } = new();

        private static string SiteId =>
            $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.Web/sites/{AppServiceName}";

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/config/slotConfigNames", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Get)
                {
                    Reads.Add(path);
                    if (_liveNewSiteShape)
                    {
                        // Verbatim shape of the live ARM answer (2026-10-16), tags omitted.
                        return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                            "{\"id\":null,\"location\":\"West US 2\",\"name\":\"" + AppServiceName + "\"," +
                            "\"properties\":{\"appSettingNames\":null,\"azureStorageConfigNames\":null,\"connectionStringNames\":null}," +
                            "\"type\":\"Microsoft.Web/sites\"}");
                    }
                    return Json(HttpStatusCode.OK, new
                    {
                        id = SiteId + "/config/slotConfigNames",
                        name = "slotConfigNames",
                        type = "Microsoft.Web/sites/config",
                        properties = new
                        {
                            appSettingNames = _stickyAppSettingNames,
                            connectionStringNames = _stickyConnectionStringNames,
                        },
                    });
                }
                if (request.Method == HttpMethod.Put)
                {
                    if (_rejectNamesWrite)
                    {
                        return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody(
                            "AuthorizationFailed",
                            "The client does not have authorization to perform action Microsoft.Web/sites/config/write."));
                    }
                    return RecordWriteAndEcho(request, path,
                        SiteId + "/config/slotConfigNames", "slotConfigNames", "Microsoft.Web/sites/config");
                }
            }

            if (path.EndsWith($"/slots/{SlotName}", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Get)
            {
                Reads.Add(path);
                return Json(HttpStatusCode.OK, new
                {
                    id = $"{SiteId}/slots/{SlotName}",
                    name = $"{AppServiceName}/{SlotName}",
                    location = "westus2",
                    properties = new { },
                });
            }

            if (path.EndsWith($"/slots/{SlotName}/config/appsettings/list", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Post)
            {
                Reads.Add(path);
                return Json(HttpStatusCode.OK, new
                {
                    id = $"{SiteId}/slots/{SlotName}/config/appsettings",
                    name = "appsettings",
                    type = "Microsoft.Web/sites/slots/config",
                    properties = _slotSettings,
                });
            }

            if (path.EndsWith($"/sites/{AppServiceName}/config/appsettings/list", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Post)
            {
                Reads.Add(path);
                return Json(HttpStatusCode.OK, new
                {
                    id = $"{SiteId}/config/appsettings",
                    name = "appsettings",
                    type = "Microsoft.Web/sites/config",
                    properties = _productionSettings,
                });
            }

            if (path.EndsWith($"/slots/{SlotName}/config/appsettings", StringComparison.OrdinalIgnoreCase)
                && request.Method == HttpMethod.Put)
            {
                if (_rejectSettingsWrite)
                {
                    return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody(
                        "AuthorizationFailed",
                        "The client does not have authorization to perform action Microsoft.Web/sites/slots/config/write."));
                }
                return RecordWriteAndEcho(request, path,
                    $"{SiteId}/slots/{SlotName}/config/appsettings", "appsettings", "Microsoft.Web/sites/slots/config");
            }

            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound,
                ArmSdkTestFakes.ArmErrorBody("NotFound", $"Unexpected {request.Method} {path}"));
        }

        private HttpResponseMessage RecordWriteAndEcho(
            HttpRequestMessage request, string path, string resourceId, string name, string type)
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(body);
            var properties = document.RootElement.GetProperty("properties").Clone();
            Writes.Add((path, properties));

            // Like ARM, answer with the full resource: the SDK builds a resource
            // object from the PUT response, which needs its id (the request body
            // the SDK sends carries none).
            return Json(HttpStatusCode.OK, new { id = resourceId, name, type, properties });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body)
            => ArmSdkTestFakes.JsonResponse(status, JsonSerializer.Serialize(body));
    }
}
