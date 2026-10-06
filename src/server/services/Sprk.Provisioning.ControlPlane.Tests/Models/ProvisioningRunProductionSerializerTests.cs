// -----------------------------------------------------------------------------
// ProvisioningRunProductionSerializerTests.cs
//
// unified-access-control-r2 task 165, owner round 49 item 2. A ProvisioningRun is
// persisted by the Cosmos SDK's DEFAULT (Newtonsoft-based) serializer — the one
// CosmosModule.BuildCosmosClient configures. These tests write and read runs
// through THAT serializer (ProductionCosmosSerializer — the client's own
// ClientOptions.Serializer, not an "equivalent"):
//   - every field of a fully-populated run comes back unchanged (judged by what
//     GET /api/runs/{id} — System.Text.Json — would return), so a field that
//     does not survive Cosmos fails here, whatever handler writes it;
//   - gate evidence (a JsonElement) survives: before the Newtonsoft converter it
//     was stored as {"valueKind":1} and read back as an Undefined element that
//     GET /api/runs/{id} could not serialize;
//   - H8's typed creation record survives — the resume record round 41 kept in
//     evidence was lost exactly this way.
//
// ADR-038: pure serialization contract over the production serializer; no I/O
// (building a CosmosClient opens no connection), no mocks.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class ProvisioningRunProductionSerializerTests
{
    [Fact]
    public void AFullyPopulatedRun_RoundTripsThroughTheProductionCosmosSerializer_Unchanged()
    {
        var run = FullyPopulatedRun();

        var readBack = ProductionCosmosSerializer.RoundTrip(run);

        // Judged as JSON (what GET /api/runs/{id} returns): equal values, whatever escaping the writer chose.
        var expected = JsonNode.Parse(JsonSerializer.Serialize(run));
        var actual = JsonNode.Parse(JsonSerializer.Serialize(readBack));
        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            "every field a handler writes must read back as written — what Cosmos loses, a resume never sees. " +
            $"Expected:\n{expected}\nActual:\n{actual}");
    }

    [Fact]
    public void GateEvidence_IsStoredAsItsJson_AndReadsBackAsTheSameJson()
    {
        var run = FullyPopulatedRun();

        var stored = ProductionCosmosSerializer.Serialize(run);
        var readBack = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(stored);

        stored.Should().NotContain("valueKind", "Newtonsoft without the converter wrote a JsonElement as {\"valueKind\":1}");
        var evidence = readBack.GateStates[SpeContainerGates.T6Verified].Evidence!.Value;
        JsonNode.DeepEquals(
                JsonNode.Parse(evidence.GetRawText()),
                JsonNode.Parse(run.GateStates[SpeContainerGates.T6Verified].Evidence!.Value.GetRawText()))
            .Should().BeTrue("the evidence reads back as the JSON it was");
        evidence.GetProperty("verifiedAt").GetString().Should().Be("2026-10-05T03:31:00.1234567+00:00",
            "a date-shaped string stays the string it was");
    }

    [Fact]
    public void ARunReadBackFromCosmos_SerializesForGetRun()
    {
        var readBack = ProductionCosmosSerializer.RoundTrip(FullyPopulatedRun());

        var serialize = () => JsonSerializer.Serialize(readBack);

        serialize.Should().NotThrow("GET /api/runs/{id} returns the run read from Cosmos (an Undefined JsonElement throws)");
    }

    [Fact]
    public void ADocumentStoredBeforeTheConverter_ReadsBack_AndSerializes()
    {
        // What every gate with evidence holds in Cosmos today.
        const string legacy = """
            {"id":"r1","customerId":"acme","environmentId":"e","tenancyModel":"Model2Dedicated","status":"WaitingOnGate",
             "completedPhases":[],"gateStates":{"h8-t6-verified":{"status":"Pending","verifierHandler":"H8","evidence":{"valueKind":1}}},
             "interStepState":{"containerTypeId":"cccccccc-dddd-eeee-ffff-000000000001","speContainerId":"b!legacy"},
             "parameters":{"nonSecret":{},"secrets":{}},"profile":"p","attemptCount":0,"handlerRetryAttempts":{},
             "createdOn":"2026-10-01T00:00:00+00:00"}
            """;

        var run = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(legacy);

        run.GateStates["h8-t6-verified"].Evidence!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        run.InterStepState.SpeContainerId.Should().Be("b!legacy",
            "a pre-round-41 run's only surviving record of its root container — H8 moves it into the typed record");
        ((Action)(() => JsonSerializer.Serialize(run))).Should().NotThrow();
    }

    [Fact]
    public void H8sTypedCreationRecord_SurvivesTheProductionCosmosSerializer()
    {
        var run = FullyPopulatedRun();

        var record = ProductionCosmosSerializer.RoundTrip(run).InterStepState.SpeContainerCreation!;

        record.RootContainerId.Should().Be("b!root");
        record.AdditionalContainerIds.Should().Equal("b!extra");
        record.RootContainerInDoubtSince.Should().Be(run.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince);
        record.Status.Should().Be(SpeContainerCreationRecord.StatusReplicationPending);
        record.OwningBusinessUnitId.Should().Be("0b0b0b0b-1111-2222-3333-444444444444");
    }

    [Fact]
    public void TheReconcilersRetryCounter_KeepsItsHandlerIdKeys_SoTheNextRetryCountsOn()
    {
        var run = FullyPopulatedRun();
        run.HandlerRetryAttempts.Clear();
        run.HandlerRetryAttempts["H12b"] = 1;

        var stored = ProductionCosmosSerializer.Serialize(run);
        var readBack = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(stored);

        stored.Should().Contain("\"H12b\":1", "the camelCase option lowered dictionary keys (\"h12b\") before the fix");
        readBack.HandlerRetryAttempts.TryGetValue("H12b", out var attempts).Should().BeTrue();
        attempts.Should().Be(1, "HandlerOutcomeApplier reads the prior attempt by handler id — a miss re-sends attempt 1, " +
                                "whose MessageId Service Bus drops as a duplicate");
    }

    [Fact]
    public void ARetryCounterPersistedBeforeTheFix_WithALoweredKey_StillCounts()
    {
        const string legacy = """
            {"id":"r1","customerId":"acme","environmentId":"e","tenancyModel":"Model2Dedicated","status":"Running",
             "completedPhases":[],"gateStates":{},"interStepState":{},"parameters":{"nonSecret":{},"secrets":{}},
             "profile":"p","attemptCount":0,"handlerRetryAttempts":{"h9":2},"createdOn":"2026-10-01T00:00:00+00:00"}
            """;

        var run = ProductionCosmosSerializer.Deserialize<ProvisioningRun>(legacy);

        run.HandlerRetryAttempts.TryGetValue("H9", out var attempts).Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public void RunParameterKeys_PersistVerbatim_AsTheModelDocuments()
    {
        var run = FullyPopulatedRun();
        run.Parameters.NonSecret["SharePointDomain"] = "acme.sharepoint.com";
        run.Parameters.Secrets["ClientSecret"] = new KeyVaultSecretRef("kv", "s");

        var readBack = ProductionCosmosSerializer.RoundTrip(run);

        readBack.Parameters.NonSecret.Should().ContainKey("SharePointDomain");
        readBack.Parameters.Secrets.Should().ContainKey("ClientSecret");
        readBack.Parameters.Secrets["ClientSecret"].SecretName.Should().Be("s");
    }

    private static ProvisioningRun FullyPopulatedRun()
    {
        var run = new ProvisioningRun
        {
            RunId = "11111111-1111-1111-1111-111111111111",
            CustomerId = "acme",
            EnvironmentId = "22222222-2222-2222-2222-222222222222",
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.WaitingOnGate,
            CurrentPhase = "H8",
            Profile = "spaarke-hosted-model2",
            AttemptCount = 2,
            CreatedOn = DateTimeOffset.Parse("2026-10-05T00:00:00.1234567+00:00"),
            CompletedOn = DateTimeOffset.Parse("2026-10-05T04:00:00+00:00"),
            ErrorDetail = "[spe-x] detail",
            Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Cleared,
                Reason = "r",
                QuarantinedByHandler = "H8",
                QuarantinedAt = DateTimeOffset.Parse("2026-10-05T01:00:00+00:00"),
                ClearedBy = "oid",
                ClearedAt = DateTimeOffset.Parse("2026-10-05T02:00:00+00:00"),
            },
        };
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H5",
            StartedAt = DateTimeOffset.Parse("2026-10-05T00:01:00+00:00"),
            CompletedAt = DateTimeOffset.Parse("2026-10-05T00:02:00+00:00"),
            IdempotencyKey = "k",
            JobId = "j",
        });
        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifiedAt = DateTimeOffset.Parse("2026-10-05T03:00:00+00:00"),
            VerifierHandler = "H8",
            Evidence = JsonSerializer.SerializeToElement(new
            {
                containerId = "b!root",
                verifiedStatus = "replication-pending",
                verifiedViaAppOnlyToken = false,
                owningBusinessUnitId = (string?)null,
                verifiedAt = "2026-10-05T03:31:00.1234567+00:00",
                count = 3,
                ratio = 0.5,
                nested = new { list = new[] { "a", "b" } },
            }),
        };
        run.GateStates["admin-consent"] = new GateEntry { Status = GateState.Verified, VerifierHandler = "H0.5" };
        run.HandlerRetryAttempts["H9"] = 1;
        run.Parameters.NonSecret["tenantId"] = "00000000-1111-2222-3333-444444444444";
        run.Parameters.Secrets["bffClientSecret"] = new KeyVaultSecretRef("kv", "secret-name", "v1");

        var s = run.InterStepState;
        s.BffAppRegId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
        s.S2SAppRegId = "s2s";
        s.MiObjectId = "mi-o";
        s.MiClientId = "mi-c";
        s.ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
        s.DataverseEnvUrl = "https://acme.crm.dynamics.com";
        s.OpenAiEndpoint = "https://oai";
        s.AiSearchEndpoint = "https://search";
        s.CosmosEndpoint = "https://cosmos";
        s.SystemUserId = "su";
        s.BffAppRegSystemUserId = "bsu";
        s.SpeConsentCorrelationId = "corr";
        s.ImportedSolutions = new List<ImportedSolutionRecord> { new("Spaarke.Core", "1.0.0.0", "sol-id", 1) };
        s.ResourceGroupName = "rg-acme";
        s.AppServiceName = "app-acme";
        s.AppServiceStagingSlotName = "staging";
        s.KeyVaultName = "kv-acme";
        s.KeyVaultUri = "https://kv-acme.vault.azure.net/";
        s.MiResourceId = "/subscriptions/x/resourceGroups/rg-acme/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi";
        s.ServiceBusFullyQualifiedNamespace = "sb-acme.servicebus.windows.net";
        s.RedisEndpoint = "redis-acme.redis.cache.windows.net:6380";
        s.FicPendingPostAppServiceVerification = true;
        s.BffApiUrl = "https://app-acme.azurewebsites.net";
        s.BffBuildId = "build-1";
        s.SpeContainerId = null;
        s.ProvisionedUsers = new List<ProvisionedUserRecord> { new("uid", "a@acme.com", "NativeAccount") };
        s.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = "b!root",
            AdditionalContainerIds = new List<string> { "b!extra" },
            RootContainerInDoubtSince = DateTimeOffset.Parse("2026-10-05T03:30:00.1234567+00:00"),
            OwningBusinessUnitId = "0b0b0b0b-1111-2222-3333-444444444444",
            Status = SpeContainerCreationRecord.StatusReplicationPending,
            UpdatedAt = DateTimeOffset.Parse("2026-10-05T03:31:00+00:00"),
        };
        return run;
    }
}
