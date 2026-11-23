using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The outcome of the confidence→status ladder (R4 FR-11) for one communication: the chosen
/// <c>sprk_associationstatus</c>, the regarding lookups to write (empty for Ambiguous / Pending Review),
/// whether the engine auto-filed, and the full machine-readable <see cref="Provenance"/> trail.
/// Produced by <see cref="AssociationStatusMapper.Decide"/>; applied by <see cref="IncomingAssociationResolver"/>.
/// </summary>
public sealed record AssociationDecision
{
    /// <summary>Chosen status option-set integer (see <see cref="AssociationStatusCodes"/>).</summary>
    public required int Status { get; init; }

    /// <summary>
    /// Regarding lookups to write (field name → typed target). Non-empty only for
    /// <see cref="AssociationStatusCodes.Resolved"/> and <see cref="AssociationStatusCodes.Suggested"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, EntityReference> RegardingWrites { get; init; }

    /// <summary><c>true</c> only when the engine auto-filed (deterministic ≥ threshold, kill-switch on).</summary>
    public required bool AutoFiled { get; init; }

    /// <summary>Full provenance trail, serialized to <c>sprk_associationprovenance</c> JSON by the resolver.</summary>
    public required AssociationProvenance Provenance { get; init; }

    /// <summary>
    /// The regarding field that is the row's denormalized PRIMARY (<c>sprk_regardingrecordid</c> / name / type), when a
    /// caller decided it; <see langword="null"/> (the default) uses the global priority order
    /// (<see cref="RegardingFieldMap.All"/>). Honoured only when that field is among <see cref="RegardingWrites"/>.
    /// Ontology task 176 (review round 2, item 4): a reply to an inquiry keeps the service request as its primary, as the
    /// inquiry's send does, so the next reply's footer still names the inquiry. §11: existing — the priority pick in
    /// <c>IncomingAssociationResolver.PrimaryRegarding</c>; extension — a per-decision preference on it rather than a
    /// reordered global list; cost of doing nothing — the reply's primary becomes the matter and the inquiry rule is lost.
    /// </summary>
    public string? PrimaryRegardingField { get; init; }
}
