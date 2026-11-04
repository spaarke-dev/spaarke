using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// Task 031 fix round 1 (F1; owner decision 2026-11-04, "exempt in code"; UAC-r2 conditions (a) to (f)):
/// <see cref="RecordCreationRequest.RouteAuthorizedLookupColumns"/> — lookups whose target the ROUTE has already authorised
/// for this caller and this record id, so step 5 asks no AppendTo on them. Read as the caller is still asked (existence and
/// Read: the uniform not-found), and nothing else about the value changes: it is the caller's value, bound, table-checked and
/// mapped like any other.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <c>POST /api/ai/analysis/create</c>'s filter makes the playbook-use decision (public, or the caller's Read
/// on the row) before the handler runs. Only "Spaarke AI Analysis Admin" holds <c>prvAppendTosprk_analysisplaybook</c>, so
/// asking AppendTo on the playbook (step 5's rule for a caller-supplied lookup) refused every ordinary user — a question the
/// route never asked before task 031.</para>
/// <para><b>A CLOSED list</b> (<see cref="RouteAuthorizedLookups"/>): (table, column, target table). Any other column — and
/// before that, any filing lookup (<see cref="IsFilingLookup(string, string)"/>: lineage, ownership-parent filing, a root's
/// filing column) or a lookup to an ownership-parent table — is the adapter's defect, an <see cref="ArgumentException"/>
/// thrown before anything is read. Who may set the option is pinned by <c>RouteAuthorizedLookupColumnsCallSiteTests</c>
/// (ArchTests): the analysis create handler only.</para>
/// <para><b>Not <see cref="RecordCreationRequest.ServerSetLookupColumns"/>.</b> A server-set lookup names the caller and is
/// never mapped or filed under; a route-authorised one is the caller's own choice of a record. The two sets never meet: one
/// column in both is an argument error, as is a column also named an optional reference.</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>
    /// The closed list of (table, lookup column, target table) a route may mark as route-authorised. Each entry names the
    /// route decision that authorises it. A new entry needs an owner decision and a matching ArchTest call site.
    /// </summary>
    internal static readonly IReadOnlyList<(string Table, string Column, string TargetTable)> RouteAuthorizedLookups =
    [
        // POST /api/ai/analysis/create: AnalysisAuthorizationFilter (AnalysisCreate) — the playbook-use decision (public, or
        // the caller's Read on the row), recorded for the request's own playbook id only after every check passed.
        ("sprk_analysis", "sprk_playbook", "sprk_analysisplaybook"),
    ];

    /// <summary>
    /// The request's route-authorised lookups, validated against <paramref name="allowList"/> (the shipped list unless a test
    /// names another). Thrown before anything is read: a principal that is not a caller, a filing lookup, a lookup to an
    /// ownership-parent table, a column not on the list for <paramref name="table"/>, or one also server-set or a reference.
    /// </summary>
    internal static void ValidateRouteAuthorizedLookupColumns(
        string table, RecordCreationRequest request, RecordCreationPrincipal principal,
        IReadOnlyList<(string Table, string Column, string TargetTable)>? allowList = null)
    {
        if (request.RouteAuthorizedLookupColumns is not { Count: > 0 } columns)
            return;

        if (principal is not RecordCreationPrincipal.CallerPrincipal)
            throw new ArgumentException("Route-authorised lookups are a caller's create only: a system principal asks no AppendTo.", nameof(request));

        allowList ??= RouteAuthorizedLookups;
        foreach (var column in columns)
        {
            if (IsFilingLookup(table, column))
            {
                throw new ArgumentException(
                    $"'{column}' files a {NounOf(table)} under a record: its AppendTo is always asked, never skipped.", nameof(request));
            }

            var entry = allowList.FirstOrDefault(e =>
                string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Column, column, StringComparison.OrdinalIgnoreCase));
            if (entry.Column is null)
            {
                throw new ArgumentException(
                    $"'{column}' of a {NounOf(table)} is not on the closed list of route-authorised lookups.", nameof(request));
            }

            if (RecordOwnershipResolver.IsOwnershipParent(entry.TargetTable))
            {
                throw new ArgumentException(
                    $"'{column}' of a {NounOf(table)} names a {entry.TargetTable}, an ownership parent: its AppendTo is always asked.",
                    nameof(request));
            }

            if (request.ServerSetLookupColumns?.Contains(column) == true || request.ReferenceLookupColumns?.Contains(column) == true)
            {
                throw new ArgumentException(
                    $"'{column}' is route-authorised and also server-set or an optional reference: it may be only one.", nameof(request));
            }
        }
    }

    /// <summary>
    /// Step 5, before the AppendTo checks: the route-authorised lookups of the CALLER's item that are still the row's value
    /// (the same record in <paramref name="merged"/>; field mapping is fill-only, so a mapped value never has their column) and
    /// whose target is the listed table. Each is asked the caller's Read (RetrievePrincipalAccess under their token): missing,
    /// unreadable, or unanswered is the same uniform not-found an AppendTo failure gives. The answer's <c>SkipAppendTo</c> is
    /// <paramref name="serverSet"/> plus exactly those columns — the set the AppendTo checks skip; a lookup whose column is
    /// listed but whose table or record does not match is not in it, so its AppendTo is asked as usual.
    /// </summary>
    private static async Task<(OwnedChildWrite.Outcome? Refusal, IReadOnlySet<string>? SkipAppendTo)> CheckRouteAuthorizedLookupsAsync(
        IDataverseUserClient user, Guid me, string table, DataverseWriteItemMapper.MappedItem callerItem,
        DataverseWriteItemMapper.MappedItem merged, RecordCreationRequest request, CancellationToken ct)
    {
        var serverSet = request.ServerSetLookupColumns;
        if (request.RouteAuthorizedLookupColumns is not { Count: > 0 } authorised)
            return (null, serverSet);

        var exempt = callerItem.Lookups
            .Where(l => authorised.Contains(l.Column)
                        && RouteAuthorizedLookups.Any(e =>
                            string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.Column, l.Column, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.TargetTable, l.RelatedTable, StringComparison.OrdinalIgnoreCase))
                        && merged.Lookups.Any(m => string.Equals(m.Column, l.Column, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(m.RelatedTable, l.RelatedTable, StringComparison.OrdinalIgnoreCase)
                                                   && m.RecordId == l.RecordId))
            .ToArray();
        if (exempt.Length == 0)
            return (null, serverSet);

        foreach (var lookup in exempt)
        {
            var rights = await CallerWriteAuthorization.RightsOnAsync(user, me, lookup.RelatedEntitySet, lookup.RecordId, ct)
                .ConfigureAwait(false);
            if (!rights.HasFlag(AccessRights.Read))
            {
                return (new OwnedChildWrite.Outcome
                {
                    Denied = $"You do not have permission to file this record under the {lookup.RelatedTable} it names.",
                    ParentUnavailable = true,
                    RefusedColumn = lookup.Column,
                }, null);
            }
        }

        var skip = new HashSet<string>(serverSet ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        skip.UnionWith(exempt.Select(l => l.Column));
        return (null, skip);
    }
}
