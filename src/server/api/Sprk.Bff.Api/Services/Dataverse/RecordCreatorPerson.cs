using System.Text.Json;
using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The PERSON who created a <c>sprk_project</c>, <c>sprk_matter</c> or <c>sprk_workassignment</c> — the
/// server-stamped <c>sprk_createdbyperson</c> lookup to <c>systemuser</c> (unified-access-control-r2 task 133; owner
/// decision round 7 item 2, option (a), 2026-10-02).
/// </summary>
/// <remarks>
/// <para><b>Why the column exists.</b> <c>createdby</c> is the identity that sent the create. For a row the BFF creates
/// APP-ONLY — Office quick-create (<c>Services/Office/RecordCreationService</c>) and
/// <c>POST /api/v1/work-assignments</c> — that is the BFF application user, not the person who asked for the record,
/// and <c>createdonbehalfby</c> is empty (verified live 2026-10-01). Secure provisioning's RESUME shares a stranded
/// secure record to the person who created it; for those rows it had nobody to share to and could only refuse.
/// This column is that person, persisted.</para>
///
/// <para><b>Who writes it.</b> Only the BFF: every BFF create path of the three tables stamps it — the app-only paths
/// in the create payload itself (the Office caller / the endpoint's caller), the user-OBO chat create
/// (<c>dataverse.create_record</c>) by an app-only update right after the create, with the row's own
/// <c>createdby</c> (the OBO caller). It is field-secured (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c>): every
/// user can READ it, only the BFF application user(s) can create or update it — so a client create cannot name
/// someone else as its creator.</para>
///
/// <para><b>Who reads it.</b> <c>ProvisionProjectEndpoint</c>'s resume: <c>createdby</c> when that is a person,
/// otherwise this column; it refuses when neither is a usable person. It is read in its OWN query, never in
/// provisioning's Step 1 select, so a BFF deployed before the schema still provisions forward (only a resume that
/// needs the column reports it unreadable).</para>
///
/// <para><b>Placement (CLAUDE.md §10/§11).</b> A static holder of one column's name and its two write shapes, so the
/// one reader and the three writers cannot drift apart. No service, no interface, no DI registration.</para>
/// </remarks>
public static class RecordCreatorPerson
{
    /// <summary>The lookup's logical name (also its navigation property name — set explicitly by the schema script).</summary>
    public const string Column = "sprk_createdbyperson";

    /// <summary>The Web API read form of the lookup.</summary>
    public const string ValueColumn = "_sprk_createdbyperson_value";

    /// <summary>The table the lookup points at.</summary>
    public const string TargetEntity = "systemuser";

    /// <summary>The tables that carry the column — the three secure roots.</summary>
    public static readonly IReadOnlySet<string> StampedTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_project", "sprk_matter", "sprk_workassignment" };

    /// <summary>Whether <paramref name="entityLogicalName"/> carries the column.</summary>
    public static bool IsStamped(string? entityLogicalName) =>
        entityLogicalName is not null && StampedTables.Contains(entityLogicalName.Trim());

    /// <summary>Stamps <paramref name="systemUserId"/> on an SDK create payload (app-only creates).</summary>
    public static void Stamp(Entity entity, Guid systemUserId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (systemUserId == Guid.Empty)
            throw new ArgumentException("The creator's systemuserid is required.", nameof(systemUserId));

        entity[Column] = new EntityReference(TargetEntity, systemUserId);
    }

    /// <summary>The update payload that stamps <paramref name="systemUserId"/> on an existing row.</summary>
    public static Dictionary<string, object> UpdateFields(Guid systemUserId)
    {
        if (systemUserId == Guid.Empty)
            throw new ArgumentException("The creator's systemuserid is required.", nameof(systemUserId));

        return new Dictionary<string, object> { [Column] = new EntityReference(TargetEntity, systemUserId) };
    }

    /// <summary>
    /// Whether a caller-supplied create item names the column (any casing, padding, or its read form). The column is
    /// stamped by the server only; a client never chooses who created a record.
    /// </summary>
    public static bool IsNamedIn(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.EnumerateObject().Any(p => NamesColumn(p.Name));

    /// <summary>Whether <paramref name="key"/> names the column or its read form.</summary>
    public static bool NamesColumn(string? key)
    {
        var trimmed = key?.Trim();
        return string.Equals(trimmed, Column, StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, ValueColumn, StringComparison.OrdinalIgnoreCase)
               || (trimmed?.StartsWith(Column + "@", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
