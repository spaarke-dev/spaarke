namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Configuration for per-tenant token-budget enforcement (spec.md FR-13 §M1/M2 + design.md D19).
/// </summary>
/// <remarks>
/// <para>
/// Bound from configuration section <c>TenantBudget</c> (e.g. app settings, Key Vault-backed
/// per-environment JSON). Feature is OFF by default when no per-tenant entries are configured:
/// every tenant is treated as <see cref="TenantBudgetTenancyMode.Model2Observation"/> (no gating,
/// observability-only). Model 1 tenants are opted in explicitly by adding an entry to
/// <see cref="Tenants"/> with <see cref="TenantBudgetEntry.TenancyMode"/> = <c>Model1Gated</c>.
/// </para>
/// <para>
/// Written by the H12c runtime-references handler (Phase D / customer-provisioning-orchestration-r1
/// task 072) when the customer's tenancy model is decided at provisioning time. Absent an entry,
/// the tenant is not gated (Model 2 default).
/// </para>
/// </remarks>
public sealed class TenantBudgetOptions
{
    /// <summary>Configuration binding root — <c>TenantBudget</c>.</summary>
    public const string SectionName = "TenantBudget";

    /// <summary>
    /// Master toggle. When <c>false</c>, all budget checks are skipped even if per-tenant entries
    /// exist. Default: <c>true</c> (per-tenant entries drive behavior). Corresponds to the
    /// "we can turn enforcement off across the platform with one setting" operational lever.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Per-tenant budget entries. Keyed by AAD tenant id (opaque GUID, lower-cased for match).
    /// Missing key = Model 2 observation (no gate). Present key with <see cref="TenantBudgetTenancyMode.Model1Gated"/>
    /// = enforcement enabled.
    /// </summary>
    public IDictionary<string, TenantBudgetEntry> Tenants { get; set; } =
        new Dictionary<string, TenantBudgetEntry>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-tenant budget entry (one row of the <c>TenantBudget:Tenants</c> map).
/// </summary>
public sealed class TenantBudgetEntry
{
    /// <summary>
    /// Which tenancy model this tenant is in.
    /// </summary>
    public TenantBudgetTenancyMode TenancyMode { get; set; } = TenantBudgetTenancyMode.Model2Observation;

    /// <summary>
    /// Monthly USD budget cap (spec.md § New Components §M1 `tokenBudgetMonthlyUSD` field).
    /// Only enforced when <see cref="TenancyMode"/> = <see cref="TenantBudgetTenancyMode.Model1Gated"/>.
    /// A value of <c>0</c> or negative disables the gate (defensive — no budget = no gate).
    /// </summary>
    public decimal MonthlyBudgetUsd { get; set; }
}

/// <summary>
/// Tenancy model classification for budget enforcement. Presence in
/// <see cref="TenantBudgetOptions.Tenants"/> without an explicit mode defaults to
/// <see cref="Model2Observation"/> (backward-compatible with the current no-gate behavior).
/// </summary>
public enum TenantBudgetTenancyMode
{
    /// <summary>
    /// Model 2 (dedicated stamp — spec.md §3A). Observability-only, no 429 gating; the tenant's
    /// dedicated OpenAI stamp bills them directly. Default when no entry exists.
    /// </summary>
    Model2Observation = 0,

    /// <summary>
    /// Model 1 (shared trial/SMB tier — spec.md §3A). Over-budget attempts return HTTP 429 to
    /// prevent one runaway tenant burning the shared platform OpenAI quota (SC #13 acceptance).
    /// </summary>
    Model1Gated = 1,
}
