using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Threads;

/// <summary>
/// Per-channel resolution of the thread a message belongs to, plus any channel-specific bookkeeping when a
/// NEW thread is created. Keyed by <see cref="SupportedType"/> and resolved by <see cref="ThreadResolver"/>
/// exactly like <c>ICommunicationChannelSender</c>/<c>ICommunicationChannelIngestor</c> are resolved by
/// <see cref="Channels.CommunicationChannelDispatcher"/> (ADR-045 rule 4 / NFR-04 — adding a channel is a
/// purely additive keyed registration, with no change to the resolver dispatch site).
/// </summary>
public interface IThreadKeyStrategy
{
    /// <summary>The channel this strategy resolves threads for.</summary>
    CommunicationType SupportedType { get; }

    /// <summary>
    /// Resolves the thread disposition for a message: JOIN an existing thread, CREATE a new one, or SKIP
    /// (no groupable key). MUST be self-contained best-effort — a throw is caught by the resolver and
    /// treated as a non-fatal skip (NFR-02).
    /// </summary>
    Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct);

    /// <summary>
    /// Invoked AFTER a new thread is created for this message, to persist channel-specific linkage — chat
    /// writes the <c>sprk_communicationchannelref</c> row mapping the ACS <c>ChatThreadId</c> → the new
    /// thread. No-op for email.
    /// </summary>
    Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IThreadKeyStrategy.ResolveAsync"/>: either JOIN an existing thread, CREATE a new
/// one when absent, or SKIP entirely (the message has no groupable key — e.g. a chat message with no ACS
/// thread id — so no thread is created, avoiding an ungroupable orphan).
/// </summary>
public readonly record struct ThreadKeyResolution(Guid? ExistingThreadId, bool CreateWhenAbsent)
{
    /// <summary>Join the given existing thread.</summary>
    public static ThreadKeyResolution Join(Guid threadId) => new(threadId, false);

    /// <summary>No existing thread found — create a new one (e.g. a fresh email = a new thread).</summary>
    public static readonly ThreadKeyResolution CreateNew = new(null, true);

    /// <summary>No groupable key — do nothing (no create, no assign).</summary>
    public static readonly ThreadKeyResolution Skip = new(null, false);
}
