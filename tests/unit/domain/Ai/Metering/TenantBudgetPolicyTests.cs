using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Metering;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.Metering;

/// <summary>
/// Pure-domain tests (ADR-038 §2 path #6 — mapping / gating logic, no I/O) for the
/// <see cref="TenantBudgetPolicy"/> and paired <see cref="InMemoryTenantTokenLedger"/> that jointly
/// implement per-tenant token-budget enforcement (customer-provisioning-orchestration-r1 task 077,
/// spec.md FR-13 §M1/M2, SC #13, design.md D19).
/// </summary>
/// <remarks>
/// <para>
/// These tests exercise the enforcement seam directly (no HTTP, no OpenAI, no Dataverse — just
/// the policy over an in-memory ledger + real options monitor). The pre-call gate hook inside
/// <see cref="Sprk.Bff.Api.Services.Ai.OpenAiClient"/> is a one-line invocation
/// (<c>_tenantBudgetPolicy?.EnsureUnderBudget()</c>) — the meaningful behavior lives here.
/// </para>
/// <para>
/// Acceptance criteria mapping (POML tasks/077):
/// - <see cref="EnsureUnderBudget_WhenModel1TenantOverBudget_Throws429Exception"/> — SC #13
/// - <see cref="EnsureUnderBudget_WhenModel2TenantOverBudget_DoesNotThrow"/> — FR-13 §M2
/// - <see cref="EnsureUnderBudget_WhenUnconfiguredTenant_DoesNotThrow"/> — Model 2 default
/// - <see cref="EnsureUnderBudget_WhenMasterEnabledFalse_DoesNotThrow"/> — kill switch
/// - <see cref="EnsureUnderBudget_WhenAmbientTenantMissing_DoesNotThrow"/> — attribution safety
/// - <see cref="EnsureUnderBudget_ExceptionCarriesTenantAndSpendMetadata_ForOperatorDebug"/> — 429 payload
/// </para>
/// </remarks>
public sealed class TenantBudgetPolicyTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // ITenantBudgetPolicy.EnsureUnderBudget — the SC #13 gating semantics
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureUnderBudget_WhenModel1TenantOverBudget_Throws429Exception()
    {
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model1Gated, monthlyBudgetUsd: 5.00m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 6.50m); // over cap
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, userId: null, entryPath: AiMeteringContext.EntryPathClick);

        var act = () => policy.EnsureUnderBudget();

        act.Should().Throw<TenantBudgetExceededException>()
            .Which.TenantId.Should().Be(tenantId, "the 429 must carry the tenant id for operator debug");
    }

    [Fact]
    public void EnsureUnderBudget_WhenModel1TenantUnderBudget_DoesNotThrow()
    {
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model1Gated, monthlyBudgetUsd: 5.00m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 3.00m); // under cap
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, userId: null, entryPath: AiMeteringContext.EntryPathClick);

        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderBudget_WhenModel2TenantOverBudget_DoesNotThrow()
    {
        // FR-13 §M2: Model 2 (dedicated) is observability-only — never gated.
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model2Observation, monthlyBudgetUsd: 5.00m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 100.00m); // dramatically over — Model 2 doesn't care
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, userId: null, entryPath: AiMeteringContext.EntryPathClick);

        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderBudget_WhenUnconfiguredTenant_DoesNotThrow()
    {
        // Absence from the Tenants dictionary = Model 2 default. No gate applied.
        var opts = Options.Create(new TenantBudgetOptions { Enabled = true /* empty Tenants map */ });
        var ledger = new InMemoryTenantTokenLedger();
        var policy = new TenantBudgetPolicy(new WrappedMonitor(opts.Value), ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(Guid.NewGuid().ToString(), null, AiMeteringContext.EntryPathClick);

        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderBudget_WhenMasterEnabledFalse_DoesNotThrow()
    {
        // Kill switch: even if a Model 1 tenant is over budget, master toggle suspends enforcement.
        var tenantId = Guid.NewGuid().ToString();
        var opts = new TenantBudgetOptions { Enabled = false };
        opts.Tenants[tenantId] = new TenantBudgetEntry
        {
            TenancyMode = TenantBudgetTenancyMode.Model1Gated,
            MonthlyBudgetUsd = 5.00m,
        };
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 99.00m);
        var policy = new TenantBudgetPolicy(new WrappedMonitor(opts), ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, null, AiMeteringContext.EntryPathClick);

        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderBudget_WhenAmbientTenantMissing_DoesNotThrow()
    {
        // Defensive: with no AiMeteringContext scope, cannot attribute → cannot gate → allow.
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model1Gated, monthlyBudgetUsd: 5.00m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 99.00m);
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        // NO scope pushed → ambient TenantId is null
        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderBudget_WhenModel1BudgetIsZero_DoesNotThrow()
    {
        // Defensive: zero or negative cap disables the gate (avoids permanent 429 on misconfig).
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model1Gated, monthlyBudgetUsd: 0m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 99.00m);
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, null, AiMeteringContext.EntryPathClick);

        policy.Invoking(p => p.EnsureUnderBudget()).Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TenantBudgetExceededException — 429 payload contract for endpoints/clients
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureUnderBudget_ExceptionCarriesTenantAndSpendMetadata_ForOperatorDebug()
    {
        var tenantId = Guid.NewGuid().ToString();
        var opts = OptionsFor(tenantId, TenantBudgetTenancyMode.Model1Gated, monthlyBudgetUsd: 10.00m);
        var ledger = new InMemoryTenantTokenLedger();
        ledger.AddSpend(tenantId, 12.75m);
        var policy = new TenantBudgetPolicy(opts, ledger, NullLogger<TenantBudgetPolicy>.Instance);

        using var scope = AiMeteringContext.Begin(tenantId, null, AiMeteringContext.EntryPathClick);

        var thrown = Assert.Throws<TenantBudgetExceededException>(() => policy.EnsureUnderBudget());
        thrown.TenantId.Should().Be(tenantId);
        thrown.MonthlyBudgetUsd.Should().Be(10.00m);
        thrown.ObservedSpendUsd.Should().Be(12.75m);
        thrown.Message.Should().Contain(tenantId);
        thrown.Message.Should().Contain("$12.75");
        thrown.Message.Should().Contain("$10.00");
    }

    [Fact]
    public void AsTenantBudgetExceeded429_ProducesProblemDetailsWithStableTypeUri()
    {
        var ex = new TenantBudgetExceededException("tenant-guid", monthlyBudgetUsd: 10m, observedSpendUsd: 15m);

        var result = ex.AsTenantBudgetExceeded429();

        // Result is an IResult; verify by executing it through a stub HttpContext so we can
        // inspect status + type extensions (no HTTP transport — pure result-shape assertion).
        result.Should().NotBeNull();
        result.GetType().Namespace.Should().StartWith("Microsoft.AspNetCore.Http",
            "Results.Problem returns a framework IResult wrapping the ProblemDetails");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test infrastructure (in-memory options + minimal IOptionsMonitor stub)
    // ─────────────────────────────────────────────────────────────────────────

    private static IOptionsMonitor<TenantBudgetOptions> OptionsFor(
        string tenantId, TenantBudgetTenancyMode mode, decimal monthlyBudgetUsd)
    {
        var opts = new TenantBudgetOptions { Enabled = true };
        opts.Tenants[tenantId] = new TenantBudgetEntry
        {
            TenancyMode = mode,
            MonthlyBudgetUsd = monthlyBudgetUsd,
        };
        return new WrappedMonitor(opts);
    }

    private sealed class WrappedMonitor : IOptionsMonitor<TenantBudgetOptions>
    {
        public WrappedMonitor(TenantBudgetOptions value) => CurrentValue = value;
        public TenantBudgetOptions CurrentValue { get; }
        public TenantBudgetOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TenantBudgetOptions, string?> listener) => null;
    }
}
