using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// #2188 (field-mapping-server-write-path-r1 DI-004) — masks an app-only mapping-source read down to the columns the CALLER can
/// read, so a column under column-level (field) security that the caller cannot see is never copied into a record the caller
/// can see.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The mapping source is read app-only (typed SDK values the rule engine needs). An app-only read is not
/// subject to field-level security, so a record-level Read check alone (<see cref="QuickCreateSourceAccessFilter"/>) let a
/// secured source column the caller cannot read be copied into the new record. Dataverse masks secured columns itself on a
/// read made AS THE CALLER (it returns them as null), so this helper makes ONE caller read of the same row through
/// <see cref="IDataverseUserClient"/> (OBO, no app-only fallback) and keeps only the app-read columns that read as non-null
/// to the caller. No per-column field-security logic is built here: Dataverse answers it.</para>
///
/// <para><b>One request shape</b> (#2191 review, K3): the caller read is <c>GET {entitySet}({id})</c> with no <c>$select</c>,
/// whatever the source's columns hold, so neither its success nor its shape depends on the row's data. A mapped column the Web
/// API does not return at all (a virtual name column, say) is therefore silently not copied — exactly like an empty one; it no
/// longer fails the read. The read answers the whole row, so a column anywhere on the row that the Web API cannot serve fails
/// the whole source (Unavailable, fail closed).</para>
///
/// <para><b>The one test: null versus not-null, failing closed</b> (UAC-r2 condition 1). For each mapped column the app read
/// returned a value for:</para>
/// <list type="bullet">
/// <item>a lookup (<see cref="EntityReference"/>) is judged by the response's <c>_col_value</c> and kept only when that is
/// non-null — the id only; the target type of a polymorphic lookup is never compared;</item>
/// <item>a scalar (text, whole/decimal/float number, money, choice, yes/no, date) is judged by its logical name and kept only
/// when non-null;</item>
/// <item>a multi-select choice (<see cref="OptionSetValueCollection"/>) is judged by its logical name too: the Web API answers
/// it as a string (<c>"1,2"</c>) or null, so the same null test applies (review pass 1, K2);</item>
/// <item>a <see cref="Guid"/> is kept only for the source's own primary key (<c>{table}id</c>); any other GUID-valued column
/// may be a file column and is UNREADABLE;</item>
/// <item>everything else — file, image (<c>byte[]</c>), party list (<see cref="EntityCollection"/>), and any type not
/// listed — is UNREADABLE and never copied, and so is a column whose name is not a plain logical name or that is ABSENT from
/// the caller's response.</item>
/// </list>
/// <para>Calculated and rollup columns need no case of their own: they come back in the same shape as their result type on
/// both reads, and field security masks them the same way.</para>
///
/// <para><b>Relies on the app read never asking for unmasked data</b> (review pass 1, K1). Under a Dataverse data-masking
/// rule the caller's read returns a non-null MASKED value (for example <c>"***"</c>), so the null test keeps the column — and
/// that is safe only because the app read returns the same masked value: no read in this path asks for
/// <c>UnMaskedData</c>. If the app read ever did, this helper would copy the unmasked value into a record the caller can read.
/// Any change that unmasks the app read must also compare values here, not only null versus not-null.</para>
///
/// <para><b>A removed column looks exactly like an empty one</b> (UAC-r2 condition 3). It is simply absent from the returned
/// entity, which is how the SDK presents an empty column, so the unchanged rule engine
/// (<see cref="CreateTimeFieldMapping"/>) skips a Copy silently and gives a Concat/Template placeholder its ordinary
/// "left blank" warning — word for word the empty-column text. Nothing here logs, counts or reports which columns were
/// removed: the only log line is the source-level one when the caller read fails.</para>
///
/// <para><b>Unavailable</b> (UAC-r2 condition 5). A missing client, an unresolvable table, a fault, a non-success status
/// (403, 404, 5xx, OBO failure), an unparseable body or a timeout make the whole source unavailable: the method returns
/// <see langword="null"/> and the caller records its single source-level warning. There is never a fallback to the app-read
/// values. A cancellation of the caller's own token is rethrown.</para>
///
/// <para><b>Known limit — the race between the two reads</b> (UAC-r2 condition 2). The app read and the caller read are two
/// requests. A column that changes between them (filled, emptied, or its field-security profile changed) is judged by the
/// caller read's null/not-null answer but copied with the app read's value. The window is the length of one request; this
/// helper deliberately does not re-read to close it.</para>
///
/// <para><b>Every source handed to the rule engine passes through here</b> (UAC-r2 condition 4) — enforced by
/// <c>tests/Spaarke.ArchTests/MappingSourceCallerMaskGuardTests.cs</c>.</para>
///
/// <para>ADR-010: static, no DI registration, no interface (the <see cref="MappingSourceReadProbe"/> pattern).</para>
/// </remarks>
internal static partial class MappingSourceCallerMask
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalNameRegex();

    /// <summary>
    /// The most the caller read may take (review pass 1, K4): past it the source is unavailable instead of holding the
    /// user's create open on a slow Dataverse. The same 15 s bound, on a token linked to the caller's, as
    /// <c>CallerRecordAccessProbe.AppOnlyQuestionTimeout</c> — so the caller's own cancellation still wins and is rethrown.
    /// </summary>
    internal static readonly TimeSpan CallerReadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The app-read <paramref name="appRead"/> reduced to the <paramref name="mappedColumns"/> the caller can read, or
    /// <see langword="null"/> when the caller read could not be made or answered (the source is then unavailable — nothing is
    /// copied). Never throws except a cancellation of <paramref name="ct"/>.
    /// </summary>
    /// <param name="caller">The caller-context (OBO) client; <see langword="null"/> makes the source unavailable.</param>
    /// <param name="sourceEntityLogicalName">The source table's logical name (as the app read used it).</param>
    /// <param name="sourceRecordId">The source row.</param>
    /// <param name="appRead">The app-only read of the source's mapped columns.</param>
    /// <param name="mappedColumns">The profile's source columns (<see cref="CreateTimeFieldMapping.CollectSourceColumns"/>).</param>
    /// <param name="logger">Optional logger; it never receives a column name or a column count.</param>
    /// <param name="ct">Cancellation; its cancellation propagates.</param>
    /// <param name="timeout">The caller read's bound; <see langword="null"/> is <see cref="CallerReadTimeout"/> (a test seam).</param>
    internal static async Task<Entity?> ApplyAsync(
        IDataverseUserClient? caller,
        string sourceEntityLogicalName,
        Guid sourceRecordId,
        Entity appRead,
        IReadOnlyCollection<string> mappedColumns,
        ILogger? logger,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(appRead);
        ArgumentNullException.ThrowIfNull(mappedColumns);

        if (caller is null)
        {
            logger?.LogWarning(
                "[FIELD-MAPPING-SOURCE] No caller-context Dataverse client for {EntityType}({RecordId}); the source is unavailable.",
                sourceEntityLogicalName, sourceRecordId);
            return null;
        }

        if (!EntityAccessFilter.TryResolveEntitySet(sourceEntityLogicalName, out var entitySet))
        {
            logger?.LogWarning(
                "[FIELD-MAPPING-SOURCE] {EntityType} has no entity-set mapping, so it cannot be read as the caller; the source is unavailable.",
                sourceEntityLogicalName);
            return null;
        }

        var mapped = new HashSet<string>(mappedColumns, StringComparer.OrdinalIgnoreCase);
        var primaryKey = sourceEntityLogicalName.Trim().ToLowerInvariant() + "id";

        // Every candidate: (the app read's attribute key, the Web API property the caller read must answer non-null). Only the
        // JUDGING depends on the app read's values; the request below does not.
        var candidates = new List<(string Key, string Property)>();
        foreach (var attribute in appRead.Attributes)
        {
            if (!mapped.Contains(attribute.Key) || attribute.Value is null)
            {
                continue; // not mapped, or already empty — nothing to copy either way
            }

            if (WebApiPropertyFor(attribute.Key, attribute.Value, primaryKey) is { } property)
            {
                candidates.Add((attribute.Key, property));
            }

            // else UNREADABLE by kind: not a candidate, so it is never copied.
        }

        // #2191 review K3: the request has ONE shape for a source row — no $select at all — so nothing about it depends on
        // what the row holds. A $select built from the app read's non-null columns did: a mapped column the Web API cannot
        // select (a virtual name column) turned the read into a 400, and the source unavailable, only while that column held
        // a value. Nor can a $select be built from every mapped column instead: a lookup is selectable only as _col_value
        // and a scalar only by its name, and which one a column is cannot be told from an empty app read (and a Concat or
        // Template placeholder declares no type). Without $select the Web API answers every column the caller can read —
        // a field-secured one as null, a lookup as _col_value — and a mapped column it does not answer at all is ABSENT,
        // which the null test below treats as unreadable. One row, read once; nothing from it is logged.
        var path = $"{entitySet}({sourceRecordId:D})";

        DataverseUserResponse response;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout ?? CallerReadTimeout);
        try
        {
            response = await caller.GetAsync(path, bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A timeout (a cancellation that is not the caller's) and any fault: unavailable, never the app-read values.
            logger?.LogWarning(ex,
                "[FIELD-MAPPING-SOURCE] The caller read of {EntityType}({RecordId}) failed; the source is unavailable.",
                sourceEntityLogicalName, sourceRecordId);
            return null;
        }

        if (!response.IsSuccess || response.Body is not { ValueKind: JsonValueKind.Object } body)
        {
            logger?.LogWarning(
                "[FIELD-MAPPING-SOURCE] The caller read of {EntityType}({RecordId}) answered {StatusCode} {ErrorCode}; the source is unavailable.",
                sourceEntityLogicalName, sourceRecordId, response.StatusCode, response.ErrorCode);
            return null;
        }

        var masked = new Entity(appRead.LogicalName, appRead.Id);
        foreach (var (key, property) in candidates)
        {
            if (!body.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                continue; // secured (masked) or absent to the caller: left out — exactly what an empty column looks like
            }

            masked[key] = appRead[key];
            if (appRead.FormattedValues.TryGetValue(key, out var label))
            {
                masked.FormattedValues[key] = label;
            }
        }

        return masked;
    }

    /// <summary>
    /// The Web API property the caller read must answer non-null for an app-read value, or <see langword="null"/> when the
    /// column is UNREADABLE by kind (never copied). Internal for the kind table's tests (ADR-038 A2).
    /// </summary>
    internal static string? WebApiPropertyFor(string column, object value, string primaryKey)
    {
        if (!LogicalNameRegex().IsMatch(column))
        {
            return null;
        }

        return value switch
        {
            EntityReference => $"_{column}_value",
            Guid => string.Equals(column, primaryKey, StringComparison.Ordinal) ? column : null,
            string or int or long or decimal or double or bool or DateTime or OptionSetValue or Money
                or OptionSetValueCollection => column,
            _ => null, // file, image, party list, anything unlisted
        };
    }
}
