using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Unit tests for <see cref="PromptBudgetTracker"/> (R6 Pillar 7 / task 068, D-C-22).
/// </summary>
/// <remarks>
/// Covers: constructor null-arg defense, NFR-10 budget ceiling clamping, granted /
/// truncated reservation paths, layer-tag normalisation, idempotent zero-cost
/// reservation, monotonically non-decreasing UsedBudget, truncation telemetry counter
/// emission (ADR-015 deterministic identifiers only), edge cases (negative request,
/// exact-budget consumption, multiple-layer accounting).
/// </remarks>
public sealed class PromptBudgetTrackerTests
{
    private const string TenantA = "tenant-a";
    private static readonly Guid SessionA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<ILogger<PromptBudgetTracker>> _logger = new();

    private static PromptBudgetTracker CreateSut(
        int totalBudget,
        ILogger<PromptBudgetTracker>? logger = null)
    {
        return new PromptBudgetTracker(totalBudget, logger ?? new Mock<ILogger<PromptBudgetTracker>>().Object);
    }

    private static PromptBudgetTracker CreateSutFromOptions(
        MemoryCompositionOptions options,
        ILogger<PromptBudgetTracker>? logger = null)
    {
        return new PromptBudgetTracker(
            Options.Create(options),
            logger ?? new Mock<ILogger<PromptBudgetTracker>>().Object);
    }

    // =========================================================================
    // Constructor + budget ceiling
    // =========================================================================


    [Fact]
    public void Ctor_RejectsNullLogger_FromOptions()
    {
        var opts = Options.Create(new MemoryCompositionOptions());
        Assert.Throws<ArgumentNullException>(() =>
            new PromptBudgetTracker(opts, null!));
    }

    [Fact]
    public void Ctor_RejectsNullLogger_FromInternalOverload()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PromptBudgetTracker(8000, null!));
    }

    [Fact]
    public void Ctor_DefaultBudget_Is8K_PerNfr10()
    {
        using var sut = CreateSutFromOptions(new MemoryCompositionOptions());

        sut.TotalBudget.Should().Be(8000);
        sut.UsedBudget.Should().Be(0);
        sut.Remaining.Should().Be(8000);
    }

    [Fact]
    public void Ctor_ClampsBudgetBelowFloor_To1024()
    {
        using var sut = CreateSut(totalBudget: 100);

        sut.TotalBudget.Should().Be(1024, "below-floor budgets clamp up to 1024 — degrades gracefully under operator misconfig");
    }

    [Fact]
    public void Ctor_ClampsBudgetAboveCeiling_To32K()
    {
        using var sut = CreateSut(totalBudget: 999_999);

        sut.TotalBudget.Should().Be(32_000, "above-ceiling budgets clamp down to 32_000 — matches MemoryCompositionOptions band");
    }

    [Fact]
    public void Ctor_RespectsCustomBudget_WithinBand()
    {
        using var sut = CreateSut(totalBudget: 12_000);

        sut.TotalBudget.Should().Be(12_000);
    }

    // =========================================================================
    // TryReserve — granted path
    // =========================================================================

    [Fact]
    public void TryReserve_Granted_WhenRequestFitsBudget()
    {
        using var sut = CreateSut(totalBudget: 8000);

        var granted = sut.TryReserve("matter-memory", 500, SessionA, TenantA);

        granted.Should().BeTrue();
        sut.UsedBudget.Should().Be(500);
        sut.Remaining.Should().Be(7500);
    }

    [Fact]
    public void TryReserve_Granted_AccumulatesAcrossLayers()
    {
        using var sut = CreateSut(totalBudget: 8000);

        sut.TryReserve("persona", 100, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("knowledge-inline", 500, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("matter-memory", 400, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("workspace-state", 200, SessionA, TenantA).Should().BeTrue();

        sut.UsedBudget.Should().Be(1200);
        sut.Remaining.Should().Be(6800);
    }

    [Fact]
    public void TryReserve_Granted_AtExactBudgetBoundary()
    {
        using var sut = CreateSut(totalBudget: 1024);

        var granted = sut.TryReserve("persona", 1024, SessionA, TenantA);

        granted.Should().BeTrue("exact-equality to budget consumes the entire ceiling and is granted");
        sut.UsedBudget.Should().Be(1024);
        sut.Remaining.Should().Be(0);
    }

    // =========================================================================
    // TryReserve — truncated / over-budget path (FR-46 + NFR-10 binding)
    // =========================================================================

    [Fact]
    public void TryReserve_Truncated_WhenSingleLayerExceedsBudget()
    {
        using var sut = CreateSut(totalBudget: 1024);

        var granted = sut.TryReserve("memory-composition", 2000, SessionA, TenantA);

        granted.Should().BeFalse();
        sut.UsedBudget.Should().Be(0, "denied reservations MUST NOT consume budget");
        sut.Remaining.Should().Be(1024);
    }

    [Fact]
    public void TryReserve_Truncated_WhenCumulativeExceedsBudget()
    {
        using var sut = CreateSut(totalBudget: 2000);

        sut.TryReserve("persona", 1500, SessionA, TenantA).Should().BeTrue();
        var granted = sut.TryReserve("matter-memory", 800, SessionA, TenantA);

        granted.Should().BeFalse("1500 + 800 = 2300 > 2000 ceiling — second reservation denied");
        sut.UsedBudget.Should().Be(1500, "first reservation preserved; denied reservations don't affect prior grants");
        sut.Remaining.Should().Be(500);
    }

    [Fact]
    public void TryReserve_Truncated_LeavesSubsequentSmallerReservationsAble_ToFit()
    {
        // R6 task 068 — over-budget denial doesn't poison the tracker; a smaller
        // subsequent fragment may still fit (FR-46 priority-ordering enables this).
        using var sut = CreateSut(totalBudget: 2000);

        sut.TryReserve("persona", 1500, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("knowledge-inline", 800, SessionA, TenantA).Should().BeFalse();
        sut.TryReserve("workspace-state", 400, SessionA, TenantA).Should().BeTrue("400 fits in remaining 500");

        sut.UsedBudget.Should().Be(1900);
        sut.Remaining.Should().Be(100);
    }

    // =========================================================================
    // TryReserve — no-op / argument normalisation
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void TryReserve_NoOp_WhenRequestNonPositive(int requested)
    {
        using var sut = CreateSut(totalBudget: 8000);

        var granted = sut.TryReserve("persona", requested, SessionA, TenantA);

        granted.Should().BeTrue("non-positive request is a no-op");
        sut.UsedBudget.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryReserve_NormalizesEmptyLayer_ToUnknown(string? layer)
    {
        using var sut = CreateSut(totalBudget: 8000);

        var granted = sut.TryReserve(layer!, 100, SessionA, TenantA);

        granted.Should().BeTrue("empty layer is a normalisation case, not a rejection");
        sut.UsedBudget.Should().Be(100);
    }

    [Fact]
    public void TryReserve_AcceptsNullSessionAndTenant()
    {
        using var sut = CreateSut(totalBudget: 8000);

        var granted = sut.TryReserve("persona", 100, sessionId: null, tenantId: null);

        granted.Should().BeTrue();
    }

    // =========================================================================
    // UsedBudget invariants
    // =========================================================================

    [Fact]
    public void UsedBudget_IsMonotonicallyNonDecreasing()
    {
        using var sut = CreateSut(totalBudget: 8000);

        var prev = sut.UsedBudget;
        for (var i = 0; i < 10; i++)
        {
            sut.TryReserve($"layer-{i}", 100, SessionA, TenantA).Should().BeTrue();
            sut.UsedBudget.Should().BeGreaterThanOrEqualTo(prev);
            prev = sut.UsedBudget;
        }

        sut.UsedBudget.Should().Be(1000);
    }

    [Fact]
    public void Remaining_NeverNegative_EvenIfDenialsAfterFullConsumption()
    {
        using var sut = CreateSut(totalBudget: 1024);

        sut.TryReserve("persona", 1024, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("matter-memory", 500, SessionA, TenantA).Should().BeFalse();

        sut.Remaining.Should().Be(0, "remaining clamps at zero — never reports negative headroom");
    }

    // =========================================================================
    // Telemetry — truncation counter emission (ADR-015 deterministic IDs only)
    // =========================================================================

    [Fact]
    public void TryReserve_EmitsTruncationCounter_OnDenial()
    {
        var observed = new List<(string Layer, string Decision)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == PromptBudgetTracker.MeterName
                    && instr.Name == PromptBudgetTracker.TruncatedCounterName)
                {
                    l.EnableMeasurementEvents(instr);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var layer = string.Empty;
            var decision = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "layer") layer = tag.Value?.ToString() ?? string.Empty;
                if (tag.Key == "decision") decision = tag.Value?.ToString() ?? string.Empty;
            }
            observed.Add((layer, decision));
        });
        listener.Start();

        using var sut = CreateSut(totalBudget: 1024);

        sut.TryReserve("memory-composition", 2000, SessionA, TenantA).Should().BeFalse();

        observed.Should().ContainSingle();
        observed[0].Layer.Should().Be("memory-composition");
        observed[0].Decision.Should().Be(PromptBudgetTracker.Decision.Truncated);
    }

    [Fact]
    public void TryReserve_EmitsGrantedCounter_OnSuccess()
    {
        var observed = new List<(string Layer, string Decision)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == PromptBudgetTracker.MeterName
                    && instr.Name == PromptBudgetTracker.GrantedCounterName)
                {
                    l.EnableMeasurementEvents(instr);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var layer = string.Empty;
            var decision = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "layer") layer = tag.Value?.ToString() ?? string.Empty;
                if (tag.Key == "decision") decision = tag.Value?.ToString() ?? string.Empty;
            }
            observed.Add((layer, decision));
        });
        listener.Start();

        using var sut = CreateSut(totalBudget: 8000);

        sut.TryReserve("persona", 100, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("matter-memory", 500, SessionA, TenantA).Should().BeTrue();

        observed.Should().HaveCount(2);
        observed.Should().AllSatisfy(o => o.Decision.Should().Be(PromptBudgetTracker.Decision.Granted));
        observed.Select(o => o.Layer).Should().BeEquivalentTo(new[] { "persona", "matter-memory" });
    }

    [Fact]
    public void TruncationTelemetry_CarriesOnlyDeterministicIdentifiers_PerAdr015()
    {
        // ADR-015 BINDING: meter tags MUST contain only deterministic identifiers,
        // enum-like decision strings, and numeric counts — NEVER user content. This
        // test verifies the tag set is exactly the documented surface.
        var tagKeys = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == PromptBudgetTracker.MeterName)
                {
                    l.EnableMeasurementEvents(instr);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags) tagKeys.Add(tag.Key);
        });
        listener.Start();

        using var sut = CreateSut(totalBudget: 1024);
        sut.TryReserve("memory-composition", 2000, SessionA, TenantA).Should().BeFalse();

        tagKeys.Should().BeEquivalentTo(new[] { "layer", "decision", "sessionId", "tenantId" },
            "ADR-015: NO user-content fields permitted in budget telemetry tags");
    }

    // =========================================================================
    // FR-46 acceptance — 4 subsystems consume the tracker
    // =========================================================================

    [Fact]
    public void TryReserve_SupportsAllFourCanonicalSubsystems()
    {
        // FR-46 acceptance: factory + document context + knowledge + memory share 8K.
        // This test exercises all four canonical layer tags so any future contract
        // drift (rename / merge / remove) trips a regression here.
        using var sut = CreateSut(totalBudget: 8000);

        sut.TryReserve("persona", 200, SessionA, TenantA).Should().BeTrue();           // factory persona block
        sut.TryReserve("active-capabilities", 200, SessionA, TenantA).Should().BeTrue();// factory capabilities block
        sut.TryReserve("workspace-state", 300, SessionA, TenantA).Should().BeTrue();    // factory workspace block
        sut.TryReserve("session-files-manifest", 100, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("knowledge-inline", 500, SessionA, TenantA).Should().BeTrue();   // context-provider knowledge
        sut.TryReserve("skill-instructions", 300, SessionA, TenantA).Should().BeTrue(); // context-provider skills
        sut.TryReserve("entity-enrichment", 50, SessionA, TenantA).Should().BeTrue();
        sut.TryReserve("matter-memory", 500, SessionA, TenantA).Should().BeTrue();      // task-068 D-C-21 wire-in
        sut.TryReserve("memory-composition", 2000, SessionA, TenantA).Should().BeTrue();// task-067 composition

        sut.UsedBudget.Should().Be(4150);
        sut.Remaining.Should().Be(3850);
    }
}
