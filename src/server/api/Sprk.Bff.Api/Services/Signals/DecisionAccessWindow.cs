using System.Collections.Concurrent;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// ISS-010 (#3184) part C: ONE decision commit's caller-access answers, kept per request so the commit asks Dataverse each
/// question once per step instead of once per check. Opened only by <see cref="DecisionCommitService"/> (just before FR-51) and
/// carried in the request's <see cref="HttpContext.Items"/> under a private key; readers reach it only through <see cref="Of"/>,
/// which answers null when no commit is open, the commit closed it, or the running step is suspended.
/// </summary>
/// <remarks>
/// <para><b>What it keeps (uac-r2 design, approved with amendments 2026-11-25).</b> Dataverse's own answers, never a decision:
/// each caller still runs its own rule over the answer it is handed.</para>
/// <list type="bullet">
/// <item>(c) record access: the probe's <c>RetrievePrincipalAccess</c> mask per (caller object id, entity set, record), a 2xx
/// mask or a 403 (<see cref="AccessRights.None"/>, Dataverse's own denial); an unknown (null) answer is never kept. And the route
/// core's <c>RetrievePrincipalAccess</c> GETs per (caller systemuserid, path), success only. Both are CLEARED after every
/// executor step (<see cref="EndStep"/>), whatever its outcome, and on any write through <see cref="CallerClient"/>.</item>
/// <item>(a) privileges: the probe's answer per (caller object id, privilege), Held or NotHeld (never Faulted), and the route
/// core's <c>RetrieveUserSetOfPrivilegesByNames</c> GETs per (caller systemuserid, path), success only; and (b) the route core's
/// <c>sprk_recordtype_refs(…)</c> GETs per (caller systemuserid, path), success only. These live until the window closes: the
/// commit path never edits roles, team membership or the record-type catalog (A2 ArchTest).</item>
/// </list>
/// <para><b>Suspension.</b> A step's own asks may reuse earlier answers only when every ask of the step comes before its one write.
/// That is true only of <see cref="ChildCreateExecutorBase"/> (its <c>WriteAsync</c> is <c>sealed</c>, and the step-12 ArchTest keeps
/// the post-create code free of caller-access questions). For any other executor <see cref="BeginStep"/> suspends the window for
/// its execute, so its asks and writes never meet a kept answer.</para>
/// <para><b>The route core's client.</b> <see cref="CallerClient"/> wraps only the concrete <see cref="DataverseUserClient"/>: every
/// instance of it in one request speaks with that request's bearer token as that one caller (the argument of #3193's
/// <c>CallerMetadataReadMemo.For</c>). Any other <see cref="IDataverseUserClient"/> is returned unwrapped and gets no kept answer.
/// The wrapper is a per-request view: production resolves a fresh transient client per create, and every create of the commit
/// shares this window's stores.</para>
/// <para><b>ADR-003 / ADR-009 (Path C, comply).</b> A per-request snapshot of Dataverse's UAC answers for one decision commit on one
/// <see cref="HttpContext"/>; nothing is shared across requests, callers or processes, and nothing is written to any shared store.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: <c>CallerMetadataReadMemo.RequestScope</c> (schema reads,
/// request-long, never cleared by a write) and <c>CallerWhoAmIMemo</c> (one answer per credential). Extension: neither can hold access
/// answers, which must clear after every step and be suspended for writers. Cost of doing nothing: the four-action commit asks
/// 23 more caller-access calls (about 2 s live) inside a commit already over its 15 s target.</para>
/// </remarks>
internal sealed class DecisionAccessWindow : IDisposable
{
    /// <summary>The <see cref="HttpContext.Items"/> key; a private object, so no other code can reach the slot.</summary>
    private static readonly object ItemsKey = new();

    private const string PrincipalAccessCall = "/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(";
    private const string PrivilegeSetCall = "/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(";
    private const string RecordTypeCatalogRead = "sprk_recordtype_refs(";

    private readonly HttpContext _request;
    private readonly Guid _callerSystemUserId;

    // (c) record access: cleared after every step.
    private readonly ConcurrentDictionary<(Guid Caller, string EntitySet, Guid Record), AccessRights> _probeRights = new();
    private readonly ConcurrentDictionary<(Guid Caller, string Path), (int StatusCode, string? RawJson)> _routeRights = new();

    // (a) privileges and (b) the record-type catalog: kept until the window closes.
    private readonly ConcurrentDictionary<(Guid Caller, string Privilege), CallerPrivilegeAnswer> _probePrivileges = new();
    private readonly ConcurrentDictionary<(Guid Caller, string Path), (int StatusCode, string? RawJson)> _routeCommitLong = new();

    private volatile bool _suspended;
    private volatile bool _closed;

    private DecisionAccessWindow(HttpContext request, Guid callerSystemUserId)
    {
        _request = request;
        _callerSystemUserId = callerSystemUserId;
    }

    /// <summary>
    /// Opens the window for one commit on <paramref name="request"/>, whose caller is <paramref name="callerSystemUserId"/> (their
    /// own <c>WhoAmI</c>). Only <see cref="DecisionCommitService"/> calls this (ArchTest). Dispose (or <see cref="Close"/>) ends it.
    /// </summary>
    internal static DecisionAccessWindow Open(HttpContext request, Guid callerSystemUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var window = new DecisionAccessWindow(request, callerSystemUserId);
        if (callerSystemUserId == Guid.Empty)
        {
            window._closed = true; // no proven caller: nothing is kept, every ask goes to Dataverse
            return window;
        }

        lock (request)
        {
            var items = request.Items;
            lock (items)
                items[ItemsKey] = window;
        }

        return window;
    }

    /// <summary>
    /// The open window of <paramref name="request"/>; <see langword="null"/> with no request, no open commit, a closed window or a
    /// suspended step. Locks the request, then its <c>Items</c>, in the order <c>CallerWhoAmIMemo</c> does (#2851).
    /// </summary>
    internal static DecisionAccessWindow? Of(HttpContext? request)
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
                    return items.TryGetValue(ItemsKey, out var known) && known is DecisionAccessWindow { IsServing: true } window
                        ? window
                        : null;
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or NullReferenceException)
        {
            return null; // an ended request: no window, every ask goes to Dataverse
        }
    }

    private bool IsServing => !_closed && !_suspended;

    /// <summary>
    /// The start of one executor step. A <see cref="ChildCreateExecutorBase"/> keeps the window (all its asks come before its one
    /// write); any other executor's execute runs with the window suspended.
    /// </summary>
    internal void BeginStep(IDecisionActionExecutor executor) =>
        _suspended = executor is not ChildCreateExecutorBase;

    /// <summary>The end of one executor step, whatever its outcome: the record-access answers are forgotten.</summary>
    internal void EndStep()
    {
        ForgetRecordAccess();
        _suspended = false;
    }

    /// <summary>Ends the window: the slot is removed (when it is still this window's) and every answer forgotten. Idempotent.</summary>
    internal void Close()
    {
        if (!_closed)
        {
            _closed = true;
            try
            {
                lock (_request)
                {
                    var items = _request.Items;
                    lock (items)
                    {
                        if (items.TryGetValue(ItemsKey, out var known) && ReferenceEquals(known, this))
                            items.Remove(ItemsKey);
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or NullReferenceException)
            {
                // an ended request: its Items are gone with it
            }
        }

        ForgetRecordAccess();
        _probePrivileges.Clear();
        _routeCommitLong.Clear();
    }

    public void Dispose() => Close();

    internal void ForgetRecordAccess()
    {
        _probeRights.Clear();
        _routeRights.Clear();
    }

    // ── The probe's questions (DecisionExec) ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's rights on one record: a kept answer, or the probe's (<see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync"/>,
    /// the same OBO, <c>WhoAmI</c> and <c>RetrievePrincipalAccess</c> path, with "unknown" kept apart from a 403). Null when unknown;
    /// only a non-null answer is kept.
    /// </summary>
    internal async Task<AccessRights?> CallerRightsAsync(
        CallerRecordAccessProbe probe, HttpContext http, string entitySet, Guid recordId, CancellationToken ct)
    {
        var caller = DecisionExec.CallerObjectId(http);
        if (caller is { } oid && IsServing && _probeRights.TryGetValue((oid, entitySet, recordId), out var known))
            return known;

        var answers = await probe.GetCallerRightsForRecordsOrUnknownAsync(
            TokenHelper.ExtractBearerTokenOrNull(http), [(entitySet, recordId)], ct).ConfigureAwait(false);
        var answer = answers is { Count: 1 } ? answers[0] : null;
        if (answer is { } rights && caller is { } asker && IsServing)
            _probeRights[(asker, entitySet, recordId)] = rights;
        return answer;
    }

    /// <summary>
    /// Whether the caller holds a table privilege: a kept answer, or the probe's (<see cref="CallerRecordAccessProbe.CallerPrivilegeAnswerAsync"/>).
    /// Only Held and NotHeld are kept; Faulted is asked again next time.
    /// </summary>
    internal async Task<CallerPrivilegeAnswer> CallerPrivilegeAsync(
        CallerRecordAccessProbe probe, HttpContext http, string privilege, CancellationToken ct)
    {
        var caller = DecisionExec.CallerObjectId(http);
        if (caller is { } oid && IsServing && _probePrivileges.TryGetValue((oid, privilege), out var known))
            return known;

        var answer = await probe.CallerPrivilegeAnswerAsync(TokenHelper.ExtractBearerTokenOrNull(http), privilege, ct).ConfigureAwait(false);
        if ((answer is CallerPrivilegeAnswer.Held or CallerPrivilegeAnswer.NotHeld) && caller is { } asker && IsServing)
            _probePrivileges[(asker, privilege)] = answer;
        return answer;
    }

    // ── The route core's client ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The route core's caller client for this commit: <paramref name="user"/> wrapped, when it is the <see cref="DataverseUserClient"/>,
    /// so its <c>RetrievePrincipalAccess</c>, privilege-set and <c>sprk_recordtype_refs</c> GETs are answered from this window;
    /// any other client is returned as it is.
    /// </summary>
    internal IDataverseUserClient CallerClient(IDataverseUserClient user) =>
        user is DataverseUserClient caller ? DecisionAccessWindowCallerClient.Over(this, caller) : user;

    /// <summary>
    /// A route-core GET through <paramref name="inner"/>: a kept answer for a path the window keeps (keyed on the window's caller
    /// systemuserid and the path), else the wire, keeping a success only; every other path passes through.
    /// </summary>
    internal async Task<DataverseUserResponse> RouteGetAsync(IDataverseUserClient inner, string relativePath, CancellationToken ct)
    {
        var store = RouteStoreOf(relativePath);
        if (store is null)
            return await inner.GetAsync(relativePath, ct).ConfigureAwait(false);

        var key = (_callerSystemUserId, relativePath);
        if (IsServing && store.TryGetValue(key, out var known))
            return Answer(known);

        var response = await inner.GetAsync(relativePath, ct).ConfigureAwait(false);
        if (response.IsSuccess && IsServing)
            Keep(store, key, response);
        return response;
    }

    /// <summary>The store a route-core GET path is kept in, or null when the path is not one the window keeps.</summary>
    private ConcurrentDictionary<(Guid Caller, string Path), (int StatusCode, string? RawJson)>? RouteStoreOf(string relativePath)
    {
        if (relativePath.Contains(PrincipalAccessCall, StringComparison.Ordinal))
            return _routeRights;
        if (relativePath.Contains(PrivilegeSetCall, StringComparison.Ordinal)
            || relativePath.TrimStart('/').StartsWith(RecordTypeCatalogRead, StringComparison.Ordinal))
            return _routeCommitLong;
        return null;
    }

    /// <summary>
    /// Keeps <paramref name="response"/> only when its own status is 2xx. A second, independent guard beside the caller's
    /// <c>IsSuccess</c> check: <see cref="Answer"/> replays a kept entry as a success, so a stored 400 or 403 would read back as an
    /// allow if that check were ever lost.
    /// </summary>
    private static void Keep(
        ConcurrentDictionary<(Guid Caller, string Path), (int StatusCode, string? RawJson)> store,
        (Guid Caller, string Path) key,
        DataverseUserResponse response)
    {
        if (response.StatusCode is < 200 or > 299)
            return;

        store[key] = (response.StatusCode, response.Body?.GetRawText());
    }

    private static DataverseUserResponse Answer((int StatusCode, string? RawJson) known)
    {
        if (known.RawJson is null)
            return DataverseUserResponse.Ok(known.StatusCode, body: null);

        using var document = JsonDocument.Parse(known.RawJson);
        return DataverseUserResponse.Ok(known.StatusCode, document.RootElement.Clone());
    }
}

/// <summary>
/// ISS-010 part C: the per-request view of a <see cref="DecisionAccessWindow"/> over one <see cref="DataverseUserClient"/>, built
/// only by <see cref="DecisionAccessWindow.CallerClient"/>. GETs go through the window (<see cref="DecisionAccessWindow.RouteGetAsync"/>);
/// a write passes through and forgets the record-access answers (belt and braces: the child create's caller client writes nothing
/// today). A top-level type, as <c>CallerMetadataReadMemo</c> is: an <see cref="IDataverseUserClient"/> decorator chooses no table and
/// no column, and the write-path ArchTests recognise it as the API, not a caller.
/// </summary>
internal sealed class DecisionAccessWindowCallerClient : IDataverseUserClient, ICallerClientView
{
    private readonly DecisionAccessWindow _window;
    private readonly DataverseUserClient _inner;

    // Private: the only way to a view is Over(), whose parameter is the DataverseUserClient itself, so a view over an app-only or any
    // other client cannot be built (CallerMetadataReadMemo.For shares the request's metadata store on that guarantee).
    private DecisionAccessWindowCallerClient(DecisionAccessWindow window, DataverseUserClient inner)
    {
        _window = window;
        _inner = inner;
    }

    internal static DecisionAccessWindowCallerClient Over(DecisionAccessWindow window, DataverseUserClient inner) => new(window, inner);

    IDataverseUserClient ICallerClientView.Inner => _inner;

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
        _window.RouteGetAsync(_inner, relativePath, cancellationToken);

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
    {
        _window.ForgetRecordAccess();
        return _inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PostAsync(
        string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
    {
        _window.ForgetRecordAccess();
        return _inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
    {
        _window.ForgetRecordAccess();
        return _inner.PatchAsync(relativePath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(
        string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
    {
        _window.ForgetRecordAccess();
        return _inner.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);
    }

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        _window.ForgetRecordAccess();
        return _inner.DeleteAsync(relativePath, cancellationToken);
    }
}
