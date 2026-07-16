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
}
