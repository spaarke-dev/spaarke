using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Extraction;

/// <summary>
/// Unit tests for <see cref="ObservationEmitter"/> — the D-P10 third mechanical gate
/// (per-field confidence threshold gating + per-field Observation emission per
/// <c>SPEC-phase-1-minimum.md §3.4</c>). Realizes <c>D-63</c> admin-tunable thresholds.
/// <para>
/// Covers the four acceptance criteria from <c>tasks/021-confidence-gating-emission.poml</c>:
/// (1) high-confidence emits N Observations, (2) below-threshold drops + logs, (3) thresholds
/// load from IConfiguration via IOptionsMonitor, (4) every emitted Observation has evidence[]
/// with verbatim quote + <c>producedBy="outcome-extraction@v1"</c>.
/// </para>
/// </summary>
public class ObservationEmitterTests
{
    // ─── Test fixtures ─────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedAsOf = new(2026, 4, 12, 8, 30, 0, TimeSpan.Zero);

    private static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>SPEC-phase-1-minimum.md §3.4 starter thresholds (also defaults on the options type).</summary>
    private static ConfidenceThresholdOptions StarterThresholds() => new()
    {
        DefaultThreshold = 0.75,
        PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["outcomeCategory"] = 0.75,
            ["settlementAmount"] = 0.85,
            ["outcomeDate"] = 0.85,
            ["matterDurationDays"] = 0.75
        }
    };

    private static IObservationEmitter NewEmitter(IOptionsMonitor<ConfidenceThresholdOptions> monitor) =>
        new ObservationEmitter(monitor, NullLogger<ObservationEmitter>.Instance);

    /// <summary>
    /// Builds the worked-example extraction (closing letter for M-2024-0341) from SPEC §3.4.1
    /// with all four fields above threshold. Lets individual tests tweak per-field confidences.
    /// </summary>
    private static ExtractionResult WorkedExampleExtraction(
        double outcomeCategoryConfidence = 0.91,
        double settlementAmountConfidence = 0.94,
        double outcomeDateConfidence = 0.97,
        double matterDurationDaysConfidence = 0.88)
    {
        return new ExtractionResult
        {
            Subject = "matter:M-2024-0341",
            DocumentRef = "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
            TenantId = "tenant-acme",
            ProducedBy = new ProducerIdentity
            {
                Kind = "playbook",
                Id = "playbook://outcome-extraction@v1",
                Version = "v1"
            },
            AsOf = FixedAsOf,
            Scope = new ExtractionScope
            {
                MatterId = "M-2024-0341",
                PracticeArea = "ip-licensing"
            },
            Fields = new Dictionary<string, ExtractionField>(StringComparer.OrdinalIgnoreCase)
            {
                ["outcomeCategory"] = new()
                {
                    Value = Raw("\"favorable_to_client\""),
                    Quote = "The matter concluded with terms favorable to our client, securing all material rights sought in the original complaint.",
                    Confidence = outcomeCategoryConfidence,
                    DisplayHint = "enum"
                },
                ["settlementAmount"] = new()
                {
                    Value = Raw("310000"),
                    Quote = "Settlement total: $310,000 USD payable within 30 days.",
                    Confidence = settlementAmountConfidence,
                    DisplayHint = "currency-usd"
                },
                ["outcomeDate"] = new()
                {
                    Value = Raw("\"2024-08-15\""),
                    Quote = "Final closing on August 15, 2024.",
                    Confidence = outcomeDateConfidence,
                    DisplayHint = "date"
                },
                ["matterDurationDays"] = new()
                {
                    Value = Raw("213"),
                    Quote = "Matter open from January 14, 2024 through closing — 213 days total.",
                    Confidence = matterDurationDaysConfidence,
                    DisplayHint = "duration-days"
                }
            }
        };
    }

    // ─── Acceptance #1 — high-confidence extraction emits N Observations ───────

    [Fact]
    public async Task EmitAsync_AllFieldsAboveThreshold_EmitsOneObservationPerField()
    {
        // Arrange: SPEC §3.4.1 worked example confidences, all above starter thresholds.
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction();
        var upserts = new List<ObservationArtifact>();

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(
            extraction,
            upsertAsync: (obs, _) => { upserts.Add(obs); return Task.CompletedTask; },
            ct: CancellationToken.None);

        // Assert — one Observation per field, same set both in return value AND via upsert callback.
        emitted.Should().HaveCount(4);
        upserts.Should().HaveCount(4);
        emitted.Select(o => o.Predicate).Should().BeEquivalentTo(new[]
        {
            "outcomeCategory", "settlementAmount", "outcomeDate", "matterDurationDays"
        });

        // Verify the SPEC §3.4.1 outcomeCategory worked example round-trips precisely.
        var outcomeCat = emitted.Single(o => o.Predicate == "outcomeCategory");
        outcomeCat.Id.Should().Be("obs:M-2024-0341:outcomeCategory:closing-letter-M-2024-0341.docx");
        outcomeCat.Subject.Should().Be("matter:M-2024-0341");
        outcomeCat.Confidence.Should().Be(0.91);
        outcomeCat.Value.DisplayHint.Should().Be("enum");
        outcomeCat.Value.Raw.GetString().Should().Be("favorable_to_client");
        outcomeCat.TenantId.Should().Be("tenant-acme");
        outcomeCat.Scope.TenantId.Should().Be("tenant-acme");
        outcomeCat.Scope.MatterId.Should().Be("M-2024-0341");
        outcomeCat.Scope.PracticeArea.Should().Be("ip-licensing");
        outcomeCat.AsOf.Should().Be(FixedAsOf);
    }

    // ─── Acceptance #4 — evidence shape + producedBy ──────────────────────────

    [Fact]
    public async Task EmitAsync_EachObservation_HasDocumentRefAndQuoteAndPlaybookRun_AndProducedByV1()
    {
        // Arrange
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction();

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(
            extraction, upsertAsync: null, ct: CancellationToken.None);

        // Assert — every emitted Observation has the SPEC §3.4.1 evidence shape.
        emitted.Should().HaveCount(4);
        foreach (var obs in emitted)
        {
            // Evidence shape: [{document + quote}, {playbook-run}]
            obs.Evidence.Should().HaveCount(2);

            var docRef = obs.Evidence[0];
            docRef.RefType.Should().Be("document");
            docRef.Ref.Should().Be("spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx");
            docRef.Quote.Should().NotBeNullOrWhiteSpace("verbatim quote is the load-bearing D-P10 acceptance criterion");

            var runRef = obs.Evidence[1];
            runRef.RefType.Should().Be("playbook-run");
            runRef.Ref.Should().StartWith("playbook://outcome-extraction@v1/run-");
            runRef.Quote.Should().BeNull();

            // ProducedBy: outcome-extraction@v1 per SPEC §3.4.1.
            obs.ProducedBy.Kind.Should().Be("playbook");
            obs.ProducedBy.Id.Should().Be("playbook://outcome-extraction@v1");
            obs.ProducedBy.Version.Should().Be("v1", "D-05 mandates Version on Observations + D-62 versioned re-extraction");
        }

        // Spot-check the outcomeCategory quote matches the SPEC §3.4.1 worked example verbatim.
        var outcomeCatQuote = emitted.Single(o => o.Predicate == "outcomeCategory").Evidence[0].Quote;
        outcomeCatQuote.Should().Be(
            "The matter concluded with terms favorable to our client, securing all material rights sought in the original complaint.");
    }

    // ─── Acceptance #2 — below-threshold field is dropped + logged ────────────

    [Fact]
    public async Task EmitAsync_OneFieldBelowThreshold_DropsThatFieldAndEmitsOthers()
    {
        // Arrange: settlementAmount at 0.80 (below 0.85 starter threshold).
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction(settlementAmountConfidence: 0.80);
        var upserts = new List<ObservationArtifact>();

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(
            extraction,
            upsertAsync: (obs, _) => { upserts.Add(obs); return Task.CompletedTask; },
            ct: CancellationToken.None);

        // Assert — three Observations, settlementAmount is the one dropped.
        emitted.Should().HaveCount(3);
        emitted.Select(o => o.Predicate).Should().NotContain("settlementAmount");
        emitted.Select(o => o.Predicate).Should().BeEquivalentTo(new[]
        {
            "outcomeCategory", "outcomeDate", "matterDurationDays"
        });

        // Upsert callback was invoked the same number of times as emitted (dropped fields
        // are not upserted — they leave no substrate trace per SPEC-phase-1-minimum.md §3.4).
        upserts.Should().HaveCount(3);
        upserts.Select(o => o.Predicate).Should().NotContain("settlementAmount");
    }

    [Fact]
    public async Task EmitAsync_AllFieldsBelowThreshold_EmitsNothingAndDoesNotInvokeUpsert()
    {
        // Arrange: every field at 0.5 (below all starter thresholds — 0.75/0.85).
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction(
            outcomeCategoryConfidence: 0.50,
            settlementAmountConfidence: 0.50,
            outcomeDateConfidence: 0.50,
            matterDurationDaysConfidence: 0.50);
        var upsertCallCount = 0;

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(
            extraction,
            upsertAsync: (_, _) => { upsertCallCount++; return Task.CompletedTask; },
            ct: CancellationToken.None);

        // Assert
        emitted.Should().BeEmpty("all fields below their per-field threshold means no Observations persist");
        upsertCallCount.Should().Be(0, "upsert callback is invoked only for surviving fields");
    }

    [Fact]
    public async Task EmitAsync_FieldExactlyAtThreshold_Emits()
    {
        // Arrange: settlementAmount exactly at 0.85 (the configured threshold).
        // The gate is < threshold (strict), so EQUAL passes.
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction(settlementAmountConfidence: 0.85);

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: CancellationToken.None);

        // Assert: settlementAmount survives.
        emitted.Should().HaveCount(4);
        emitted.Single(o => o.Predicate == "settlementAmount").Confidence.Should().Be(0.85);
    }

    // ─── Acceptance #3 — IOptionsMonitor reload propagates between calls ──────

    [Fact]
    public async Task EmitAsync_ThresholdsReloadedViaOptionsMonitor_AreAppliedOnSubsequentCalls()
    {
        // Arrange: start with very permissive thresholds so all fields emit.
        var permissive = new ConfidenceThresholdOptions
        {
            DefaultThreshold = 0.10,
            PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["outcomeCategory"] = 0.10,
                ["settlementAmount"] = 0.10,
                ["outcomeDate"] = 0.10,
                ["matterDurationDays"] = 0.10
            }
        };
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(permissive);
        var emitter = NewEmitter(monitor);

        var extraction = WorkedExampleExtraction(
            outcomeCategoryConfidence: 0.60,
            settlementAmountConfidence: 0.60,
            outcomeDateConfidence: 0.60,
            matterDurationDaysConfidence: 0.60);

        // Act 1: permissive thresholds — every field is above 0.10.
        var firstEmission = await emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: CancellationToken.None);
        firstEmission.Should().HaveCount(4, "permissive thresholds let all 0.60-confidence fields through");

        // Act 2: admin tightens thresholds at runtime (simulates appsettings reload / Key Vault refresh).
        var strict = new ConfidenceThresholdOptions
        {
            DefaultThreshold = 0.99,
            PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["outcomeCategory"] = 0.99,
                ["settlementAmount"] = 0.99,
                ["outcomeDate"] = 0.99,
                ["matterDurationDays"] = 0.99
            }
        };
        monitor.SimulateReload(strict);

        var secondEmission = await emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: CancellationToken.None);

        // Assert: same extraction, same code path, NEW thresholds — nothing emits.
        secondEmission.Should().BeEmpty(
            "IOptionsMonitor.CurrentValue picks up reloaded options on the next emission call (D-63 admin-tunable contract)");
    }

    // ─── Per-field threshold lookup — case-insensitive + default fallback ────

    [Fact]
    public async Task EmitAsync_FieldNotInPerFieldMap_UsesDefaultThreshold()
    {
        // Arrange: configure default = 0.99, no per-field override for "weirdField".
        var opts = new ConfidenceThresholdOptions
        {
            DefaultThreshold = 0.99,
            PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        };
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(opts);
        var emitter = NewEmitter(monitor);

        var extraction = new ExtractionResult
        {
            Subject = "matter:M-X",
            DocumentRef = "spe://drive/x/item/doc.docx",
            TenantId = "tenant-x",
            ProducedBy = new ProducerIdentity { Kind = "playbook", Id = "playbook://outcome-extraction@v1", Version = "v1" },
            AsOf = FixedAsOf,
            Fields = new Dictionary<string, ExtractionField>
            {
                ["weirdField"] = new()
                {
                    Value = Raw("\"foo\""),
                    Quote = "the document says foo",
                    Confidence = 0.90,
                    DisplayHint = "text"
                }
            }
        };

        // Act: 0.90 < 0.99 default → drop.
        var emitted = await emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: CancellationToken.None);

        // Assert
        emitted.Should().BeEmpty("default threshold applies when the field name is not in PerField");
    }

    [Fact]
    public void ConfidenceThresholdOptions_GetThresholdFor_IsCaseInsensitive()
    {
        var opts = new ConfidenceThresholdOptions
        {
            DefaultThreshold = 0.10,
            PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["outcomeCategory"] = 0.75
            }
        };

        opts.GetThresholdFor("outcomeCategory").Should().Be(0.75);
        opts.GetThresholdFor("OutcomeCategory").Should().Be(0.75, "the configured StringComparer is OrdinalIgnoreCase");
        opts.GetThresholdFor("unknownField").Should().Be(0.10, "default applies when missing");
    }

    // ─── Argument validation ────────────────────────────────────────────────

    [Fact]
    public async Task EmitAsync_NullExtraction_Throws()
    {
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);

        var act = () => emitter.EmitFromExtractionAsync(extraction: null!, upsertAsync: null, ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EmitAsync_NullUpsertCallback_StillReturnsObservations()
    {
        // Arrange — exercises the "task 025 not landed yet" code path.
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction();

        // Act
        var emitted = await emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: CancellationToken.None);

        // Assert
        emitted.Should().HaveCount(4);
    }

    [Fact]
    public async Task EmitAsync_CancellationRequested_PropagatesOperationCanceled()
    {
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(StarterThresholds());
        var emitter = NewEmitter(monitor);
        var extraction = WorkedExampleExtraction();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => emitter.EmitFromExtractionAsync(extraction, upsertAsync: null, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── Test helper: in-process IOptionsMonitor implementation ──────────────

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{TOptions}"/> implementation that lets tests simulate
    /// configuration reloads via <see cref="SimulateReload(TOptions)"/>. Avoids pulling in a
    /// mocking framework just to flip <c>CurrentValue</c>.
    /// </summary>
    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        private TOptions _current;
        private readonly List<Action<TOptions, string?>> _listeners = new();

        public TestOptionsMonitor(TOptions initial) => _current = initial;

        public TOptions CurrentValue => _current;
        public TOptions Get(string? name) => _current;

        public IDisposable OnChange(Action<TOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void SimulateReload(TOptions next)
        {
            _current = next;
            foreach (var listener in _listeners) listener(next, null);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _onDispose;
            public Subscription(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }
    }
}
