using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// The ORIGINAL authorship of the versions a relocation replayed into a moved file (unified-access-control-r2 task 166,
/// owner round 45 item 1) — the record, and how the version history routes report it.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <see cref="DocumentContainerRelocator"/> moves a document's file by REPLAYING the source's versions
/// into the copy through the BFF identity, oldest first (round 45 item 1). Graph cannot set a version's author or date,
/// so every replayed version would read "the BFF, at the time of the move". The relocation therefore records, against
/// each NEW version id, the ORIGINAL author, date and size — in <see cref="Column"/>, written by the relocator in the SAME
/// Dataverse update as the re-point (so a moved row never names a copy whose authorship is unrecorded) — and
/// <c>GET /api/documents/{id}/versions</c> and the external version list report those values, so the history a user sees
/// is unchanged.</para>
/// <para><b>Why its own column, not the relocation ledger's.</b> <see cref="DocumentContainerRelocator.RelocationLedgerColumn"/>
/// holds what a move still OWES and is cleared when the move is settled (the migration's read-only cross-check counts
/// rows where it is not null); this record is permanent and as long as the history (a 4000-character ledger could not hold
/// it). Both are the relocation's record on the row, both BFF-written only (field-level security,
/// <c>scripts/Set-DocumentPointerFieldSecurity.ps1</c>; created secured by <c>scripts/Set-DocumentRelocationSchema.ps1</c>).</para>
/// <para><b>Keyed to the item.</b> The record names the copy it describes (<see cref="Map.Item"/>); a version list of any
/// other item ignores it, and a later move replaces it (carrying the originals forward — a version replayed twice keeps
/// its first author).</para>
/// <para>Not a service (no DI registration): one parser and one projection over the existing app-only
/// <see cref="IGenericEntityService"/>, shared by the relocator and the two version routes.</para>
/// </remarks>
public sealed class RelocatedVersionHistory
{
    /// <summary><c>sprk_document.sprk_relocatedversions</c> — created by <c>scripts/Set-DocumentRelocationSchema.ps1</c>.</summary>
    public const string Column = "sprk_relocatedversions";

    /// <summary>The column's maximum length (Multiple lines of text); a longer record keeps its NEWEST versions.</summary>
    internal const int MaxLength = 1_048_576;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private RelocatedVersionHistory()
    {
    }

    /// <summary>One replayed version: its NEW id, and who wrote the original, when, how large, and where it came from.</summary>
    /// <param name="Id">The version id in the copy.</param>
    /// <param name="By">The original author's display name.</param>
    /// <param name="ByUser">The original author's Entra object id (a person).</param>
    /// <param name="ByApp">The original writing application's id (an app-only write).</param>
    /// <param name="At">When the original was written.</param>
    /// <param name="Size">The original's size in bytes.</param>
    /// <param name="FromItem">The item the original belonged to.</param>
    /// <param name="FromVersion">The original's version id there.</param>
    internal sealed record Entry(
        string Id, string? By, string? ByUser, string? ByApp, DateTimeOffset? At, long Size, string? FromItem, string? FromVersion);

    /// <summary>The record for one item: <see cref="Item"/> and its replayed versions.</summary>
    internal sealed record Map(string Item, IReadOnlyList<Entry> Versions)
    {
        /// <summary>The record in <paramref name="json"/>; <see langword="null"/> for no value or an unreadable one.</summary>
        public static Map? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(json, Json);
                return stored is { V: 1, Item: { Length: > 0 } item, Versions: { } versions }
                       && versions.All(v => !string.IsNullOrWhiteSpace(v.Id))
                    ? new Map(item, versions)
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The JSON to store, and how many of the OLDEST entries had to be left out to fit <see cref="MaxLength"/> (those
        /// versions then show Graph's own author and date — reported, never silent).
        /// </summary>
        public (string Json, int Dropped) Serialize()
        {
            var kept = Versions.ToList();
            var dropped = 0;
            while (true)
            {
                var json = JsonSerializer.Serialize(new Stored(1, Item, kept), Json);
                if (json.Length <= MaxLength || kept.Count == 0)
                {
                    return (json, dropped);
                }

                kept.RemoveAt(0);
                dropped++;
            }
        }

        /// <summary>The recorded original of <paramref name="versionId"/> of <paramref name="itemId"/>, if this record describes it.</summary>
        public Entry? Of(string? itemId, string? versionId)
            => itemId is not null && versionId is not null && string.Equals(Item, itemId.Trim(), StringComparison.Ordinal)
                ? Versions.LastOrDefault(v => string.Equals(v.Id, versionId, StringComparison.Ordinal))
                : null;

        private sealed record Stored(int V, string? Item, List<Entry>? Versions);
    }

    /// <summary>
    /// <paramref name="versions"/> of the document's file <paramref name="itemId"/>, with every version a relocation
    /// replayed reporting its ORIGINAL author and date (round 45 item 1). Order, ids and sizes are Graph's. A version the
    /// record does not name, a record of another item, no record, or a record that cannot be read leaves Graph's values
    /// (the last two logged) — this is a presentation of history, never an access decision: the caller has already been
    /// authorized for the document.
    /// </summary>
    public static async Task<IReadOnlyList<VersionInfoDto>> WithOriginalAuthorshipAsync(
        IGenericEntityService dataverse, Guid documentId, string? itemId, IReadOnlyList<VersionInfoDto> versions,
        ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(logger);
        if (versions.Count == 0 || documentId == Guid.Empty || string.IsNullOrWhiteSpace(itemId))
        {
            return versions;
        }

        string? stored;
        try
        {
            var row = await dataverse.RetrieveAsync("sprk_document", documentId, [Column], ct).ConfigureAwait(false);
            stored = row?.GetAttributeValue<string>(Column);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[DOCUMENT-VERSIONS] the relocation record of document {DocumentId} could not be read; its version history is "
                + "reported as Graph returns it.", documentId);
            return versions;
        }

        var map = Map.Parse(stored);
        if (map is null)
        {
            if (!string.IsNullOrWhiteSpace(stored))
            {
                logger.LogWarning(
                    "[DOCUMENT-VERSIONS] {Column} of document {DocumentId} is not a relocation record; its version history is "
                    + "reported as Graph returns it.", Column, documentId);
            }

            return versions;
        }

        return Apply(map, itemId, versions);
    }

    /// <summary>The projection itself: each version the record names for <paramref name="itemId"/> takes its original author and date.</summary>
    internal static IReadOnlyList<VersionInfoDto> Apply(Map map, string itemId, IReadOnlyList<VersionInfoDto> versions)
        => versions.Select(v => map.Of(itemId, v.Id) is { } original
                ? v with
                {
                    LastModifiedDateTime = original.At ?? v.LastModifiedDateTime,
                    LastModifiedBy = original.By ?? v.LastModifiedBy,
                    LastModifiedByUserId = original.ByUser,
                    LastModifiedByApplicationId = original.ByApp,
                }
                : v)
            .ToList();
}
