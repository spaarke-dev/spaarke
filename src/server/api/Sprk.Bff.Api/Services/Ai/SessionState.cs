using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Current session state for the Playbook Builder conversational surface.
/// Carried on <c>BuilderSseEvents.DoneEvent.SessionState</c> and written by
/// <c>Infrastructure/Streaming/ServerSentEventWriter</c> so multi-turn builder
/// clients can persist canvas + plan continuity across requests.
/// </summary>
/// <remarks>
/// Relocated from the deleted engine-shell file (FR-P3-05,
/// spaarke-ai-architecture-redesign-r1 task 044) — the builder SSE surface is this
/// type's only remaining consumer.
/// </remarks>
public record SessionState
{
    /// <summary>
    /// Unique session identifier for continuity across requests.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Current canvas state (nodes and edges).
    /// </summary>
    public required CanvasState CanvasState { get; init; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the session was last active.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional build plan being executed.
    /// </summary>
    public BuildPlan? ActiveBuildPlan { get; init; }

    /// <summary>
    /// Current step in the build plan (if active).
    /// </summary>
    public int? CurrentBuildStep { get; init; }

    /// <summary>
    /// Variables accumulated during the session (e.g., scope IDs created).
    /// </summary>
    public Dictionary<string, object?>? SessionVariables { get; init; }
}
