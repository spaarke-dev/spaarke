using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Capturing <see cref="IChatDataverseRepository"/> double shared by the analysis promote and review-memo contract
/// tests. Records the sessions created, archived and bound, and serves seeded sessions from the cold (repo) read
/// path. <see cref="FailOnCreate"/> and <see cref="FailOnBind"/> simulate catastrophic failures so compensation
/// paths can be exercised. (Moved here unchanged by unified-access-control-r2 task 162 when the fork endpoint and
/// its contract tests, where it used to live, were deleted.)
/// </summary>
public sealed class CapturingChatDataverseRepository : IChatDataverseRepository
{
    public List<ChatSession> Created { get; } = new();
    public List<(string TenantId, string SessionId)> Archived { get; } = new();
    public Dictionary<string, ChatSession> SessionsById { get; } = new(StringComparer.Ordinal);
    public bool FailOnCreate { get; set; }

    public Task CreateSessionAsync(ChatSession session, CancellationToken ct = default)
    {
        if (FailOnCreate)
        {
            // Non-InvalidOperationException so ChatSessionManager.CreateSessionAsync does NOT swallow
            // it (that catch is reserved for the tolerated Dataverse-write failure) — it propagates,
            // driving the caller's compensation path.
            throw new InvalidTimeZoneException("simulated hot-path create failure (compensation test)");
        }
        Created.Add(session);
        return Task.CompletedTask;
    }

    public Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(SessionsById.TryGetValue(sessionId, out var s) ? s : null);

    public Task ArchiveSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        Archived.Add((tenantId, sessionId));
        return Task.CompletedTask;
    }

    public Task AddMessageAsync(ChatMessage message, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int maxMessages, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());

    public Task<int> GetMessageCountAsync(string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task UpdateSessionActivityAsync(string tenantId, string sessionId, int messageCount, DateTimeOffset lastActivity, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpdateSessionSummaryAsync(string tenantId, string sessionId, string summary, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AnalysisSessionSummary>> GetSessionsByAnalysisAsync(string tenantId, Guid analysisId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AnalysisSessionSummary>>(Array.Empty<AnalysisSessionSummary>());

    public List<(string TenantId, string SessionId, Guid AnalysisId)> Bound { get; } = new();
    public bool FailOnBind { get; set; }

    public Task<bool> BindSessionToAnalysisAsync(string tenantId, string sessionId, Guid analysisId, CancellationToken ct = default)
    {
        if (FailOnBind)
        {
            throw new InvalidTimeZoneException("simulated bind failure (promote compensation test)");
        }
        Bound.Add((tenantId, sessionId, analysisId));
        return Task.FromResult(true);
    }
}
