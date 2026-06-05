using System.Threading.Channels;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R5 task 007 (D1-07) — concrete singleton that owns the in-process
/// <see cref="Channel{T}"/> bridging <see cref="ChatSessionManager"/> producers
/// to the <see cref="SessionFilesCleanupJob"/> consumer.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the canonical channel-signal pattern in
/// <c>Sprk.Bff.Api.Services.Ai.PlaybookEmbedding.PlaybookIndexingService</c>:
/// producers call <see cref="SignalSessionEnded"/> (fire-and-forget);
/// the background service drains via <see cref="Reader"/>. The reader-side
/// is internal-by-design — only the hosted-service in this same assembly
/// reads from the channel.
/// </para>
/// <para>
/// Channel options: unbounded (session-end is a relatively rare event;
/// bounded would risk dropping signals if the cleanup loop is briefly
/// behind), single-reader (the one hosted service), multi-writer (any
/// request thread executing <see cref="ChatSessionManager.DeleteSessionAsync"/>).
/// </para>
/// <para>
/// Lifetime: singleton. Registered inside
/// <c>AnalysisServicesModule.AddAnalysisServicesModule</c> under the
/// compound AI gate so the channel is created only when AI is enabled;
/// when AI is OFF, <see cref="ChatSessionManager"/> resolves the optional
/// dependency as <c>null</c> (via
/// <see cref="System.IServiceProvider.GetService{T}()"/>) and the
/// fire-and-forget call short-circuits at the call site.
/// </para>
/// </remarks>
public sealed class SessionFilesCleanupSignal : ISessionFilesCleanupSignal
{
    private readonly Channel<SessionEndSignal> _channel =
        Channel.CreateUnbounded<SessionEndSignal>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Reader-side of the channel. Consumed by <see cref="SessionFilesCleanupJob"/>
    /// in the same assembly. Internal to enforce the single-reader invariant —
    /// outside callers MUST NOT read from this channel.
    /// </summary>
    internal ChannelReader<SessionEndSignal> Reader => _channel.Reader;

    /// <inheritdoc />
    public void SignalSessionEnded(string tenantId, string sessionId)
    {
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(sessionId))
        {
            // Per contract: silent no-op on invalid input (fire-and-forget).
            return;
        }

        // Unbounded channel — TryWrite always returns true; defensive guard
        // keeps the signature aligned with bounded-channel future variants.
        _channel.Writer.TryWrite(new SessionEndSignal(tenantId, sessionId));
    }
}

/// <summary>
/// R5 task 007 (D1-07) — in-process payload carrying the (tenantId, sessionId)
/// pair from <see cref="ChatSessionManager.DeleteSessionAsync"/> producers to
/// <see cref="SessionFilesCleanupJob"/>.
/// </summary>
/// <remarks>
/// Internal record struct — never serialised, never crosses process boundary.
/// </remarks>
internal readonly record struct SessionEndSignal(string TenantId, string SessionId);
