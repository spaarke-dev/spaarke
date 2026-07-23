using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Reads the Microsoft Graph mail-folder <c>messages/delta</c> query using the app-only client from
/// <see cref="IGraphClientFactory"/> (ADR-028 central auth — never self-builds a credential). Mirrors
/// <c>SpeFileStore.EnumerateDriveDeltaAsync</c> for the mail-folder <c>messages</c> resource: an
/// initial <c>messages/delta</c> call, then replay of the stored <c>@odata.deltaLink</c>, paging
/// through <c>@odata.nextLink</c> until a terminal <c>@odata.deltaLink</c> is reached.
///
/// This class is the ONLY Graph-touching type in the delta path — it returns primitive DTOs
/// (<see cref="MailFolderDeltaResult"/>) so <see cref="MailboxDeltaReconciliationService"/> is free of
/// Microsoft.Graph references and unit-testable without mocking sealed Graph SDK types. Registered as
/// a concrete singleton per ADR-010 (DI minimalism — no interface); <see cref="QueryDeltaAsync"/> is
/// <c>virtual</c> so tests can substitute it via <c>Mock&lt;GraphMailFolderDeltaReader&gt;</c>
/// (the same seam pattern as <c>JobSubmissionService</c>).
/// </summary>
public class GraphMailFolderDeltaReader
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<GraphMailFolderDeltaReader> _logger;

    public GraphMailFolderDeltaReader(
        IGraphClientFactory graphClientFactory,
        ILogger<GraphMailFolderDeltaReader> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs a delta query over the given mailbox folder. When <paramref name="deltaLink"/> is null a
    /// full delta is performed; otherwise the opaque cursor is replayed to return only changes since
    /// the last run. Pages through <c>@odata.nextLink</c> until the terminal <c>@odata.deltaLink</c>.
    /// Virtual to support unit-test substitution via <c>Mock&lt;GraphMailFolderDeltaReader&gt;</c>.
    /// </summary>
    /// <param name="mailboxEmail">Mailbox (user principal name / email) to query.</param>
    /// <param name="monitorFolder">Mail folder id or well-known name (e.g., "Inbox").</param>
    /// <param name="deltaLink">Opaque cursor from a prior round, or null for a full delta.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task<MailFolderDeltaResult> QueryDeltaAsync(
        string mailboxEmail, string monitorFolder, string? deltaLink, CancellationToken ct = default)
    {
        var graph = _graphClientFactory.ForApp();
        var messages = new List<DeltaMessage>();

        // Initial call: /users/{id}/mailFolders/{folder}/messages/delta. Subsequent rounds replay the
        // stored @odata.deltaLink. Page through @odata.nextLink until a terminal @odata.deltaLink.
        var response = deltaLink is null
            ? await graph.Users[mailboxEmail].MailFolders[monitorFolder].Messages.Delta
                .GetAsDeltaGetResponseAsync(config =>
                {
                    config.QueryParameters.Select = new[] { "id", "internetMessageId", "subject" };
                }, ct).ConfigureAwait(false)
            : await graph.Users[mailboxEmail].MailFolders[monitorFolder].Messages.Delta
                .WithUrl(deltaLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);

        string? advancedDeltaLink = null;

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var message in response.Value)
                {
                    if (string.IsNullOrEmpty(message.Id))
                        continue;

                    // Removed items in a delta response carry an "@removed" facet in AdditionalData.
                    var isRemoved = message.AdditionalData is not null
                        && message.AdditionalData.ContainsKey("@removed");

                    messages.Add(new DeltaMessage(
                        MessageId: message.Id!,
                        InternetMessageId: message.InternetMessageId,
                        Subject: message.Subject,
                        IsRemoved: isRemoved));
                }
            }

            if (!string.IsNullOrEmpty(response.OdataDeltaLink))
            {
                advancedDeltaLink = response.OdataDeltaLink;
                break;
            }

            if (string.IsNullOrEmpty(response.OdataNextLink))
                break;

            response = await graph.Users[mailboxEmail].MailFolders[monitorFolder].Messages.Delta
                .WithUrl(response.OdataNextLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Mail-folder delta for {Mailbox}/{Folder}: {Count} changes, cursor {Cursor}",
            mailboxEmail, monitorFolder, messages.Count, advancedDeltaLink is null ? "unchanged" : "advanced");

        return new MailFolderDeltaResult(messages, advancedDeltaLink);
    }
}

/// <summary>Result of a mail-folder delta query.</summary>
/// <param name="Messages">Changed messages discovered in this delta round.</param>
/// <param name="DeltaLink">Opaque <c>@odata.deltaLink</c> cursor for the next round (null if unchanged).</param>
public sealed record MailFolderDeltaResult(
    IReadOnlyList<DeltaMessage> Messages,
    string? DeltaLink);

/// <summary>A single message surfaced by a delta query.</summary>
/// <param name="MessageId">Graph message id (used to build the resource path + idempotency key).</param>
/// <param name="InternetMessageId">RFC 5322 Message-Id, when selected (secondary idempotency signal).</param>
/// <param name="Subject">Subject line (telemetry only).</param>
/// <param name="IsRemoved">True when the delta entry represents a removed/deleted message.</param>
public sealed record DeltaMessage(
    string MessageId,
    string? InternetMessageId,
    string? Subject,
    bool IsRemoved);
