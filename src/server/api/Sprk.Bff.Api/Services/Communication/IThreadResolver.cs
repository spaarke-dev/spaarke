using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Direction-symmetric, channel-agnostic thread resolver (FR-06). Find-or-creates the
/// <c>sprk_communicationthread</c> grouping record for a persisted <c>sprk_communication</c> and stamps
/// the <c>sprk_communicationthread</c> lookup on it, so email reply-chains and chat conversations both
/// map onto ONE queryable thread record. Invoked from BOTH the inbound capture path AND the outbound send
/// path, for ALL channels — the thread analog of the direction-symmetric enrichment invocation (ADR-045
/// rule 3): one inbound call site (email = <see cref="IncomingCommunicationProcessor"/>, chat =
/// <see cref="Channels.MessagingIngestor"/>), one outbound (<see cref="CommunicationService"/>), one
/// channel-agnostic seam with a per-channel key strategy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Best-effort / non-fatal (NFR-02):</b> a resolve/create failure MUST NOT fail the send or
/// inbound-capture path. The implementation logs and returns <c>null</c>; the message still persists
/// WITHOUT a <c>sprk_communicationthread</c>. Call sites also treat a null return as "no thread yet".
/// </para>
/// <para>
/// <b>Anchor reuses ADR-024:</b> when a NEW thread is created, its anchor is copied from the message's
/// resolved regarding (the denormalized <c>sprk_regardingrecord*</c> family, falling back to the typed
/// <c>sprk_regarding*</c> lookups via <see cref="Engine.RegardingFieldMap"/>) — NOT a second regarding
/// mechanism.
/// </para>
/// <para><b>Point-forward only (FR-06):</b> no historical backfill of pre-existing messages.</para>
/// </remarks>
public interface IThreadResolver
{
    /// <summary>
    /// Resolves (find-or-create) the thread for the persisted communication and assigns the
    /// <c>sprk_communicationthread</c> lookup. Returns the thread id, or <c>null</c> when no thread could
    /// be resolved/created (never throws to the caller — NFR-02).
    /// </summary>
    Task<Guid?> ResolveAndAssignThreadAsync(ThreadResolutionRequest request, CancellationToken ct = default);
}

/// <summary>
/// Channel-neutral input to <see cref="IThreadResolver.ResolveAndAssignThreadAsync"/>. The resolver
/// dispatches to a per-channel key strategy by <see cref="ChannelType"/> (email = <c>sprk_inreplyto</c>
/// ancestry root; <c>Message</c> = ACS <see cref="AcsThreadId"/>).
/// </summary>
public sealed record ThreadResolutionRequest
{
    /// <summary>The already-persisted <c>sprk_communication</c> whose thread is being resolved.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>Channel dispatch key: <c>Email</c> → ancestry-root strategy; <c>Message</c> → ACS thread strategy.</summary>
    public required CommunicationType ChannelType { get; init; }

    /// <summary>Direction (recorded for symmetry/provenance; the resolver does not branch on it).</summary>
    public required CommunicationDirection Direction { get; init; }

    /// <summary>
    /// Normalized envelope — the email strategy reads <see cref="NormalizedMessage.InReplyTo"/> /
    /// <see cref="NormalizedMessage.References"/> and <see cref="NormalizedMessage.Subject"/>.
    /// </summary>
    public required NormalizedMessage Message { get; init; }

    /// <summary>
    /// ACS <c>ChatThreadId</c> for the <c>Message</c> channel — the <c>sprk_communicationchannelref</c>
    /// external key. Null for email and when unavailable (e.g. outbound chat in R1, where the authoritative
    /// ACS thread id is carried by the inbound echo path). A <c>Message</c> request with no thread id is a
    /// no-op (the resolver skips rather than creating an ungroupable thread).
    /// </summary>
    public string? AcsThreadId { get; init; }
}
