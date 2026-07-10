using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Integration.Seam.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// F-1/F-2/F-7 envelope-convergence (D2 parity pins + D4 live budget). Proves — with the REAL producers,
/// the REAL <see cref="ContextBinder"/>, and the REAL <see cref="ContextEnvelopeRenderer"/> — that the
/// interactive/dispatch prompt's migrated sections render BYTE-IDENTICALLY to the shipped direct-append
/// producers (so the D1/D6 cutover consumes the envelope without changing existing bytes), and that the
/// volatile tail is now measured live at bind time (F-7). Pure + DI-free (KEEP path
/// <c>tests/integration/contract/**</c>) — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertions.
/// </summary>
public class EnvelopeConvergenceParityContractTests
{
    private const string Tenant = "tenant-envelope-convergence";
    private const string MatterId = "11111111-1111-1111-1111-111111111111";

    // =====================================================================================
    // D2 parity — the renderer is a faithful pass-through of the producer-built fragments, so
    // "consume the envelope via the renderer" == "consume the producer output" (byte-for-byte).
    // =====================================================================================

    [Fact]
    public void RenderStablePrefixAdditions_HostOnly_IsByteIdenticalToLegacyHostIdentityBlock()
    {
        var block = HostIdentityProducer.BuildEnrichmentBlock("matter", MatterId, "Acme Corp v. Beta LLC", "main form view");
        var envelope = ContextEnvelopeReferenceProducer.Assemble(businessFragment: block);

        ContextEnvelopeRenderer.RenderStablePrefixAdditions(envelope).Should().Be(block,
            "with no user memory the stable-prefix additions are EXACTLY the legacy host-identity block — the D1 host-identity parity pin");
    }

    [Fact]
    public void RenderEnvironmentSuffix_IsByteIdenticalToLegacyDateDirective()
    {
        var instant = DateTimeOffset.Parse("2026-07-10T12:00:00Z");
        var date = EnvironmentFactsProducer.BuildCurrentDateDirective(instant);
        var envelope = ContextEnvelopeReferenceProducer.Assemble(workspaceFragment: date);

        ContextEnvelopeRenderer.RenderEnvironmentSuffix(envelope).Should().Be(date,
            "the environment SUFFIX the factory now renders from the envelope is byte-identical to the legacy BuildCurrentDateDirective — the D1 date parity pin");
    }

    // =====================================================================================
    // D2 parity — the BINDER produces the migrated fragments from the SAME producers the legacy
    // provider used. Feeding the same resolved name/page yields a byte-identical Business fragment.
    // =====================================================================================

    [Fact]
    public async Task Binder_BusinessFragment_IsByteIdenticalToLegacyHostIdentityBlock()
    {
        var (binder, _) = BuildBinder();

        var bound = await binder.BindAsync(new ContextBindingRequest
        {
            HostEntityType = "matter",
            HostEntityId = MatterId,
            HostEntityName = "Acme Corp v. Beta LLC",
            HostPageTypeLabel = "main form view",
        }, CancellationToken.None);

        var legacy = HostIdentityProducer.BuildEnrichmentBlock("matter", MatterId, "Acme Corp v. Beta LLC", "main form view");
        bound.Context.Business!.Fragment.Should().Be(legacy,
            "the Binder's Business fragment is produced by the SAME HostIdentityProducer with the same resolved inputs");
        ContextEnvelopeRenderer.RenderStablePrefixAdditions(bound.Context).Should().Be(legacy,
            "no user memory → the interactive/dispatch stable prefix is byte-identical to the legacy host-identity block");
    }

    [Fact]
    public async Task Binder_RecordMemoryFragment_IsByteIdenticalToLegacyToRecordPromptFragment()
    {
        var store = new FakeMemoryItemStore();
        await store.UpsertAsync(RecordFact(MemoryFactType.KeyDate, "Filing deadline", "2026-09-01"), Tenant);
        var (binder, _) = BuildBinder(store);

        var bound = await binder.BindAsync(new ContextBindingRequest
        {
            HostEntityType = "matter",
            HostEntityId = MatterId,
        }, CancellationToken.None);

        var legacy = await store.ToRecordPromptFragmentAsync("matter", MatterId);
        bound.RecordMemoryFragment.Should().Be(legacy,
            "the Binder-produced record-memory fragment is byte-identical to the legacy provider's ToRecordPromptFragmentAsync (single source)");
        bound.RecordMemoryFragment.Should().Contain("2026-09-01");
        // Record memory is carried on BoundInputs — NEVER copied into the envelope Memory slice (ADR-040).
        bound.Context.Memory!.Meta.EstimatedTokens.Should().Be(0, "the envelope Memory slice stays references-only");
    }

    // =====================================================================================
    // F-7 (D4) — the volatile tail is now measured LIVE at bind time: the structural ~8k
    // Conversation worst case (8 × 4,000 chars) breaches the 2,000 budget on a real bind, the
    // breach is LOGGED (warn) — and the bind returns NORMALLY (warn-never-500).
    // =====================================================================================

    [Fact]
    public async Task Bind_WithStructuralConversationWorstCase_MeasuresLive_Breaches_Warns_NeverThrows()
    {
        var store = new FakeMemoryItemStore();
        await store.UpsertAsync(RecordFact(MemoryFactType.KeyFact, "Governing law", "New York"), Tenant);
        var (binder, logs) = BuildBinder(store);

        var outputs = Enumerable.Range(1, ConversationContextProducer.MaxContextOutputs)
            .Select(i => BuildOutput($"bind-x@t{i}", "uc-x", i, PayloadString(ConversationContextProducer.MaxContextPayloadChars)))
            .ToArray();

        // BindAsync must RETURN (warn-never-500) — a throw here would fail the test as an exception.
        var bound = await binder.BindAsync(new ContextBindingRequest
        {
            HostEntityType = "matter",
            HostEntityId = MatterId,
            LedgerOutputs = outputs,
        }, CancellationToken.None);

        bound.BudgetReport.Should().NotBeNull();
        bound.BudgetReport!.HasBreach.Should().BeTrue("the ~8k conversation tail exceeds the 2,000 Conversation budget");
        bound.BudgetReport.BreachedSlices.Should().Contain(ContextBudgetSlice.Conversation,
            "the Conversation ledger tail is now measured live (F-7) — conversationTokens=0 no longer masks the worst case");

        var conversationLine = bound.BudgetReport.Lines.Single(l => l.Slice == ContextBudgetSlice.Conversation);
        conversationLine.ActualTokens.Should().BeGreaterThan(EnvelopeBudget.Conversation, "real non-zero conversation tokens flow");
        var recordLine = bound.BudgetReport.Lines.Single(l => l.Slice == ContextBudgetSlice.RecordMemory);
        recordLine.ActualTokens.Should().BeGreaterThan(0, "the record-memory fragment is measured live too (F-7)");

        logs.Should().Contain(l => l.Contains("BREACH"),
            "the breach is SURFACED as a warn (FR-B-05) — asserted via logger capture, not a 500");
    }

    // -------------------------------------------------------------------------------------
    // Helpers — the REAL ContextBinder over an in-memory session manager + a capturing logger.
    // -------------------------------------------------------------------------------------

    private static (ContextBinder Binder, List<string> Logs) BuildBinder(IMemoryItemStore? store = null)
    {
        var cache = new InMemoryTenantCache();
        var sessionManager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            NullLogger<ChatSessionManager>.Instance);

        var logs = new List<string>();
        var binder = new ContextBinder(
            sessionManager,
            new ListLogger<ContextBinder>(logs),
            memoryItemStore: store,
            timeProvider: TimeProvider.System);
        return (binder, logs);
    }

    private static MemoryItem RecordFact(MemoryFactType type, string key, string value) => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.Record,
        SubjectType = "matter",
        SubjectId = MatterId,
        Source = MemoryOrigin.User,
        Fact = new MemoryFact { Type = type, Key = key, Value = value, ConfirmedByUser = true, Confidence = 1.0 },
    };

    private static SessionOutput BuildOutput(string key, string ucId, int turn, JsonElement payload) => new()
    {
        Key = key,
        BindingId = key.Split('@')[0],
        UcId = ucId,
        Turn = turn,
        Disposition = "informational",
        Payload = payload,
        CreatedAt = DateTimeOffset.Parse("2026-07-10T12:00:00Z"),
    };

    private static JsonElement PayloadString(int chars) => JsonSerializer.SerializeToElement(new string('s', chars));

    /// <summary>Minimal capturing logger — records formatted messages so a breach warn can be asserted (not a 500).</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _logs;
        public ListLogger(List<string> logs) => _logs = logs;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _logs.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
