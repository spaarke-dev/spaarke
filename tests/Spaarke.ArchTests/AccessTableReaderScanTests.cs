using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2253 fix round 1 (K3): every Dataverse table the access code names is either refused whole to an app-only generic writer
/// (<see cref="InheritedAccessPermission.AccessTables"/>) or listed here as a table whose rows are not access, with its
/// reason. It replaces the hand-kept drift list in <c>AccessTableAppOnlyWriterTests</c>: a new reader of a new access table
/// fails here until the table is refused or listed.
/// </summary>
/// <remarks>
/// <para><b>What is scanned.</b> The <c>.cs</c> files under the BFF's <c>Infrastructure/ExternalAccess</c>,
/// <c>Services/Access</c>, <c>Services/ExternalAccess</c> and <c>Api/ExternalAccess</c>, and (#2253 fix round 3) every
/// other folder that reads grant, membership, wall, container or secure-root data (<c>ScannedFolders</c>), line
/// comments stripped. A table is
/// named by: a string constant whose name ends in <c>EntityLogicalName</c>, <c>EntitySet</c>, <c>EntitySetName</c>,
/// <c>LogicalName</c>, <c>Entity</c> or <c>Table</c>; a literal passed to <c>QueryExpression</c>, <c>LinkEntity</c>,
/// <c>AddLink</c>, <c>RetrieveAsync</c>, <c>QueryAsync</c>, <c>CreateAsync</c>, <c>UpdateAsync</c> or <c>DeleteAsync</c>;
/// or a URL literal that starts with a collection (<c>"contacts?$</c>, <c>$"systemusers({</c>).</para>
/// <para><b>Collections.</b> An entity-set name is matched to its logical name by Dataverse's plural (<c>s</c>,
/// <c>es</c>, <c>y</c> to <c>ies</c>).</para>
/// <para><b>Controls.</b> <see cref="TheDetector_MustFireAndMustNotFire"/> runs the same detector over fixture text.
/// <see cref="EveryListedTable_IsStillNamed"/> keeps the list from going stale.</para>
/// </remarks>
public class AccessTableReaderScanTests
{
    /// <summary>The tables the access code names whose rows are NOT access, each with its reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotAccessTables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "A secure root: its access columns are refused per column (SecureRootColumns), the rest is ordinary data.",
            ["sprk_project"] = "A secure root: its access columns are refused per column (SecureRootColumns).",
            ["sprk_workassignment"] = "A secure root: its access columns are refused per column (SecureRootColumns).",
            ["contact"] = "A person: its identity-binding and standing-grant columns are refused per column (AccessColumns).",
            ["sprk_organization"] = "An organisation: its standing-grant columns are refused per column (AccessColumns).",
            ["systemuser"] = "A user: its home link, task-141 link, external flag, business unit, identity and eligibility are refused per column (AccessColumns).",
            ["businessunit"] = "A business unit: its container, name and parent (a plain Update can re-parent it) are refused per column (AccessColumns).",
            ["team"] = "A team: its owner-team predicates are refused per column (AccessColumns); membership is teammembership (refused).",
            ["sprk_document"] = "A child record: its Access Permission, own narrowing, D7 links and BFF-owned columns are refused per column.",
            ["sprk_communication"] = "A child record: its internal-only flag is refused per column (AccessColumns), its owner is refused (OwnerColumns); the rest follows its parents.",
            ["sprk_event"] = "A child record; its access follows its parents, its owner and creator are refused (OwnerColumns, CreatorColumns).",
            ["sprk_todo"] = "A child record; its access follows its parents, its owner and creator are refused.",
            ["sprk_invoice"] = "A child record; its access follows its parents, its owner and creator are refused.",
            ["sprk_servicerequest"] = "A child record; its requester is refused (CreatorColumns), its access follows its parents.",
            ["account"] = "A customer account; no access decision reads its columns.",

            // #2253 fix round 3 (K): found by the wider scan. Refused per column (AccessColumns):
            ["sprk_communicationthread"] = "A thread: its privacy state and thread type are refused per column (AccessColumns); its owner is refused.",
            ["sprk_analysis"] = "An analysis: every anchor lookup the read rule requires Read on is refused per column (AccessColumns).",
            ["sprk_communicationattachment"] = "An attachment row: its file pointer and its communication / document links are refused per column (AccessColumns).",
            // #2253 fix round 4 (F-1): the proposal's message decides who sees and applies it.
            ["sprk_emailreviewlog"] = "The proposal and decision log: its communication, which decides who sees and applies a proposal, is refused per column (AccessColumns); its target is re-checked against the caller's own rights.",
            // ebilling-r1 (master merge): the external submission routes scope a submission by its firm and core record.
            // UAC review F4 (on e371dce6bf): + its invoice and tracking number, which D-176's Duplicate withholding reads.
            ["sprk_invoicesubmission"] = "An invoice submission: its firm (sprk_vendororg) and core-record lookups, which scope the external submission reads, and its invoice (sprk_invoice) and tracking number (sprk_trackingnumber), which D-176's Duplicate withholding resolves an earlier invoice through (EarlierInvoiceReadability), are refused per column (AccessColumns).",

            // Not access, each read in the code:
            ["sprk_analysisskill"] = "A scope row /create associates; the check is the caller's own rights on the analysis, no column of it is read (AnalysisAuthorizationFilter).",
            ["sprk_analysisknowledge"] = "A scope row /create associates; the check is the caller's own rights on the analysis, no column of it is read (AnalysisAuthorizationFilter).",
            ["sprk_analysistool"] = "A scope row /create associates; the check is the caller's own rights on the analysis, no column of it is read (AnalysisAuthorizationFilter).",
            ["sprk_emailupdatefield"] = "The fields an email proposal may set; the apply runs impersonated (CommunicationProposalApplyService), so Dataverse still enforces the caller's rights.",
            ["sprk_communicationrule"] = "AI confidence thresholds and task due days (CommunicationRuleGate); sprk_flagprivilege is legal privilege, not access.",
            ["sprk_gridconfiguration"] = "UI grid configuration (GridConfigurationService).",
            ["savedquery"] = "View definitions (SavedQueryService).",
            ["privilege"] = "Platform-defined and read-only (Dataverse refuses a write); UserPrivilegeChecker reads its names.",
            ["template"] = "Email templates; the filter checks the caller's own Read on the row (CommunicationRecordAuthorizationFilter).",
            // ebilling-r1 (master merge): the invoice family's FR-05 guard reads these tables' filing lookups.
            // UAC review F4 (option b): its invoice IS read by an access decision; refusing it is pending a decision (see the reason).
            ["sprk_billingevent"] = "An invoice line, a child record: its access follows its parents (SecureChildLineage); the FR-05 guard reads its core-record lookups and its invoice to keep it filed with its invoice (InvoiceFamilyCoreRecordGuard, OwnedChildWrite). Its invoice (sprk_invoice) is also read, as the caller, to reach a billing-event Duplicate's current invoice, whose submissions give the vendor org D-176's withholding resolves in (SignalReadService.DuplicatesShownAsync). It is NOT refused per column yet: the supported detach / move / re-attach sequence (D-96, DetachMoveReattachHint) writes it through the playbook update and the output orchestrator. Pending a decision (UAC review F4).",
            // UAC review F4 (on e371dce6bf): found when the scan took in Services/Signals.
            ["sprk_policy"] = "A Signal rule, a child of its matter (SecureChildLineage, #2084): its access follows its parents. The Signals code reads its scope, code, severity and work type to choose which rules apply and how a Signal is labelled (PolicyScopeResolver, SignalReadService), never who may see a row.",
            ["sprk_policyversion"] = "A rule's version (its body), a child of its rule (SecureChildLineage): the Signals code reads it to evaluate and label (DecisionPlanService, SignalReevaluationJob, DecisionRecordWriter); no access decision reads its columns.",
            ["sprk_ruleset"] = "Billing rule configuration: BillingFactLoader reads the active rule set to evaluate an invoice; no access decision reads its columns.",
            ["sprk_budget"] = "A budget, a child record (SecureChildLineage): a rule predicate compares its figures (PredicateCompiler) and a decision action revises them (DecisionActionExecutors); no access decision reads its columns.",
            ["sprk_budgetrevision"] = "A budget revision, a child of its budget and matter (SecureChildLineage): a decision action writes it (DecisionCommitFollowUps, DecisionActionExecutors); no access decision reads its columns.",
            ["sprk_externalidentifier"] = "A party's identifier for an invoice or core record (D-68), a child record: its access follows its parents (SecureChildLineage); the FR-05 guard reads its core-record and invoice lookups (InvoiceFamilyCoreRecordGuard), and nothing in the BFF reads its value to match or show anything.",
        };

    /// <summary>
    /// #2253 fix round 3 (K): names the detector matches that are not tables, each with why. Kept apart from
    /// <see cref="NotAccessTables"/> so that list holds only tables.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotTables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["contains"] = "The OData function in a thread-name search filter (CommunicationThreadReadService).",
            ["sprk_entitylogicalname"] = "A column (on sprk_userentityassociation) whose constant's name ends in LogicalName.",
            ["sprk_vendororg"] = "A column: EarlierInvoiceReadability.VendorOrgLogicalName, the LOGICAL name of sprk_vendororg that a Microsoft.Dynamics.CRM.In filter needs.",
        };

    /// <summary>
    /// UAC review F4 (on e371dce6bf), a ratchet baseline: tables found when the scan took in <c>Services/Signals</c> whose rows are
    /// not access but some of whose columns an access decision DOES read, and which are not yet refused per column
    /// (<see cref="InheritedAccessPermission.AccessColumns"/>). Each reason names the columns. Reported for a decision; a new table
    /// of this kind fails the scan like any other. Remove an entry when its columns are refused (it then moves to
    /// <see cref="NotAccessTables"/>) or judged to decide nothing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AccessColumnsNotYetRefused =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_signal"] = "A Signal, a child record (SecureChildLineage). SignalCoreRecordAccess.AuthorizeAsync decides who sees a Signal from its core-record pair (sprk_corerecordtype, sprk_corerecordid: not lineage lookups, so a write of them is no re-file), its lane (sprk_lane: the Do-lane 'mine' rule) and its subject (sprk_regardingrecordid and the subject lookups). None is refused per column.",
            ["sprk_decisionrecord"] = "A Decision Record, a child record (SecureChildLineage), append-only by convention. GateDecisionRecordSharing shares a gate record Read with its communication's internal sharees, selecting the records by sprk_confirmedby being empty: a generic write that clears it would make a confirmed record shareable. Not refused per column.",
        };

    private static readonly string[] ScannedFolders =
    [
        Path.Combine("Infrastructure", "ExternalAccess"),
        Path.Combine("Services", "Access"),
        Path.Combine("Services", "ExternalAccess"),
        Path.Combine("Api", "ExternalAccess"),
        // #2253 fix round 3 (K): every other folder that reads grant, membership, wall, container or secure-root data.
        Path.Combine("Infrastructure", "Dataverse"),
        Path.Combine("Infrastructure", "Authentication"),
        Path.Combine("Infrastructure", "Graph"),
        Path.Combine("Services", "Dataverse"),
        Path.Combine("Services", "Communication"),
        Path.Combine("Services", "Ai", "Membership"),
        Path.Combine("Services", "Documents"),
        Path.Combine("Services", "RecordCreation"),
        Path.Combine("Api", "Filters"),
        // #2253 fix round 4 (K-2): the SPE admin endpoints and services, which read the container-type configuration.
        Path.Combine("Api", "SpeAdmin"),
        Path.Combine("Services", "SpeAdmin"),
        // UAC review F4 (on e371dce6bf): D-176's Duplicate withholding (EarlierInvoiceReadability, SignalReadService) decides
        // what a reader sees from the submission and billing-event columns it reads.
        Path.Combine("Services", "Signals"),
    ];

    private static readonly Regex TableConstant = new(
        @"const\s+string\s+(?<name>\w*(?:EntityLogicalName|EntitySetName|EntitySet|LogicalName|Entity|Table))\s*=\s*""(?<table>[a-z][a-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly Regex TableArgument = new(
        @"\b(?:QueryExpression|LinkEntity|AddLink|RetrieveAsync|QueryAsync|CreateAsync|UpdateAsync|DeleteAsync)(?:<[\w,\s<>]*>)?\(\s*""(?<table>[a-z][a-z0-9_]*)""",
        RegexOptions.Compiled);

    private static readonly Regex UrlLiteral = new(
        @"\$?""(?<table>[a-z][a-z0-9_]*)(?:\?\$|\(\{)",
        RegexOptions.Compiled);

    [Fact(DisplayName = "#2253 K3: every table the access code names is refused whole or listed as not access")]
    public void EveryTableTheAccessCodeNames_IsRefusedWholeOrListed()
    {
        var offenders = ScanServer()
            .Where(hit => !IsCovered(hit.Table))
            .Select(hit => $"{hit.Table} ({hit.File}:{hit.Line})")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "These tables are named by the access code but are neither in InheritedAccessPermission.AccessTables nor listed in "
            + "AccessTableReaderScanTests.NotAccessTables with a reason:\n  " + string.Join("\n  ", offenders));
    }

    [Fact(DisplayName = "#2253 K3: every table listed as not access is still named by the access code")]
    public void EveryListedTable_IsStillNamed()
    {
        var named = ScanServer().Select(hit => Logical(hit.Table)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = NotAccessTables.Keys.Concat(AccessColumnsNotYetRefused.Keys).Where(t => !named.Contains(t)).ToList();
        Assert.True(stale.Count == 0, "Listed but no longer named (remove them): " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "#2253 K3: no table is both refused whole and listed as not access")]
    public void NoTable_IsBothRefusedAndListed()
    {
        Assert.Empty(NotAccessTables.Keys.Where(InheritedAccessPermission.AccessTables.Contains));
        Assert.Empty(AccessColumnsNotYetRefused.Keys.Where(t =>
            InheritedAccessPermission.AccessTables.Contains(t) || InheritedAccessPermission.AccessColumns.ContainsKey(t)
            || NotAccessTables.ContainsKey(t)));
    }

    [Fact(DisplayName = "#2253 K3: the detector fires on a new access table and not on a comment, a column or a covered set")]
    public void TheDetector_MustFireAndMustNotFire()
    {
        const string mustFire = """
            internal const string EntityLogicalName = "sprk_accessgrantledger";
            var rows = await _dataverse.QueryAsync<Row>("sprk_sharingrules", filter, select, cancellationToken: ct);
            var url = $"sprk_walls({id})/sprk_members";
            var q = new QueryExpression("sprk_delegation");
            """;
        const string mustNotFire = """
            // internal const string EntityLogicalName = "sprk_secretgrant";
            internal const string StandingGrantAttribute = "sprk_standinggrant";
            internal const string RecordTypeLogicalNameColumn = "sprk_recordlogicalname";
            internal const string EntitySet = "sprk_noaccessentries";
            var url = $"teammemberships?$filter=systemuserid eq {id}";
            """;

        Assert.Equal(
            new[] { "sprk_accessgrantledger", "sprk_delegation", "sprk_sharingrules", "sprk_walls" },
            Scan("fixture.cs", mustFire).Where(h => !IsCovered(h.Table)).Select(h => h.Table).Order().ToArray());
        Assert.Empty(Scan("fixture.cs", mustNotFire).Where(h => !IsCovered(h.Table)));
        Assert.DoesNotContain(Scan("fixture.cs", mustNotFire), h => h.Table == "sprk_secretgrant");
    }

    private static IEnumerable<(string Table, string File, int Line)> ScanServer()
    {
        var bff = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        var files = ScannedFolders
            .Select(f => Path.Combine(bff, f))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToList();
        Assert.NotEmpty(files);
        return files.SelectMany(f => Scan(SourceScan.Relative(f), File.ReadAllText(f))).ToList();
    }

    private static IEnumerable<(string Table, string File, int Line)> Scan(string file, string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        foreach (var regex in new[] { TableConstant, TableArgument, UrlLiteral })
        {
            foreach (Match m in regex.Matches(code))
                yield return (m.Groups["table"].Value, file, SourceScan.LineOf(code, m.Index));
        }
    }

    /// <summary>Whether <paramref name="table"/> (a logical name or a collection) is refused whole or listed.</summary>
    private static bool IsCovered(string table)
    {
        var known = InheritedAccessPermission.AccessTables.Concat(NotAccessTables.Keys).Concat(AccessColumnsNotYetRefused.Keys);
        return known.Any(t => SameTable(table, t)) || NotTables.ContainsKey(table);
    }

    /// <summary>The listed or refused logical name <paramref name="table"/> names, or itself.</summary>
    private static string Logical(string table) =>
        InheritedAccessPermission.AccessTables.Concat(NotAccessTables.Keys).Concat(AccessColumnsNotYetRefused.Keys)
            .FirstOrDefault(t => SameTable(table, t)) ?? table;

    [Fact(DisplayName = "#2253 fix round 3: every name listed as not a table is still matched by the detector")]
    public void EveryListedNonTable_IsStillMatched()
    {
        var named = ScanServer().Select(hit => hit.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = NotTables.Keys.Where(t => !named.Contains(t)).ToList();
        Assert.True(stale.Count == 0, "Listed but no longer matched (remove them): " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "#2253 fix round 3: the access columns' tables are listed, never also refused whole")]
    public void EveryAccessColumnTable_IsListed_AndNotRefusedWhole()
    {
        foreach (var table in InheritedAccessPermission.AccessColumns.Keys)
        {
            Assert.False(InheritedAccessPermission.AccessTables.Contains(table), table + " is refused whole and per column");

            // #2253 fix round 4 (K-3): and listed, so its reason says it is refused per column.
            Assert.True(NotAccessTables.ContainsKey(table), table + " has access columns but is not listed in NotAccessTables");
        }

        Assert.DoesNotContain("sprk_recordtype_ref", NotAccessTables.Keys);
        Assert.Contains("sprk_recordtype_ref", InheritedAccessPermission.AccessTables);
    }

    private static bool SameTable(string named, string logical) =>
        string.Equals(named, logical, StringComparison.OrdinalIgnoreCase)
        || string.Equals(named, logical + "s", StringComparison.OrdinalIgnoreCase)
        || string.Equals(named, logical + "es", StringComparison.OrdinalIgnoreCase)
        || (logical.EndsWith('y') && string.Equals(named, logical[..^1] + "ies", StringComparison.OrdinalIgnoreCase));
}
