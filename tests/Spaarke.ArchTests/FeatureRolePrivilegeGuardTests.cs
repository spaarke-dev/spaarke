using System.Text.Json;
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
/// <para><b>Standalone add-ons (task 046b; owner, 2026-11-05).</b> An add-on role may be held without a base role, so each add-on
/// must meet its own features alone. <see cref="RoleClasses"/> classifies every role file in the folder (base, add-on, app users'
/// role); a file it does not list fails. For each add-on it names the declared features the add-on exists for, and
/// <see cref="Requirements"/> repeats their rows with the add-on as the only role ("… [role alone]"). The gaps those rows find wait on
/// one owner decision through unified-access-control-r2; no role file changes for them here.</para>
/// <para><b>Must not hold (task 046b).</b> <see cref="MustNotHold"/> lists privileges no person role holds at any depth (every role
/// file except the app users' role), each with its exceptions: the writer-only tables, and Create on the invoice line.</para>
/// <para><b>The ruled changes (task 046b).</b> <c>config/feature-role-privileges.json</c> lists the role changes the owner ruled on
/// task 046's gaps; <c>scripts/Set-FeatureRolePrivileges.ps1</c> applies it to an environment. The role files hold each change at
/// its declared depth, and putting any one back to its earlier level fails this guard.</para>
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
    private const string OntologyAdmin = "Spaarke Ontology Administrator";
    private const string AiAdmin = "Spaarke AI Analysis Admin";

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

    /// <summary>The Office "Save as version" offer needs Read on the colliding document, asked of the caller.</summary>
    private const string VersionOfferRead = Bff + "Api/Office/OfficeEndpoints.cs:547-605";

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

    /// <summary>The declared feature permission table (task 046). Add a row when a feature asks a new privilege of its caller.
    /// <see cref="Requirements"/> is this table plus each add-on's rows alone (<see cref="RoleClasses"/>), except the ruled exceptions
    /// (<see cref="StandaloneExceptions"/>).</summary>
    private static readonly IReadOnlyList<Requirement> Declared =
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
        // KPI assessment (the secure-record ribbon's "New KPI Assessment" on a report card, through the same route): the row is
        // filed under the report card, so AppendTo is asked on it as the caller (G1, owner 2026-11-05: Deep).
        .. Wizard("KPI assessment (filed under a report card)", "prvCreatesprk_KPIAssessment", "prvAppendsprk_KPIAssessment"),
        .. For("KPI assessment (filed under a report card)", [Core], "Deep",
            ChildCreate + "; " + Bff + "Services/Access/SecureChildLineage.cs:302-303", "prvAppendTosprk_ReportCard"),

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
        // A created budget binds its currency (transactioncurrencyid@odata.bind), so AppendTo on the currency is asked as the
        // caller. transactioncurrency is organization-owned: any depth reaches it.
        .. For("Decision actions (Console)", [Core, Console], "Basic",
            Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:700", "prvAppendToTransactionCurrency"),
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
        // A version save (Word, FR-11) writes a new version of an existing document: Write on its row, as the caller. The owner put
        // that Write on Office Add In User, not Basic User (G6, 2026-11-05), so a Basic User saves versions with the add-on.
        .. For("Office version save", [Core], "Deep",
            Bff + "Api/Office/OfficeEndpoints.cs:194; " + Bff + "Api/Filters/OfficeVersionSaveAuthorizationFilter.cs:53,89", "prvWritesprk_Document"),
        .. For("Office version save (Basic User + Office Add In User)", [Basic, Office], "Deep",
            Bff + "Api/Office/OfficeEndpoints.cs:194; " + Bff + "Api/Filters/OfficeVersionSaveAuthorizationFilter.cs:53,89", "prvWritesprk_Document"),
        // K4 (fix round 1): the version save itself reads the row app-only (OfficeDocumentPersistence.cs:633-646), but the "Save as
        // version" offer on a name collision is made only to a caller who holds Read on the colliding document, asked as the caller
        // (OfficeEndpoints.cs:547-605, GetCallerAccessAsync). After G6 the Write is on Office Add In User and the Read is not, so the
        // add-on alone shows the gap below.
        .. For("Office version save", [Core], "Deep", VersionOfferRead, "prvReadsprk_Document"),
        .. For("Office version save (Basic User + Office Add In User)", [Basic, Office], "Deep", VersionOfferRead, "prvReadsprk_Document"),

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

        // Office To Do (POST /api/office/todo): Create and Read in the filter; the row is created by the service. Core-only (owner,
        // 2026-11-05): no Basic User or Office Add In User row.
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

        // Analysis run (POST /api/ai/analysis/execute, fix round 2 F1-1): when the run can write (the document-profile playbook, or
        // a playbook with a node that writes documents), Write is asked on every document in addition to Read (AddAnalysisExecuteAuthorizationFilter asks Read first, then AddAnalysisRunAuthorizationFilter adds Write). Documents are team-owned
        // (I-6): Deep.
        .. For("Analysis run (writing playbook)", [Core], "Deep",
            Bff + "Api/Ai/AnalysisEndpoints.cs:118-120; " + Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:922-952,970", "prvWritesprk_Document"),
        // G9 dropped (owner 2026-11-05, decisions.md:836): a run that can write documents is asked of Core User only; Basic User with AI
        // Analysis User is not meant for it, so there is no row for that set.
        // Any other run: Read on every document, and the playbook-use decision (Read on the playbook unless it is public).
        .. For("Analysis run", [Core], "Deep",
            Bff + "Api/Ai/AnalysisEndpoints.cs:115-120; " + Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:26-28,943-952", "prvReadsprk_Document"),
        .. For("Analysis run", [Core], "Global",
            Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:930-931; " + Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:307-345", "prvReadsprk_analysisplaybook"),
        .. For("Analysis run (Basic User + AI Analysis User)", [Basic, AiUser], "Deep",
            Bff + "Api/Ai/AnalysisEndpoints.cs:115-120; " + Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:26-28,943-952", "prvReadsprk_Document"),
        .. For("Analysis run (Basic User + AI Analysis User)", [Basic, AiUser], "Global",
            Bff + "Api/Filters/AnalysisAuthorizationFilter.cs:930-931; " + Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:307-345", "prvReadsprk_analysisplaybook"),

        // Decision commit in the Console (POST /api/v1/signals/{signalId}/decision): every Signal and its core record read as the
        // caller, AppendTo on the core record, Write on the subject. Signals are owned by the writer identity, their owning business
        // unit set to the matter's (I-18); core records by business-unit default owner teams (I-6): Deep.
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/SignalCoreRecordAccess.cs:191-193; " + Bff + "Services/Signals/DecisionCommitService.cs:456-466",
            "prvReadsprk_Signal"),
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/SignalCoreRecordAccess.cs:239-247; " + Bff + "Services/Dataverse/CoreAncestorResolver.cs:67-73",
            "prvReadsprk_Matter", "prvReadsprk_Project", "prvReadsprk_WorkAssignment", "prvReadsprk_ServiceRequest"),
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/DecisionCallerRights.cs:61; " + Bff + "Services/Dataverse/CoreAncestorResolver.cs:67-73",
            "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendTosprk_WorkAssignment", "prvAppendTosprk_ServiceRequest"),
        // Write on each Signal's subject, in every lane (fix round 2 F3-1): the subject is one of SignalWriter.VerifiedSubjects. The
        // Do-lane actions ask the same Write on an event, To Do or work assignment subject. No row for an invoice line
        // (sprk_billingevent): no role change; invoice-line Signals are decided in the invoice review, and the Console route will
        // refuse them (#2572, ontology DI-139; owner 2026-11-05).
        .. For("Decision commit (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/DecisionCallerRights.cs:42,67-83; " + Bff + "Services/Signals/SignalWriter.cs:503-507; "
            + Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:774,787",
            "prvWritesprk_Matter", "prvWritesprk_Project", "prvWritesprk_Communication", "prvWritesprk_Event", "prvWritesprk_Todo",
            "prvWritesprk_WorkAssignment", "prvWritesprk_Invoice"),

        // The decision actions, asked as the caller before anything is written (fix round 2 F3-2). Create by name, at any depth; the
        // row is created through the child-records core (RecordCreationService as the caller), which asks Append on the table (the
        // row is filed under the core record) and AppendTo on the record type of the ADR-024 pair (organization-owned).
        .. For("Decision actions (Console)", [Core, Console], "Basic",
            Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:217,588,1247,1321,1355,1386,1449; " + Bff + "Api/Filters/CommunicationRecordAuthorizationFilter.cs:158",
            "prvCreatesprk_Todo", "prvCreatesprk_Event", "prvCreatesprk_WorkAssignment", "prvCreatesprk_Budget"),
        .. For("Decision actions (Console)", [Core, Console], "Basic",
            Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:1278-1281,1299; " + Bff + "Services/Signals/Actions/DecisionRouteCores.cs:74-85; "
            + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:63-90",
            "prvAppendsprk_Todo", "prvAppendsprk_Event", "prvAppendsprk_WorkAssignment", "prvAppendsprk_Budget"),
        .. For("Decision actions (Console)", [Core, Console], "Basic",
            Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:1281; " + Bff + "Services/Signals/Actions/BudgetInquiryExecutor.cs:348; "
            + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:390-409",
            "prvAppendTosprk_RecordType_Ref"),
        // Revise budget: Write on the chosen budget (the revision itself is the writer's).
        .. For("Decision actions (Console)", [Core, Console], "Deep", Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:523", "prvWritesprk_Budget"),
        // Add To Do, assign work, assign task (an assignee) and reassign: AppendTo on the contact.
        .. For("Decision actions (Console)", [Core, Console], "Deep", Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:880,1254-1255,1323,1399,1451",
            "prvAppendToContact"),
        // Assign task: AppendTo on the Task event type (the type rows are owned outside the users' business units).
        .. For("Decision actions (Console)", [Core, Console], "Global", Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:1462-1463",
            "prvAppendTosprk_EventType_Ref"),
        // Budget inquiry: a service request created as the caller (CheckCallerMayCreateAsync, then RecordCreationService), filed
        // under the matter (the core AppendTo row above) and naming the outside firm when the caller picks one.
        .. For("Decision actions (Console)", [Core, Console], "Basic",
            Bff + "Services/Signals/Actions/BudgetInquiryExecutor.cs:146,181,276-282; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:63-90",
            "prvCreatesprk_ServiceRequest", "prvAppendsprk_ServiceRequest"),
        .. For("Decision actions (Console)", [Core, Console], "Deep",
            Bff + "Services/Signals/Actions/BudgetInquiryExecutor.cs:181,352; " + Bff + "Services/Dataverse/CallerWriteAuthorization.cs:116-131",
            "prvAppendTosprk_Organization"),

        // Signal, Decision Record and Budget Revision writers (the app users' role; D-204: only they create these rows).
        .. For("Signal and Decision Record writers", [Writer], "Global",
            Bff + "Services/Signals/SignalWriter.cs:526,653,720; " + Bff + "Services/Signals/DecisionRecordWriter.cs:511,559; "
            + Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:390,455",
            "prvCreatesprk_Signal", "prvWritesprk_Signal", "prvCreatesprk_DecisionRecord", "prvCreatesprk_BudgetRevision"),
        // The writers read the rows they close, link and verify, and set lookups on what they create and update (fix round 2 K2).
        .. For("Signal and Decision Record writers", [Writer], "Global",
            Bff + "Services/Signals/SignalWriter.cs:1046,1168; " + Bff + "Services/Signals/DecisionRecordWriter.cs:2722",
            "prvReadsprk_Signal", "prvReadsprk_DecisionRecord"),
        .. For("Signal and Decision Record writers", [Writer], "Global",
            Bff + "Services/Signals/SignalWriter.cs:1083,1197; " + Bff + "Services/Signals/DecisionRecordWriter.cs:2163,2946; "
            + Bff + "Services/Signals/Actions/DecisionActionExecutors.cs:433,455",
            "prvAppendsprk_Signal", "prvAppendsprk_DecisionRecord", "prvAppendsprk_BudgetRevision"),

        // Playbook administration (task 046b, the AI Analysis Admin add-on's feature): creating or cloning a playbook asks the caller's
        // Create privilege on the table, sharing or unsharing its Share privilege, at any depth, before anything runs (owner
        // 2026-10-30: only playbook administrators do these).
        .. For("Playbook administration (create, clone, share, unshare)", [AiAdmin], "Basic",
            Bff + "Api/Ai/PlaybookEndpoints.cs:27,107,120,176; " + Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:55-75,166,172",
            "prvCreatesprk_analysisplaybook", "prvSharesprk_analysisplaybook"),
        // Editing a playbook (fix round 2 F3-1): the update, canvas save and node create / update / delete / scopes routes ask the caller's
        // Write privilege on the table before anything else (the services write app-only), then ownership of the row (own row: Basic is
        // enough, the table is user-owned).
        .. For("Playbook administration (update, canvas, nodes)", [AiAdmin], "Basic",
            Bff + "Api/Ai/PlaybookEndpoints.cs:41,155; " + Bff + "Api/Ai/NodeEndpoints.cs:34,59,73,89; " + Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:55-75,166-172",
            "prvWritesprk_analysisplaybook"),
        // Cloning also reads the playbook being copied (the playbook-use decision, OwnerOrSharedOrPublic): a public playbook needs no
        // Read; any other is the caller's own Dataverse Read on a user-owned row, which for another owner's playbook is the role depth.
        // Global, as the other rows that read a playbook (analysis from a playbook, analysis run).
        .. For("Playbook administration (clone: read the source playbook)", [AiAdmin], "Global",
            Bff + "Api/Ai/PlaybookEndpoints.cs:177; " + Bff + "Api/Filters/PlaybookAuthorizationFilter.cs:307-330",
            "prvReadsprk_analysisplaybook"),

        // Billing (ebilling-r1 task 089e, owner-approved): removing or previewing a Procedure override asks Read on the Procedure.
        // The route (ProcedureAssignmentEndpoints.cs) comes with ebilling-r1 089e; on this branch the requirement is its task.
        .. For("Billing: Procedure override remove / preview", [BillingAdmin], "Deep",
            "projects/ebilling-r1/tasks/089-assignment-override-service.poml:40,113 (the route comes with ebilling-r1 089e)", "prvReadsprk_procedure"),
    ];

    internal enum RoleKind { Base, AddOn, AppUsers }

    /// <summary>A role file's kind. For an add-on: the declared features it exists for (repeated with the add-on alone), the
    /// privileges of those features it is not meant to meet alone, and, when no feature in this matrix is its own, why.</summary>
    internal sealed record RoleClass(string Role, RoleKind Kind, string[] AloneFrom, string[] AloneExcept, string? NotInMatrix);

    private const string NoBffFeature = "No feature in this matrix is its own: ";

    /// <summary>The roles classified <see cref="RoleKind.AppUsers"/>: exactly these, and no more. <see cref="PersonRoles"/> leaves
    /// every AppUsers role out of the person-role set, so what has that kind decides which roles the writer-only rules cover.</summary>
    internal static readonly string[] ExpectedAppUsersRoles = [Writer];

    /// <summary>What is wrong with the AppUsers classification: a role with the kind that is not expected, or an expected role without it.</summary>
    internal static List<string> AppUsersProblems(IReadOnlyList<RoleClass> classes, IReadOnlyCollection<string> expected)
    {
        var actual = classes.Where(c => c.Kind == RoleKind.AppUsers).Select(c => c.Role).ToList();
        var problems = new List<string>();
        problems.AddRange(actual.Where(r => !expected.Contains(r, StringComparer.Ordinal))
            .Select(r => $"{r}: classified AppUsers, which leaves it out of the person-role set; only {string.Join(", ", expected)} may have that kind"));
        problems.AddRange(expected.Where(r => !actual.Contains(r, StringComparer.Ordinal))
            .Select(r => $"{r}: expected to be classified AppUsers, but is not"));
        return problems;
    }

    /// <summary>Every role file under the Roles folder (task 046b). A role file not listed here fails
    /// <see cref="EveryRoleFileIsClassifiedAndEachAddOnIsCheckedAlone"/>, so a new role is classified before it ships.</summary>
    internal static readonly IReadOnlyList<RoleClass> RoleClasses =
    [
        new(Core, RoleKind.Base, [], [], null),
        new(Basic, RoleKind.Base, [], [], null),
        new(Writer, RoleKind.AppUsers, [], [], null),
        // The Office add-in's features: save, version save (G6 put its Write on this role), quick create, and the typed event route
        // the doc names for it. Office To Do is Core-only (owner 2026-11-05).
        new(Office, RoleKind.AddOn,
            ["Office save", "Office version save", "Office quick create (Office Add In User)", "Event (typed route, Office Add In User)"], [], null),
        new(AiUser, RoleKind.AddOn,
            ["Analysis from a playbook (Basic User + AI Analysis User)", "Analysis run (Basic User + AI Analysis User)"], [], null),
        // The Console's Decide lane. The Do lane stays Core-only (owner 2026-11-05): its subjects are events, To Dos and work
        // assignments (src/server/api/Sprk.Bff.Api/Services/Signals/SignalReadService.cs:229-234), so Write on them is not asked
        // of Console User alone.
        new(Console, RoleKind.AddOn, ["Decision commit (Console)", "Decision actions (Console)"],
            ["prvWritesprk_Event", "prvWritesprk_Todo", "prvWritesprk_WorkAssignment"], null),
        // Their feature rows are declared for them alone (the Billing and playbook administration rows above).
        new(BillingAdmin, RoleKind.AddOn, [], [], null),
        new(AiAdmin, RoleKind.AddOn, [], [], null),
        new(OntologyAdmin, RoleKind.AddOn, [], [],
            NoBffFeature + "ontology administration writes run as the user on its own routes, so Dataverse asks each right itself "
            + "(OntologyAdminCallerBoundaryGuardTests); it is also the cleanup role for the writer-only tables (MustNotHold)."),
        new("Spaarke Access Administrator", RoleKind.AddOn, [], [],
            NoBffFeature + "contact-organization membership writes, whose privileges ContactOrganizationMembershipWriterGuardTests pins "
            + "against config/contact-organization-membership-role.json."),
        new("Spaarke Reporting Access Admin", RoleKind.AddOn, [], [], NoBffFeature + ReportingByRole),
        new("Spaarke Reporting Access Author", RoleKind.AddOn, [], [], NoBffFeature + ReportingByRole),
        new("Spaarke Reporting Access Viewer", RoleKind.AddOn, [], [], NoBffFeature + ReportingByRole),
    ];

    private const string ReportingByRole =
        "the reporting routes admit a caller by the role itself (src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingAuthorizationFilter.cs), "
        + "not by a privilege asked of the caller.";

    /// <summary>The feature name of a declared feature's rows repeated for an add-on alone.</summary>
    internal static string AloneFeature(string feature, string role) => $"{feature} [{role} alone]";

    /// <summary>The add-ons ruled NOT standalone (owner 2026-11-05): each is not meant to be held alone, so its features are not repeated
    /// with it as the only role. An explicit list, pinned by <see cref="TheStandaloneExceptionsArePinned"/>; every other add-on stays
    /// checked alone. Role to reason.</summary>
    internal static readonly IReadOnlyDictionary<string, string> StandaloneExceptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Office] = "Ruled not standalone (owner 2026-11-05): not meant to be held alone; a base role is held with it.",
        [AiUser] = "Ruled not standalone (owner 2026-11-05): not meant to be held alone; a base role is held with it.",
        [Console] = "Ruled not standalone (owner 2026-11-05): needs Spaarke Core User.",
    };

    /// <summary>The exception list as the owner ruled it, literal so that adding or dropping an exception fails the pin.</summary>
    internal static readonly string[] ExpectedStandaloneExceptions =
        ["Spaarke Office Add In User", "Spaarke AI Analysis User", "Spaarke Console User"];

    /// <summary>What is wrong with the exception list: a role in it that is not expected, an expected role missing, a role that is not an
    /// add-on with features to repeat, or no reason.</summary>
    internal static List<string> StandaloneExceptionProblems(
        IReadOnlyDictionary<string, string> exceptions, IReadOnlyCollection<string> expected, IReadOnlyList<RoleClass> classes)
    {
        var problems = new List<string>();
        problems.AddRange(exceptions.Keys.Where(r => !expected.Contains(r, StringComparer.Ordinal))
            .Select(r => $"{r}: ruled not standalone in the code, but not in the owner's list ({string.Join(", ", expected)})"));
        problems.AddRange(expected.Where(r => !exceptions.ContainsKey(r)).Select(r => $"{r}: ruled not standalone, but is checked alone"));
        foreach (var (role, reason) in exceptions)
        {
            var c = classes.FirstOrDefault(x => x.Role == role);
            if (c is null || c.Kind != RoleKind.AddOn) problems.Add($"{role}: a standalone exception names a role that is not a classified add-on");
            else if (c.AloneFrom.Length == 0) problems.Add($"{role}: a standalone exception for an add-on with no features to repeat");
            if (string.IsNullOrWhiteSpace(reason)) problems.Add($"{role}: a standalone exception has no reason");
        }

        return problems;
    }

    /// <summary>Each add-on's features repeated with the add-on as the only role (standalone add-ons, owner 2026-11-05), except the add-ons
    /// in <paramref name="exceptions"/>.</summary>
    internal static IEnumerable<Requirement> AloneRows(
        IReadOnlyList<Requirement> declared, IReadOnlyList<RoleClass> classes, IReadOnlyCollection<string> exceptions) =>
        from c in classes
        where c.Kind == RoleKind.AddOn && !exceptions.Contains(c.Role, StringComparer.Ordinal)
        from feature in c.AloneFrom
        from r in declared
        where r.Feature == feature && !c.AloneExcept.Contains(r.Privilege, StringComparer.OrdinalIgnoreCase)
        select r with { Feature = AloneFeature(feature, c.Role), Roles = [c.Role] };

    /// <summary>The declared table plus each add-on's rows alone: what the guard checks.</summary>
    internal static readonly IReadOnlyList<Requirement> Requirements = [.. Declared, .. AloneRows(Declared, RoleClasses, StandaloneExceptions.Keys.ToArray())];

    /// <summary>A rule that no person role holds any of <paramref name="Privileges"/> at any depth, except the roles named. With
    /// <paramref name="Only"/> set, the rule covers just those roles.</summary>
    internal sealed record MustNotHoldRule(string Rule, string[] Privileges, string[] Except, string Evidence, string[]? Only = null);

    /// <summary>What no person role holds (task 046b). A person role is every role file but the app users' role.</summary>
    internal static readonly IReadOnlyList<MustNotHoldRule> MustNotHold =
    [
        new("Writer-only tables (owner 2026-11-05; ontology D-204): no person role creates, writes or deletes a Signal or Decision Record, "
            + "or writes or deletes a Budget Revision; Spaarke Ontology Administrator is the designated cleanup role (ontology D-160)",
            [
                "prvCreatesprk_Signal", "prvWritesprk_Signal", "prvDeletesprk_Signal",
                "prvCreatesprk_DecisionRecord", "prvWritesprk_DecisionRecord", "prvDeletesprk_DecisionRecord",
                "prvWritesprk_BudgetRevision", "prvDeletesprk_BudgetRevision",
            ],
            [OntologyAdmin],
            "projects/field-mapping-server-write-path-r1/notes/decisions.md:830; docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:186"),
        // Fix round 2 (owner 2026-11-05, decisions.md:836): the cleanup role writes and deletes a Budget Revision but does not create one.
        new("Budget Revision create: only the writer creates one; the cleanup role writes and deletes (owner 2026-11-05)",
            ["prvCreatesprk_BudgetRevision"], [],
            "projects/field-mapping-server-write-path-r1/notes/decisions.md:836; docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:186"),
        // Fix round 2 (owner 2026-11-05, decisions.md:836): Spaarke Basic User holds no AppendTo on a report card; no feature in the
        // matrix asks it on a role set with Basic User (the KPI assessment's AppendTo is Core User's).
        new("Spaarke Basic User: no AppendTo on sprk_reportcard (owner 2026-11-05)",
            ["prvAppendTosprk_ReportCard"], [],
            "projects/field-mapping-server-write-path-r1/notes/decisions.md:836; docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:199",
            [Basic]),
        new("Invoice line (sprk_billingevent) create: none, by design (BFF-written, ebilling D-18)",
            ["prvCreatesprk_BillingEvent"], [], "projects/ebilling-r1/notes/decisions-log.md:24"),
    ];

    /// <summary>The ratchet: today's gaps (tasks 046 and 046b), each with the change it waits on. Remove an entry when its change
    /// lands. G1, G2 and G4-G8 were applied to the role files by task 046b (owner rulings 2026-11-05); G10 was withdrawn (no role
    /// change: invoice-line Signals are decided in the invoice review, #2572); G9 and the standalone gaps of Office Add In User, AI
    /// Analysis User and Console User are not gaps (those three are ruled not standalone, <see cref="StandaloneExceptions"/>).</summary>
    internal static readonly IReadOnlyList<KnownGap> KnownGaps =
    [
        new("Office save (Basic User)", "prvAppendToContact", "Basic",
            "G3. Accepted, not applied (owner 2026-11-05): Spaarke Basic User is unchanged; Office Add In User carries it (G4)."),
    ];

    private static readonly string[] DepthOrder = ["Basic", "Local", "Deep", "Global"];

    /// <summary>Every <c>Roles</c> folder under here is scanned for the person-role set, as <see cref="ProcedureRolePrivilegeGuardTests"/> does.</summary>
    private static string SolutionsDirectory => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

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

    // ── Standalone add-ons (task 046b) ──────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "046b: every role file is classified, and each add-on is checked alone against its features or says why none is in the matrix")]
    public void EveryRoleFileIsClassifiedAndEachAddOnIsCheckedAlone()
    {
        var files = RoleFileNames(RolesDirectory);
        Assert.True(files.Count >= 13, $"Found only {files.Count} role files under {RolesDirectory} (expected >= 13).");
        _ = LoadRoles(RolesDirectory, files); // each file parses, holds privileges and names its own role

        var problems = ClassificationProblems(files, RoleClasses, Declared, Requirements, StandaloneExceptions.Keys.ToArray());
        problems.AddRange(StandaloneExceptionProblems(StandaloneExceptions, ExpectedStandaloneExceptions, RoleClasses));
        Assert.True(problems.Count == 0,
            "Classify every role file in RoleClasses (base, add-on or the app users' role). An add-on may be held without a base role "
            + "(owner, 2026-11-05), so it names the declared features it exists for (checked with it alone) or says why no feature in "
            + "this matrix is its own:\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "046b fix round 1: the AppUsers role set is exactly {Spaarke Ontology Service}; any other role marked AppUsers, or that role marked otherwise, fails")]
    public void TheAppUsersSetIsPinned()
    {
        Assert.Equal(new[] { "Spaarke Ontology Service" }, ExpectedAppUsersRoles); // the pin itself is literal
        Assert.Empty(AppUsersProblems(RoleClasses, ExpectedAppUsersRoles)); // must-not-fire: the real set

        // must-fire: another role marked AppUsers (it would drop out of every must-not-hold rule)
        var seeded = RoleClasses.Select(c => c.Role == Console ? c with { Kind = RoleKind.AppUsers } : c).ToList();
        Assert.Equal(
            new[] { $"{Console}: classified AppUsers, which leaves it out of the person-role set; only {Writer} may have that kind" },
            AppUsersProblems(seeded, ExpectedAppUsersRoles));

        // must-fire: the expected role no longer has the kind
        var dropped = RoleClasses.Select(c => c.Role == Writer ? c with { Kind = RoleKind.AddOn } : c).ToList();
        Assert.Equal(new[] { $"{Writer}: expected to be classified AppUsers, but is not" }, AppUsersProblems(dropped, ExpectedAppUsersRoles));
    }

    [Fact(DisplayName = "046b classification controls: an unclassified role file, a classified role with no file, an add-on with no row alone and no reason, a stale feature or exception are reported; the real set is not")]
    public void ClassificationCheckFires()
    {
        var declared = new Requirement[]
        {
            new("F-base", ["B"], "prvA", "Basic", "x.cs:1"),
            new("F-base", ["B"], "prvB", "Basic", "x.cs:2"),
        };
        var classes = new RoleClass[]
        {
            new("B", RoleKind.Base, [], [], null),
            new("A1", RoleKind.AddOn, ["F-base"], ["prvB"], null),            // must-not-fire: checked alone, a live exception
            new("A2", RoleKind.AddOn, [], [], null),                         // must-fire: no row alone, no reason
            new("A3", RoleKind.AddOn, ["F-gone"], [], null),                 // must-fire: names a feature no row has
            new("A4", RoleKind.AddOn, ["F-base"], ["prvZ"], null),            // must-fire: an exception no row of its features asks
            new("A5", RoleKind.AddOn, [], [], "no feature of its own"),      // must-not-fire: says why
            new("Gone", RoleKind.AddOn, [], [], "x"),                        // must-fire: no file
        };
        var all = declared.Concat(AloneRows(declared, classes, [])).ToArray();

        var problems = ClassificationProblems(["B", "A1", "A2", "A3", "A4", "A5", "New"], classes, declared, all, []);

        Assert.Equal(
            new[]
            {
                "New: role file not classified in RoleClasses",
                "Gone: classified, but no role file defines it",
                "A2: add-on with no feature row alone and no NotInMatrix reason",
                "A3: AloneFrom names 'F-gone', which no declared row has",
                "A3: add-on with no feature row alone and no NotInMatrix reason",
                "A4: AloneExcept names prvZ, which none of its AloneFrom features asks",
            },
            problems);
        Assert.Single(all, r => r.Feature == AloneFeature("F-base", "A1")); // prvB is excepted for A1
        Assert.Empty(ClassificationProblems(RoleFileNames(RolesDirectory), RoleClasses, Declared, Requirements, StandaloneExceptions.Keys.ToArray())); // must-not-fire

        // A ruled exception is not checked alone, and needs no row alone and no NotInMatrix reason (A1 as an exception).
        var excepted = new RoleClass[] { new("B", RoleKind.Base, [], [], null), new("A1", RoleKind.AddOn, ["F-base"], [], null) };
        Assert.Empty(AloneRows(declared, excepted, ["A1"]));
        Assert.Empty(ClassificationProblems(["B", "A1"], excepted, declared, declared, ["A1"]));
        Assert.NotEmpty(AloneRows(declared, excepted, [])); // must-fire: without the exception the rows alone are there
    }

    [Fact(DisplayName = "046b fix round 2: the add-ons ruled not standalone are exactly Office Add In User, AI Analysis User and Console User; any other, or one dropped, fails")]
    public void TheStandaloneExceptionsArePinned()
    {
        Assert.Equal(new[] { "Spaarke Office Add In User", "Spaarke AI Analysis User", "Spaarke Console User" }, ExpectedStandaloneExceptions); // the pin is literal
        Assert.Empty(StandaloneExceptionProblems(StandaloneExceptions, ExpectedStandaloneExceptions, RoleClasses)); // must-not-fire: the real list

        // must-fire: another add-on added to the list (it would escape the alone check)
        var more = new Dictionary<string, string>(StandaloneExceptions) { [BillingAdmin] = "x" };
        Assert.Contains(StandaloneExceptionProblems(more, ExpectedStandaloneExceptions, RoleClasses),
            p => p.StartsWith($"{BillingAdmin}: ruled not standalone in the code", StringComparison.Ordinal));
        // must-fire: an exception dropped
        var fewer = StandaloneExceptions.Where(kv => kv.Key != Console).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(new[] { $"{Console}: ruled not standalone, but is checked alone" }, StandaloneExceptionProblems(fewer, ExpectedStandaloneExceptions, RoleClasses));
        // must-fire: no reason; a base role as an exception
        Assert.Contains(StandaloneExceptionProblems(new Dictionary<string, string>(StandaloneExceptions) { [Office] = " " }, ExpectedStandaloneExceptions, RoleClasses),
            p => p == $"{Office}: a standalone exception has no reason");
        Assert.Contains(StandaloneExceptionProblems(new Dictionary<string, string>(StandaloneExceptions) { [Core] = "x" }, ExpectedStandaloneExceptions, RoleClasses),
            p => p == $"{Core}: a standalone exception names a role that is not a classified add-on");
    }

    [Fact(DisplayName = "046b fix round 2: taking an exception away brings that add-on's gaps back (Office Add In User, AI Analysis User, Console User); with the exceptions there are none")]
    public void RemovingAStandaloneExceptionBringsItsGapsBack()
    {
        var roles = LoadRoles(RolesDirectory, Declared.SelectMany(r => r.Roles).Concat(RoleClasses.Select(c => c.Role)));

        // must-not-fire: with the exceptions, the three are not checked alone, and the real requirements have no gap outside the baseline
        Assert.DoesNotContain(Requirements, r => r.Roles.Length == 1 && StandaloneExceptions.ContainsKey(r.Roles[0]));
        Assert.Empty(Ratchet(Gaps(Requirements, roles), KnownGaps).Unlisted);

        // must-fire: take each exception away in turn
        foreach (var role in StandaloneExceptions.Keys)
        {
            var without = StandaloneExceptions.Keys.Where(k => k != role).ToArray();
            var rows = AloneRows(Declared, RoleClasses, without).ToArray();
            Assert.NotEmpty(rows);
            var gaps = Gaps(rows, roles);
            Assert.True(gaps.Count > 0, $"{role}: no gap came back with its exception removed.");
            Assert.All(gaps, g => Assert.Equal(new[] { role }, g.Roles));
        }
    }

    /// <summary>What is wrong with the role classification: files not classified, classes with no file, add-ons neither checked
    /// alone nor explained, features or exceptions that name nothing.</summary>
    internal static List<string> ClassificationProblems(
        IReadOnlyCollection<string> roleFiles, IReadOnlyList<RoleClass> classes, IReadOnlyList<Requirement> declared, IReadOnlyList<Requirement> all,
        IReadOnlyCollection<string> exceptions)
    {
        var problems = new List<string>();
        problems.AddRange(roleFiles.Where(f => !classes.Any(c => c.Role == f)).Select(f => $"{f}: role file not classified in RoleClasses"));
        problems.AddRange(classes.Where(c => !roleFiles.Contains(c.Role)).Select(c => $"{c.Role}: classified, but no role file defines it"));
        problems.AddRange(classes.GroupBy(c => c.Role).Where(g => g.Count() > 1).Select(g => $"{g.Key}: classified more than once"));
        foreach (var c in classes)
        {
            if (c.Kind != RoleKind.AddOn)
            {
                if (c.AloneFrom.Length > 0 || c.AloneExcept.Length > 0 || c.NotInMatrix is not null)
                    problems.Add($"{c.Role}: only an add-on names features alone, exceptions or a reason");
                continue;
            }

            problems.AddRange(c.AloneFrom.Where(f => !declared.Any(r => r.Feature == f)).Select(f => $"{c.Role}: AloneFrom names '{f}', which no declared row has"));
            problems.AddRange(c.AloneExcept
                .Where(p => !declared.Any(r => c.AloneFrom.Contains(r.Feature) && Same(r.Privilege, p)))
                .Select(p => $"{c.Role}: AloneExcept names {p}, which none of its AloneFrom features asks"));
            if (exceptions.Contains(c.Role, StringComparer.Ordinal))
                continue; // ruled not standalone: no row alone, no reason in the matrix (StandaloneExceptionProblems checks the exception)
            var alone = all.Any(r => r.Roles.Length == 1 && r.Roles[0] == c.Role);
            var reason = !string.IsNullOrWhiteSpace(c.NotInMatrix);
            if (!alone && !reason)
                problems.Add($"{c.Role}: add-on with no feature row alone and no NotInMatrix reason");
            if (alone && reason)
                problems.Add($"{c.Role}: has feature rows alone; drop its NotInMatrix reason");
        }

        return problems;
    }

    private static List<string> RoleFileNames(string directory) =>
        Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly).Select(p => Path.GetFileNameWithoutExtension(p)!).Order(StringComparer.Ordinal).ToList();

    // ── Must not hold (task 046b) ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "046b: no person role holds a privilege it must not (writer-only tables, except the Ontology Administrator cleanup role; invoice line Create)")]
    public void PersonRolesHoldNothingTheyMustNot()
    {
        var personRoles = PersonRoles(SolutionsDirectory);
        Assert.All(MustNotHold, rule =>
        {
            Assert.NotEmpty(rule.Privileges);
            Assert.All(rule.Privileges, p => Assert.StartsWith("prv", p, StringComparison.Ordinal));
            Assert.All(rule.Except, role => Assert.True(personRoles.ContainsKey(role), $"'{role}' is excepted from a rule but is not a person role file."));
            Assert.All(rule.Only ?? [], role => Assert.True(personRoles.ContainsKey(role), $"'{role}' is named by a rule but is not a person role file."));
            Assert.Empty(MissingEvidencePaths(rule.Evidence, SourceScan.RepoRoot));
        });

        var held = ForbiddenHeld(personRoles, MustNotHold);
        Assert.True(held.Count == 0,
            "A person role holds a privilege no person role may hold (task 046b; owner rulings 2026-11-05). Remove it from the role file "
            + "and raise it with unified-access-control-r2:\n  " + string.Join("\n  ", held));
    }

    private static readonly string[] PrivilegeVerbs = ["AppendTo", "Append", "Create", "Read", "Write", "Delete", "Assign", "Share"];

    /// <summary>The table of a privilege name (<c>prv</c> + verb + table), or null when the name does not start with <c>prv</c> and a known verb.</summary>
    internal static string? PrivilegeTable(string privilege)
    {
        if (!privilege.StartsWith("prv", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = privilege[3..];
        // AppendTo is listed before Append, so the longer verb wins.
        var verb = PrivilegeVerbs.FirstOrDefault(v => rest.StartsWith(v, StringComparison.OrdinalIgnoreCase));
        return verb is null || rest.Length == verb.Length ? null : rest[verb.Length..];
    }

    /// <summary>Every entity folder name under any <c>Entities</c> folder of any solution under <paramref name="root"/>.</summary>
    internal static HashSet<string> EntityNames(string root) =>
        Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(d => string.Equals(Path.GetFileName(Path.GetDirectoryName(d)), "Entities", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.GetFileName(d)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each rule privilege whose name has no known verb, or whose table is not an entity in <paramref name="entities"/> (case-insensitive):
    /// a misspelt name would match no role and so keep the rule quiet for ever (fix round 2 K3).</summary>
    internal static List<string> UnknownMustNotHoldTables(IReadOnlyList<MustNotHoldRule> rules, ISet<string> entities) =>
        (from rule in rules
         from privilege in rule.Privileges
         let table = PrivilegeTable(privilege)
         where table is null || !entities.Contains(table)
         select $"{privilege} ({rule.Rule}): " + (table is null ? "not 'prv' + a known verb + a table" : $"no entity '{table}' under src/dataverse/solutions/**/Entities")).ToList();

    [Fact(DisplayName = "046b must-not-hold names: every rule privilege names a real table (an entity under src/dataverse/solutions/**/Entities); a misspelt name fails")]
    public void MustNotHoldNamesRealTables()
    {
        var entities = EntityNames(SolutionsDirectory);
        Assert.NotEmpty(entities);

        // must-not-fire: the real list
        Assert.Empty(UnknownMustNotHoldTables(MustNotHold, entities));
        // must-not-fire: other casing of a real table
        Assert.Empty(UnknownMustNotHoldTables([new("R", ["PRVCREATESPRK_SIGNAL", "prvAppendTosprk_BILLINGEVENT"], [], "x")], entities));

        // must-fire: a misspelt table, a misspelt verb, no prefix, a verb with no table
        var misspelt = UnknownMustNotHoldTables(
            [new("R", ["prvCreatesprk_Signall", "prvCreatesprk_DecisonRecord", "prvMakesprk_Signal", "sprk_Signal", "prvCreate"], [], "x")], entities);
        Assert.Equal(5, misspelt.Count);
        Assert.Contains(misspelt, m => m.StartsWith("prvCreatesprk_Signall", StringComparison.Ordinal));
        Assert.Contains(misspelt, m => m.StartsWith("prvCreatesprk_DecisonRecord", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "046b must-not-hold controls: a forbidden privilege at any depth or casing is reported; an excepted role, the app users' role and other privileges are not")]
    public void MustNotHoldFires()
    {
        var rules = new MustNotHoldRule[] { new("R-writer", ["prvCreatesprk_Signal", "prvDeletesprk_DecisionRecord"], ["Cleanup"], "x.md:1") };
        var personRoles = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Person"] = Privileges(("PRVCREATESPRK_SIGNAL", "Basic"), ("prvReadsprk_Signal", "Global")), // must-fire (other casing); Read is not
            ["Other"] = Privileges(("prvDeletesprk_DecisionRecord", "Global")),                         // must-fire
            ["Cleanup"] = Privileges(("prvCreatesprk_Signal", "Deep"), ("prvDeletesprk_DecisionRecord", "Deep")), // must-not-fire: excepted
        };

        Assert.Equal(
            new[] { "Other: prvDeletesprk_DecisionRecord at Global — R-writer", "Person: prvCreatesprk_Signal at Basic — R-writer" },
            ForbiddenHeld(personRoles, rules));
    }

    [Fact(DisplayName = "046b must-not-hold seeded drift: each forbidden privilege given to any person role but the excepted one is reported")]
    public void MustNotHoldFiresForEverySeededPersonRole()
    {
        var real = PersonRoles(SolutionsDirectory);
        var misses = new List<string>();
        var seededCount = 0;
        foreach (var rule in MustNotHold)
        {
            foreach (var role in real.Keys)
            {
                foreach (var privilege in rule.Privileges)
                {
                    var seeded = new Dictionary<string, IReadOnlyDictionary<string, string>>(real)
                    {
                        [role] = new Dictionary<string, string>(real[role], StringComparer.OrdinalIgnoreCase) { [privilege.ToLowerInvariant()] = "Basic" },
                    };
                    var found = ForbiddenHeld(seeded, [rule]);
                    var expected = rule.Except.Contains(role, StringComparer.Ordinal) || (rule.Only is not null && !rule.Only.Contains(role, StringComparer.Ordinal))
                        ? Array.Empty<string>()
                        : new[] { $"{role}: {privilege} at Basic — {rule.Rule}" };
                    if (!found.SequenceEqual(expected))
                        misses.Add($"{role} + {privilege}: found [{string.Join("; ", found)}]");
                    seededCount++;
                }
            }
        }

        Assert.True(seededCount >= 100, $"Only {seededCount} seeded cases ran.");
        Assert.Empty(misses);
    }

    [Fact(DisplayName = "046b must-not-hold seeded drift through the role files: seeded copies of Console User and Basic User are reported; the Ontology Administrator and the app users' role are not")]
    public void MustNotHoldFiresOnSeededRoleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "SpaarkeMaster", "Roles");
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.EnumerateFiles(RolesDirectory, "*.xml"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            // must-not-fire: the copy is the real set
            Assert.Equal(PersonRoles(SolutionsDirectory).Keys.Order(StringComparer.Ordinal), PersonRoles(root).Keys.Order(StringComparer.Ordinal));
            Assert.Empty(ForbiddenHeld(PersonRoles(root), MustNotHold));

            AddPrivilege(dir, Console, "prvwritesprk_signal", "Deep");
            AddPrivilege(dir, Basic, "prvCreatesprk_BillingEvent", "Basic");
            AddPrivilege(dir, OntologyAdmin, "prvCreatesprk_Signal", "Global"); // excepted: the cleanup role
            AddPrivilege(dir, Writer, "prvDeletesprk_Signal", "Global");         // the app users' role is not a person role
            AddPrivilege(dir, OntologyAdmin, "prvCreatesprk_BudgetRevision", "Deep"); // fix round 2: the cleanup role does not create one
            AddPrivilege(dir, Basic, "prvAppendTosprk_ReportCard", "Deep");           // fix round 2: seeded back on Basic User

            // K1 (fix round 1): a role file in ANY other solution's Roles folder is a person role too.
            var other = Path.Combine(root, "SomeOtherSolution", "src", "Roles");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "Spaarke Added Elsewhere.xml"),
                "<Role name=\"Spaarke Added Elsewhere\"><RolePrivileges><RolePrivilege name=\"prvDeletesprk_DecisionRecord\" level=\"Basic\" /></RolePrivileges></Role>");
            File.WriteAllText(Path.Combine(root, "SomeOtherSolution", "Not A Role.xml"),                  // must-not-fire: not in a Roles folder
                "<Role name=\"Not A Role\"><RolePrivileges><RolePrivilege name=\"prvDeletesprk_DecisionRecord\" level=\"Basic\" /></RolePrivileges></Role>");

            var found = ForbiddenHeld(PersonRoles(root), MustNotHold).Order(StringComparer.Ordinal).ToList();

            Assert.Equal(5, found.Count);
            Assert.StartsWith("Spaarke Added Elsewhere: prvDeletesprk_DecisionRecord at Basic", found[0], StringComparison.Ordinal);
            Assert.StartsWith("Spaarke Basic User: prvAppendTosprk_ReportCard at Deep", found[1], StringComparison.Ordinal);
            Assert.StartsWith("Spaarke Basic User: prvCreatesprk_BillingEvent at Basic", found[2], StringComparison.Ordinal);
            Assert.StartsWith("Spaarke Console User: prvWritesprk_Signal at Deep", found[3], StringComparison.Ordinal);
            Assert.StartsWith("Spaarke Ontology Administrator: prvCreatesprk_BudgetRevision at Deep", found[4], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Every role file in any <c>Roles</c> folder under <paramref name="root"/> (<see cref="ProcedureRolePrivilegeGuardTests.RoleFiles"/>)
    /// but the app users' role (an unclassified file counts as a person role). A role named by two files fails.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PersonRoles(string root)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var files = ProcedureRolePrivilegeGuardTests.RoleFiles(root).Order(StringComparer.Ordinal).ToList();
        // Fix round 2 F3-2: the duplicate check runs over EVERY role file, before the app users' role is left out, so a second
        // file named for the app users' role fails too (it would otherwise be skipped by name and never read).
        var duplicates = DuplicateRoleFiles(files);
        if (duplicates.Count > 0)
            throw new InvalidOperationException("A role is named by two role files: " + string.Join("; ", duplicates));
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (RoleClasses.Any(c => c.Role == name && c.Kind == RoleKind.AppUsers))
                continue;
            result[name] = LoadRoles(Path.GetDirectoryName(file)!, [name])[name];
        }

        return result;
    }

    /// <summary>The role names (file names, any casing) that more than one role file carries, each with its files.</summary>
    internal static List<string> DuplicateRoleFiles(IEnumerable<string> files) =>
        files.GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' in {string.Join(" and ", g.Order(StringComparer.Ordinal))}")
            .ToList();

    [Fact(DisplayName = "046b role-file duplicates: a second file named for ANY role, the app users' role included, fails; the repo's role files do not")]
    public void ASecondFileNamedForAnyRoleFails()
    {
        // must-not-fire: the repo as it is
        Assert.Empty(DuplicateRoleFiles(ProcedureRolePrivilegeGuardTests.RoleFiles(SolutionsDirectory)));
        Assert.NotEmpty(PersonRoles(SolutionsDirectory));

        var root = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-dup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "SpaarkeMaster", "Roles");
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.EnumerateFiles(RolesDirectory, "*.xml"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            Assert.Empty(DuplicateRoleFiles(ProcedureRolePrivilegeGuardTests.RoleFiles(root)));
            Assert.NotEmpty(PersonRoles(root));

            // must-fire: a second file named for the app users' role, in another solution's Roles folder
            var other = Path.Combine(root, "SomeOtherSolution", "Roles");
            Directory.CreateDirectory(other);
            File.Copy(Path.Combine(RolesDirectory, Writer + ".xml"), Path.Combine(other, Writer + ".xml"));
            var duplicates = DuplicateRoleFiles(ProcedureRolePrivilegeGuardTests.RoleFiles(root));
            Assert.Single(duplicates);
            Assert.Contains($"'{Writer}'", duplicates[0], StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => PersonRoles(root));
            File.Delete(Path.Combine(other, Writer + ".xml"));

            // must-fire: a second file named for a person role
            File.Copy(Path.Combine(RolesDirectory, Console + ".xml"), Path.Combine(other, Console + ".xml"));
            Assert.Throws<InvalidOperationException>(() => PersonRoles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Each person role's held privilege that a rule forbids, unless the rule excepts the role.</summary>
    internal static List<string> ForbiddenHeld(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> personRoles, IReadOnlyList<MustNotHoldRule> rules) =>
        (from rule in rules
         from role in personRoles.Keys.Order(StringComparer.Ordinal)
         where !rule.Except.Contains(role, StringComparer.Ordinal) && (rule.Only is null || rule.Only.Contains(role, StringComparer.Ordinal))
         from privilege in rule.Privileges
         where personRoles[role].ContainsKey(privilege)
         select $"{role}: {privilege} at {personRoles[role][privilege]} — {rule.Rule}").ToList();

    private static void AddPrivilege(string directory, string role, string privilege, string level)
    {
        var path = Path.Combine(directory, role + ".xml");
        var doc = XDocument.Parse(File.ReadAllText(path));
        var container = doc.Descendants().First(e => e.Name.LocalName == "RolePrivileges");
        container.Add(new XElement(container.Name.Namespace + "RolePrivilege", new XAttribute("name", privilege), new XAttribute("level", level)));
        doc.Save(path);
    }

    // ── The ruled changes (task 046b) ──────────────────────────────────────────────────────────────────────────────────

    internal sealed record RuledChange(string Gap, string Role, string Table, string Privilege, string Depth, string From);

    private static string RuledChangesPath => Path.Combine(SourceScan.RepoRoot, "config", "feature-role-privileges.json");

    [Fact(DisplayName = "046b: the role files hold each ruled change (config/feature-role-privileges.json) at exactly its declared depth")]
    public void RuledChangesAreInTheRoleFiles()
    {
        var changes = LoadRuledChanges(RuledChangesPath);
        var problems = RuledChangeProblems(changes, LoadRoles(RolesDirectory, changes.Select(c => c.Role)));
        Assert.True(problems.Count == 0,
            "config/feature-role-privileges.json and the role files disagree. The config is what scripts/Set-FeatureRolePrivileges.ps1 "
            + "applies; correct whichever is wrong:\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "046b ruled-change controls: held deeper, shallower or not at all, a 'from' not below 'depth', and a duplicate are reported; an exact hold in other casing is not")]
    public void RuledChangeCheckFires()
    {
        var roles = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["R"] = Privileges(("PRVEXACT", "Deep"), ("prvDeeper", "Global"), ("prvShallower", "Basic"), ("prvBackwards", "Deep")),
        };
        var changes = new RuledChange[]
        {
            new("G", "R", "t", "prvExact", "Deep", "None"),       // must-not-fire
            new("G", "R", "t", "prvDeeper", "Deep", "Basic"),
            new("G", "R", "t", "prvShallower", "Deep", "None"),
            new("G", "R", "t", "prvMissing", "Deep", "None"),
            new("G", "R", "t", "prvBackwards", "Deep", "Global"),
            new("G", "R", "t", "prvexact", "Deep", "None"),       // duplicate
        };

        Assert.Equal(
            new[]
            {
                "R: prvDeeper — role file holds Global, declared Deep",
                "R: prvShallower — role file holds Basic, declared Deep",
                "R: prvMissing — role file holds none, declared Deep",
                "R: prvBackwards — 'from' Global is not below 'depth' Deep",
                "R: prvexact — declared more than once",
            },
            RuledChangeProblems(changes, roles));
    }

    [Fact(DisplayName = "046b seeded proof (AC1): putting any one ruled change back to its earlier level in a copy of its role file fails the feature guard")]
    public void PuttingARuledChangeBackFailsTheGuard()
    {
        var changes = LoadRuledChanges(RuledChangesPath);
        var dir = Path.Combine(Path.GetTempPath(), "sprk-feature-roles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.EnumerateFiles(RolesDirectory, "*.xml"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            var roleNames = Requirements.SelectMany(r => r.Roles).ToArray();
            var (unlistedBefore, staleBefore, changedBefore) = Ratchet(Gaps(Requirements, LoadRoles(dir, roleNames)), KnownGaps);
            Assert.Empty(unlistedBefore.Concat(staleBefore).Concat(changedBefore)); // the copy passes before any change is put back

            var misses = new List<string>();
            foreach (var change in changes)
            {
                var path = Path.Combine(dir, change.Role + ".xml");
                var original = File.ReadAllText(path);
                var doc = XDocument.Parse(original);
                var element = doc.Descendants().Single(e => e.Name.LocalName == "RolePrivilege" && Same((string?)e.Attribute("name") ?? "", change.Privilege));
                if (Same(change.From, "None"))
                    element.Remove();
                else
                    element.SetAttributeValue("level", change.From);
                doc.Save(path);

                var (unlisted, _, changed) = Ratchet(Gaps(Requirements, LoadRoles(dir, roleNames)), KnownGaps);
                if (!unlisted.Concat(changed).Any(s => s.Contains(": " + change.Privilege + " ", StringComparison.OrdinalIgnoreCase)))
                    misses.Add($"{change.Gap} {change.Role}: {change.Privilege} back to {change.From} — the guard did not fail");
                File.WriteAllText(path, original);
            }

            Assert.Empty(misses);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    internal sealed record Removal(string Gap, string Role, string Table, string Privilege, string ExpectedDepth);

    /// <summary>The config's "removals" block (fix round 2): privileges the owner ruled off a role. Only <c>-Remove</c> reads it.</summary>
    internal static IReadOnlyList<Removal> LoadRemovals(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("removals").EnumerateArray().Select(c => new Removal(
            c.GetProperty("gap").GetString()!, c.GetProperty("role").GetString()!, c.GetProperty("table").GetString()!,
            c.GetProperty("privilege").GetString()!, c.GetProperty("expectedDepth").GetString()!)).ToList();
    }

    /// <summary>Each removal whose role file still holds the privilege, that names an unknown depth, or that is listed twice or also added.</summary>
    internal static List<string> RemovalProblems(
        IReadOnlyList<Removal> removals, IReadOnlyList<RuledChange> changes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> roles)
    {
        var problems = new List<string>();
        var seen = new HashSet<(string, string)>();
        foreach (var r in removals)
        {
            var key = (r.Role, r.Privilege.ToLowerInvariant());
            if (!seen.Add(key)) problems.Add($"{r.Role}: {r.Privilege} — removed more than once");
            if (changes.Any(c => c.Role == r.Role && Same(c.Privilege, r.Privilege))) problems.Add($"{r.Role}: {r.Privilege} — both added and removed");
            if (Rank(r.ExpectedDepth) < 0) problems.Add($"{r.Role}: {r.Privilege} — expectedDepth {r.ExpectedDepth} is not a depth");
            if (roles.TryGetValue(r.Role, out var held) && held.TryGetValue(r.Privilege, out var level))
                problems.Add($"{r.Role}: {r.Privilege} — role file still holds it at {level}, the config removes it");
        }

        return problems;
    }

    [Fact(DisplayName = "046b fix round 2: the config's removals are absent from the role files and forbidden by a must-not-hold rule; putting one back fails both")]
    public void RemovalsAreAbsentFromTheRoleFilesAndForbidden()
    {
        var removals = LoadRemovals(RuledChangesPath);
        var changes = LoadRuledChanges(RuledChangesPath);
        Assert.Equal(2, removals.Count);
        var personRoles = PersonRoles(SolutionsDirectory);
        var roles = LoadRoles(RolesDirectory, removals.Select(r => r.Role));

        // must-not-fire: the repo as it is
        Assert.Empty(RemovalProblems(removals, changes, roles));
        Assert.Empty(ForbiddenHeld(personRoles, MustNotHold));

        foreach (var r in removals)
        {
            // each removed privilege is covered by a must-not-hold rule for that role, so the role file cannot get it back unseen
            var seededRole = new Dictionary<string, string>(personRoles[r.Role], StringComparer.OrdinalIgnoreCase) { [r.Privilege] = r.ExpectedDepth };
            var seeded = new Dictionary<string, IReadOnlyDictionary<string, string>>(personRoles) { [r.Role] = seededRole };
            Assert.Contains(ForbiddenHeld(seeded, MustNotHold), f => f.StartsWith($"{r.Role}: {r.Privilege} at {r.ExpectedDepth}", StringComparison.Ordinal));

            // must-fire: the role file holds it again
            var seededRoles = new Dictionary<string, IReadOnlyDictionary<string, string>>(roles) { [r.Role] = seededRole };
            Assert.Single(RemovalProblems(removals, changes, seededRoles), p => p.StartsWith($"{r.Role}: {r.Privilege} — role file still holds it", StringComparison.Ordinal));
        }

        // must-fire: listed twice, also added, a bad depth
        var first = removals[0];
        Assert.Contains(RemovalProblems([first, first], changes, roles), p => p.EndsWith("removed more than once", StringComparison.Ordinal));
        var added = changes.Append(new RuledChange("X", first.Role, first.Table, first.Privilege, "Deep", "None")).ToList();
        Assert.Contains(RemovalProblems(removals, added, roles), p => p.EndsWith("both added and removed", StringComparison.Ordinal));
        Assert.Contains(RemovalProblems([first with { ExpectedDepth = "Everywhere" }], changes, roles), p => p.Contains("is not a depth", StringComparison.Ordinal));
    }

    internal static IReadOnlyList<RuledChange> LoadRuledChanges(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Ruled-change config is missing: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException($"Unsupported schemaVersion in {path}.");
        var changes = doc.RootElement.GetProperty("changes").EnumerateArray().Select(c => new RuledChange(
            c.GetProperty("gap").GetString()!, c.GetProperty("role").GetString()!, c.GetProperty("table").GetString()!,
            c.GetProperty("privilege").GetString()!, c.GetProperty("depth").GetString()!, c.GetProperty("from").GetString()!)).ToList();
        if (changes.Count == 0)
            throw new InvalidOperationException($"{path} declares no change.");
        return changes;
    }

    /// <summary>Each ruled change the role files do not hold at exactly its depth, or that is malformed or repeated.</summary>
    internal static List<string> RuledChangeProblems(IReadOnlyList<RuledChange> changes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> roles)
    {
        var problems = new List<string>();
        var seen = new HashSet<(string, string)>();
        foreach (var c in changes)
        {
            if (!seen.Add((c.Role, c.Privilege.ToLowerInvariant())))
            {
                problems.Add($"{c.Role}: {c.Privilege} — declared more than once");
                continue;
            }

            var held = roles.TryGetValue(c.Role, out var p) && p.TryGetValue(c.Privilege, out var level) ? level : "none";
            if (!Same(held, c.Depth))
                problems.Add($"{c.Role}: {c.Privilege} — role file holds {held}, declared {c.Depth}");
            if (Rank(c.Depth) < 0 || (!Same(c.From, "None") && Rank(c.From) < 0) || Rank(c.From) >= Rank(c.Depth))
                problems.Add($"{c.Role}: {c.Privilege} — 'from' {c.From} is not below 'depth' {c.Depth}");
        }

        return problems;
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
