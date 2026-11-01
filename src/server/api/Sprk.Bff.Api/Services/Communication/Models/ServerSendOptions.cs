namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The server-only options of a send-as-user email (ebilling-r1 task 151c-1a, EB-D-182 (1)): set by server code on
/// <see cref="SendCommunicationRequest.ServerSend"/>; absent on every other send, which is then unchanged. Used by Email to AP, which
/// mails a batch's stored CSV and JSON files.
/// </summary>
/// <remarks>
/// <para>When it is set, <see cref="CommunicationService"/>:</para>
/// <list type="number">
/// <item>attaches <see cref="Attachments"/> to the message, after the document attachments (<c>AttachmentDocumentIds</c>), under the path's
/// existing count and total-size limits applied to the combined list. The bytes go to the mail channel ONLY: no <c>sprk_document</c>,
/// <c>sprk_communicationattachment</c> or archived copy is written for them;</item>
/// <item>resolves the communication's owner as a CONTENT row of <see cref="OwnerFollows"/> through the one ownership resolver (the
/// record's team), and never leaves the default owner; a record whose owner cannot be resolved refuses before anything is sent, and so
/// does an answer that is not <see cref="ServerOwnerRecord.ExpectedOwnerTeamId"/> (the team that owns the record: the resolver answers
/// a team-owned parent's business-unit default team, which need not be the record's own owner);</item>
/// <item>does NOT run communication enrichment when <see cref="SkipEnrichment"/> is set or <see cref="OwnerFollows"/> is set (no AI
/// indexing, triage, proposals, and no category routing, which would re-assign the owner above);</item>
/// <item>sends the signed <see cref="ServerSendMarker"/> header when there are <see cref="Attachments"/> and a signing key, so the inbound
/// mailbox path does not file the attachments or archive the message again.</item>
/// </list>
/// <para>Only the send-as-user email path accepts the option, and not with <c>ArchiveToSpe</c>: any other send that carries it is refused
/// before anything is sent (the archive would copy the attachments into the unit's container).</para>
/// <para>Internal and never JSON-bound: only server code can set it. A client that could set it could attach bytes of its choosing
/// or choose the owner of the record.</para>
/// </remarks>
internal sealed record ServerSendOptions
{
    /// <summary>Files attached from memory (name, content type, bytes).</summary>
    public IReadOnlyList<ServerAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// The record the communication's owner follows (for example <c>sprk_billingexport</c> and the batch id). Null: the owner is decided
    /// as for any send.
    /// </summary>
    public ServerOwnerRecord? OwnerFollows { get; init; }

    /// <summary>True: the send is not enriched.</summary>
    public bool SkipEnrichment { get; init; }
}

/// <summary>One file attached from memory by server code.</summary>
internal sealed record ServerAttachment(string Name, string ContentType, byte[] Content);

/// <summary>
/// The record a communication's owner follows, and the team that owns it (read by the caller from the record). The resolver's answer must
/// equal <paramref name="ExpectedOwnerTeamId"/>: this is a check, never a computed owner.
/// </summary>
internal sealed record ServerOwnerRecord(string EntityLogicalName, Guid RecordId, Guid ExpectedOwnerTeamId)
{
    /// <summary>The refusal code when the resolver names a team other than the record's owner.</summary>
    public const string OwnerMismatchCode = "server_send_owner_mismatch";
}
