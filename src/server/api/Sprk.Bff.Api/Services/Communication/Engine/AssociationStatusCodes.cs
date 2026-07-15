namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// <c>sprk_associationstatus</c> option-set integers (R4 W0 task 002, verified via Dataverse MCP;
/// see <c>docs/data-model/sprk_communication.md</c>). Centralized so the mapper, resolver, and tests
/// share one source of truth for the ladder codes.
/// </summary>
public static class AssociationStatusCodes
{
    /// <summary>Auto-filed: a deterministic ≥ threshold match was written as the regarding association.</summary>
    public const int Resolved = 100000000;

    /// <summary>No match (or below the suggest floor): surfaced for manual review; no regarding written.</summary>
    public const int PendingReview = 100000001;

    /// <summary>Legacy value retained from pre-R4 data; the R4 engine treats it as equivalent to <see cref="PendingReview"/>. Never written by R4.</summary>
    public const int Unresolved = 100000002;

    /// <summary>Suggest-only: a regarding was proposed but not auto-filed (mid confidence, AI-involved, or kill-switch off). Awaits human confirmation.</summary>
    public const int Suggested = 100000003;

    /// <summary>Conflicting high-confidence matches on the same field: the engine will not guess; no regarding written.</summary>
    public const int Ambiguous = 100000004;

    /// <summary>Human-readable label for a status code (provenance + logging).</summary>
    public static string Name(int code) => code switch
    {
        Resolved => "Resolved",
        PendingReview => "Pending Review",
        Unresolved => "Unresolved",
        Suggested => "Suggested",
        Ambiguous => "Ambiguous",
        _ => $"Unknown({code})",
    };
}
