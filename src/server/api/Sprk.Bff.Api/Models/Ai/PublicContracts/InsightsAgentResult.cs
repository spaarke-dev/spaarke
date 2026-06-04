using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Result returned from <see cref="Services.Ai.PublicContracts.IInsightsAi.AnswerQuestionAsync"/>.
/// Carries exactly one of <see cref="Artifact"/> (success path) or <see cref="Decline"/>
/// (structured decline per D-49), never both, never neither.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — both <see cref="InsightArtifact"/> and
/// <see cref="DeclineResponse"/> live in <c>Models/Insights/</c> which is Zone B, so
/// the D-P15 endpoint can deserialise + serialise this shape without importing any
/// AI-internal types.
/// </para>
/// <para>
/// <b>CacheHit and ProcessingTimeMs</b> are diagnostic fields the endpoint may use
/// to populate response headers (e.g., <c>x-insights-cache</c>, <c>x-insights-elapsed-ms</c>)
/// for client-side observability and warm-vs-cold characterisation in load tests.
/// </para>
/// <para>
/// Use <see cref="Success(InsightArtifact, bool, long)"/> and
/// <see cref="Declined(DeclineResponse, bool, long)"/> factories rather than the
/// raw constructor so the "exactly one of" invariant is enforced at the call site.
/// </para>
/// </remarks>
public sealed record InsightsAgentResult
{
    /// <summary>The synthesized artifact when the playbook succeeded. Null on decline.</summary>
    public InsightArtifact? Artifact { get; init; }

    /// <summary>The structured decline when the playbook took the decline path. Null on success.</summary>
    public DeclineResponse? Decline { get; init; }

    /// <summary>True if the artifact was served from the D-P13 cache; false on engine invocation.
    /// Always false on the decline path (declines are not cached).</summary>
    public bool CacheHit { get; init; }

    /// <summary>Wall-clock processing time in milliseconds, measured by the orchestrator
    /// from request acceptance through result production. Includes cache lookup and engine
    /// invocation time but excludes endpoint serialisation overhead.</summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>Construct a success result carrying an <see cref="InsightArtifact"/>.</summary>
    public static InsightsAgentResult Success(InsightArtifact artifact, bool cacheHit, long processingTimeMs)
        => new()
        {
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact)),
            Decline = null,
            CacheHit = cacheHit,
            ProcessingTimeMs = processingTimeMs
        };

    /// <summary>Construct a decline result carrying a <see cref="DeclineResponse"/>.</summary>
    public static InsightsAgentResult Declined(DeclineResponse decline, bool cacheHit, long processingTimeMs)
        => new()
        {
            Artifact = null,
            Decline = decline ?? throw new ArgumentNullException(nameof(decline)),
            CacheHit = cacheHit,
            ProcessingTimeMs = processingTimeMs
        };
}
