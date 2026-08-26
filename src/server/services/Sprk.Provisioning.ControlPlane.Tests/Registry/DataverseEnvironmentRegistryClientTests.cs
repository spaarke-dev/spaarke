// -----------------------------------------------------------------------------
// DataverseEnvironmentRegistryClientTests.cs
//
// L2 CONTROL-PLANE task-112 unit + smoke coverage for the real Path X
// DataverseEnvironmentRegistryClient. ADR-038 alignment:
//
//   - UNIT tier (default; runs in CI):
//       - Options validation (fail-fast; NFR-05 parity with
//         CustomerRunGuardOptionsTests / CosmosModuleTests).
//       - Pure-function seams — BuildPatchBody + ParseSnapshot — exercised
//         directly through internal-visible seams. No HttpMessageHandler mock
//         per the ADR-038 KEEP-path ban.
//       - Null-Object contract (WRITE + READ + argument-guards).
//       - Guard rejects non-GUID environmentId with a typed Failure (no live HTTP).
//
//   - SMOKE tier (env-guarded; skipped by default):
//       Live round-trip against a canary sprk_dataverseenvironment row under
//       the L2 UAMI's Dataverse App User identity (post task 111 grant). Env
//       vars mirror CosmosSmokeTests / ServiceBusSmokeTests naming:
//         DVREG_L2_SMOKE_ADMIN_ENV_URL   e.g. https://spaarkedev1.crm.dynamics.com
//         DVREG_L2_SMOKE_ROW_ID          existing canary row id (GUID string)
//         DVREG_L2_SMOKE_TENANT_ID       tenantId literal to search by
//         DVREG_L2_SMOKE_MI_CLIENTID     optional — pins DefaultAzureCredential
//                                        to a specific UAMI. Leave unset when
//                                        running `az login` locally.
//       Skipped unless DVREG_L2_SMOKE_ADMIN_ENV_URL is set. When set, the
//       smoke exercises: (a) LookupByTenantIdAsync round-trip against the
//       canary row; (b) UpdateSetupStatusAsync round-trip with a benign
//       set-status write followed by a re-read to confirm.
//
// NOT a unit test — the smoke tier is `tests/integration/seam/**` category
// per ADR-038's E-40 addition (vertical-slice seam tests exercising the real
// credential + real HTTP path). Documented here inline per the task 037
// / task 038 precedent (smoke lives with the owning code, env-gated).
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Registry;

public sealed class DataverseEnvironmentRegistryClientTests
{
    // -------------------------------------------------------------------------
    // Options.Validate — parity with CustomerRunGuardOptions validation shape.
    // -------------------------------------------------------------------------

    [Fact]
    public void Options_Validate_Throws_When_AdminEnvironmentUrl_Missing()
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = null,
        };

        FluentActions.Invoking(() => InvokeValidate(options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*AdminEnvironmentUrl*required*");
    }

    [Fact]
    public void Options_Validate_Throws_When_AdminEnvironmentUrl_Relative()
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "not-a-uri",
        };

        FluentActions.Invoking(() => InvokeValidate(options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*AdminEnvironmentUrl*absolute URI*");
    }

    [Fact]
    public void Options_Validate_Throws_When_EntitySetName_Empty()
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
            EntitySetName = "  ",
        };

        FluentActions.Invoking(() => InvokeValidate(options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EntitySetName*non-empty*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_Validate_Throws_When_RequestTimeout_TooSmall(int seconds)
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
            RequestTimeout = TimeSpan.FromSeconds(seconds),
        };

        FluentActions.Invoking(() => InvokeValidate(options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*RequestTimeout*");
    }

    [Fact]
    public void Options_Validate_Throws_When_RequestTimeout_TooLarge()
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
            RequestTimeout = TimeSpan.FromMinutes(6),
        };

        FluentActions.Invoking(() => InvokeValidate(options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*RequestTimeout*");
    }

    [Fact]
    public void Options_Validate_Passes_With_Defaults_Plus_AdminEnvironmentUrl()
    {
        var options = new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
            // EntitySetName + RequestTimeout defaults are valid; ManagedIdentityClientId is optional.
        };

        FluentActions.Invoking(() => InvokeValidate(options)).Should().NotThrow();
        options.EntitySetName.Should().Be("sprk_dataverseenvironments");
        options.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // -------------------------------------------------------------------------
    // BuildPatchBody — pure-function coverage (ADR-038 avoids HttpMessageHandler mock).
    //
    // WIRE-SHAPE INVARIANT (task 184 correctness fix): sprk_setupstatus is a
    // Dataverse CHOICE (option-set integer) — a JSON STRING body would 400.
    // The test suite asserts the on-wire integer (verified via Dataverse MCP
    // describe against admin env spaarkedev1 2026-08-20:
    // Ready=2, InProgress=1, NotStarted=0, Issue=3).
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildPatchBody_Emits_SetupStatus_Only_When_ClearCurrentRunId_False()
    {
        var body = DataverseEnvironmentRegistryClient.BuildPatchBody("Ready", clearCurrentRunId: false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // Ready = 2 per Dataverse choice option-set (task 184 wire-shape fix).
        root.GetProperty("sprk_setupstatus").GetInt32().Should().Be(2);
        // Absent property, per H0.5 registry semantics "don't touch what you didn't ask to touch".
        root.TryGetProperty("sprk_currentrunid", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildPatchBody_Emits_Null_CurrentRunId_When_ClearCurrentRunId_True()
    {
        var body = DataverseEnvironmentRegistryClient.BuildPatchBody("Ready", clearCurrentRunId: true);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("sprk_setupstatus").GetInt32().Should().Be(2);
        root.GetProperty("sprk_currentrunid").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("NotStarted", 0)]
    [InlineData("Not Started", 0)]
    [InlineData("InProgress", 1)]
    [InlineData("In Progress", 1)]
    [InlineData("Ready", 2)]
    [InlineData("Issue", 3)]
    [InlineData("ready", 2)]  // case-insensitive
    [InlineData("READY", 2)]  // case-insensitive
    public void BuildPatchBody_Maps_Every_Dataverse_Choice_Value(string displayName, int expectedOptionSet)
    {
        var body = DataverseEnvironmentRegistryClient.BuildPatchBody(displayName, clearCurrentRunId: false);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sprk_setupstatus").GetInt32().Should().Be(expectedOptionSet);
    }

    [Theory]
    [InlineData("Running")]           // H0.5 NoOpStatuses; NOT a Dataverse choice value
    [InlineData("WaitingOnGate")]     // H0.5 NoOpStatuses; NOT a Dataverse choice value
    [InlineData("Failed")]            // H0.5 RestartStatuses; NOT a Dataverse choice value
    [InlineData("Cancelled")]         // H0.5 RestartStatuses; NOT a Dataverse choice value
    [InlineData("banana")]
    public void BuildPatchBody_Throws_For_Unmapped_Status(string status)
    {
        // Silent-fail guard: writing an unknown display name would either 400
        // (best case) or silently PATCH an arbitrary integer if the mapping
        // fell through. Fail-loud instead.
        FluentActions.Invoking(() => DataverseEnvironmentRegistryClient.BuildPatchBody(status, clearCurrentRunId: false))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown sprk_setupstatus display name*");
    }

    [Fact]
    public void MapDisplayNameToOptionSet_Verified_Against_Dataverse_MCP_Schema()
    {
        // Anchor test — pins the mapping to the MCP-verified schema (Dataverse
        // MCP describe against admin env spaarkedev1 2026-08-20). If this test
        // fails after a schema change, review DataverseEnvironmentRegistryClient
        // .MapDisplayNameToOptionSet in lockstep with the updated schema.
        DataverseEnvironmentRegistryClient.MapDisplayNameToOptionSet("NotStarted").Should().Be(0);
        DataverseEnvironmentRegistryClient.MapDisplayNameToOptionSet("InProgress").Should().Be(1);
        DataverseEnvironmentRegistryClient.MapDisplayNameToOptionSet("Ready").Should().Be(2);
        DataverseEnvironmentRegistryClient.MapDisplayNameToOptionSet("Issue").Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // ParseSnapshot — the Dataverse null-column projection convention
    // (missing property == null value) is load-bearing.
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseSnapshot_Populates_All_Required_Fields_With_Integer_SetupStatus()
    {
        // sprk_setupstatus ships from Dataverse Web API as an INTEGER choice
        // option-set value; ParseSnapshot maps back to the display name
        // string form H0.5 compares against.
        const string payload = """
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "sprk_setupstatus": 1,
          "sprk_currentrunid": "65109e91-5968-4300-933e-9e79dea4109c"
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        var snapshot = DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement);

        snapshot.EnvironmentId.Should().Be("87d7b4a7-399b-f111-b8de-7ced8ddc4a05");
        snapshot.CustomerId.Should().Be("trial-2026-08-18");
        snapshot.TenantId.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        snapshot.SetupStatus.Should().Be("InProgress");
        snapshot.CurrentRunId.Should().Be("65109e91-5968-4300-933e-9e79dea4109c");
    }

    [Theory]
    [InlineData(0, "NotStarted")]
    [InlineData(1, "InProgress")]
    [InlineData(2, "Ready")]
    [InlineData(3, "Issue")]
    public void ParseSnapshot_Maps_Every_Dataverse_Choice_Option_To_Display_Name(int optionSetValue, string expectedDisplayName)
    {
        // Round-trip anchor: MCP-verified schema (Dataverse MCP describe against
        // admin env spaarkedev1 2026-08-20).
        var payload = $$"""
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "sprk_setupstatus": {{optionSetValue}}
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        var snapshot = DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement);

        snapshot.SetupStatus.Should().Be(expectedDisplayName);
    }

    [Fact]
    public void ParseSnapshot_Preserves_Unknown_Option_Set_As_Integer_String()
    {
        // Silent-fail defense: an unknown integer maps to its string form
        // (e.g. "42") rather than silently coercing to a known display name.
        // H0.5 surfaces this via its "unmapped SetupStatus" WARN branch.
        const string payload = """
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "sprk_setupstatus": 42
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        var snapshot = DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement);
        snapshot.SetupStatus.Should().Be("42");
    }

    [Fact]
    public void ParseSnapshot_Treats_Missing_SetupStatus_As_Empty_String()
    {
        // Absent property (Dataverse null-column projection) — parity with
        // pre-task-184 shape; H0.5's "no snapshot" path handles it upstream.
        const string payload = """
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        var snapshot = DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement);
        snapshot.SetupStatus.Should().Be(string.Empty);
    }

    [Fact]
    public void ParseSnapshot_Treats_Missing_CurrentRunId_As_Null()
    {
        // Dataverse projects a null column as ABSENT from the row payload.
        const string payload = """
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "sprk_setupstatus": 2
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        var snapshot = DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement);

        snapshot.CurrentRunId.Should().BeNull();
    }

    [Fact]
    public void ParseSnapshot_Throws_When_EnvironmentId_Missing()
    {
        const string payload = """
        {
          "sprk_customerid": "trial-2026-08-18",
          "sprk_tenantid": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "sprk_setupstatus": 2
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        FluentActions.Invoking(() => DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*sprk_dataverseenvironmentid*");
    }

    [Fact]
    public void ParseSnapshot_Throws_When_TenantId_Missing()
    {
        const string payload = """
        {
          "sprk_dataverseenvironmentid": "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
          "sprk_customerid": "trial-2026-08-18",
          "sprk_setupstatus": 2
        }
        """;
        using var doc = JsonDocument.Parse(payload);
        FluentActions.Invoking(() => DataverseEnvironmentRegistryClient.ParseSnapshot(doc.RootElement))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*sprk_tenantid*");
    }

    // -------------------------------------------------------------------------
    // UpdateSetupStatusAsync argument guards — the non-GUID environmentId
    // guard is the only branch we can exercise without live HTTP; the token
    // acquisition + PATCH branches are covered by the smoke tier.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateSetupStatusAsync_Returns_Failure_When_EnvironmentId_Not_Guid()
    {
        var client = NewClientPointedAtLoopback();
        var update = new RegistrySetupStatusUpdate(
            EnvironmentId: "not-a-guid",
            SetupStatus: "Ready",
            ClearCurrentRunId: true,
            CustomerIdForLog: "trial-2026-08-18",
            RunIdForLog: "65109e91-5968-4300-933e-9e79dea4109c");

        var result = await client.UpdateSetupStatusAsync(update, CancellationToken.None);

        result.Should().BeOfType<RegistryUpdateOutcome.Failure>()
              .Which.Diagnostic.Should().Contain("not a valid GUID");
    }

    [Fact]
    public async Task UpdateSetupStatusAsync_Throws_When_Update_Null()
    {
        var client = NewClientPointedAtLoopback();
        await FluentActions.Invoking(() => client.UpdateSetupStatusAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateSetupStatusAsync_Throws_When_SetupStatus_Empty()
    {
        var client = NewClientPointedAtLoopback();
        var update = new RegistrySetupStatusUpdate(
            EnvironmentId: Guid.NewGuid().ToString(),
            SetupStatus: "  ",
            ClearCurrentRunId: false,
            CustomerIdForLog: "cust",
            RunIdForLog: "run");
        await FluentActions.Invoking(() => client.UpdateSetupStatusAsync(update, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LookupByTenantIdAsync_Throws_When_TenantId_Empty()
    {
        var client = NewClientPointedAtLoopback();
        await FluentActions.Invoking(() => client.LookupByTenantIdAsync("  ", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // Null-Object contract — both methods return safe defaults + WARN log.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NullDataverseEnvironmentRegistryClient_Lookup_Returns_Null()
    {
        var nullClient = new NullDataverseEnvironmentRegistryClient(
            NullLogger<NullDataverseEnvironmentRegistryClient>.Instance);
        var snapshot = await nullClient.LookupByTenantIdAsync(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", CancellationToken.None);
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task NullDataverseEnvironmentRegistryClient_UpdateSetupStatus_Returns_Success_NoOp()
    {
        var nullClient = new NullDataverseEnvironmentRegistryClient(
            NullLogger<NullDataverseEnvironmentRegistryClient>.Instance);
        var update = new RegistrySetupStatusUpdate(
            EnvironmentId: Guid.NewGuid().ToString(),
            SetupStatus: "Ready",
            ClearCurrentRunId: true,
            CustomerIdForLog: "cust",
            RunIdForLog: "run");
        var outcome = await nullClient.UpdateSetupStatusAsync(update, CancellationToken.None);
        outcome.Should().BeOfType<RegistryUpdateOutcome.Success>();
    }

    [Fact]
    public async Task NullDataverseEnvironmentRegistryClient_Rejects_Empty_TenantId()
    {
        var nullClient = new NullDataverseEnvironmentRegistryClient(
            NullLogger<NullDataverseEnvironmentRegistryClient>.Instance);
        await FluentActions.Invoking(() => nullClient.LookupByTenantIdAsync("  ", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task NullDataverseEnvironmentRegistryClient_Rejects_Null_Update()
    {
        var nullClient = new NullDataverseEnvironmentRegistryClient(
            NullLogger<NullDataverseEnvironmentRegistryClient>.Instance);
        await FluentActions.Invoking(() => nullClient.UpdateSetupStatusAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Direct call into the internal validator (mirrors CustomerRunGuardOptionsTests
    // pattern; InternalsVisibleTo binds .Tests to .Core so the internal method
    // is callable without reflection — reflection wraps thrown exceptions in
    // TargetInvocationException which breaks the .Throw<InvalidOperationException>()
    // assertions we rely on).
    private static void InvokeValidate(DataverseEnvironmentRegistryOptions options)
        => options.Validate();

    // Builds a client wired to a well-formed but unreachable admin URL. All
    // tests that use this exercise ONLY the pre-HTTP argument-guard paths
    // (env URL parse, environmentId parse, argument nullness). No HTTP call
    // is made so no HttpMessageHandler mock is needed (ADR-038-compliant).
    private static DataverseEnvironmentRegistryClient NewClientPointedAtLoopback()
    {
        var options = Options.Create(new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = "https://127.0.0.1.nip.io",  // parseable, never resolved by the tests
            EntitySetName = "sprk_dataverseenvironments",
            RequestTimeout = TimeSpan.FromSeconds(1),
        });
        var httpClientFactory = new SingleClientFactory();
        return new DataverseEnvironmentRegistryClient(
            httpClientFactory,
            options,
            NullLogger<DataverseEnvironmentRegistryClient>.Instance);
    }

    // Trivial IHttpClientFactory — the pre-HTTP-guard tests never call
    // CreateClient(...); this factory is here only to satisfy the ctor
    // ArgumentNullException guard. No HttpMessageHandler mock (ADR-038).
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    }
}

// -----------------------------------------------------------------------------
// SMOKE TIER — env-guarded. Skipped by default. Set DVREG_L2_SMOKE_ADMIN_ENV_URL
// (plus companions) to opt in per the DataverseEnvironmentRegistryClientTests
// file header. When enabled, exercises the full task 184 H13 write chain
// end-to-end against a real canary sprk_dataverseenvironment row on the admin
// env:
//   real DataverseRegistrySetupStatusUpdater
//     -> real DataverseEnvironmentRegistryClient (Path X, MI-native)
//       -> real Dataverse Web API PATCH
//         -> re-read via LookupByTenantIdAsync to confirm Ready landed.
//
// ADR-038 KEEP category: `tests/integration/seam/**` — vertical-slice-seam
// tests exercising the real credential + real HTTP path against a canary
// row. This is the POML task-184 acceptance criterion #4 mechanization.
//
// CANARY-ROW CONVENTION:
//   The smoke test PATCHes an existing row identified by DVREG_L2_SMOKE_ROW_ID.
//   Operator responsibility: point at a row dedicated to smoke tests (NOT a
//   real customer row). The test intentionally does NOT restore the row's
//   prior state — the operator's canary is idempotent by design ("this row
//   is for smoke tests; its status is whatever the last smoke wrote").
// -----------------------------------------------------------------------------

[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "Dataverse")]
public sealed class DataverseRegistrySetupStatusUpdaterSmokeTests
{
    private const string AdminEnvUrlEnvVar = "DVREG_L2_SMOKE_ADMIN_ENV_URL";
    private const string RowIdEnvVar = "DVREG_L2_SMOKE_ROW_ID";
    private const string TenantIdEnvVar = "DVREG_L2_SMOKE_TENANT_ID";
    private const string MiClientIdEnvVar = "DVREG_L2_SMOKE_MI_CLIENTID";

    [Fact]
    public async Task RealUpdater_Patches_Ready_And_Reads_Back_Ready_Via_C1_4_Client()
    {
        var adminEnvUrl = Environment.GetEnvironmentVariable(AdminEnvUrlEnvVar);
        var rowId = Environment.GetEnvironmentVariable(RowIdEnvVar);
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar);
        if (string.IsNullOrWhiteSpace(adminEnvUrl)
            || string.IsNullOrWhiteSpace(rowId)
            || string.IsNullOrWhiteSpace(tenantId))
        {
            return; // env-guarded skip
        }

        // Build the real client (Path X, MI-native). No mocks; real HTTP.
        var miClientId = Environment.GetEnvironmentVariable(MiClientIdEnvVar);
        var options = Options.Create(new DataverseEnvironmentRegistryOptions
        {
            AdminEnvironmentUrl = adminEnvUrl,
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(miClientId) ? null : miClientId,
        });
        var httpClientFactory = new SingleClientFactoryForSmoke();
        var registryClient = new DataverseEnvironmentRegistryClient(
            httpClientFactory,
            options,
            NullLogger<DataverseEnvironmentRegistryClient>.Instance);

        // Real updater on real client — this is the H13 chain that would run
        // in production after all traps / invariants / naming / cost / validate
        // pass green.
        var updater = new DataverseRegistrySetupStatusUpdater(
            registryClient,
            NullLogger<DataverseRegistrySetupStatusUpdater>.Instance);

        var request = new RegistrySetupStatusUpdateRequest(
            CustomerId: "smoke-canary",
            RunId: $"smoke-run-{Guid.NewGuid():N}",
            TenantId: tenantId,
            EnvironmentId: rowId,
            RegistryDataverseUrl: adminEnvUrl);

        var outcome = await updater.TransitionToReadyAsync(request, CancellationToken.None);
        outcome.Should().BeOfType<RegistrySetupStatusUpdateOutcome.Success>(
            "the H13 real Ready-writer MUST land against a real canary row");

        // Read-back the row via the same C1.4 client's LookupByTenantIdAsync
        // and confirm sprk_setupstatus rehydrates as Ready and sprk_currentrunid
        // is cleared (companion update per spec.md FR-23).
        var snapshot = await registryClient.LookupByTenantIdAsync(tenantId, CancellationToken.None);
        snapshot.Should().NotBeNull("the canary row MUST re-read via tenantId lookup after the PATCH");
        snapshot!.SetupStatus.Should().Be("Ready",
            "criterion #4: the PATCH actually lands and is readable back as Ready");
        snapshot.CurrentRunId.Should().BeNull(
            "criterion #3: sprk_currentrunid was cleared in the SAME transaction");
    }

    private sealed class SingleClientFactoryForSmoke : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
