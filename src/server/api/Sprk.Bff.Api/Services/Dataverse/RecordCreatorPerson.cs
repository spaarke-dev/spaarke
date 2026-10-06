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
/// APP-ONLY — Office quick-create (<c>Services/Office/RecordCreationService</c>), and before task 166 deleted it,
/// <c>POST /api/v1/work-assignments</c> — that is the BFF application user, not the person who asked for the record,
/// and <c>createdonbehalfby</c> is empty (verified live 2026-10-01). Secure provisioning's RESUME shares a stranded
/// secure record to the person who created it; for those rows it had nobody to share to and could only refuse.
/// This column is that person, persisted.</para>
///
/// <para><b>Who writes it.</b> Only the BFF: every BFF create path of the three tables stamps it — the app-only paths
/// in the create payload itself (Office quick-create: the Office caller; the deleted <c>POST /api/v1/work-assignments</c>
/// stamped its caller, resolved by WhoAmI, so rows it created carry the column too), and the chat create (<c>dataverse.create_record</c>), which task 146 moved to
/// create-as-the-app (owner round 7 item 3), in that create's payload too (<c>OwnedChildWrite.CreateAsync</c>; task 133's
/// interim app-only follow-up update was removed at the batch 4 integration, so there is one stamp). It is field-secured
/// (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c>): every user can READ it (the reader profile sits on every
/// business unit's default team), only the BFF application user(s) can create or update it — so a client create cannot
/// name someone else as its creator.</para>
///
/// <para><b>Who reads it.</b> <c>ProvisionProjectEndpoint</c>'s resume: <c>createdby</c> when that is a person,
/// otherwise this column; it refuses when neither is a usable person. It is read in its OWN query, never in
/// provisioning's Step 1 select, so a BFF deployed before the schema still provisions forward (only a resume that
/// needs the column reports it unreadable).</para>
///
/// <para><b>Placement (CLAUDE.md §10/§11).</b> A static holder of one column's name and its two write shapes, so the
/// one reader and the three writers cannot drift apart. No service, no interface, no DI registration.</para>
///
/// <para><b>The CHILD tables too (unified-access-control-r2 task 146 c1-r1; owner round 13 item 9, 2026-10-03).</b>
/// "Children the BFF creates as the application record the person who asked." F3 (owner round 10 item 7) lets the
/// person who created a secure record's child move it out of the secure record; for a child the BFF created app-only,
/// <c>createdby</c> is the application user, so without this column the creator branch could never admit anyone. The
/// same column, with the same field security, goes on every child table the BFF creates app-only
/// (<see cref="StampedChildTables"/>, created by <c>scripts/Set-ChildRecordCreatorPersonSchema.ps1</c>, pinned against it by
/// <c>ChildRecordCreatorPersonSchemaAgreementTests</c>). Every app-create writer that acts for a person stamps it through
/// the owner decision (<c>RecordOwnershipContext.RequestedBy</c> → <c>RecordOwnerResolution.CreatedByPerson</c>, written by
/// <c>RecordOwnerResolution.ApplyTo</c> / <c>StampCreatorOn</c>). Writers that act for nobody (inbound mail, background
/// jobs) leave it empty. <see cref="StampedTables"/> stays the three roots: task 133's schema script and its agreement test
/// pin exactly that set.</para>
/// </remarks>
public static class RecordCreatorPerson
{
    /// <summary>The lookup's logical name. The SDK writes address it by this name; the schema script creates it.</summary>
    /// <remarks>Task 146 c1-r1: spelled once, in <see cref="Spaarke.Dataverse.RecordCreatorPersonColumn"/>, which the shared
    /// create seams also write through.</remarks>
    public const string Column = Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName;

    /// <summary>The Web API read form of the lookup.</summary>
    public const string ValueColumn = "_sprk_createdbyperson_value";

    /// <summary>The table the lookup points at.</summary>
    public const string TargetEntity = Spaarke.Dataverse.RecordCreatorPersonColumn.TargetEntity;

    /// <summary>
    /// The lookup's single-valued navigation property — what a Web API write binds (<c>sprk_CreatedByPerson@odata.bind</c>).
    /// Dataverse names it after the lookup's schema name; read live 2026-10-03 on the roots
    /// (<c>RelationshipDefinitions(SchemaName='sprk_systemuser_sprk_matter_createdbyperson')</c> →
    /// <c>ReferencingEntityNavigationPropertyName = sprk_CreatedByPerson</c>), and the child schema script sets it explicitly.
    /// </summary>
    public const string NavigationProperty = Spaarke.Dataverse.RecordCreatorPersonColumn.NavigationProperty;

    /// <summary>The tables that carry the column — the three secure roots.</summary>
    public static readonly IReadOnlySet<string> StampedTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_project", "sprk_matter", "sprk_workassignment" };

    /// <summary>
    /// The CHILD tables that carry the column (task 146 c1-r1, owner round 13 item 9): every child or content table the BFF
    /// creates as the application — the census's routed and seam create tables (<c>RecordOwnerAssignmentCensusTests</c>
    /// pins that every one of them is here), each in the codified Secure Record Owner role set.
    /// </summary>
    public static readonly IReadOnlySet<string> StampedChildTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_document", "sprk_todo", "sprk_event", "sprk_eventlog", "sprk_communication", "sprk_communicationthread",
        "sprk_communicationattachment", "sprk_communicationparticipant", "sprk_emailreviewlog", "sprk_analysis",
        "sprk_analysisoutput", "sprk_emailartifact", "sprk_attachmentartifact", "sprk_fileversion", "sprk_invoice",
        "sprk_spendsignal", "sprk_spendsnapshot",
        // Task 147 r1 (owner round 28 item 1): the browser's memo and report-card creates are app-only too (G5), so they
        // record the person who asked. Added to scripts/Set-ChildRecordCreatorPersonSchema.ps1 in the same change; the
        // column lands in dev through that script's dry run / -Apply / -Verify (manual gate G147-5).
        "sprk_memo", "sprk_reportcard",
        // Task 147 r1 (owner round 28 item 2, E2): the secure-record ribbon's "New Budget" creates a budget app-only
        // through POST /api/v1/child-records/sprk_budget, so it records the person who asked too (same script, gate G147-5).
        "sprk_budget",
        // Task 147 r1c (E2, the live inventory of every main form): "New KPI Assessment" (matter, project and report card
        // forms) and "New Billing Event" (invoice form) create through the same route, so they record the person too
        // (same script, gate G147-5).
        "sprk_kpiassessment", "sprk_billingevent",
    };

    /// <summary>Whether <paramref name="entityLogicalName"/> carries the column: a secure root or a stamped child table.</summary>
    public static bool IsStamped(string? entityLogicalName) =>
        entityLogicalName is not null
        && (StampedTables.Contains(entityLogicalName.Trim()) || StampedChildTables.Contains(entityLogicalName.Trim()));

    /// <summary>Binds <paramref name="systemUserId"/> on a Web API create payload (app-only creates through a PATCH/POST).</summary>
    public static void Bind(IDictionary<string, object?> fields, Guid systemUserId)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (systemUserId == Guid.Empty)
            throw new ArgumentException("The creator's systemuserid is required.", nameof(systemUserId));

        Spaarke.Dataverse.RecordCreatorPersonColumn.BindIfKnown(fields, systemUserId);
    }

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
