using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Ambient context passed to each <see cref="IAssociationRung"/> alongside the normalized envelope.
/// Carries channel/mailbox/caller context that is NOT part of the message itself, so rungs stay pure
/// over the envelope while still seeing authoritative caller signals (e.g. an outbound send request's
/// regarding, or the add-in save-pane selection).
/// </summary>
public sealed record AssociationContext
{
    /// <summary>
    /// The communication account (shared mailbox) the message was received on, when known. Currently
    /// unused by the deterministic rungs (the inbound path has no mailbox-context fallback rung);
    /// available for future direction/mailbox-aware rungs.
    /// </summary>
    public CommunicationAccount? Account { get; init; }

    /// <summary>
    /// Caller-supplied regarding associations (rung 0, highest determinism): the authoritative
    /// entities the caller explicitly linked — the outbound send request's <c>Associations</c>, or the
    /// Outlook add-in save-pane selection. Empty on the plain inbound path (no caller signal).
    /// </summary>
    public IReadOnlyList<CommunicationAssociation> CallerSuppliedRegarding { get; init; } =
        Array.Empty<CommunicationAssociation>();
}
