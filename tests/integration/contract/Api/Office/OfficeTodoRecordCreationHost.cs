using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 030a: what an Office To Do host needs once <c>POST /api/office/todo</c> creates through
/// <c>RecordCreationService</c> as the caller. The caller's Dataverse (<see cref="OfficeCallerDataverseDouble"/>) is scripted for
/// <c>sprk_todo</c> and the records a To Do names, and the service's one app-only create
/// (<see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/>) is captured and viewed as the SDK entity the old
/// <c>IDataverseService.CreateAsync</c> capture held (<see cref="RecordCreationWriteCapture"/>), so a suite that asserted on that
/// entity asserts the same thing.
/// </summary>
/// <remarks>
/// The To Do suites name their records by fresh ids per test, so the tables a To Do names are OPEN in the caller's world
/// (<see cref="OfficeCallerDataverseDouble.OpenTables"/>): every row exists and the caller holds every right on it, unless a test
/// lists it in <see cref="OfficeCallerDataverseDouble.NoAppendTo"/> or <see cref="OfficeCallerDataverseDouble.Missing"/>. The
/// caller <c>WhoAmI()</c> names is <see cref="Caller"/>, whatever the endpoint's resolver answered: the service identifies the
/// caller under their own token, not through that resolver.
/// </remarks>
public sealed class OfficeTodoRecordCreationHost
{
    /// <summary>The systemuser <c>WhoAmI()</c> names for the caller.</summary>
    public static readonly Guid Caller = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1030");

    /// <summary>The tables a To Do names (its regarding records, carriers and Assigned To contact), with their entity sets.</summary>
    private static readonly (string Table, string EntitySet, string PrivilegeSchema)[] NamedTables =
    [
        ("sprk_todo", "sprk_todos", "sprk_Todo"),
        ("sprk_document", "sprk_documents", "sprk_Document"),
        ("sprk_communication", "sprk_communications", "sprk_Communication"),
        ("sprk_workassignment", "sprk_workassignments", "sprk_WorkAssignment"),
        ("sprk_servicerequest", "sprk_servicerequests", "sprk_ServiceRequest"),
    ];

    public OfficeTodoRecordCreationHost()
    {
        CallerDataverse = new OfficeCallerDataverseDouble(
            new MappingSourceCallerReadDouble(id => CallerReadableRows.TryGetValue(id, out var row) ? row : null), () => Caller)
        {
            Me = Caller,
        };
        SeedTodoWorld(CallerDataverse);
        Capture = RecordCreationWriteCapture.Attach(FieldMappings, CallerDataverse);
    }

    /// <summary>
    /// The rows the caller's own read of a mapping source answers (the #2188 mask read), by id; none by default, so a source is
    /// unreadable to the caller unless a test lists it here.
    /// </summary>
    public Dictionary<Guid, Entity> CallerReadableRows { get; } = new();

    /// <summary>The service's app-only writer and reader (the create, and the mapping profile reads: none by default).</summary>
    public Mock<IFieldMappingDataverseService> FieldMappings { get; } = new(MockBehavior.Loose);

    /// <summary>The caller's Dataverse, as the service questions it.</summary>
    public OfficeCallerDataverseDouble CallerDataverse { get; }

    /// <summary>The service's app-only create, captured.</summary>
    public RecordCreationWriteCapture Capture { get; }

    /// <summary>Every created row, as the SDK entity the old create capture held, in order.</summary>
    public IReadOnlyList<Entity> CreatedEntities => Capture.Entities;

    /// <summary>Registers the caller's Dataverse and the app-only writer in place of the host's.</summary>
    public void Register(IServiceCollection services)
    {
        services.RemoveAll<IFieldMappingDataverseService>();
        services.AddSingleton(FieldMappings.Object);
        services.RemoveAll<IDataverseUserClient>();
        services.AddSingleton<IDataverseUserClient>(CallerDataverse);
    }

    /// <summary>
    /// The <c>sprk_todo</c> world: the table, its lookups (live navigation-property casing, the schema name), its columns, the
    /// tables its lookups name (open), and the privileges a caller who may create a To Do holds.
    /// </summary>
    private static void SeedTodoWorld(OfficeCallerDataverseDouble world)
    {
        foreach (var (table, entitySet, schema) in NamedTables)
            world.Tables[table] = (entitySet, "UserOwned", schema, table + "id");

        // The ADR-024 record-type catalog (organization-owned): the rows a typed pair names, as the catalog holds them.
        world.Tables["sprk_recordtype_ref"] = ("sprk_recordtype_refs", "OrganizationOwned", "sprk_RecordType_Ref", "sprk_recordtype_refid");
        world.Rows[("sprk_recordtype_ref", RecordTypeCatalogSetup.MatterTypeId)] = new() { ["sprk_recordlogicalname"] = "sprk_matter" };
        world.Rows[("sprk_recordtype_ref", RecordTypeCatalogSetup.ProjectTypeId)] = new() { ["sprk_recordlogicalname"] = "sprk_project" };

        world.OpenTables.UnionWith(
        [
            "sprk_matter", "sprk_project", "sprk_invoice", "sprk_document", "sprk_communication", "sprk_workassignment",
            "sprk_servicerequest", "contact", "sprk_recordtype_ref", "team", "systemuser",
        ]);

        world.Lookups["sprk_todo"] =
        [
            ("sprk_regardingmatter", "sprk_matter", "sprk_RegardingMatter"),
            ("sprk_regardingproject", "sprk_project", "sprk_RegardingProject"),
            ("sprk_regardinginvoice", "sprk_invoice", "sprk_RegardingInvoice"),
            ("sprk_regardingworkassignment", "sprk_workassignment", "sprk_RegardingWorkAssignment"),
            ("sprk_regardingservicerequest", "sprk_servicerequest", "sprk_RegardingServiceRequest"),
            ("sprk_regardingdocument", "sprk_document", "sprk_RegardingDocument"),
            ("sprk_regardingcommunication", "sprk_communication", "sprk_RegardingCommunication"),
            ("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_RegardingRecordType"),
            ("sprk_assignedto", "contact", "sprk_AssignedTo"),
            ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
            ("ownerid", "team", "ownerid"),
        ];

        world.Columns["sprk_todo"] = new(StringComparer.Ordinal)
        {
            ["sprk_name"] = ("String", true),
            ["sprk_description"] = ("Memo", true),
            ["sprk_duedate"] = ("DateTime", true),
            ["sprk_priorityscore"] = ("Integer", true),
            ["sprk_effortscore"] = ("Integer", true),
            ["statecode"] = ("State", true),
            ["statuscode"] = ("Status", true),
            ["sprk_regardingrecordid"] = ("String", true),
            ["sprk_regardingrecordname"] = ("String", true),
            ["sprk_accesspermission"] = ("Picklist", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
            ["sprk_regardingproject"] = ("Lookup", true),
            ["sprk_regardinginvoice"] = ("Lookup", true),
            ["sprk_regardingworkassignment"] = ("Lookup", true),
            ["sprk_regardingservicerequest"] = ("Lookup", true),
            ["sprk_regardingdocument"] = ("Lookup", true),
            ["sprk_regardingcommunication"] = ("Lookup", true),
            ["sprk_regardingrecordtype"] = ("Lookup", true),
            ["sprk_assignedto"] = ("Lookup", true),
            ["sprk_createdbyperson"] = ("Lookup", true),
            ["ownerid"] = ("Owner", true),
        };
        world.DateBehaviours[("sprk_todo", "sprk_duedate")] = "DateOnly";

        world.Held.UnionWith(
        [
            "prvCreatesprk_Todo", "prvAppendsprk_Todo",
            "prvAppendTosprk_Document", "prvAppendTosprk_Communication", "prvAppendTosprk_WorkAssignment",
            "prvAppendTosprk_ServiceRequest",
            "prvReadsprk_RecordType_Ref", "prvAppendTosprk_RecordType_Ref",
        ]);
    }
}
