namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The message-grain address set handed to <see cref="CommunicationParticipantIndexer"/> at the persist
/// point (outbound send + inbound capture) to write the queryable participant index (FR-08 / ADR-048).
/// One junction row is produced per (message × person/address × role {From,To,Cc,Bcc}).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FromResolved"/> lets a caller that ALREADY holds the sender's typed identity (e.g. the
/// outbound ACS MESSAGE path resolves its sender to a <see cref="ParticipantReference.SystemUser"/> from
/// the authenticated <c>oid</c>) supply it directly, so the indexer sets <c>sprk_systemuser</c> WITHOUT a
/// redundant lookup. Every other address is resolved by REUSING the existing email→contact resolver
/// (<c>ICommunicationDataverseService.QueryContactByEmailAsync</c>) — no second resolver (ADR-048 rule 5).
/// </para>
/// </remarks>
public sealed class CommunicationParticipantSet
{
    /// <summary>The sender address (role = From). May be an email or a channel identifier (e.g. an ACS MRI).</summary>
    public string? FromAddress { get; init; }

    /// <summary>
    /// A pre-resolved typed identity for <see cref="FromAddress"/> when the caller already holds it
    /// (e.g. the ACS message sender resolved to a <c>systemuser</c>). When null the From address is resolved
    /// via the shared email→contact resolver like every other address.
    /// </summary>
    public ParticipantReference? FromResolved { get; init; }

    /// <summary>To recipients (role = To).</summary>
    public IReadOnlyList<string>? To { get; init; }

    /// <summary>Cc recipients (role = Cc).</summary>
    public IReadOnlyList<string>? Cc { get; init; }

    /// <summary>Bcc recipients (role = Bcc).</summary>
    public IReadOnlyList<string>? Bcc { get; init; }
}
