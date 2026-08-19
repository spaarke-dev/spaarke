// -----------------------------------------------------------------------------
// ProvisioningRunSerializerContractTests.cs
//
// REGRESSION test for DS-5 §C4.5 (task 106): RunStatus, GateState, and
// QuarantineState previously carried a System.Text.Json
// [JsonConverter(typeof(JsonStringEnumConverter))] attribute ONLY. The
// Cosmos SDK's default serializer (CosmosModule.cs — no custom STJ
// CosmosSerializer registered on the CosmosClientBuilder) is Newtonsoft-
// based and silently IGNORES the STJ attribute, so the enum was written to
// Cosmos as an INTEGER instead of its string name. CosmosActiveRunScanner's
// query (`c.status IN ('Running', 'WaitingOnGate')`) compares against
// strings, so it returned ZERO rows forever — permanently blinding
// StateReconcilerService (DAG advance) and CrashRecoveryStartupService (I6
// boot scan).
//
// This test reproduces the OBSERVABLE symptom without a live/emulated
// Cosmos account: it serializes a fully-populated ProvisioningRun through
// Newtonsoft.Json with a CamelCasePropertyNamesContractResolver — the
// wire-observable equivalent of what CosmosClientBuilder's
// `CosmosSerializationOptions { PropertyNamingPolicy = CamelCase }` produces
// for the SDK's default (Newtonsoft) serializer — and asserts the exact
// defects DS-5 catalogued:
//   1. `id` is present (not `runId`) — the already-landed #20 fix.
//   2. `status` is a JSON STRING equal to "Running" — NOT a number. This is
//      the assertion that fails against the pre-fix code (RunStatus with
//      only the STJ attribute serializes as `0` under Newtonsoft).
//   3. `gateStates` entries' `status` values are STRINGS, not numbers
//      (same defect class as #2, applied to GateState).
//   4. `quarantine.state` is a STRING, not a number (QuarantineState).
//   5. No `ttl` member is emitted — the already-landed #19 fix (property
//      removed entirely; asserted here as a non-regression guard since this
//      test already builds a fully-populated document).
//
// Fails against the pre-task-106 code because Newtonsoft.Json.JsonConvert
// ignores the STJ-only [JsonConverter(typeof(JsonStringEnumConverter))]
// attribute and falls back to its own default enum handling (integer).
//
// KEEP category: tests/unit/domain/** equivalent for L2 — pure
// serialization-contract logic, no I/O, no mocks. Lives under this test
// project's Models/ folder (mirrors the SUT's Models/ folder) per this
// project's existing test-layout convention (Reconciler/, Handlers/,
// Rollback/ subfolders mirror SUT subfolders).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class ProvisioningRunSerializerContractTests
{
    /// <summary>
    /// Mirrors the wire-observable behavior of the Cosmos SDK's DEFAULT
    /// (Newtonsoft-based) serializer as configured by CosmosModule.cs:
    /// <c>CosmosSerializationOptions { PropertyNamingPolicy = CamelCase }</c>
    /// maps to Newtonsoft's <see cref="CamelCasePropertyNamesContractResolver"/>.
    /// </summary>
    private static JsonSerializerSettings CosmosDefaultSerializerEquivalentSettings() => new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    private static ProvisioningRun BuildFullyPopulatedRun()
    {
        var run = new ProvisioningRun
        {
            RunId = "11111111-1111-1111-1111-111111111111",
            CustomerId = "acme-corp",
            EnvironmentId = "22222222-2222-2222-2222-222222222222",
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            CurrentPhase = "H2a",
            Profile = "spaarke-hosted-model2",
            AttemptCount = 1,
            CreatedOn = DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
        };

        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H0",
            StartedAt = DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-08-19T00:01:00Z"),
            IdempotencyKey = "idem-h0",
            JobId = "job-h0",
        });

        run.GateStates["admin-consent"] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = DateTimeOffset.Parse("2026-08-19T00:02:00Z"),
            VerifierHandler = "H0.5",
        };

        run.Quarantine = new QuarantineInfo
        {
            State = QuarantineState.Cleared,
            Reason = "Operator manually resolved partial Bicep deploy",
            QuarantinedByHandler = "H2a",
            QuarantinedAt = DateTimeOffset.Parse("2026-08-19T00:03:00Z"),
            ClearedBy = "operator@spaarke.com",
            ClearedAt = DateTimeOffset.Parse("2026-08-19T00:04:00Z"),
        };

        return run;
    }

    [Fact]
    public void Serialize_ViaNewtonsoftCosmosDefaultEquivalent_EmitsIdNotRunId()
    {
        var run = BuildFullyPopulatedRun();

        var json = JsonConvert.SerializeObject(run, CosmosDefaultSerializerEquivalentSettings());
        var payload = JObject.Parse(json);

        // #20 precedent: Cosmos requires the document identity property to be
        // literally `id`. RunId's [Newtonsoft.Json.JsonProperty("id")]
        // attribute must survive under the Cosmos-equivalent serializer.
        payload.ContainsKey("id").Should().BeTrue();
        payload["id"]!.Value<string>().Should().Be(run.RunId);
        payload.ContainsKey("runId").Should().BeFalse();
    }

    [Fact]
    public void Serialize_ViaNewtonsoftCosmosDefaultEquivalent_StatusIsJsonStringNotNumber()
    {
        var run = BuildFullyPopulatedRun();

        var json = JsonConvert.SerializeObject(run, CosmosDefaultSerializerEquivalentSettings());
        var payload = JObject.Parse(json);

        // THE regression assertion (DS-5 §C4.5): pre-fix, RunStatus carried
        // ONLY the STJ [JsonConverter(typeof(JsonStringEnumConverter))]
        // attribute, which Newtonsoft ignores — `status` serialized as the
        // integer `1` (RunStatus.Running's ordinal), not the string
        // "Running". CosmosActiveRunScanner's query compares against the
        // STRING form, so a pre-fix write would be permanently invisible to
        // the scanner. This assertion fails against the pre-fix code.
        var statusToken = payload["status"];
        statusToken.Should().NotBeNull();
        statusToken!.Type.Should().Be(JTokenType.String,
            "the Cosmos SDK's default (Newtonsoft) serializer must honor the " +
            "Newtonsoft StringEnumConverter attribute on RunStatus, not silently " +
            "fall back to integer enum serialization");
        statusToken.Value<string>().Should().Be("Running");
    }

    [Fact]
    public void Serialize_ViaNewtonsoftCosmosDefaultEquivalent_GateStateValuesAreStrings()
    {
        var run = BuildFullyPopulatedRun();

        var json = JsonConvert.SerializeObject(run, CosmosDefaultSerializerEquivalentSettings());
        var payload = JObject.Parse(json);

        var gateStates = (JObject)payload["gateStates"]!;
        var adminConsentStatus = gateStates["admin-consent"]!["status"];
        adminConsentStatus.Should().NotBeNull();
        adminConsentStatus!.Type.Should().Be(JTokenType.String,
            "GateState must serialize as a string under the Cosmos SDK's default serializer");
        adminConsentStatus.Value<string>().Should().Be("Verified");
    }

    [Fact]
    public void Serialize_ViaNewtonsoftCosmosDefaultEquivalent_QuarantineStateIsString()
    {
        var run = BuildFullyPopulatedRun();

        var json = JsonConvert.SerializeObject(run, CosmosDefaultSerializerEquivalentSettings());
        var payload = JObject.Parse(json);

        var quarantineState = payload["quarantine"]!["state"];
        quarantineState.Should().NotBeNull();
        quarantineState!.Type.Should().Be(JTokenType.String,
            "QuarantineState must serialize as a string under the Cosmos SDK's default serializer");
        quarantineState.Value<string>().Should().Be("Cleared");
    }

    [Fact]
    public void Serialize_ViaNewtonsoftCosmosDefaultEquivalent_EmitsNoTtlMember()
    {
        var run = BuildFullyPopulatedRun();

        var json = JsonConvert.SerializeObject(run, CosmosDefaultSerializerEquivalentSettings());
        var payload = JObject.Parse(json);

        // #19 precedent non-regression guard: the Ttl property was removed
        // entirely from ProvisioningRun (2026-08-18) because Cosmos rejects
        // a null `ttl` member. Assert it never reappears.
        payload.ContainsKey("ttl").Should().BeFalse();
    }
}
