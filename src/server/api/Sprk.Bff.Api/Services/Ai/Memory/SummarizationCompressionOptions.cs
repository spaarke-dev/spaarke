using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Configuration options for <see cref="SummarizationCompressionService"/> — the R6 Pillar 7
/// sliding-window compression service that replaces the oldest M turns of a chat session with
/// an LLM-generated summary when the conversation history exceeds the system-prompt token budget.
/// </summary>
/// <remarks>
/// <para>
/// Bound from appsettings section <c>SummarizationCompression</c> via
/// <c>services.AddOptions&lt;SummarizationCompressionOptions&gt;().BindConfiguration(...)</c>
/// inside <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>.
/// </para>
/// <para>
/// <b>B-G11 hardening pattern</b>: per CLAUDE.md §10 and the
/// <see cref="Foundry.BingGroundingOptions"/> / <see cref="Foundry.AgentServiceOptions"/>
/// precedent, fields conditional on <see cref="Enabled"/> are NOT decorated with
/// <c>[Required]</c>. Validation happens at use-site (inside
/// <see cref="SummarizationCompressionService.CompressAsync"/>) so the app starts cleanly
/// when <c>Enabled=false</c> and no compression-specific config is present.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: the 8K system prompt budget is the binding ceiling. This service's
/// output (the LLM-generated summary) MUST fit a reserved ~512 token slot inside that 8K
/// budget; <see cref="MaxSummaryTokens"/> enforces that ceiling on the LLM max-output-tokens
/// hint AND on the post-call defensive truncation.
/// </para>
/// </remarks>
public sealed class SummarizationCompressionOptions
{
    /// <summary>Configuration section name used for binding.</summary>
    public const string SectionName = "SummarizationCompression";

    /// <summary>
    /// Kill switch. When <c>false</c>, <see cref="SummarizationCompressionService.CompressAsync"/>
    /// returns <c>null</c> immediately without making an LLM call — the caller (chat agent factory)
    /// short-circuits and uses the raw sliding window as-is. Default: <c>true</c> (compression on
    /// by default since the 8K budget binding REQUIRES this mechanism per NFR-10).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of tokens the LLM-generated summary is allowed to consume in the final
    /// composed system prompt. This is the hard cap passed as <c>maxOutputTokens</c> to
    /// <see cref="IOpenAiClient.GetCompletionAsync"/>, and the post-call defensive truncation
    /// ceiling. NFR-10 reserves a ~512 token slot inside the 8K system prompt budget for the
    /// rolling summary; values outside [128, 1024] are rejected by use-site validation.
    /// </summary>
    [Range(128, 1024)]
    public int MaxSummaryTokens { get; init; } = 512;

    /// <summary>
    /// Number of oldest messages to fold into a single summary when compression triggers.
    /// Lower values keep more turn-level detail at the cost of more frequent compression calls;
    /// higher values reduce LLM-call frequency but lose more fine-grained context. Use-site
    /// validation enforces a sensible range [2, 50].
    /// </summary>
    [Range(2, 50)]
    public int OldestTurnsToCompress { get; init; } = 6;

    /// <summary>
    /// Optional model override (Azure OpenAI deployment name). When <c>null</c>, the service
    /// passes <c>null</c> to <see cref="IOpenAiClient.GetCompletionAsync"/>, which falls back
    /// to the configured <c>SummarizeModel</c> deployment (cheap-tier GPT-4o-mini per
    /// <c>chat-architecture.md</c>).
    /// </summary>
    public string? ModelDeploymentOverride { get; init; }

    /// <summary>
    /// Approximate characters-per-token estimate used for input-side budget guards.
    /// Conservative default of 4.0 matches GPT-4o English-prose tokenisation.
    /// </summary>
    [Range(1.0, 10.0)]
    public double CharsPerToken { get; init; } = 4.0;
}
