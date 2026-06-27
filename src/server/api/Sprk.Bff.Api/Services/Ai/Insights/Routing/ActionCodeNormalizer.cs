using System;

namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// Backward-compat normalization layer for <c>sprk_actioncode</c> values per FR-06
/// (chat-routing-redesign-r1 Phase 1 §1.7 Stable Codes Migration, task 023).
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention (FR-06)</b>: NEW <c>sprk_actioncode</c> values are kebab-case
/// WITHOUT a <c>@v1</c> suffix (e.g., <c>"summarize-nda"</c>, <c>"ins-fetch-kpi"</c>).
/// Existing <c>@v1</c>-suffixed values (e.g., <c>"SUM-CHAT@v1"</c>,
/// <c>"INS-L1C-CTRNS@v1"</c>) remain valid during the stabilization window and are
/// NOT rewritten in Dataverse by this task (data migration is out of scope; the
/// later stabilization-window cutover handles it).
/// </para>
/// <para>
/// <b>Behavior</b>: <see cref="Normalize"/> strips a trailing <c>@v1</c> suffix
/// so callers passing either form (slug or slug@v1) resolve identically at the
/// lookup boundary. <see cref="Format"/> returns a tier-1-safe telemetry tag value
/// — <c>"clean"</c> when input lacks the suffix, <c>"v1Suffix"</c> when present —
/// so the deprecation-window decay rate can be measured by counting tag values.
/// </para>
/// <para>
/// <b>Lookup-boundary placement</b>: applied inside
/// <c>InsightsActionRouter.LoadActionByCodeAsync</c> (the only call site that
/// performs an alternate-key lookup against <c>sprk_actioncode</c> in the BFF).
/// <c>PlaybookExecutionEngine.ResolveActionConfigViaFkChainAsync</c> uses FK
/// resolution (not alternate-key) per the FR-26 invariant, so no normalization
/// is needed there.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>: static helper with no DI footprint — no interface,
/// no registration, no <c>IServiceProvider.GetService&lt;T&gt;()</c>. The method
/// is pure and side-effect-free; calling it from the router does not change the
/// DI graph.
/// </para>
/// <para>
/// <b>ADR-015 tier-1 logging</b>: the <see cref="Format"/> output (<c>"clean"</c>
/// or <c>"v1Suffix"</c>) is a low-cardinality enum-shaped string safe to attach
/// as a structured-log property or <see cref="System.Diagnostics.Activity"/> tag.
/// </para>
/// </remarks>
internal static class ActionCodeNormalizer
{
    /// <summary>Trailing version suffix used by the legacy convention.</summary>
    internal const string V1Suffix = "@v1";

    /// <summary>
    /// Strip a trailing <c>@v1</c> suffix from an action code, so callers passing
    /// either <c>"summarize-nda"</c> or <c>"summarize-nda@v1"</c> resolve to the
    /// same Dataverse row.
    /// </summary>
    /// <param name="raw">Input action code (may be null/empty/clean/suffixed).</param>
    /// <returns>
    /// The input with a trailing <c>@v1</c> stripped if present; otherwise the
    /// input verbatim. Null or empty input is returned unchanged.
    /// </returns>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        return raw.EndsWith(V1Suffix, StringComparison.Ordinal)
            ? raw[..^V1Suffix.Length]
            : raw;
    }

    /// <summary>
    /// Classify an action code's input form for telemetry. Tier-1 safe per
    /// ADR-015 — returns a low-cardinality enum-shaped string.
    /// </summary>
    /// <param name="raw">Input action code (the form the caller supplied).</param>
    /// <returns>
    /// <c>"v1Suffix"</c> when input ends with <c>@v1</c>; <c>"clean"</c> otherwise
    /// (including null/empty — those are treated as the clean form for tag purposes).
    /// </returns>
    public static string Format(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "clean";
        }

        return raw.EndsWith(V1Suffix, StringComparison.Ordinal) ? "v1Suffix" : "clean";
    }
}
