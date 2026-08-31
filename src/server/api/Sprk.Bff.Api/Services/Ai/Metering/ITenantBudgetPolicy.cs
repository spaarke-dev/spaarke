namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Per-tenant token-budget enforcement policy (spec.md FR-13 §M1/M2 + design.md D19 + SC #13).
/// </summary>
/// <remarks>
/// <para>
/// Called PRE-call by <see cref="OpenAiClient"/> (before every OpenAI request that has token
/// spend semantics). Model 1 (shared trial/SMB) tenants over their configured monthly USD budget
/// see the call short-circuited to <see cref="TenantBudgetExceededException"/> → 429 (per SC #13).
/// Model 2 (dedicated stamp) tenants and unconfigured tenants pass through unchanged
/// (observability-only via the existing <c>ai.metering.tokens</c> counter shipped by
/// <c>spaarke-ai-architecture-redesign-r1</c> task 054).
/// </para>
/// <para>
/// Tenant identity is resolved from the ambient <c>Telemetry.AiMeteringContext.Current</c> scope
/// set at the entry seams (ChatEndpoints, DispatchSessionEndpoint, DailyBriefing, EventRules, etc.
/// per task 054) — same source as the observability path, so gate + observability agree on which
/// tenant is calling.
/// </para>
/// <para>
/// Fail-open by design (§11 rationale in <c>notes/per-tenant-metering-impl-2026-08-17.md</c>):
/// if the policy service throws unexpectedly, the call proceeds. Observability is authoritative;
/// enforcement is a safety net. Cost of enforcement misfire = one over-budget tenant continues
/// consuming for minutes until the operator toggles the flag; cost of policy failure blocking
/// the platform = every AI call fails. The former is bounded, the latter is not.
/// </para>
/// </remarks>
public interface ITenantBudgetPolicy
{
    /// <summary>
    /// Verify the ambient tenant is under budget for the current billing month. Throws
    /// <see cref="TenantBudgetExceededException"/> if the tenant is Model 1 (gated) AND the
    /// current month-to-date spend already exceeds the configured cap.
    /// </summary>
    /// <remarks>
    /// No-op for: (a) unconfigured tenants (Model 2 default); (b) tenants explicitly
    /// <see cref="TenantBudgetTenancyMode.Model2Observation"/>; (c) missing/empty ambient tenant
    /// context (defensive — cannot gate what we cannot attribute); (d) <see cref="TenantBudgetOptions.Enabled"/> = false.
    /// </remarks>
    /// <exception cref="TenantBudgetExceededException">The tenant is Model 1 gated and over budget.</exception>
    void EnsureUnderBudget();
}
