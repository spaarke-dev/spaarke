using Microsoft.Xrm.Sdk;

namespace Spaarke.Dataverse;

/// <summary>
/// The server-stamped "Created By (Person)" lookup — <c>sprk_createdbyperson</c>, to <c>systemuser</c> — as the shared
/// create seams write it (unified-access-control-r2 tasks 133 and 146 c1-r1).
/// </summary>
/// <remarks>
/// <para><b>Why the seams carry it.</b> Owner round 13 item 9 (2026-10-03): "children the BFF creates as the application
/// record the person who asked". A row the BFF creates app-only has the application user as <c>createdby</c>, so F3's
/// "or the creator" branch (owner round 10 item 7) could never admit the person who made it. The BFF decides the person
/// (its <c>IRecordOwnershipResolver</c>, from the request's own caller) and passes the id; these seams only write it, as
/// they write the owner team they are handed.</para>
/// <para><b>One spelling.</b> The BFF's <c>RecordCreatorPerson</c> takes its column names from here, so the seams and the
/// BFF cannot drift. The column is field-secured: only the BFF application user(s) may write it
/// (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c> for the roots, <c>scripts/Set-ChildRecordCreatorPersonSchema.ps1</c>
/// for the children).</para>
/// </remarks>
public static class RecordCreatorPersonColumn
{
    /// <summary>The lookup's logical name (SDK writes).</summary>
    public const string LogicalName = "sprk_createdbyperson";

    /// <summary>The lookup's single-valued navigation property (Web API binds).</summary>
    public const string NavigationProperty = "sprk_CreatedByPerson";

    /// <summary>The table the lookup points at.</summary>
    public const string TargetEntity = "systemuser";

    /// <summary>
    /// Stamps <paramref name="systemUserId"/> on an SDK create payload. Nothing when there is no person — a writer that acts
    /// for nobody records nobody.
    /// </summary>
    public static void StampIfKnown(Entity entity, Guid? systemUserId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (systemUserId is { } person && person != Guid.Empty)
            entity[LogicalName] = new EntityReference(TargetEntity, person);
    }

    /// <summary>Binds <paramref name="systemUserId"/> on a Web API create payload. Nothing when there is no person.</summary>
    public static void BindIfKnown(IDictionary<string, object?> payload, Guid? systemUserId)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (systemUserId is { } person && person != Guid.Empty)
            payload[NavigationProperty + "@odata.bind"] = $"/systemusers({person:D})";
    }
}
