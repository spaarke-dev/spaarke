using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// FR-P3-08 persisted-envelope schema-conformance contract (NFR-06,
/// spaarke-ai-architecture-redesign-r1 task 047): what the work_product disposition leg
/// writes onto a host Dataverse record MUST conform to the pinned
/// <c>work-product-envelope-v1</c> contract — a schema drift (dropped field, renamed key,
/// opened version) fails CI here, not at a widget reading the record copy in UAT.
/// </summary>
/// <remarks>
/// <para>
/// <b>What runs REAL here</b> (mirrors the eval harness's live-surface pattern): the real
/// <see cref="OutputRouter"/> (the universal store-then-route seam) composed with the real
/// <see cref="TopicRegistryWorkProductPersister"/> (registry resolution + envelope
/// derivation + PATCH construction). The only doubles sit at the module boundaries —
/// the session persistence seam (<see cref="ChatSessionManager.UpdateSessionCacheAsync"/>)
/// and the user-OBO Dataverse Web API (<see cref="IDataverseUserClient"/>), stubbed with
/// rows shaped like the seeded spaarkedev1 catalog (task 047: topic
/// <c>matter-summary</c> → <c>sprk_matter.sprk_mattersummary</c>).
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class, contract path)</b>: the envelope is a cross-system
/// contract — future record widgets (the widgets-r1 InsightSummaryCard successor) parse it
/// from the host record, and 048/G-P3 UAT reads it off the matter. The pinned schema file
/// + this round-trip are the regression anchor.
/// </para>
/// </remarks>
public class WorkProductEnvelopePersistenceContractTests
{
    private static readonly Guid BindingId = Guid.Parse("4b2c30d1-dddd-f111-ab0e-70a8a590c51c");
    private static readonly Guid HostRecordId = Guid.Parse("8e9c30d1-eeee-f111-ab0e-70a8a590c51c");

    // ─── The pinned contract file (NFR-06 — mirrors the SUM-CHAT@v1 pin pattern) ──────────────

    [Fact]
    public void WorkProductEnvelopeSchemaContract_PinsLoadBearingShape()
    {
        var schemaPath = SchemaPath();
        File.Exists(schemaPath).Should().BeTrue(
            $"the work-product-envelope contract must exist at {schemaPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = doc.RootElement;

        root.GetProperty("title").GetString().Should().Contain("work-product-envelope v1",
            "the contract file names the envelope version the persister emits");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "the envelope is closed — record readers can rely on the exact member set");
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(
                new[] { "schemaVersion", "ledgerKey", "bindingId", "ucId", "turn", "disposition", "generatedAt", "payload" },
                "every envelope member a record reader navigates by is required");
        root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString()
            .Should().Be("1.0", "schemaVersion is a literal string per the widgets-r1 convention");
        root.GetProperty("properties").GetProperty("disposition").GetProperty("const").GetString()
            .Should().Be("work_product");
    }

    // ─── Round-trip: real router + real persister → PATCHed envelope conforms ─────────────────

    [Fact]
    public async Task RouteAsync_WorkProduct_PersistedEnvelopeConformsToPinnedSchema_AndStorePrecedesPatch()
    {
        // Arrange — real OutputRouter + real TopicRegistryWorkProductPersister; doubles at
        // the two module boundaries only.
        var events = new List<string>();
        var sessionManager = new RecordingChatSessionManager(events);
        var dataverse = new FakeDataverseUserClient(events);
        var router = new OutputRouter(
            sessionManager,
            Mock.Of<ILogger<OutputRouter>>(),
            emailSender: null,
            workProductPersister: new TopicRegistryWorkProductPersister(
                dataverse, Mock.Of<ILogger<TopicRegistryWorkProductPersister>>()));

        var binding = new Binding
        {
            BindingId = BindingId,
            ConsumerType = "chat-summarize",
            ConsumerCode = "matter-summary",
            Ucid = "UC-A-1",
            Disposition = BindingDisposition.WorkProduct,
        };
        var session = new ChatSession(
            SessionId: "session-wp-contract",
            TenantId: "tenant-wp-contract",
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>())
        {
            HostContext = new ChatHostContext("matter", HostRecordId.ToString("D")),
        };

        // Act — route a schema-validated capability output through the universal seam.
        var routed = await router.RouteAsync(
            session, binding,
            ParseJson("""{"tldr":"t","summary":"s","keywords":["k"],"entities":{"orgs":[]}}"""),
            sourceRefs: new[] { "file-9" });

        // Assert — ADR-040 ordering: the ledger store happened BEFORE the record PATCH.
        events.Should().ContainInOrder("store", "patch");

        // Assert — the PATCHed envelope carries EVERY schema-required member, correctly valued.
        dataverse.PatchedPath.Should().Be($"sprk_matters({HostRecordId:D})");
        using var patchBody = JsonDocument.Parse(dataverse.PatchedBody!);
        var envelopeText = patchBody.RootElement.GetProperty("sprk_mattersummary").GetString()!;
        using var envelope = JsonDocument.Parse(envelopeText);
        var root = envelope.RootElement;

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var requiredMembers = schemaDoc.RootElement.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
        foreach (var member in requiredMembers)
        {
            root.TryGetProperty(member, out _).Should().BeTrue(
                $"the persisted envelope must carry schema-required member '{member}'");
        }

        var declaredMembers = schemaDoc.RootElement.GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToList();
        root.EnumerateObject().Select(p => p.Name)
            .Should().BeSubsetOf(declaredMembers,
                "additionalProperties is false — the envelope emits no undeclared members");

        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        root.GetProperty("ledgerKey").GetString().Should().Be(routed.Entry.Key,
            "the record copy is addressable back to its ledger source (ADR-040)");
        root.GetProperty("disposition").GetString().Should().Be("work_product");
        root.GetProperty("turn").GetInt32().Should().Be(routed.Entry.Turn);
        root.GetProperty("payload").GetProperty("tldr").GetString().Should().Be("t",
            "the payload is the stored ledger payload, verbatim");
        root.GetProperty("sourceRefs").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "file-9" });
        DateTimeOffset.TryParse(root.GetProperty("generatedAt").GetString(), out _).Should().BeTrue(
            "generatedAt is an ISO-8601 date-time");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static string SchemaPath() => Path.Combine(
        FindRepoRoot(), "infra", "dataverse", "outputschemas", "work-product-envelope-v1.schema.json");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "infra")))
        {
            dir = dir.Parent!;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (containing infra/) not found above test base directory.");
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Session persistence seam double — records the "store" event for ordering proof.</summary>
    private sealed class RecordingChatSessionManager : ChatSessionManager
    {
        private readonly List<string> _events;

        public RecordingChatSessionManager(List<string> events) : base(
            cache: Mock.Of<ITenantCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
            _events = events;
        }

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            _events.Add("store");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// User-OBO Web API double shaped like the seeded spaarkedev1 catalog (task 047):
    /// registry topic <c>matter-summary</c> → <c>sprk_matter.sprk_mattersummary</c>.
    /// Records the "patch" event for ordering proof and captures the PATCH for inspection.
    /// </summary>
    private sealed class FakeDataverseUserClient : IDataverseUserClient
    {
        private readonly List<string> _events;

        public FakeDataverseUserClient(List<string> events)
        {
            _events = events;
        }

        public string? PatchedPath { get; private set; }
        public string? PatchedBody { get; private set; }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (relativePath.StartsWith("EntityDefinitions(LogicalName='sprk_aitopicregistry')", StringComparison.Ordinal))
            {
                return Ok("""{"EntitySetName":"sprk_aitopicregistries"}""");
            }
            if (relativePath.StartsWith("EntityDefinitions(LogicalName='sprk_matter')", StringComparison.Ordinal))
            {
                return Ok("""{"EntitySetName":"sprk_matters"}""");
            }
            if (relativePath.StartsWith("sprk_aitopicregistries?", StringComparison.Ordinal))
            {
                return Ok("""
                    {"value":[{"sprk_topicname":"matter-summary","sprk_mode":"single","sprk_hostentity":"sprk_matter","sprk_targetfield":"sprk_mattersummary"}]}
                    """);
            }
            throw new InvalidOperationException($"Unexpected GET: {relativePath}");
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            _events.Add("patch");
            PatchedPath = relativePath;
            PatchedBody = jsonBody;
            return Task.FromResult(DataverseUserResponse.Ok(204, null));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Work-product persistence never creates records.");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Work-product persistence never creates records.");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Work-product persistence never deletes records.");

        private static Task<DataverseUserResponse> Ok(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Task.FromResult(DataverseUserResponse.Ok(200, doc.RootElement.Clone()));
        }
    }
}
