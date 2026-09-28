// -----------------------------------------------------------------------------
// ArmSlotStickyAppSettingWriterTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmSlotStickyAppSettingWriter — the ARM
// write behind H9's ADR-036 A1 rule 2 scheduled-jobs slot guard (GitHub #987).
// ADR-038 path #1.
//
// TWO LAYERS:
//   1. The MERGE is tested as pure functions (MergeAppSetting /
//      MergeStickyAppSettingNames): every existing setting and sticky name
//      survives, the guard is added, and a re-run reports no change.
//   2. The SDK PATH runs the real Azure.ResourceManager.AppService client
//      against the project's hand-rolled FakeArmHttpMessageHandler (the same
//      fake transport ArmSlotSwapperTests uses — NOT Mock<HttpMessageHandler>,
//      which .claude/constraints/testing.md bans). It proves what only the
//      wire can: the two whole-object PUTs carry everything that was read (so
//      nothing is wiped), the sticky name is written BEFORE the value, and an
//      already-guarded slot gets no write at all.
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

    // ---------- pure merge: slot app settings ----------

    [Fact]
    public void MergeAppSetting_GuardAbsent_AddsIt_AndKeepsEveryOtherSetting()
    {
        var current = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Redis__ConnectionString"] = KeyVaultRef,
        };

        var (settings, changed) = ArmSlotStickyAppSettingWriter.MergeAppSetting(current, GuardName, GuardValue);

        changed.Should().BeTrue();
        settings.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Redis__ConnectionString"] = KeyVaultRef,
            [GuardName] = GuardValue,
        });
    }

    [Fact]
    public void MergeAppSetting_GuardHoldsAnotherValue_OverwritesOnlyThatSetting()
    {
        var current = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            [GuardName] = "true",
        };

        var (settings, changed) = ArmSlotStickyAppSettingWriter.MergeAppSetting(current, GuardName, GuardValue);

        changed.Should().BeTrue();
        settings.Should().HaveCount(2);
        settings[GuardName].Should().Be(GuardValue);
        settings["ASPNETCORE_ENVIRONMENT"].Should().Be("Staging");
    }

    [Fact]
    public void MergeAppSetting_GuardAlreadySet_ReportsNoChange()
    {
        var current = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            [GuardName] = GuardValue,
        };

        var (settings, changed) = ArmSlotStickyAppSettingWriter.MergeAppSetting(current, GuardName, GuardValue);

        changed.Should().BeFalse("a re-run must write nothing (and so not restart the slot)");
        settings.Should().BeEquivalentTo(current);
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
    public async Task EnsureAsync_FirstRun_MakesNameStickyThenWritesSetting_PreservingEverythingElse()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { "ASPNETCORE_ENVIRONMENT", "WEBSITE_SWAP_WARMUP_PING_PATH" },
            stickyConnectionStringNames: new[] { "SqlDb" },
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
                [GuardName] = GuardValue,
            }, "the PUT replaces the whole dictionary — every existing setting must be in it");
    }

    [Fact]
    public async Task EnsureAsync_AlreadyGuarded_MakesNoWrite()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { "ASPNETCORE_ENVIRONMENT", GuardName },
            stickyConnectionStringNames: Array.Empty<string>(),
            slotSettings: new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                [GuardName] = GuardValue,
            });

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        result.Should().Be(new SlotStickyAppSettingResult.Success(StickyNameAdded: false, SettingWritten: false));
        arm.Writes.Should().BeEmpty("a re-run changes nothing — no PUT, so no slot restart");
        arm.Reads.Should().HaveCount(3, "sticky names, the slot, and its app settings are still read to decide that");
    }

    [Fact]
    public async Task EnsureAsync_ArmRejectsSettingsWrite_ReturnsFailure_DoesNotThrow()
    {
        var arm = new FakeAppServiceArm(
            stickyAppSettingNames: new[] { GuardName },
            stickyConnectionStringNames: Array.Empty<string>(),
            slotSettings: new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Staging" },
            rejectSettingsWrite: true);

        var result = await NewWriter(arm).EnsureAsync(NewRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<SlotStickyAppSettingResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("AuthorizationFailed");
        failure.Diagnostic.Should().Contain(SlotName);
        failure.Diagnostic.Should().NotContain("Staging", "app-setting values never reach a diagnostic");
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
    /// Canned ARM for one site + slot: answers the four calls the writer makes
    /// and records every write's path + <c>properties</c> body, in order.
    /// Anything else gets a 404 so an unexpected call surfaces as a failure.
    /// </summary>
    private sealed class FakeAppServiceArm
    {
        private readonly string[] _stickyAppSettingNames;
        private readonly string[] _stickyConnectionStringNames;
        private readonly Dictionary<string, string> _slotSettings;
        private readonly bool _rejectSettingsWrite;

        public FakeAppServiceArm(
            string[] stickyAppSettingNames,
            string[] stickyConnectionStringNames,
            Dictionary<string, string> slotSettings,
            bool rejectSettingsWrite = false)
        {
            _stickyAppSettingNames = stickyAppSettingNames;
            _stickyConnectionStringNames = stickyConnectionStringNames;
            _slotSettings = slotSettings;
            _rejectSettingsWrite = rejectSettingsWrite;
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
