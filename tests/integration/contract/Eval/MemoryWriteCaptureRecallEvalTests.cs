using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Integration.Seam.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Eval;

/// <summary>
/// AIR2-057 (FR-B-08 / FR-30) — the CAPTURE→RECALL eval, joined to the golden-utterance MERGE GATE
/// via the <c>Category=GoldenUtteranceEval</c> trait (no CI-YAML change; README §"Adding a case").
/// It guards the silent-failure mode where <c>memory.write</c> ships but the model never actually
/// captures, AND proves — through the REAL <see cref="SideEffectGateAIFunction"/> +
/// <see cref="ConfirmationPolicyEngine"/> path over the REAL authored catalog row — that the write is
/// AI-INITIATED + SILENT (no confirmation dialog), the ENGINE's decision from catalog DATA, never a
/// gate bypass (task-044 anti-shim precedent).
/// </summary>
[Trait("Category", "GoldenUtteranceEval")]
public class MemoryWriteCaptureRecallEvalTests
{
    private const string Tenant = "tenant-memory-eval-057";
    private const string EnvUrl = "https://spaarkedev1.crm.dynamics.com";

    // =====================================================================================
    // The authored row DECLARES the silent-execute contract: Write (gate-wrapped) + a low-tier
    // reversible riskProfile the REAL engine resolves to Execute. (criterion 2 — catalog DATA proof)
    // =====================================================================================
    [Fact]
    public void MemoryWriteRow_DeclaresWrite_AndItsRiskProfileResolvesToSilentExecute()
    {
        var row = LoadMemoryWriteRow();

        // Gate-WRAPPED: Write fires PendingPlanManager.RequiresConfirmation (the ONE gate keys on the
        // declaration, ADR-039). Declaring Read/Pure to dodge the gate would be the forbidden bypass.
        ((ToolSideEffectClass)row.SideEffectClass).Should().Be(ToolSideEffectClass.Write,
            "memory.write is a side effect — it is gate-wrapped, then silently executed by the engine");
        PendingPlanManager.RequiresConfirmation((ToolSideEffectClass)row.SideEffectClass).Should().BeTrue();

        // SILENT: the declared riskProfile DATA resolves — through the real resolver + projector — to
        // Execute (no dialog), independent of request origin (Tier 0/1 auto-execute either way).
        using var configDoc = JsonDocument.Parse(row.ConfigurationJson);
        var profile = GateRiskProfile.FromConfiguration(configDoc.RootElement);
        profile.Should().NotBeNull("the row declares a riskProfile block");
        profile!.Reversible.Should().BeTrue("a memory item is reversible / user-erasable");
        profile.RecordOfTruthImpact.Should().BeFalse("memory holds derived knowledge, not a system-of-record row");

        var tier = RiskTierResolver.Resolve(profile);
        tier.Should().Be(GateRiskTier.Tier1, "tier 1 + no escalating factor stands (factor floor = Tier0)");

        GateDecisionProjector.Project(tier, GateRequestOrigin.Explicit, GateArgCompleteness.Complete)
            .Outcome.Should().Be(GateOutcome.Execute, "silent execution is the ENGINE's decision from catalog DATA");
        GateDecisionProjector.Project(tier, GateRequestOrigin.Inferred, GateArgCompleteness.Complete)
            .Outcome.Should().Be(GateOutcome.Execute, "Tier 0/1 auto-execute regardless of origin — AI-initiated capture never depends on origin");
    }

    // =====================================================================================
    // ROUND TRIP through the REAL gate: the tool executes SILENTLY (no confirmation dialog),
    // persists the fact, and a later read RECALLS it. (criteria 2 + 3)
    // =====================================================================================
    [Fact]
    public async Task MemoryWrite_ThroughRealGate_ExecutesSilently_NoDialog_AndTheFactIsRecalled()
    {
        var row = LoadMemoryWriteRow();
        var store = new FakeMemoryItemStore(TimeProvider.System);
        var matterId = Guid.NewGuid();
        var contextSessionId = Guid.NewGuid();

        var gate = BuildGate(store, row, matterId, contextSessionId,
            out var sessionManager, out var gateSessionId, out var sse);

        // Turn N: the assistant automatically captures a salient fact (record scope).
        var result = await gate.InvokeAsync(
            new AIFunctionArguments
            {
                ["scope"] = "record",
                ["factType"] = "keyDate",
                ["key"] = "Filing deadline",
                ["value"] = "2026-09-01",
            },
            CancellationToken.None);

        // SILENT execution through the REAL gate — no confirmation dialog, no suspend marker.
        result.Should().BeOfType<string>().Which.Should().Contain("ACTION EXECUTED",
            "a low-tier reversible memory write auto-executes — the engine's decision, not a gate bypass");
        sse.Should().NotContain(e => e.Type == "action_confirmation", "AI-initiated memory capture is SILENT — no confirmation dialog");
        var gates = (await sessionManager.GetSessionAsync(Tenant, gateSessionId))!.Gates ?? [];
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending, "a silent write writes NO pending suspend marker");

        // The fact PERSISTED (guards the silent-failure mode: tool ships but never captures).
        var stored = await store.GetForRecordAsync("matter", matterId.ToString());
        var item = stored.Should().ContainSingle("the AI-initiated write persisted exactly one memory item").Subject;
        item.Source.Should().Be(MemoryOrigin.AiDerived);
        item.SessionId.Should().Be(contextSessionId.ToString());

        // Turn M (a later read): the fact is RECALLED for the same subject.
        var recalled = await store.GetForRecordAsync("matter", matterId.ToString());
        recalled.Should().Contain(i => i.Fact.Key == "Filing deadline" && i.Fact.Value == "2026-09-01",
            "a later read for the same subject surfaces the captured fact (round-trip recall)");
        var fragment = await store.ToRecordPromptFragmentAsync("matter", matterId.ToString());
        fragment.Should().Contain("2026-09-01", "the captured fact is recalled into the record's system-prompt memory fragment");
    }

    // =====================================================================================
    // F-2 (D3/D5.3) — USER-scope capture→recall through the REAL bind + render path: a
    // memory.write scope=user fact is recalled into the User slice fragment and rendered into the
    // stable-prefix additions the interactive/dispatch prompt consumes. Joins the CI gate via the
    // class-level GoldenUtteranceEval trait.
    // =====================================================================================
    [Fact]
    public async Task UserScopeMemory_IsRecalled_ViaTheRealBindAndRenderPath()
    {
        var store = new FakeMemoryItemStore(TimeProvider.System);
        const string systemUserId = "9b0e6a1e-0000-4000-8000-000000000009";

        // Turn N (an earlier session): the assistant captured a durable USER preference (scope=user).
        await store.UpsertAsync(new MemoryItem
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = MemoryScope.User,
            UserId = systemUserId,
            Source = MemoryOrigin.AiDerived,
            Fact = new MemoryFact
            {
                Type = MemoryFactType.KeyFact,
                Key = "Preferred citation style",
                Value = "Bluebook",
                ConfirmedByUser = true,
                Confidence = 1.0,
            },
        }, Tenant);

        // Turn M (next session): the REAL ContextBinder resolves the User slice's recall fragment.
        var binder = BuildBinder(store);
        var bound = await binder.BindAsync(
            new ContextBindingRequest { CallerSystemUserId = systemUserId }, CancellationToken.None);

        bound.Context.User!.Fragment.Should().Contain("Bluebook",
            "F-2: the captured user preference is recalled into the ContextEnvelope User slice fragment");
        ContextEnvelopeRenderer.RenderStablePrefixAdditions(bound.Context).Should().Contain("Bluebook",
            "and it renders into the stable-prefix additions the interactive (D1) + dispatch (D6) prompts consume");
    }

    // =====================================================================================
    // F-2 negative (D5.4) — a user with ZERO memory contributes no recall fragment, so the stable
    // prefix is byte-IDENTICAL to the host-identity block alone (additive-only — no prompt drift).
    // =====================================================================================
    [Fact]
    public async Task UserWithNoMemory_ProducesNoUserFragment_StablePrefixIsHostIdentityOnly()
    {
        var store = new FakeMemoryItemStore(TimeProvider.System);
        var binder = BuildBinder(store);

        var bound = await binder.BindAsync(new ContextBindingRequest
        {
            CallerSystemUserId = "00000000-0000-0000-0000-0000000000ff",
            HostEntityType = "matter",
            HostEntityId = "22222222-2222-2222-2222-222222222222",
        }, CancellationToken.None);

        bound.Context.User?.Fragment.Should().BeNull("a user with zero memory contributes no recall fragment");
        var hostOnly = HostIdentityProducer.BuildEnrichmentBlock("matter", "22222222-2222-2222-2222-222222222222", null, null);
        ContextEnvelopeRenderer.RenderStablePrefixAdditions(bound.Context).Should().Be(hostOnly,
            "with no user memory the stable prefix is byte-identical to the host-identity block alone — the negative parity pin");
    }

    private static ContextBinder BuildBinder(FakeMemoryItemStore store)
    {
        var cache = new InMemoryTenantCache();
        var sessionManager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>().Object);

        return new ContextBinder(
            sessionManager,
            NullLogger<ContextBinder>.Instance,
            memoryItemStore: store,
            timeProvider: TimeProvider.System);
    }

    // =====================================================================================
    // Harness (mirrors ConfirmationPolicyGateLiveDecisionTests.BuildGate — the REAL gate over the
    // REAL engine, real PendingPlanManager + ChatSessionManager over the in-memory tenant cache).
    // =====================================================================================
    private static SideEffectGateAIFunction BuildGate(
        FakeMemoryItemStore store,
        MemoryWriteRow row,
        Guid matterId,
        Guid contextSessionId,
        out ChatSessionManager sessionManager,
        out string gateSessionId,
        out List<ChatSseEvent> sseEvents)
    {
        var cache = new InMemoryTenantCache();
        sessionManager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>().Object);

        gateSessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        sessionManager.UpdateSessionCacheAsync(new ChatSession(
            SessionId: gateSessionId,
            TenantId: Tenant,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<global::Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>())).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantCache>(cache);
        services.AddSingleton(sessionManager);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PendingPlanManager>>(NullLogger<PendingPlanManager>.Instance);
        services.AddScoped<PendingPlanManager>();
        services.AddSingleton<IOptions<DataverseOptions>>(Options.Create(new DataverseOptions { EnvironmentUrl = EnvUrl }));
        var rootServices = services.BuildServiceProvider();

        var handler = new MemoryWriteHandler(store, TimeProvider.System, NullLogger<MemoryWriteHandler>.Instance);

        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-Memory Write",
            Description = "memory.write eval row",
            Type = ToolType.Custom,
            HandlerClass = nameof(MemoryWriteHandler),
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = row.JsonSchema,               // the REAL authored arg schema
            Configuration = row.ConfigurationJson,     // the REAL authored riskProfile DATA
            SideEffectClass = (ToolSideEffectClass)row.SideEffectClass,
        };

        var adapter = new ToolHandlerToAIFunctionAdapter(
            tool,
            handler,
            contextFactory: () => new ChatInvocationContext
            {
                ChatSessionId = contextSessionId,
                TenantId = Tenant,
                MatterId = matterId,
                UserId = "9b0e6a1e-0000-4000-8000-000000000009",
            });

        var captured = new List<ChatSseEvent>();
        sseEvents = captured;

        return new SideEffectGateAIFunction(
            adapter,
            (ToolSideEffectClass)row.SideEffectClass,
            rootServices,
            Tenant,
            gateSessionId,
            NullLogger.Instance,
            sseWriter: (evt, _) => { captured.Add(evt); return Task.CompletedTask; });
    }

    private readonly record struct MemoryWriteRow(int SideEffectClass, string ConfigurationJson, string JsonSchema);

    private static MemoryWriteRow LoadMemoryWriteRow()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_analysistool-memory-write-row.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var sideEffect = root.GetProperty("sprk_sideeffectclass").GetInt32();
        // The seed stores sprk_configuration / sprk_jsonschema as serialized JSON STRINGS at seed time;
        // here we read the authored object form and serialize it to the same compact string the runtime sees.
        var config = root.GetProperty("sprk_configuration").GetRawText();
        var schema = root.GetProperty("sprk_jsonschema").GetRawText();
        return new MemoryWriteRow(sideEffect, config, schema);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory.");
    }
}
