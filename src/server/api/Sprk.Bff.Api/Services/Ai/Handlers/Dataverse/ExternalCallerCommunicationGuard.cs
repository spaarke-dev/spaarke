using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// The ISS-010 quick fix (external-access r3 task 078, owner decision 2026-10-20, #1864) for the assistant's GENERIC
/// Dataverse read tools (<c>dataverse.read_query</c>, <c>dataverse.search_data</c>, <c>dataverse.describe</c> record
/// paths). Those tools run under the caller's OBO token, so without this guard Dataverse security alone decides, and a
/// licensed systemuser flagged <c>sprk_isexternal</c> (owner 2026-10-14: treated like a partner for Internal Only) could
/// read an internal-only or Private-thread communication that task 065 hides on the BFF's own reads.
/// </summary>
/// <remarks>
/// <para><b>The rule (task 065, reused, never redefined).</b> An external caller sees a <c>sprk_communication</c> only
/// when <see cref="CommunicationInternalOnlyFloor.InternalOnlyField"/> is <c>false</c> (true or unset hides) AND its
/// thread lookup (<see cref="CommunicationOpenThreadGate.MessageThreadLookupValue"/>) is set AND that thread reads Open
/// now (<see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/>). Every other communication-family table (logical
/// name starting <see cref="FamilyPrefix"/>: threads, attachments, participants, accounts, rules, channel refs) is refused
/// outright for an external caller.</para>
/// <para><b>Who is external.</b> Decided only when the request touches the communication family, so internal and
/// non-communication requests are byte-identical. The request principal → <see cref="ICallerSystemUserResolver"/> →
/// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/>. An unresolved caller is EXTERNAL (fail closed).</para>
/// <para><b>Logs (ADR-015).</b> Callers log the entity and a reason code only; this class logs nothing.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — the row rule lives in
/// <see cref="CommunicationInternalOnlyFloor"/> / <see cref="CommunicationOpenThreadGate"/> and caller externality in
/// <see cref="ICallerSystemUserResolver"/> + <see cref="ISystemUserIdentityResolver"/>; all are reused as they are, no
/// name is redefined. (2) Extension — those read the communication services' own query seams; the generic tools hold only
/// an <see cref="IDataverseUserClient"/> and build OData from model SQL, so the family predicate, the query clause and
/// the user-client thread delegate the three handlers share sit here beside the other <c>dataverse.*</c> helpers.
/// (3) Cost of doing nothing — a licensed user flagged external reads internal-only email and message bodies through the
/// assistant (ISS-010), which the owner decided must not happen.</para>
/// </remarks>
internal static class ExternalCallerCommunicationGuard
{
    /// <summary>Every table whose logical name starts with this is in the communication family.</summary>
    internal const string FamilyPrefix = "sprk_communication";

    /// <summary>The one family table an external caller may read (row-filtered).</summary>
    internal const string CommunicationTable = "sprk_communication";

    /// <summary>Log event name for every guard decision (entity + reason code only, ADR-015).</summary>
    internal const string LogEvent = "sdap.ai.tool.external_communication_guard";

    /// <summary>The model-actionable refusal for a family table other than <see cref="CommunicationTable"/>.</summary>
    internal const string RefusalMessage =
        "This table is not available to the calling user through the assistant. Communications (emails and messages) " +
        "can be read from the sprk_communication table only.";

    /// <summary>True when <paramref name="logicalName"/> is in the communication family.</summary>
    internal static bool IsCommunicationFamily(string? logicalName) =>
        logicalName is not null && logicalName.StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the <c>sprk_communication</c> table itself.</summary>
    internal static bool IsCommunicationTable(string? logicalName) =>
        string.Equals(logicalName, CommunicationTable, StringComparison.OrdinalIgnoreCase);

    /// <summary>The query-side half of the rule: flag explicitly false and a thread set.</summary>
    internal static string FilterClause =>
        $"{CommunicationInternalOnlyFloor.InternalOnlyField} eq false and {CommunicationOpenThreadGate.MessageThreadLookupValue} ne null";

    /// <summary>
    /// The model's filter ANDed with <see cref="FilterClause"/>. The model's whole predicate is parenthesised so an OR in
    /// it can never widen the guard (the translator only emits balanced parentheses).
    /// </summary>
    internal static string ApplyFilter(string? modelFilter) =>
        string.IsNullOrEmpty(modelFilter) ? FilterClause : $"({modelFilter}) and {FilterClause}";

    /// <summary>
    /// True when the request's caller is external: flagged <c>sprk_isexternal</c>, or not resolvable to an active
    /// systemuser (no principal, no oid, no row, lookup failed). A failing externality read throws (fail closed: the
    /// caller's tool call fails with no data).
    /// </summary>
    internal static async Task<bool> IsExternalCallerAsync(
        IHttpContextAccessor httpContextAccessor,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        CancellationToken ct)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var resolution = await callerResolver.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!resolution.IsResolved || !Guid.TryParse(resolution.SystemUserId, out var systemUserId))
        {
            return true;
        }

        return await identityResolver.IsExternalAsync(systemUserId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The thread-privacy read for <see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/> over the caller's own
    /// user client. A failed read THROWS, so a fault becomes the tool's error result with no rows — never "show it".
    /// </summary>
    internal static CommunicationOpenThreadGate.ThreadRowQuery ThreadQueryOver(IDataverseUserClient dataverse) =>
        async (odata, ct) =>
        {
            var response = await dataverse.GetAsync($"{CommunicationOpenThreadGate.ThreadSet}?{odata}", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"The communication thread privacy read failed ({response.ErrorCode}); the rows stay hidden (fail closed).");
            }

            var rows = new List<Dictionary<string, JsonElement>>();
            if (response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in value.EnumerateArray())
                {
                    if (record.ValueKind != JsonValueKind.Object) continue;
                    var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in record.EnumerateObject())
                    {
                        row[property.Name] = property.Value.Clone();
                    }
                    rows.Add(row);
                }
            }

            return rows;
        };

    /// <summary>The thread id of one OData communication row (raw <c>_sprk_communicationthread_value</c>), or empty.</summary>
    internal static Guid ThreadIdOf(JsonElement record) =>
        record.TryGetProperty(CommunicationOpenThreadGate.MessageThreadLookupValue, out var v)
        && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id)
            ? id
            : Guid.Empty;

    /// <summary>True only when the row's <c>sprk_isinternalonly</c> reads explicitly <c>false</c>.</summary>
    internal static bool IsExplicitlyShareable(JsonElement record) =>
        record.TryGetProperty(CommunicationInternalOnlyFloor.InternalOnlyField, out var v) && v.ValueKind == JsonValueKind.False;
}
