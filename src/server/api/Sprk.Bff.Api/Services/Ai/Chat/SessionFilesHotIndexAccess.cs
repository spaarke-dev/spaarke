using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// spaarkeai-compose-r8 FR-B03 (task 061) — the complete, closed set of things
/// <see cref="SessionFilesCleanupJob"/> is allowed to touch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> FR-B03 requires that the 24h cleanup sweep evict the HOT INDEX ONLY and
/// that it never delete durable bytes. Before task 061 the job held an <see cref="IServiceProvider"/> and
/// resolved its collaborators through it on every pass. That satisfied FR-B03 only by the fact that nobody
/// had yet typed <c>GetService&lt;SessionFileBlobStore&gt;()</c> — an ambient service locator inside a
/// component whose job is to DELETE things is a reach, not a boundary. It is about to matter: task 063
/// (GDPR erasure) adds the first delete surface to the durable store, at which point "the store has no
/// delete method" stops being the second line of defence.
/// </para>
/// <para>
/// <b>The property this buys.</b> After the job's constructor returns, no field, property, parameter or
/// local anywhere in <see cref="SessionFilesCleanupJob"/> has a type from which a
/// <c>SessionFileBlobStore</c> — or any other service — can be obtained. The job can reach exactly two
/// things, and both of them are the hot tier:
/// <list type="bullet">
///   <item><see cref="HotIndex"/> — the <c>spaarke-session-files</c> Azure AI Search index. This is the
///     ONLY delete target in the whole component.</item>
///   <item><see cref="ActiveSessionKeys"/> — READ-ONLY Redis key-existence probes that decide which
///     sessions are orphaned. The job never writes or deletes a Redis key.</item>
/// </list>
/// This is enforced by <c>tests/Spaarke.ArchTests/SessionFilesCleanupScopeTests.cs</c>, which fails the
/// build if the job ever regains a service-locator dependency or takes an IL-level dependency on the
/// durable store or on <c>Azure.Storage.Blobs</c>.
/// </para>
/// <para>
/// <b>Resolution is eager and total.</b> Every dependency is resolved once, here, with
/// <see cref="ServiceProviderServiceExtensions.GetService{T}(IServiceProvider)"/> — never
/// <c>GetRequiredService</c> — and a failing factory degrades to <c>null</c> rather than propagating.
/// The job is a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> constructed during host
/// start, so a throw here would take the whole API down over a background sweep. A null collaborator
/// makes the sweep a logged no-op, which is the behaviour the job already had for the
/// not-registered case.
/// </para>
/// </remarks>
internal sealed class SessionFilesHotIndexAccess
{
    private SessionFilesHotIndexAccess(
        SearchClient? hotIndex,
        IConnectionMultiplexer? activeSessionKeys,
        string indexName)
    {
        HotIndex = hotIndex;
        ActiveSessionKeys = activeSessionKeys;
        IndexName = indexName;
    }

    /// <summary>
    /// The session-files AI Search index — the hot tier, and the ONLY store this component may delete
    /// from. <c>null</c> when AI Search is not configured for this deployment (the sweep then no-ops).
    /// </summary>
    public SearchClient? HotIndex { get; }

    /// <summary>
    /// Redis, used for <see cref="IDatabase.KeyExistsAsync"/> probes ONLY — the job reads key existence
    /// to decide which indexed sessions are orphaned and never mutates Redis. <c>null</c> when Redis is
    /// not registered (the scheduled orphan scan then no-ops; the on-session-end signal path is
    /// unaffected).
    /// </summary>
    public IConnectionMultiplexer? ActiveSessionKeys { get; }

    /// <summary>Resolved index name, retained for logging only.</summary>
    public string IndexName { get; }

    /// <summary>
    /// Resolves the closed set from the container ONCE, at construction of the hosted service.
    /// </summary>
    /// <remarks>
    /// The <see cref="IServiceProvider"/> is consumed here and deliberately NOT retained by either this
    /// type or its caller — that non-retention is the whole point (see the type remarks).
    /// </remarks>
    public static SessionFilesHotIndexAccess Resolve(IServiceProvider serviceProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var indexName = TryResolve<IOptions<AiSearchOptions>>(serviceProvider, logger, nameof(AiSearchOptions))
            ?.Value?.SessionFilesIndexName;

        if (string.IsNullOrWhiteSpace(indexName))
        {
            indexName = new AiSearchOptions().SessionFilesIndexName;
        }

        var searchIndexClient = TryResolve<SearchIndexClient>(serviceProvider, logger, nameof(SearchIndexClient));
        SearchClient? hotIndex = null;
        if (searchIndexClient is not null)
        {
            try
            {
                hotIndex = searchIndexClient.GetSearchClient(indexName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "SessionFilesCleanupJob: could not bind the session-files search client for index " +
                    "{IndexName} — the hot-index sweep will no-op for this process.", indexName);
            }
        }

        var multiplexer = TryResolve<IConnectionMultiplexer>(serviceProvider, logger, nameof(IConnectionMultiplexer));

        return new SessionFilesHotIndexAccess(hotIndex, multiplexer, indexName);
    }

    /// <summary>Test seam: build the closed set directly, without a container.</summary>
    internal static SessionFilesHotIndexAccess ForTests(
        SearchClient? hotIndex,
        IConnectionMultiplexer? activeSessionKeys,
        string indexName)
        => new(hotIndex, activeSessionKeys, indexName);

    private static T? TryResolve<T>(IServiceProvider serviceProvider, ILogger logger, string label)
        where T : class
    {
        try
        {
            return serviceProvider.GetService<T>();
        }
        catch (Exception ex)
        {
            // A throwing registration factory must not take the host down over a background sweep.
            logger.LogWarning(ex,
                "SessionFilesCleanupJob: {Dependency} could not be resolved — the affected cleanup pass " +
                "will no-op for this process.", label);
            return null;
        }
    }
}
