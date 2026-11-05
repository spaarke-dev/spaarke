using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 046 (owner decision 1, 2026-11-04: "ensure our security roles are correctly set up and
/// aligned with the required permissions &lt;&gt; functionality"): every product feature's required privileges are held by the roles
/// meant for that feature. <see cref="Requirements"/> is the ONE declared feature permission table; the "Feature permission matrix"
/// section of <c>docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md</c> cites it, so the two stay one list.
/// </summary>
/// <remarks>
/// <para><b>What a row says.</b> A feature, the role set a user of the feature holds (a base role — Spaarke Core User or Spaarke
/// Basic User — plus the add-on role the feature is meant for, if any; the roles combine, as Dataverse combines every role a user
/// holds), one privilege the feature's code asks of the caller, the least depth that serves the feature, and the code that asks it
/// (file:line). A row is met when ANY role in its set holds the privilege at that depth or deeper.</para>
/// <para><b>Depths.</b> <c>Basic</c> where the BFF asks a privilege by name at any depth (the Create and Append checks,
/// <c>RetrieveUserSetOfPrivilegesByNames</c>) or the target table is organization-owned (any depth reaches every row,
/// <c>CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync</c>). <c>Deep</c> where the caller's right on a business record
/// is asked (<c>RetrievePrincipalAccess</c>): those rows are owned by business-unit default owner teams (I-6), which a user-depth
/// grant does not reach. <c>Global</c> where the rows are owned outside the user's business units (the event-type exception in
/// <c>config/core-user-wizard-linking-role.json</c>; the analysis playbooks, skills, knowledge and tools, user-owned by their authors in
/// any business unit).</para>
/// <para><b>Not repeated here.</b> Spaarke Core User's AppendTo on every target the create wizards bind is guarded by
/// <see cref="CoreUserWizardLinkingGuardTests"/>; the ontology writer role's AppendTo on Signal and Decision Record lookup targets by
/// <see cref="OntologyWriterAppendToGuardTests"/>; role depths per table belong to task 060's ownership matrix.</para>
/// <para><b>Not guarded: tables chosen at run time.</b> The chat create, update and delete tools act on whatever table the caller
/// names, and the push writes the child tables its mapping profile names; no row can fix those tables, so the doc marks them
/// "not guarded". Their fixed halves (the push source's Read) are rows here.</para>
/// <para><b>The ratchet.</b> <see cref="KnownGaps"/> holds exactly today's gaps, each with the depth the role set holds today (the
/// deepest of its roles, or "none") and the change it waits on (proposed to unified-access-control-r2, which applies role changes with
/// the owner's go; or another project's merge). A gap not listed fails; a listed gap whose held depth differs from the baseline fails
/// (it got worse, got better without closing, or the baseline is wrong); a listed gap the role files no longer reproduce fails, so it
/// is removed. A role file that is missing, unparsable or empty fails, and so does evidence naming a file not in the repo.</para>
/// </remarks>
public class FeatureRolePrivilegeGuardTests
{
    internal sealed record Requirement(string Feature, string[] Roles, string Privilege, string MinDepth, string Evidence);

    /// <summary>A known gap: the feature row, the depth the row's role set holds today (the deepest, or "none"), and its change.</summary>
    internal sealed record KnownGap(string Feature, string Privilege, string Held, string WaitsOn);

    private const string Core = "Spaarke Core User";
    private const string Basic = "Spaarke Basic User";
    private const string Office = "Spaarke Office Add In User";
    private const string Console = "Spaarke Console User";
    private const string AiUser = "Spaarke AI Analysis User";
    private const string Writer = "Spaarke Ontology Service";
    private const string BillingAdmin = "Spaarke Billing Administrator";

    private const string Bff = "src/server/api/Sprk.Bff.Api/";
    private const string ChildCreate = Bff + "Api/ChildRecordEndpoints.cs:70-85,279-286; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:63-90";

    /// <summary>The typed event route's regarding check: AppendTo (event.attach_regarding) on the record the body names, which is one
    /// of the eight regarding types.</summary>
    private const string EventRegarding = Bff + "Api/Events/EventEndpoints.cs:131,378-405; src/server/shared/Spaarke.Dataverse/Models.cs:1439-1449; "
        + "src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:255";

    private static readonly string[] EventRegardingAppendTo =
    [
        "prvAppendTosprk_Project", "prvAppendTosprk_Matter", "prvAppendTosprk_Invoice", "prvAppendTosprk_analysis", "prvAppendToAccount",
        "prvAppendToContact", "prvAppendTosprk_WorkAssignment", "prvAppendTosprk_Budget",
    ];

    /// <summary>The playbook Read (RetrievePrincipalAccess on the row, unless public) and the scope Reads (a Record-path Read on each
    /// skill, knowledge and tool row). Playbooks and scopes are user-owned by their authors in any business unit, so Global.</summary>
    private const string AnalysisPlaybookRead = Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:307-330; "
        + Bff + "Services/RecordCreation/RecordCreationService.RouteAuthorized.cs:150-165";
    private const string AnalysisScopeRead = Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:833-835,881-892";

    /// <summary>The quick create's source Read (one of EntityAccessFilter's tables; contact is the assignee row's).</summary>
    private const string QuickCreateSource = Bff + "Api/Filters/QuickCreateSourceAccessFilter.cs:246-260; " + Bff + "Api/Filters/EntityAccessFilter.cs:115-155";

    /// <summary>A project created from a matter or project is filed under it: the ADR-024 pair names it, and AppendTo is asked on it.</summary>
    private const string QuickCreateFiledUnder = Bff + "Services/Office/OfficeService.cs:2402-2419,2633-2638; "
        + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:115-131";

    private static readonly string[] EntityAccessTablesRead =
        ["prvReadsprk_Matter", "prvReadsprk_Project", "prvReadsprk_Invoice", "prvReadsprk_WorkAssignment", "prvReadsprk_Event", "prvReadsprk_Todo", "prvReadContact"];

    private static readonly string[] EntityAccessTablesAppendTo =
    [
        "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendTosprk_Invoice", "prvAppendTosprk_WorkAssignment", "prvAppendTosprk_Event",
        "prvAppendTosprk_Todo", "prvAppendToContact",
    ];

    private static Requirement[] Wizard(string feature, string create, string append) =>
    [
        new(feature, [Core], create, "Basic", ChildCreate),
        new(feature, [Core], append, "Basic", ChildCreate),
    ];

    private static Requirement[] For(string feature, string[] roles, string minDepth, string evidence, params string[] privileges) =>
        privileges.Select(p => new Requirement(feature, roles, p, minDepth, evidence)).ToArray();

    /// <summary>The declared feature permission table (task 046). Add a row when a feature asks a new privilege of its caller.</summary>
    internal static readonly IReadOnlyList<Requirement> Requirements =
    [
        // Create wizards, through POST /api/v1/child-records/{table} (RecordCreationService as the caller). AppendTo on bind targets:
        // CoreUserWizardLinkingGuardTests.
        .. Wizard("Matter wizard", "prvCreatesprk_Matter", "prvAppendsprk_Matter"),
        .. Wizard("Project wizard", "prvCreatesprk_Project", "prvAppendsprk_Project"),
        .. Wizard("Event wizard", "prvCreatesprk_Event", "prvAppendsprk_Event"),
        .. Wizard("To Do wizard", "prvCreatesprk_Todo", "prvAppendsprk_Todo"),
        .. Wizard("Invoice wizard", "prvCreatesprk_Invoice", "prvAppendsprk_Invoice"),
        .. Wizard("Work Assignment wizard", "prvCreatesprk_WorkAssignment", "prvAppendsprk_WorkAssignment"),
        .. Wizard("Report Card wizard", "prvCreatesprk_ReportCard", "prvAppendsprk_ReportCard"),
        .. Wizard("Document upload (wizard document rows)", "prvCreatesprk_Document", "prvAppendsprk_Document"),

        // Analysis from a playbook: POST /api/ai/analysis/create. The playbook is route-authorised (task 031): Read is asked, AppendTo
        // is not (RecordCreationService.RouteAuthorized.cs:36-40,131-170).
        .. For("Analysis from a playbook", [Core], "Basic",
            Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:159; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:63-77",
            "prvCreatesprk_analysis", "prvAppendsprk_analysis"),
        .. For("Analysis from a playbook", [Core], "Global", AnalysisPlaybookRead, "prvReadsprk_analysisplaybook"),
        .. For("Analysis from a playbook", [Core], "Global", AnalysisScopeRead,
            "prvReadsprk_analysisskill", "prvReadsprk_analysisknowledge", "prvReadsprk_analysistool"),
        .. For("Analysis from a playbook", [Core], "Deep", Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:152,781-788 (analysis.attach)",
            "prvReadsprk_Document", "prvAppendTosprk_Document"),
        // The AI Analysis User add-on is held with a base role (fix round 1 K4): the same asks, met by either role of the set.
        .. For("Analysis from a playbook (Basic User + AI Analysis User)", [Basic, AiUser], "Basic",
            Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:159; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:63-77",
            "prvCreatesprk_analysis", "prvAppendsprk_analysis"),
        .. For("Analysis from a playbook (Basic User + AI Analysis User)", [Basic, AiUser], "Global", AnalysisPlaybookRead, "prvReadsprk_analysisplaybook"),
        .. For("Analysis from a playbook (Basic User + AI Analysis User)", [Basic, AiUser], "Global", AnalysisScopeRead,
            "prvReadsprk_analysisskill", "prvReadsprk_analysisknowledge", "prvReadsprk_analysistool"),
        .. For("Analysis from a playbook (Basic User + AI Analysis User)", [Basic, AiUser], "Deep",
            Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:152,781-788 (analysis.attach)", "prvReadsprk_Document", "prvAppendTosprk_Document"),

        // Events, typed route: POST /api/v1/events (Create privilege in the route filter; the rest in the service as the caller).
        .. For("Event (typed route)", [Core], "Basic", Bff + "Api/Events/EventEndpoints.cs:66,131", "prvCreatesprk_Event"),
        .. For("Event (typed route)", [Core], "Basic", Bff + "Services/Dataverse/CallerWriteAuthorization.cs:72-77", "prvAppendsprk_Event"),
        .. For("Event (typed route)", [Core], "Global",
            Bff + "Api/Events/EventEndpoints.cs:928; config/core-user-wizard-linking-role.json (sprk_eventtype_ref appendToAboveRead)",
            "prvAppendTosprk_EventType_Ref"),
        .. For("Event (typed route)", [Core], "Basic", Bff + "Api/Events/EventEndpoints.cs:949; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:390-409",
            "prvAppendTosprk_RecordType_Ref"),
        .. For("Event (typed route, Office Add In User)", [Basic, Office], "Basic", Bff + "Api/Events/EventEndpoints.cs:66,131",
            "prvCreatesprk_Event", "prvAppendsprk_Event"),
        .. For("Event (typed route, Office Add In User)", [Basic, Office], "Global",
            Bff + "Api/Events/EventEndpoints.cs:928; config/core-user-wizard-linking-role.json (sprk_eventtype_ref appendToAboveRead)",
            "prvAppendTosprk_EventType_Ref"),
        .. For("Event (typed route, Office Add In User)", [Basic, Office], "Basic",
            Bff + "Api/Events/EventEndpoints.cs:949; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:390-409", "prvAppendTosprk_RecordType_Ref"),
        // AppendTo on the regarding record (fix round 1): the route takes any of the eight regarding types from any caller; no
        // Office add-in path posts it, so both role sets the doc names are held to every type.
        .. For("Event (typed route)", [Core], "Deep", EventRegarding, EventRegardingAppendTo),
        .. For("Event (typed route, Office Add In User)", [Basic, Office], "Deep", EventRegarding, EventRegardingAppendTo),

        // Office add-in: save (POST /api/office/save) asks AppendTo on the target record; the document is created by the service.
        .. For("Office save", [Core], "Deep", Bff + "Api/Office/OfficeEndpoints.cs:185-194; " + Bff + "Api/Filters/EntityAccessFilter.cs:94,115-155,273-282",
            "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendTosprk_Invoice", "prvAppendTosprk_WorkAssignment",
            "prvAppendTosprk_Event", "prvAppendTosprk_Todo", "prvAppendToContact"),
        .. For("Office save (Basic User)", [Basic], "Deep", Bff + "Api/Office/OfficeEndpoints.cs:185-194; " + Bff + "Api/Filters/EntityAccessFilter.cs:94,115-155,273-282",
            "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendTosprk_Invoice", "prvAppendTosprk_WorkAssignment",
            "prvAppendTosprk_Event", "prvAppendTosprk_Todo", "prvAppendToContact"),
        // A version save (Word, FR-11) writes a new version of an existing document: Write on its row, as the caller.
        .. For("Office version save", [Core], "Deep",
            Bff + "Api/Office/OfficeEndpoints.cs:194; " + Bff + "Api/Filters/OfficeVersionSaveAuthorizationFilter.cs:53,89", "prvWritesprk_Document"),
        .. For("Office version save (Basic User)", [Basic], "Deep",
            Bff + "Api/Office/OfficeEndpoints.cs:194; " + Bff + "Api/Filters/OfficeVersionSaveAuthorizationFilter.cs:53,89", "prvWritesprk_Document"),

        // Office quick create (POST /api/office/quickcreate/{entityType}): Create in the filter; Matter and Project through the service.
        .. For("Office quick create", [Core], "Basic", Bff + "Api/Filters/QuickCreateSourceAccessFilter.cs:100-117; " + Bff + "Api/Filters/FinanceAuthorizationFilter.cs:335",
            "prvCreatesprk_Matter", "prvCreatesprk_Project", "prvCreatesprk_Invoice"),
        .. For("Office quick create", [Core], "Basic", Bff + "Services/Office/OfficeService.cs:2428-2445",
            "prvAppendsprk_Matter", "prvAppendsprk_Project"),
        .. For("Office quick create", [Core], "Deep", Bff + "Api/Filters/QuickCreateSourceAccessFilter.cs:310-345; " + Bff + "Services/Office/OfficeService.cs:2437-2455",
            "prvReadContact", "prvAppendToContact"),
        .. For("Office quick create", [Core], "Basic", Bff + "Services/Office/OfficeService.cs:2408-2419", "prvAppendTosprk_RecordType_Ref"),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Basic",
            Bff + "Api/Filters/QuickCreateSourceAccessFilter.cs:100-117; " + Bff + "Api/Filters/FinanceAuthorizationFilter.cs:335",
            "prvCreatesprk_Matter", "prvCreatesprk_Project", "prvCreatesprk_Invoice"),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Basic", Bff + "Services/Office/OfficeService.cs:2428-2445",
            "prvAppendsprk_Matter", "prvAppendsprk_Project"),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Deep",
            Bff + "Api/Filters/QuickCreateSourceAccessFilter.cs:310-345; " + Bff + "Services/Office/OfficeService.cs:2437-2455",
            "prvReadContact", "prvAppendToContact"),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Basic", Bff + "Services/Office/OfficeService.cs:2408-2419",
            "prvAppendTosprk_RecordType_Ref"),
        // Read on the named source (fix round 1; contact is the assignee row above), and AppendTo on the matter or project a new
        // project is filed under.
        .. For("Office quick create", [Core], "Deep", QuickCreateSource, EntityAccessTablesRead.Where(p => p != "prvReadContact").ToArray()),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Deep", QuickCreateSource,
            EntityAccessTablesRead.Where(p => p != "prvReadContact").ToArray()),
        .. For("Office quick create", [Core], "Deep", QuickCreateFiledUnder, "prvAppendTosprk_Matter", "prvAppendTosprk_Project"),
        .. For("Office quick create (Office Add In User)", [Basic, Office], "Deep", QuickCreateFiledUnder, "prvAppendTosprk_Matter", "prvAppendTosprk_Project"),

        // Office To Do (POST /api/office/todo): Create and Read in the filter; the row is created by the service.
        .. For("Office To Do", [Core], "Basic", Bff + "Api/Filters/TodoSourceAccessFilter.cs:221-259", "prvCreatesprk_Todo"),
        .. For("Office To Do", [Core], "Deep", Bff + "Api/Filters/TodoSourceAccessFilter.cs:99,202-212",
            "prvReadsprk_Matter", "prvReadsprk_Project", "prvReadsprk_Invoice", "prvReadsprk_Document", "prvReadsprk_Communication", "prvReadContact"),

        // Mapping-source reads (every create that maps; the push's source): Read on the source, as the caller.
        .. For("Mapping-source read", [Core], "Deep",
            Bff + "Services/Dataverse/FieldMapping/MappingSourceReadProbe.cs:119,141-171; " + Bff + "Api/Filters/EntityAccessFilter.cs:115-155",
            "prvReadsprk_Matter", "prvReadsprk_Project", "prvReadsprk_Invoice", "prvReadsprk_WorkAssignment", "prvReadsprk_Event",
            "prvReadsprk_Todo", "prvReadContact"),

        // Push (POST /api/v1/field-mappings/push): Read on the source, as the caller, before anything is read app-only. The child
        // writes are the caller's own (MSCRMCallerID) on the tables the mapping profile names: chosen at run time, not guarded here.
        .. For("Push", [Core], "Deep",
            Bff + "Api/FieldMappings/FieldMappingEndpoints.cs:814-840; " + Bff + "Api/Filters/EntityAccessFilter.cs:115-155", EntityAccessTablesRead),

        // Record-keyed upload (PUT /api/obo/records/{entityLogicalName}/{recordId}/files, POST .../upload-session): AppendTo on the
        // owning record (entity.associate_document), one of EntityAccessFilter's tables.
        .. For("Document upload (record-keyed)", [Core], "Deep",
            Bff + "Api/OBOEndpoints.cs:102,225,397,500; " + Bff + "Api/Filters/RecordRouteAccessAuthorizationFilter.cs:188; "
            + Bff + "Api/Filters/EntityAccessFilter.cs:115-155",
            EntityAccessTablesAppendTo),

        // Decision commit in the Console (POST /api/v1/signals/{signalId}/decision): every Signal and its core record read as the
        // caller, AppendTo on the core record, Write on the subject. Signals and core records are team-owned (I-6): Deep.
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/SignalCoreRecordAccess.cs:191-193; " + Bff + "Services/Signals/DecisionCommitService.cs:456-466",
            "prvReadsprk_Signal"),
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/SignalCoreRecordAccess.cs:239-247; " + Bff + "Services/Dataverse/CoreAncestorResolver.cs:67-73",
            "prvReadsprk_Matter", "prvReadsprk_Project", "prvReadsprk_WorkAssignment", "prvReadsprk_ServiceRequest"),
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/DecisionCallerRights.cs:61; " + Bff + "Services/Dataverse/CoreAncestorResolver.cs:67-73",
            "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendTosprk_WorkAssignment", "prvAppendTosprk_ServiceRequest"),
        .. For("Decision commit (Console)", [Core, Console], "Deep", Bff + "Services/Signals/DecisionCallerRights.cs:42,79",
            "prvWritesprk_Todo", "prvWritesprk_Event"),

        // Signal, Decision Record and Budget Revision writers (the app users' role; D-204: only they create these rows).
        .. For("Signal and Decision Record writers", [Writer], "Global",
            Bff + "Services/Signals/SignalWriter.cs:526,653,720; " + Bff + "Services/Signals/DecisionRecordWriter.cs:511,559; "
            + Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:390,455",
            "prvCreatesprk_Signal", "prvWritesprk_Signal", "prvCreatesprk_DecisionRecord", "prvCreatesprk_BudgetRevision"),

        // Billing (ebilling-r1 task 089e, owner-approved): removing or previewing a Procedure override asks Read on the Procedure.
        // The route (ProcedureAssignmentEndpoints.cs) comes with ebilling-r1 089e; on this branch the requirement is its task.
        .. For("Billing: Procedure override remove / preview", [BillingAdmin], "Deep",
            "projects/ebilling-r1/tasks/089-assignment-override-service.poml:40,113 (the route comes with ebilling-r1 089e)", "prvReadsprk_procedure"),
    ];

    /// <summary>The ratchet: today's gaps (task 046), each with the change it waits on. Remove an entry when its change lands.</summary>
    internal static readonly IReadOnlyList<KnownGap> KnownGaps =
    [
        new("Report Card wizard", "prvCreatesprk_ReportCard", "none",
            "G1. UAC-r2 role change proposed by task 046: Spaarke Core User Create on sprk_reportcard (Deep)."),
        new("Report Card wizard", "prvAppendsprk_ReportCard", "none",
            "G1. UAC-r2 role change proposed by task 046: Spaarke Core User Append on sprk_reportcard (Deep)."),
        new("Event (typed route, Office Add In User)", "prvAppendTosprk_EventType_Ref", "Deep",
            "G2. UAC-r2 role change proposed by task 046: Spaarke Office Add In User AppendTo on sprk_eventtype_ref Deep -> Global (task 031 K1)."),
        new("Office save (Basic User)", "prvAppendToContact", "Basic",
            "G3. UAC-r2 decision asked by task 046: Spaarke Basic User AppendTo on contact Basic -> Deep (its Read depth)."),
        new("Office quick create (Office Add In User)", "prvAppendToContact", "Basic",
            "G4. UAC-r2 decision asked by task 046: AppendTo on contact at Deep for Basic User + Office Add In User (either role)."),
        new("Billing: Procedure override remove / preview", "prvReadsprk_procedure", "none",
            "G5. ebilling-r1 task 089e (branch task/ebilling-089e) adds it to the role file; applied on dev already."),
        new("Office version save (Basic User)", "prvWritesprk_Document", "none",
            "G6. UAC-r2 decision asked by task 046 fix round 1: Spaarke Basic User Write on sprk_document at Deep, or the version save is Core User only."),
        new("Event (typed route, Office Add In User)", "prvAppendToAccount", "Basic",
            "G7. UAC-r2 decision asked by task 046 fix round 1: AppendTo on account at Deep for Basic User + Office Add In User (either role)."),
        new("Event (typed route, Office Add In User)", "prvAppendToContact", "Basic",
            "G7. UAC-r2 decision asked by task 046 fix round 1: AppendTo on contact at Deep for Basic User + Office Add In User (G3's change covers it)."),
        new("Event (typed route, Office Add In User)", "prvAppendTosprk_Budget", "none",
            "G8. UAC-r2 decision asked by task 046 fix round 1: AppendTo on sprk_budget at Deep for Basic User + Office Add In User, or events regarding a budget are Core User only."),
    ];

    private static readonly string[] DepthOrder = ["Basic", "Local", "Deep", "Global"];

    private static string RolesDirectory => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");

    [Fact(DisplayName = "046: every feature's required privileges are held by the roles meant for it, or the gap is in the ratchet baseline")]
    public void FeatureRequirementsAreHeldByTheirRoles()
    {
        var roles = LoadRoles(RolesDirectory, Requirements.SelectMany(r => r.Roles));
        var (unlisted, stale, changed) = Ratchet(Gaps(Requirements, roles), KnownGaps);

        Assert.True(unlisted.Count == 0,
            "A role meant for a feature lacks a privilege the feature's code asks of the caller (task 046 feature permission matrix). "
            + "Propose the role change to unified-access-control-r2 (it applies role changes with the owner's go), or correct the "
            + "declared row with its code evidence:\n  " + string.Join("\n  ", unlisted));
        Assert.True(changed.Count == 0,
            "A known gap's held depth is not the depth the ratchet baseline records (the gap got worse, narrowed without closing, or "
            + "the baseline is wrong). Correct the role file, or update the KnownGaps entry and the doc's gap table:\n  "
            + string.Join("\n  ", changed));
        Assert.True(stale.Count == 0,
            "A ratchet baseline entry no longer reproduces (the role now holds the privilege, or the row changed) — remove it from "
            + "KnownGaps:\n  " + string.Join("\n  ", stale));
    }

    [Fact(DisplayName = "046 ratchet controls: a gap not in the baseline is unlisted; a baseline entry with no gap is stale; a listed gap is neither")]
    public void RatchetReportsUnlistedAndStale()
    {
        var found = new List<Gap>
        {
            new("F-listed", "prvA", "Basic", "none", ["R"]),
            new("F-new", "prvB", "Deep", "Basic", ["R"]),
        };
        var baseline = new KnownGap[] { new("F-listed", "PRVA", "NONE", "x"), new("F-fixed", "prvC", "none", "x") };

        var (unlisted, stale, changed) = Ratchet(found, baseline);

        Assert.Equal(new[] { "F-new: prvB at Deep or deeper in [R] — held: Basic" }, unlisted);
        Assert.Equal(new[] { "F-fixed: prvC" }, stale);
        Assert.Empty(changed); // the listed gap's held depth matches (other casing)
    }

    [Fact(DisplayName = "046 ratchet must-fire (K1): a listed gap whose held depth differs from the baseline — worse, narrower, or a bogus level — is reported")]
    public void RatchetFiresOnAChangedHeldDepth()
    {
        var found = new List<Gap>
        {
            new("F-worse", "prvA", "Deep", "none", ["R"]),     // baseline says Basic: the grant was removed
            new("F-narrower", "prvB", "Global", "Deep", ["R"]), // baseline says Basic: deeper, still short of Global
            new("F-bogus", "prvC", "Deep", "Basic", ["R"]),     // baseline names a level that does not exist
            new("F-same", "prvD", "Deep", "Basic", ["R"]),      // must-not-fire: held as recorded
        };
        var baseline = new KnownGap[]
        {
            new("F-worse", "prvA", "Basic", "x"), new("F-narrower", "prvB", "Basic", "x"), new("F-bogus", "prvC", "Deeep", "x"),
            new("F-same", "prvD", "basic", "x"),
        };

        var (unlisted, stale, changed) = Ratchet(found, baseline);

        Assert.Empty(unlisted);
        Assert.Empty(stale);
        Assert.Equal(
            new[] { "F-worse: prvA — held: none, baseline: Basic", "F-narrower: prvB — held: Deep, baseline: Basic", "F-bogus: prvC — held: Basic, baseline: Deeep" },
            changed);
    }

    [Fact(DisplayName = "046 ratchet must-fire (K1): a seeded copy of Spaarke Basic User with G3's partial grant removed reports G3's held depth as changed")]
    public void RatchetFiresOnASeededRoleFileWhoseGapGotWorse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var doc = XDocument.Parse(File.ReadAllText(Path.Combine(RolesDirectory, Basic + ".xml")));
            doc.Descendants().Where(e => e.Name.LocalName == "RolePrivilege"
                                         && Same((string?)e.Attribute("name") ?? "", "prvAppendToContact")).Remove();
            doc.Save(Path.Combine(dir, Basic + ".xml"));

            var rows = Requirements.Where(r => r.Feature == "Office save (Basic User)").ToArray();
            var (unlisted, _, changed) = Ratchet(Gaps(rows, LoadRoles(dir, rows.SelectMany(r => r.Roles))), KnownGaps);

            Assert.Empty(unlisted);
            Assert.Equal(new[] { "Office save (Basic User): prvAppendToContact — held: none, baseline: Basic" }, changed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The gaps the baseline does not list, the baseline entries no gap reproduces, and the listed gaps whose held depth is
    /// not the baseline's.</summary>
    internal static (List<string> Unlisted, List<string> Stale, List<string> Changed) Ratchet(IReadOnlyList<Gap> found, IReadOnlyList<KnownGap> baseline)
    {
        KnownGap? Listed(Gap g) => baseline.FirstOrDefault(k => k.Feature == g.Feature && Same(k.Privilege, g.Privilege));
        return (
            found.Where(g => Listed(g) is null).Select(g => g.Describe()).ToList(),
            baseline.Where(k => !found.Any(g => g.Feature == k.Feature && Same(k.Privilege, g.Privilege))).Select(k => $"{k.Feature}: {k.Privilege}").ToList(),
            found.Where(g => Listed(g) is { } k && !Same(k.Held, g.Held))
                .Select(g => $"{g.Feature}: {g.Privilege} — held: {g.Held}, baseline: {Listed(g)!.Held}").ToList());
    }

    [Fact(DisplayName = "046: the declared table and the baseline are well formed (known roles and depths, evidence in the repo, no duplicate, every gap has a row)")]
    public void DeclaredTableIsWellFormed()
    {
        Assert.All(Requirements, r =>
        {
            Assert.Contains(r.MinDepth, DepthOrder);
            Assert.NotEmpty(r.Roles);
            Assert.StartsWith("prv", r.Privilege, StringComparison.Ordinal);
            Assert.Matches(@":\d", r.Evidence); // file:line
        });
        var missingEvidence = Requirements.SelectMany(r => MissingEvidencePaths(r.Evidence, SourceScan.RepoRoot).Select(p => $"{r.Feature} / {r.Privilege}: {p}"))
            .Distinct().ToArray();
        Assert.True(missingEvidence.Length == 0,
            "A row's evidence names a file that is not in the repo (renamed, moved, or on another branch):\n  " + string.Join("\n  ", missingEvidence));
        var duplicates = Requirements.GroupBy(r => (r.Feature, r.Privilege.ToLowerInvariant())).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.Empty(duplicates);
        Assert.All(KnownGaps, k =>
        {
            var row = Assert.Single(Requirements, r => r.Feature == k.Feature && Same(r.Privilege, k.Privilege));
            Assert.False(string.IsNullOrWhiteSpace(k.WaitsOn));
            Assert.True(Same(k.Held, "none") || Rank(k.Held) >= 0, $"{k.Feature}: {k.Privilege} — baseline held '{k.Held}' is not a depth or 'none'.");
            Assert.True(Rank(k.Held) < Rank(row.MinDepth), $"{k.Feature}: {k.Privilege} — baseline held '{k.Held}' meets the row's {row.MinDepth}: no gap.");
        });
        Assert.Equal(KnownGaps.Count, KnownGaps.Select(k => (k.Feature, k.Privilege.ToLowerInvariant())).Distinct().Count());
    }

    [Fact(DisplayName = "046 evidence controls (K2): a path not in the repo or a segment with no path is reported; a path in the repo is not")]
    public void EvidencePathCheckFires()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-feature-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "a"));
            File.WriteAllText(Path.Combine(root, "src", "a", "Present.cs"), "");

            Assert.Empty(MissingEvidencePaths("src/a/Present.cs:1-2; src/a/Present.cs:9 (note: with a colon)", root)); // must-not-fire
            Assert.Equal(new[] { "src/a/Gone.cs" }, MissingEvidencePaths("src/a/Present.cs:1; src/a/Gone.cs:4", root));  // must-fire
            Assert.Equal(new[] { "(no path)" }, MissingEvidencePaths("src/a/Present.cs:1; :12", root));                 // must-fire
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        // Seeded drift against the real repo: the Billing row's former evidence names a file that is not on this branch.
        Assert.Equal(new[] { Bff + "Api/Finance/ProcedureAssignmentEndpoints.cs" },
            MissingEvidencePaths(Bff + "Api/Finance/ProcedureAssignmentEndpoints.cs:114-133,310-380 (branch task/ebilling-089e)", SourceScan.RepoRoot));
    }

    /// <summary>Each "; "-separated evidence segment starts with a repo-relative path (up to its first ':', ' ' or '('); the paths that
    /// do not exist under <paramref name="root"/>, and "(no path)" for a segment that names none.</summary>
    internal static List<string> MissingEvidencePaths(string evidence, string root) =>
        evidence.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment[..IndexOfAnyOrLength(segment, ':', ' ', '(')])
            .Where(path => path.Length == 0 || !File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(path => path.Length == 0 ? "(no path)" : path)
            .Distinct()
            .ToList();

    private static int IndexOfAnyOrLength(string s, params char[] chars) => s.IndexOfAny(chars) is var i and >= 0 ? i : s.Length;

    [Fact(DisplayName = "046 must-fire: a role missing a required privilege, holding it below the declared depth, or a new feature row whose role lacks it, is a gap")]
    public void GuardFires()
    {
        var roles = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["R1"] = Privileges(("prvCreatesprk_Matter", "Deep"), ("prvAppendToContact", "Basic")),
        };
        var requirements = new Requirement[]
        {
            new("F-missing", ["R1"], "prvAppendsprk_Matter", "Basic", "x.cs:1"),       // missing
            new("F-shallow", ["R1"], "prvAppendToContact", "Deep", "x.cs:2"),         // held below depth
            new("F-new", ["R1"], "prvCreatesprk_ReportCard", "Basic", "x.cs:3"),     // a seeded new feature row
        };

        var found = Gaps(requirements, roles);

        Assert.Equal(new[] { "F-missing", "F-new", "F-shallow" }, found.Select(g => g.Feature).Order(StringComparer.Ordinal));
        Assert.Contains(found, g => g.Feature == "F-shallow" && g.Describe().Contains("Basic", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "046 must-fire: a source role file with a required privilege removed is reported (seeded copy of Spaarke Core User)")]
    public void GuardFiresOnASeededRoleFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var doc = XDocument.Parse(File.ReadAllText(Path.Combine(RolesDirectory, Core + ".xml")));
            doc.Descendants().Where(e => e.Name.LocalName == "RolePrivilege"
                                         && Same((string?)e.Attribute("name") ?? "", "prvCreatesprk_Matter")).Remove();
            doc.Save(Path.Combine(dir, Core + ".xml"));

            var rows = Requirements.Where(r => r.Feature == "Matter wizard").ToArray();
            var found = Gaps(rows, LoadRoles(dir, rows.SelectMany(r => r.Roles)));

            Assert.Equal(new[] { "Matter wizard: prvCreatesprk_Matter" }, found.Select(g => $"{g.Feature}: {g.Privilege}"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "046 must-not-fire: held at the declared depth or deeper, held by another role of the set, and a privilege no feature declares")]
    public void GuardDoesNotFire()
    {
        var roles = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Base"] = Privileges(("PRVAPPENDTOCONTACT", "Deep"), ("prvDeletesprk_Matter", "Global")),
            ["AddOn"] = Privileges(("prvAppendTosprk_EventType_Ref", "Global")),
        };
        var requirements = new Requirement[]
        {
            new("F-equal", ["Base"], "prvAppendToContact", "Deep", "x.cs:1"),             // exact depth, other casing
            new("F-deeper", ["AddOn"], "prvAppendTosprk_EventType_Ref", "Deep", "x.cs:2"), // deeper than declared
            new("F-union", ["Base", "AddOn"], "prvAppendTosprk_EventType_Ref", "Global", "x.cs:3"), // another role of the set
        };

        Assert.Empty(Gaps(requirements, roles)); // prvDeletesprk_Matter is declared by no row and is not reported
    }

    [Fact(DisplayName = "046 controls: a missing, unparsable or empty role file fails the load")]
    public void RoleFileLoadFailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Unparsable.xml"), "<Role name=\"Unparsable\"><RolePrivileges>");
            File.WriteAllText(Path.Combine(dir, "Empty.xml"), "<Role name=\"Empty\"><RolePrivileges /></Role>");
            File.WriteAllText(Path.Combine(dir, "Renamed.xml"), "<Role name=\"Other\"><RolePrivileges><RolePrivilege name=\"prvReadContact\" level=\"Deep\" /></RolePrivileges></Role>");

            Assert.Contains("missing", Assert.Throws<InvalidOperationException>(() => LoadRoles(dir, ["Missing"])).Message, StringComparison.Ordinal);
            Assert.Contains("unparsable", Assert.Throws<InvalidOperationException>(() => LoadRoles(dir, ["Unparsable"])).Message, StringComparison.Ordinal);
            Assert.Contains("no privileges", Assert.Throws<InvalidOperationException>(() => LoadRoles(dir, ["Empty"])).Message, StringComparison.Ordinal);
            Assert.Contains("names role", Assert.Throws<InvalidOperationException>(() => LoadRoles(dir, ["Renamed"])).Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── The scan ────────────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Gap(string Feature, string Privilege, string MinDepth, string Held, string[] Roles)
    {
        internal string Describe() =>
            $"{Feature}: {Privilege} at {MinDepth} or deeper in [{string.Join(" + ", Roles)}] — held: {Held}";
    }

    /// <summary>Every requirement that no role of its set holds at its depth or deeper, with the deepest depth the set holds ("none"
    /// when no role of the set holds the privilege).</summary>
    internal static List<Gap> Gaps(IEnumerable<Requirement> requirements, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> roles)
    {
        var gaps = new List<Gap>();
        foreach (var r in requirements)
        {
            var deepest = r.Roles
                .Select(role => roles.TryGetValue(role, out var p) && p.TryGetValue(r.Privilege, out var level) ? level : null)
                .Where(level => level is not null)
                .Select(level => level!)
                .OrderByDescending(Rank)
                .FirstOrDefault();
            if (deepest is null || Rank(deepest) < Rank(r.MinDepth))
                gaps.Add(new Gap(r.Feature, r.Privilege, r.MinDepth, deepest ?? "none", r.Roles));
        }

        return gaps;
    }

    /// <summary>
    /// Each named role's privileges (name, case-insensitive → level) from <c>{directory}/{role}.xml</c>. A file that is missing,
    /// unparsable, names another role or holds no privilege throws: the guard never reads an absent role as "no gap".
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadRoles(string directory, IEnumerable<string> roleNames)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var name in roleNames.Distinct(StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, name + ".xml");
            if (!File.Exists(path))
                throw new InvalidOperationException($"Role file for '{name}' is missing: {path}");
            XDocument doc;
            try
            {
                doc = XDocument.Parse(File.ReadAllText(path));
            }
            catch (System.Xml.XmlException ex)
            {
                throw new InvalidOperationException($"Role file for '{name}' is unparsable: {path} ({ex.Message})", ex);
            }

            var declared = (string?)doc.Root?.Attribute("name");
            if (!string.Equals(declared, name, StringComparison.Ordinal))
                throw new InvalidOperationException($"Role file {path} names role '{declared}', not '{name}'.");
            var privileges = doc.Descendants().Where(e => e.Name.LocalName == "RolePrivilege")
                .Select(e => ((string?)e.Attribute("name") ?? string.Empty, (string?)e.Attribute("level") ?? string.Empty))
                .ToArray();
            if (privileges.Length == 0)
                throw new InvalidOperationException($"Role file for '{name}' holds no privileges: {path}");
            result[name] = Privileges(privileges);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> Privileges(params (string Name, string Level)[] privileges)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, level) in privileges)
        {
            if (!map.TryGetValue(name, out var existing) || Rank(level) > Rank(existing))
                map[name] = level;
        }

        return map;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The depth's rank; an unknown level ranks below Basic, so it never meets a requirement.</summary>
    private static int Rank(string level) => Array.FindIndex(DepthOrder, d => Same(d, level));
}
