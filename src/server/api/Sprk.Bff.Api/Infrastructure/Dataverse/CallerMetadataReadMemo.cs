using System.Collections.Concurrent;
using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The caller's Dataverse for ONE record create's caller checks (RecordCreationService step 5), with each table-metadata
/// read (<c>EntityDefinitions(…)</c>) asked once: a later identical read is answered with the first successful answer
/// (field-mapping-server-write-path-r1 task 049, NFR-02 Part C).
/// </summary>
/// <remarks>
/// <para><b>What is remembered.</b> Only a SUCCESSFUL answer to a GET whose path starts with <c>EntityDefinitions(</c> — a
/// table's privileges' names, its columns, its relationships, its entity set, its ownership type: data about the schema, read
/// under the caller's token. A failure is never remembered, so the next identical read asks again, as before. Nothing else is
/// remembered by THIS memo: <c>RetrievePrincipalAccess</c>, <c>RetrieveUserSetOfPrivilegesByNames</c>, record reads and every other
/// caller question go on to the client it wraps (ADR-009: this memo caches no authorization answer; inside a decision commit that client may be
/// the <see cref="Sprk.Bff.Api.Services.Signals.DecisionAccessWindow"/> view, which answers some of them within its own rules: one request, record access per step). A write (POST, PATCH,
/// DELETE) passes through; a memo of its own then forgets everything remembered (a request-scoped one keeps it, below).</para>
/// <para><b>For how long, for whom.</b> One instance wraps one caller's client for one create's step 5, and (task 049c) a
/// second instance for the same create's steps 2 and 3 (the caller's payload, then the merge of the mapped values, which
/// re-read the table's relationships and the entity sets step 2 read); each is then dropped (locals of
/// <c>RecordCreationService.DecideAsync</c>). A create that does not go through <c>ChildRecordEndpoints.CreateAsync</c> (a job, any other off-route create) keeps its own
/// answers: nothing is shared across those groups, creates, requests or callers. On that path (ISS-010, #3184), which the
/// decision commit from the MCP complete-decision tool also takes (<c>DecisionRouteCores.CreateChildAsync</c>), the instances are built
/// with <see cref="For"/> over the request's <see cref="RequestScope"/> (when the client is the <see cref="DataverseUserClient"/>), so they share one store for that request,
/// across the groups and across every create the request runs (the decision commit runs four, each with its own transient client); see
/// <see cref="RequestScope"/> for that contract. The
/// other steps (3½, the pre-write rules, 6 to 10) keep the caller's own client, so a read they make is asked as before (a
/// stamp bind whose metadata read fails still fails), and a pre-write rule still receives the caller's own client. Each hit returns its own freshly parsed body
/// (a parsed body is not shared across threads), and the store is concurrent, because the caller checks ask in parallel
/// (task 049 Part A).</para>
/// <para><b>Repeats it removes</b> (measured from the source): a mapped create's step 5 asks the same table's privilege
/// names and each organization-owned target's ownership metadata once per pass (the caller's own values, then the merged
/// row, and on a failure each value, then the row as written), and a re-map within it re-reads the table's relationships
/// and the related tables' entity sets.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: <see cref="CallerWhoAmIMemo"/> (the caller's
/// <c>WhoAmI</c> only, per request, in <c>HttpContext.Items</c>) and <c>Spaarke.Core.Cache.RequestCache</c> (scoped, not
/// thread-safe, and absent when the service runs off a request, a job for one). Extension: neither can hold this
/// without becoming thread-safe and request-bound, and the create service runs off-request too; this is a decorator with
/// no DI registration. Cost of doing nothing: each repeat is a sequential Dataverse round trip (about 80 ms live) inside
/// the create's NFR-02 budget.</para>
/// </remarks>
internal sealed class CallerMetadataReadMemo : IDataverseUserClient
{
    private readonly IDataverseUserClient inner;
    private readonly ConcurrentDictionary<string, (int StatusCode, string? RawJson)> _answers;
    private readonly bool _forgetOnWrite;

    /// <summary>A memo of its own: answers kept by this instance only, and forgotten on any write (the per-create behaviour).</summary>
    public CallerMetadataReadMemo(IDataverseUserClient inner)
        : this(inner, new ConcurrentDictionary<string, (int StatusCode, string? RawJson)>(StringComparer.Ordinal), forgetOnWrite: true)
    {
    }

    private CallerMetadataReadMemo(
        IDataverseUserClient inner, ConcurrentDictionary<string, (int StatusCode, string? RawJson)> answers, bool forgetOnWrite)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _answers = answers;
        _forgetOnWrite = forgetOnWrite;
    }

    /// <summary>
    /// The memo a create's caller checks use: over <paramref name="requestReads"/>'s store when the create runs in a request that
    /// carries one (the child-records route, and so the decision commit's in-process creates) AND <paramref name="inner"/> is
    /// the <see cref="DataverseUserClient"/> itself, so every create of that request shares one answer per metadata path and a
    /// row write forgets nothing; otherwise a memo of its own, exactly as before (a job, every create that does not go through
    /// <c>ChildRecordEndpoints.CreateAsync</c>, and any other <see cref="IDataverseUserClient"/> even inside a request).
    /// </summary>
    /// <remarks>ISS-010 part C: an <see cref="ICallerClientView"/> (the decision commit's access-window view) counts as the
    /// <see cref="DataverseUserClient"/> only when the client it wraps IS the <see cref="DataverseUserClient"/>: the gate checks
    /// the wrapped type, not who built the view.</remarks>
    internal static CallerMetadataReadMemo For(IDataverseUserClient inner, RequestScope? requestReads) =>
        requestReads is not null
        && (inner is DataverseUserClient || (inner is ICallerClientView view && view.Inner is DataverseUserClient))
            ? new CallerMetadataReadMemo(inner, requestReads.Store, forgetOnWrite: false)
            : new CallerMetadataReadMemo(inner);

    /// <summary>A write passes through; a memo of its own then forgets everything (a request-scoped one keeps the schema it read).</summary>
    private void Forget()
    {
        if (_forgetOnWrite)
            _answers.Clear();
    }

    /// <summary>
    /// ISS-010 (#3184): ONE request's table-metadata answers, carried in that request's <see cref="HttpContext.Items"/> (the
    /// <see cref="CallerWhoAmIMemo"/> pattern), so a request that runs several creates (the decision commit: four creates,
    /// each checked in two passes) reads each <c>EntityDefinitions(…)</c> path once.
    /// </summary>
    /// <remarks>
    /// <para><b>Why one store per request, not per client, and why only for <see cref="DataverseUserClient"/>.</b> In production
    /// the caller's client is a transient typed <c>HttpClient</c> (<c>AddHttpClient&lt;IDataverseUserClient, DataverseUserClient&gt;</c>),
    /// so a request that runs four creates resolves four clients; a store keyed by client instance would never be shared. The
    /// store is shared on this argument: every <see cref="DataverseUserClient"/> of one request takes the SAME caller bearer
    /// token from <c>IHttpContextAccessor</c> (the request's own <c>Authorization</c> header), exchanges it on behalf of that
    /// caller, and sends it as <c>Authorization: Bearer</c>; the type has no <c>MSCRMCallerID</c>, no impersonation and no
    /// app-only path. So one request means one caller identity for everything the type sends, and a table's metadata read under
    /// it is the same answer every time. The share is therefore gated on the concrete type
    /// (<see cref="For"/>): any other <see cref="IDataverseUserClient"/> (a test double, a later token-based or impersonating
    /// client, a chat or job client that happens to run inside a request) keeps its own per-create memo and never touches this
    /// store, because nothing says it speaks as the request's caller. This store wraps only the caller's client, never an
    /// app-only one; a system-principal or no-user create has no caller client and gets no memo, as before.</para>
    /// <para><b>Contract change.</b> Before, every memo instance kept its own answers for one step of one create and forgot them
    /// on any write. A memo built over this scope shares its answers with every other memo of the same request, and a row write
    /// (POST, PATCH, DELETE) forgets nothing: a row write does not change a table's schema. What
    /// is kept is unchanged: only a SUCCESSFUL answer to a GET whose path starts with <c>EntityDefinitions(</c> (its
    /// <c>/Attributes</c> and other sub-paths included). Never a row, a record-type catalog row (<c>sprk_recordtype_refs</c>), an
    /// access answer (<c>RetrievePrincipalAccess</c>, <c>RetrieveUserSetOfPrivilegesByNames</c>) or a failure: this scope keeps none of them
    /// (ADR-009: this memo caches no authorization answer; inside a decision commit the client it wraps may be the <see cref="Sprk.Bff.Api.Services.Signals.DecisionAccessWindow"/>
    /// view, which may answer them within its own rules: one request, record access per step). The store lives and dies with the request: nothing is
    /// shared across requests, and there is no process-wide cache.</para>
    /// <para><b>Who gets it.</b> Only a create whose request names it (<c>RecordCreationRequest.MetadataReads</c>), which
    /// <c>ChildRecordEndpoints.CreateAsync</c> sets from its own request: the shared store applies to every create that goes
    /// through it, including the decision commit from the MCP complete-decision tool (via <c>DecisionRouteCores.CreateChildAsync</c>).
    /// Any create that does not go through <c>ChildRecordEndpoints.CreateAsync</c> (a job, any other off-route create) names none
    /// and keeps the per-create memo.</para>
    /// <para><b>Component justification (CLAUDE.md §11).</b> Existing: this memo (one step of one create) and
    /// <see cref="CallerWhoAmIMemo"/> (the caller's <c>WhoAmI</c> per request, static over <c>HttpContext.Items</c>). Extension:
    /// this IS the extension of the memo, scoped to a request instead of a step; the WhoAmI memo holds one keyed answer per
    /// credential and cannot hold a store of answers per path. Cost of doing nothing: the four-action decision reads the same
    /// table metadata 37 times over 21 distinct paths (ISS-010 profile, over the real transient client: 34 with a store per client instance, 25 with this scope), each a sequential round trip of about 90 ms
    /// live inside a commit already over its 15 s target.</para>
    /// </remarks>
    internal sealed class RequestScope
    {
        /// <summary>The <see cref="HttpContext.Items"/> key; a private object, so no other code can reach it.</summary>
        private static readonly object ItemsKey = new();

        private RequestScope()
        {
        }

        /// <summary>
        /// The scope of <paramref name="request"/>, created on first use; <see langword="null"/> without a request or once it
        /// has ended (then the create keeps the per-create memo). Locks the request, then its <c>Items</c>, in the order
        /// <see cref="CallerWhoAmIMemo"/> does (#2851: the <c>Items</c> getter is lazy and unsynchronised).
        /// </summary>
        internal static RequestScope? Of(HttpContext? request)
        {
            if (request is null)
                return null;

            try
            {
                lock (request)
                {
                    var items = request.Items;
                    lock (items)
                    {
                        if (items.TryGetValue(ItemsKey, out var known) && known is RequestScope scope)
                            return scope;
                        var created = new RequestScope();
                        items[ItemsKey] = created;
                        return created;
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or NullReferenceException)
            {
                return null; // an ended request: no memo, every read asked as before
            }
        }

        /// <summary>The request's store; handed out only by <see cref="For"/>, and only for a <see cref="DataverseUserClient"/>.</summary>
        internal ConcurrentDictionary<string, (int StatusCode, string? RawJson)> Store { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Whether a GET path reads table metadata (and so may be remembered).</summary>
    internal static bool IsMetadataRead(string relativePath) =>
        relativePath.TrimStart('/').StartsWith("EntityDefinitions(", StringComparison.Ordinal);

    public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!IsMetadataRead(relativePath))
            return await inner.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);

        if (_answers.TryGetValue(relativePath, out var known))
            return Answer(known);

        var response = await inner.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
            _answers.TryAdd(relativePath, (response.StatusCode, response.Body?.GetRawText()));
        return response;
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
    {
        Forget();
        return inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PostAsync(
        string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
    {
        Forget();
        return inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
    {
        Forget();
        return inner.PatchAsync(relativePath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(
        string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
    {
        Forget();
        return inner.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);
    }

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        Forget();
        return inner.DeleteAsync(relativePath, cancellationToken);
    }

    private static DataverseUserResponse Answer((int StatusCode, string? RawJson) known)
    {
        if (known.RawJson is null)
            return DataverseUserResponse.Ok(known.StatusCode, body: null);

        using var document = JsonDocument.Parse(known.RawJson);
        return DataverseUserResponse.Ok(known.StatusCode, document.RootElement.Clone());
    }
}
