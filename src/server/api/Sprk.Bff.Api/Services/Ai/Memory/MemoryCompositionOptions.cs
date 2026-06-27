using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Configuration options for <see cref="MemoryCompositionService"/> — the R6 Pillar 7
/// (task 067, D-C-20) hierarchical memory composition service that orchestrates the
/// three Pillar 7 primitives (compression / pinned-context / selective recall) into a
/// single tagged memory block consumed by the chat prompt-assembly path.
/// </summary>
/// <remarks>
/// <para>
/// Bound from appsettings section <c>MemoryComposition</c> via
/// <c>services.AddOptions&lt;MemoryCompositionOptions&gt;().BindConfiguration(...)</c>
/// inside <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>.
/// </para>
/// <para>
/// <b>B-G11 hardening pattern</b> (per CLAUDE.md §10 + the
/// <see cref="SummarizationCompressionOptions"/> + <see cref="PinnedContextRecallOptions"/>
/// precedents): fields conditional on <see cref="Enabled"/> are NOT decorated with
/// <c>[Required]</c>. Use-site clamping happens inside
/// <see cref="MemoryCompositionService.ComposeAsync"/> so the app starts cleanly when
/// <c>Enabled=false</c> and no composition-specific config is present.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: the <see cref="TotalTokenBudget"/> ceiling (default 8K)
/// is the BINDING ceiling the chat agent factory must respect. Composition layers
/// are dropped in priority order (retrieved-old → compressed-mid → recent-verbatim
/// oldest-first) when the composed total exceeds the budget. The pinned tier is
/// NEVER dropped — pin items participate in the budget arithmetic but the layer
/// itself is preserved even if it alone exceeds the budget (in which case the
/// service returns ONLY the pinned tier and tags the overflow in telemetry).
/// </para>
/// </remarks>
public sealed class MemoryCompositionOptions
{
    /// <summary>Configuration section name used for binding.</summary>
    public const string SectionName = "MemoryComposition";

    /// <summary>
    /// Kill switch. When <c>false</c>, <see cref="MemoryCompositionService.ComposeAsync"/>
    /// returns an empty composition immediately — the caller (chat prompt-assembly path)
    /// short-circuits and proceeds without hierarchical memory. Default: <c>true</c>
    /// (composition on by default since FR-44 binds hierarchical composition as the
    /// canonical mechanism for chat memory under the 8K budget).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Total token budget for the composed memory block. NFR-10 binding ceiling: 8K.
    /// When the four layers' aggregate exceeds this budget, layers are dropped in the
    /// priority order documented on <see cref="MemoryCompositionService"/>. Range
    /// [1024, 32_000]; the upper bound accommodates model upgrades while keeping the
    /// default in line with NFR-10.
    /// </summary>
    [Range(1024, 32_000)]
    public int TotalTokenBudget { get; init; } = 8000;

    /// <summary>
    /// Number of most-recent messages preserved verbatim (un-summarised, un-recalled).
    /// Spec FR-44 binding: "last 10 turns as-is". Range [1, 50]; values outside this
    /// band are clamped at use-site rather than rejected because composition is on the
    /// chat hot path and misconfiguration must degrade gracefully.
    /// </summary>
    [Range(1, 50)]
    public int RecentVerbatimTurns { get; init; } = 10;

    /// <summary>
    /// Exclusive upper bound (from the end of the conversation) on the mid-distance
    /// compression window. Spec FR-44 binding: "turns 10-50 as summary blocks".
    /// Together with <see cref="RecentVerbatimTurns"/>, defines the window
    /// <c>[length - MidWindowEnd, length - RecentVerbatimTurns)</c> that is fed to
    /// <see cref="ISummarizationCompressionService"/>. Range [11, 200].
    /// </summary>
    [Range(11, 200)]
    public int MidWindowEnd { get; init; } = 50;

    /// <summary>
    /// Maximum number of pinned items to select via similarity recall when the
    /// conversation has at least <see cref="MidWindowEnd"/> turns (representing the
    /// "retrieved old" tier per FR-44). The caller pays for K embedding lookups.
    /// Range [1, 20]; clamped at use-site.
    /// </summary>
    [Range(1, 20)]
    public int RetrievedOldTopK { get; init; } = 5;

    /// <summary>
    /// Conservative chars-per-token estimate used for budget arithmetic. Matches the
    /// <see cref="SummarizationCompressionOptions.CharsPerToken"/> default (4.0) — the
    /// shared estimate keeps the composed-block accounting consistent with the
    /// upstream compression output.
    /// </summary>
    [Range(1.0, 10.0)]
    public double CharsPerToken { get; init; } = 4.0;

    /// <summary>
    /// Maximum tokens the compression sub-call is allowed to consume for the
    /// mid-distance summary. Passed verbatim to
    /// <see cref="ISummarizationCompressionService.CompressAsync"/>. The service
    /// internally clamps to [128, 1024]; this option lets ops re-target the budget
    /// without touching code. Range [128, 1024].
    /// </summary>
    [Range(128, 1024)]
    public int CompressedMidMaxTokens { get; init; } = 512;
}
