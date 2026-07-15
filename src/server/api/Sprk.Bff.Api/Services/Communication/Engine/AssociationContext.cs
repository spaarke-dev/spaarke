using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Ambient context passed to each <see cref="IAssociationRung"/> alongside the normalized envelope.
/// Carries channel/mailbox context that is NOT part of the message itself. Deliberately minimal in
/// task 011 (the current rungs use only the envelope); it exists so later rungs (e.g. mailbox-scoped
/// or direction-aware correlation in tasks 012/013) can extend context WITHOUT changing the rung
/// interface signature.
/// </summary>
public sealed record AssociationContext
{
    /// <summary>
    /// The communication account (shared mailbox) the message was received on, when known. Currently
    /// unused by the deterministic rungs (the inbound path has no mailbox-context fallback rung);
    /// available for future direction/mailbox-aware rungs.
    /// </summary>
    public CommunicationAccount? Account { get; init; }
}
