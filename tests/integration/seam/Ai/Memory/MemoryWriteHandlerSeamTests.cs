using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai.Memory;

/// <summary>
/// Vertical-slice seam for AIR2-057 (FR-B-08): the AI-initiated <c>memory.write</c> handler →
/// generalized <see cref="IMemoryItemStore"/> → read-back. Exercises the handler over the REAL
/// store semantics (via <see cref="FakeMemoryItemStore"/>, which reuses the production
/// <see cref="MemoryItemStore.BuildItemId"/> supersession keying) — so provenance recording,
/// upsert-by-(Type,Key) supersession, scope isolation, and the deferred-governance posture are
/// asserted on the actual capture path, not a mock.
/// </summary>
public class MemoryWriteHandlerSeamTests
{
    private const string Tenant = "tenant-memory-057";
    private const string UserId = "9b0e6a1e-0000-4000-8000-000000000001"; // Dataverse systemuserid shape

    // =====================================================================================
    // Provenance envelope recorded at capture time + surfaced on read (criteria 5 + 6)
    // =====================================================================================
    [Fact]
    public async Task RecordScopeCapture_RecordsAiDerivedProvenanceEnvelope_AndSurfacesItOnRead()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);
        var matterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var ctx = RecordContext(sessionId, matterId,
            Args("record", "keyFact", "Governing law", "New York"));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();

        var stored = await store.GetForRecordAsync("matter", matterId.ToString());
        var item = stored.Should().ContainSingle().Subject;

        // Provenance envelope (METADATA, not a gate) recorded at capture time:
        item.Source.Should().Be(MemoryOrigin.AiDerived, "memory.write is AI-initiated capture");
        item.BindingId.Should().Be(MemoryWriteHandler.LoopBindingId, "the reserved loop binding is the 'which action' provenance");
        item.SessionId.Should().Be(sessionId.ToString(), "the originating session is recorded");
        item.Fact.Source.Should().Be(MemoryOrigin.AiDerived);

        // And it is SURFACED on read so the FR-B-03 review/delete surface can act on it.
        item.Fact.Key.Should().Be("Governing law");
        item.Fact.Value.Should().Be("New York");
        item.Scope.Should().Be(MemoryScope.Record);
        item.SubjectType.Should().Be("matter");
        item.SubjectId.Should().Be(matterId.ToString());
    }

    // =====================================================================================
    // Upsert-by-(Type,Key) supersession — a repeated capture UPDATES, never duplicates (criterion 4)
    // =====================================================================================
    [Fact]
    public async Task RepeatedCaptureSameFactTypeAndKey_SupersedesTheFact_NoDuplicateAccumulation()
    {
        var store = new FakeMemoryItemStore(TimeProvider.System);
        var (handler, tool) = Build(store);
        var matterId = Guid.NewGuid();

        var first = await handler.ExecuteChatAsync(
            RecordContext(Guid.NewGuid(), matterId, Args("record", "keyFact", "Governing law", "New York")),
            tool, CancellationToken.None);
        first.Success.Should().BeTrue();

        var second = await handler.ExecuteChatAsync(
            RecordContext(Guid.NewGuid(), matterId, Args("record", "keyFact", "Governing law", "California")),
            tool, CancellationToken.None);
        second.Success.Should().BeTrue();

        var stored = await store.GetForRecordAsync("matter", matterId.ToString());
        var item = stored.Should().ContainSingle(
            "a repeated capture for the same (factType, key) SUPERSEDES the prior fact rather than accumulating duplicates").Subject;
        item.Fact.Value.Should().Be("California", "the superseding capture replaces the value");
        item.UpdatedAt.Should().NotBeNull("supersession stamps the update instant");

        var payload = second.GetData<MemoryWriteHandler.MemoryWritePayload>();
        payload!.Superseded.Should().BeTrue("the result reports the write UPDATED an existing fact");
    }

    // =====================================================================================
    // Scope isolation: user scope needs an authenticated user; record ≠ user partition (criterion 4/6)
    // =====================================================================================
    [Fact]
    public async Task UserScopeCapture_WithAuthenticatedUser_PersistsUnderUser_NotVisibleToRecordReads()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);

        var ok = await handler.ExecuteChatAsync(
            UserContext(Guid.NewGuid(), UserId, Args("user", "keyFact", "Preferred citation style", "Bluebook")),
            tool, CancellationToken.None);
        ok.Success.Should().BeTrue();

        (await store.GetForUserAsync(UserId)).Should().ContainSingle().Which.Fact.Value.Should().Be("Bluebook");
        // Scope isolation: the user-scope fact never leaks into a record read.
        (await store.GetForRecordAsync("matter", UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task UserScopeCapture_WithoutAuthenticatedUser_ReturnsHonestError_NothingStored()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);

        var result = await handler.ExecuteChatAsync(
            UserContext(Guid.NewGuid(), userId: null, Args("user", "keyFact", "X", "Y")),
            tool, CancellationToken.None);

        result.Success.Should().BeFalse("user memory requires an authenticated user — never an LLM-supplied identity");
        result.ErrorMessage.Should().Contain("Authenticate");
        store.All.Should().BeEmpty("no user id ⇒ nothing captured");
    }

    [Fact]
    public async Task RecordScopeCapture_WithNoRecordInContextAndNoSubjectArgs_ReturnsHonestError_NothingStored()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);

        // Record scope, but no MatterId in context and no subjectType/subjectId args.
        var ctx = new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = Tenant,
            ToolArgumentsJson = Args("record", "keyFact", "Plaintiff", "Acme"),
        };

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("subject");
        store.All.Should().BeEmpty();
    }

    // =====================================================================================
    // Deferred governance: provenance NEVER blocks the write (criterion 7 boundary)
    // =====================================================================================
    [Fact]
    public async Task Capture_IsNotBlockedByProvenance_TrustLevelCarriedButInert()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);
        var matterId = Guid.NewGuid();

        var result = await handler.ExecuteChatAsync(
            RecordContext(Guid.NewGuid(), matterId, Args("record", "party", "Opposing counsel", "Dewey Cheatem")),
            tool, CancellationToken.None);

        result.Success.Should().BeTrue("provenance is METADATA, not a gate — the write always proceeds (FR-B-08)");
        var item = (await store.GetForRecordAsync("matter", matterId.ToString())).Single();
        item.TrustLevel.Should().BeNull("trustLevel is carried, not acted on — enforcement is DEFERRED to the governance project");
    }

    // =====================================================================================
    // FR-B-01 passthrough: the store rejects a record fact that mirrors a live Dataverse field
    // =====================================================================================
    [Fact]
    public async Task RecordScopeCapture_WithDataverseFieldMirrorKey_RelaysHonestRejection_NothingStored()
    {
        var store = new FakeMemoryItemStore();
        var (handler, tool) = Build(store);

        var result = await handler.ExecuteChatAsync(
            RecordContext(Guid.NewGuid(), Guid.NewGuid(), Args("record", "keyFact", "sprk_matternumber", "M-1001")),
            tool, CancellationToken.None);

        result.Success.Should().BeFalse("record memory holds derived knowledge, not a raw Dataverse-field mirror (FR-B-01)");
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        store.All.Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private static (MemoryWriteHandler handler, AnalysisTool tool) Build(IMemoryItemStore store)
    {
        var handler = new MemoryWriteHandler(store, TimeProvider.System, NullLogger<MemoryWriteHandler>.Instance);
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-Memory Write",
            Description = "test memory.write row",
            Type = ToolType.Custom,
            HandlerClass = nameof(MemoryWriteHandler),
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = """{"type":"object","properties":{"scope":{"type":"string"}}}""",
            SideEffectClass = ToolSideEffectClass.Write,
        };
        return (handler, tool);
    }

    private static ChatInvocationContext RecordContext(Guid sessionId, Guid matterId, string argsJson) => new()
    {
        ChatSessionId = sessionId,
        TenantId = Tenant,
        MatterId = matterId,
        UserId = UserId,
        ToolArgumentsJson = argsJson,
    };

    private static ChatInvocationContext UserContext(Guid sessionId, string? userId, string argsJson) => new()
    {
        ChatSessionId = sessionId,
        TenantId = Tenant,
        UserId = userId,
        ToolArgumentsJson = argsJson,
    };

    private static string Args(string scope, string factType, string key, string value) =>
        $$"""{"scope":"{{scope}}","factType":"{{factType}}","key":"{{key}}","value":"{{value}}"}""";
}
