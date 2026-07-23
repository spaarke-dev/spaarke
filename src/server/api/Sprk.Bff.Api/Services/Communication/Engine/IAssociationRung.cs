using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// One rung of the Association Engine (ADR-045 / FR-09). A rung inspects the NORMALIZED envelope
/// (never <c>Microsoft.Graph.Message</c>) and proposes zero or more <see cref="RungMatch"/> typed
/// regarding writes with per-attribute confidence + provenance. The engine evaluates rungs in
/// ascending <see cref="Order"/> and applies the matches of the FIRST rung that yields any
/// (deterministic cascade — preserves the existing thread → participant → subject precedence).
/// </summary>
public interface IAssociationRung
{
    /// <summary>The taxonomy kind of this rung (see <see cref="RungKind"/>).</summary>
    RungKind Kind { get; }

    /// <summary>
    /// Evaluation order (ascending). Governs cascade precedence independently of <see cref="Kind"/>'s
    /// numeric value, so the current inbound cascade order is preserved exactly.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Evaluates the rung over the envelope. Returns an empty list when the rung does not match.
    /// MUST be best-effort/non-fatal internally: a rung that throws is treated by the engine as a
    /// non-match (NFR-06) — association never fails the capture path.
    /// </summary>
    Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct);
}
