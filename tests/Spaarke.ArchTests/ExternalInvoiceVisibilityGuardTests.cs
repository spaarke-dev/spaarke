using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 098 (procedure-and-file-model 9.1, (a′)): every external-plane method that reads <c>sprk_invoice</c> or a
/// table of its taint closure REACHES THE invoice predicate (<see cref="ExternalInvoiceVisibility"/>), or is listed with its
/// reason. Self-contained; it reuses only <see cref="IlCallScan"/> (the ONE IL reader).
/// </summary>
/// <remarks>
/// <para><b>Scope (the external plane).</b> Types under <c>Sprk.Bff.Api.Api.ExternalAccess</c> and
/// <c>Sprk.Bff.Api.Infrastructure.ExternalAccess</c>, the assistant's Dataverse tools (<c>Sprk.Bff.Api.Services.Ai.Handlers</c>,
/// which run for whoever is signed in, an external caller included), and the types later tasks add to that plane by name
/// (<see cref="NamedExternalTypes"/>: 130's firm findings). The predicate's own types are its owners, not surfaces.</para>
/// <para><b>Surface primitives</b> (what an invoice-family read is built from): a string constant that names a family table
/// (its logical name or entity set, as a whole word, so <c>_sprk_invoice_value</c> on another table does not count); a call to
/// an <see cref="ExternalDataService"/> method whose body names one (an app-only invoice-family reader, such as
/// <c>GetInvoiceParentsAsync</c>); a call into a family reader service (<see cref="FamilyReaderTypes"/>: Stored Files,
/// submissions, the invoice search 152 reads through); and the family test
/// <see cref="ExternalInvoiceVisibility.IsInvoiceFamilyTable"/>.</para>
/// <para><b>Reach primitives</b>: a <see cref="ExternalInvoiceHiding"/>, <see cref="ExternalInvoiceSubmissionHiding"/> or
/// <see cref="AppOnlyInvoiceFamilyHiding"/> invocation, <see cref="ExternalDataService.HideInvoicesForAsync"/>,
/// <see cref="ExternalDataService.HideSubmissionsForAsync"/>, <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/>,
/// <see cref="ExternalInvoiceVisibility.DecideAsync"/>, <see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/> (review
/// round 4b: the submission routes' decision, which applies the invoice predicate to a submission with an invoice) and
/// <see cref="ExternalInvoiceVisibility.HiddenFamilyRowsAsync"/>. The source decision alone
/// (<see cref="ExternalInvoiceVisibility.DecideSourceDocumentsAsync"/>) is NOT a reach: it does not decide an invoice. Reach
/// is transitive over the BFF's own methods, per SOURCE method (lambdas, local functions and state machines attributed to the
/// method they were written in), as in task 188's guard. Reach says a method CAN get to the predicate; the surface tests
/// (ExternalInvoiceVisibilityTests, the route and tool tests) pin the paths.</para>
/// <para><b>Limits.</b> A family read built from none of the primitives (a table name assembled at run time, a new reader service
/// not in <see cref="FamilyReaderTypes"/>) is not seen; the primitive lists are the contract a reviewer extends.</para>
/// </remarks>
public class ExternalInvoiceVisibilityGuardTests
{
    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    private static readonly string[] ExternalPlaneNamespaces =
    {
        "Sprk.Bff.Api.Api.ExternalAccess", "Sprk.Bff.Api.Infrastructure.ExternalAccess", "Sprk.Bff.Api.Services.Ai.Handlers",
    };

    /// <summary>External-plane types outside those namespaces, by name (they need not exist yet), with their owning task.</summary>
    private static readonly IReadOnlyDictionary<string, string> NamedExternalTypes = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Api.Finance.FirmOutcomeEndpoints"] = "ebilling-r1 task 130: the firm findings routes (external).",
        ["Sprk.Bff.Api.Services.Finance.FirmFindingsProjection"] = "ebilling-r1 task 130: the firm-visible findings projection.",
    };

    /// <summary>Services that read invoice-family rows: a call into one from the external plane is a surface (by name, so a
    /// reader a later task adds is covered when it appears).</summary>
    private static readonly IReadOnlyDictionary<string, string> FamilyReaderTypes = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Services.Finance.Files.StoredFileService"] = "sprk_storedfile (090); 093's firm file routes read through it.",
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService"] = "sprk_invoicesubmission (the submission routes).",
        ["Sprk.Bff.Api.Services.Finance.InvoiceSearchService"] = "the invoices index (152's external invoice-index retrieval).",
        ["Sprk.Bff.Api.Services.Finance.FirmFindingsProjection"] = "sprk_signal / sprk_decisionrecord findings (130).",
    };

    /// <summary>The predicate's own types: they implement it (or are its app-only reader), so they are not surfaces.</summary>
    private static readonly Type[] PredicateOwners = { typeof(ExternalInvoiceVisibility), typeof(ExternalDataService) };

    /// <summary>
    /// External-plane methods that name an invoice-family table or call a family reader but return no invoice-family row to an
    /// external caller, each with its reason. A new entry needs a reason a reviewer can check; one that starts reaching the
    /// predicate, or stops being a surface, must be removed (the ratchet).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotInvoiceSurfaces = new Dictionary<string, string>
    {
        // ── configuration: names a family table, reads nothing ──
        [$"{typeof(ExternalMessageEndpoints).FullName}::.cctor"] =
            "The message roots table (\"invoices\" → sprk_invoice): configuration; the routes it serves reach the predicate.",
        [$"{typeof(ExternalMessageEndpoints).FullName}::DataverseConfirmedEntitySet"] =
            "Maps a message root to its entity set for the caller's own Dataverse probe; reads no row.",
        [$"{typeof(WorkforceRoleCeilingGate).FullName}::.cctor"] =
            "The Workforce role's privilege table (table names → privileges); reads no row.",
        [$"{typeof(WorkforceRoleCeilingGate).FullName}::SubmitInvoiceTo"] =
            "Names the privilege a submit needs (Create on sprk_invoicesubmission); reads no row.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler::ToTableLogicalName"] =
            "Maps a scope word (\"invoices\") to a table name for the search filter; the hits are decided in ExecuteChatAsync.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.EmailDraftToolHandler::.cctor"] =
            "The regarding lookup map (sprk_invoice → sprk_regardinginvoice); configuration.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.EmailDraftToolHandler::.ctor"] =
            "The regarding lookup map (initializer); configuration.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler::.cctor"] =
            "The chat create's blocked server-written invoice tables (ebilling-r1 review of f758480f3c); refuses a create, reads no row.",
        // ── parts of the predicate's application (inputs and the decision's consequence) ──
        [$"{typeof(ExternalMessageEndpoints).FullName}::RegardingInvoicesOf"] =
            "Extracts a message page's regarding invoice ids FOR the predicate (SecureRootsFilter → RegardingInvoicesVisibleAsync).",
        [$"{typeof(ExternalMessageEndpoints).FullName}::RightsFromGate"] =
            "Turns the record gate's answer into rights; the gate (EvaluateRecordGateAsync) applied the predicate first.",
        [$"{typeof(ExternalModuleDataEndpoints).FullName}::InvoiceFamilyKeyAttributes"] =
            "Projects sprk_invoiceid so the predicate can key each row (ExecuteScopedFetchCoreAsync applies it).",
        // ── reads that return no invoice-family row ──
        [$"{typeof(ExternalInvoiceSubmissionAuthorizationFilter).FullName}::DenyUnlinkedFirmAsync"] =
            "Checks the firm is linked to the core record (D-106); returns a refusal or nothing, no submission row.",
        [$"{typeof(ExternalInvoiceSubmissionEndpoints).FullName}::FirmRecordsAsync"] =
            "Returns the ids of the caller's OWN readable matters / projects / work assignments linked to a firm; no family row.",
        [$"{typeof(ExternalInvoiceSubmissionEndpoints).FullName}::SubmitAsync"] =
            "Creates the caller's own submission and returns its receipt (tracking number); a new submission has no invoice yet.",
        [$"{typeof(ExternalUserContextEndpoint).FullName}::HandleAsync"] =
            "Reads the caller's active firm NAMES for the SPA header; no family row.",
    };

    /// <summary>
    /// The surfaces task 098 wired (model 9.1 (a)-(d)): each must be SEEN as a surface and seen REACHING the predicate, so the
    /// scan cannot lose its signal silently.
    /// </summary>
    private static readonly string[] WiredSurfaces =
    {
        $"{typeof(ExternalModuleDataEndpoints).FullName}::ExecuteScopedFetchCoreAsync",       // (a) the invoices module fetch
        $"{typeof(ExternalMessageEndpoints).FullName}::EvaluateRecordGateAsync",              // (b) an invoice thread's gate
        $"{typeof(ExternalProjectDataEndpoints).FullName}::RightsForInvoiceAsync",            // (b′) invoice documents
        $"{typeof(ExternalInvoiceSubmissionEndpoints).FullName}::ListAsync",                   // (c) the submission list
        $"{typeof(ExternalInvoiceSubmissionAuthorizationFilter).FullName}::DecideReadOneAsync", // (c) the submission read
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseReadQueryHandler::ExecuteChatAsync",      // (d)
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler::ExecuteChatAsync",     // (d)
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseDescribeHandler::DescribeRecordAsync",   // (d)
    };

    /// <summary>Surfaces that reach the predicate through a parameter rather than a family read of their own: each must REACH.</summary>
    private static readonly string[] WiredReaches =
    {
        $"{typeof(ExternalMessageEndpoints).FullName}::SecureRootsFilter",          // (b) a message regarding a hidden invoice
        $"{typeof(ExternalMessageEndpoints).FullName}::GetMessagesCoreAsync",
        $"{typeof(ExternalMessageEndpoints).FullName}::PostMessageCoreAsync",
        $"{typeof(ExternalNotificationFeedEndpoints).FullName}::GetFeedCoreAsync",  // (b) the feed
        $"{typeof(ExternalProjectDataEndpoints).FullName}::AuthorizeDeleteOwnAsync", // delete-own under an invoice
    };

    [Fact(DisplayName = "Task 098: every external invoice-family surface reaches THE (a') predicate (or is listed)")]
    public void EveryExternalInvoiceFamilySurface_ReachesThePredicate()
    {
        var scan = Scan(BffTypes.Value);

        var problems = new List<string>();
        foreach (var surface in WiredSurfaces)
        {
            if (!scan.Surfaces.ContainsKey(surface))
                problems.Add($"SIGNAL LOST: {surface} is no longer seen reading the invoice family — fix the primitives.");
            if (!scan.Reaches.Contains(surface))
                problems.Add($"WIRED surface no longer reaches the predicate: {surface}");
        }

        foreach (var surface in WiredReaches.Where(s => !scan.Reaches.Contains(s)))
            problems.Add($"WIRED surface no longer reaches the predicate: {surface}");

        foreach (var (method, primitives) in scan.Surfaces.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (scan.Reaches.Contains(method) || NotInvoiceSurfaces.ContainsKey(method))
                continue;
            problems.Add($"NEW external invoice-family surface without the (a') predicate: {method} (reads through {string.Join(", ", primitives.Order())})");
        }

        foreach (var listed in NotInvoiceSurfaces.Keys)
        {
            if (!scan.Surfaces.ContainsKey(listed))
                problems.Add($"STALE entry {listed}: it no longer reads the invoice family — remove it.");
            else if (scan.Reaches.Contains(listed))
                problems.Add($"STALE entry {listed}: it now reaches the predicate — remove it (the ratchet).");
        }

        Assert.True(problems.Count == 0,
            "An external surface that returns invoice-family rows must apply the (a') predicate (ebilling-r1 task 098, model 9.1): " +
            "inject ExternalInvoiceHiding or ExternalInvoiceSubmissionHiding (external routes) or AppOnlyInvoiceFamilyHiding " +
            "(assistant tools), or list the method " +
            "with its reason in NotInvoiceSurfaces:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 098: negative control — an invoice read with only a mention of the predicate is found; a lambda or a helper that applies it is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(InvoiceGuardControlMentionOnly), typeof(InvoiceGuardControlHidesInALambda),
            typeof(InvoiceGuardControlHidesThroughAHelper), typeof(InvoiceGuardControlNoInvoices),
            typeof(InvoiceGuardControlLineReadOnly), typeof(InvoiceGuardControlCallsAFamilyReader),
            typeof(InvoiceGuardControlCallsAFamilyReaderThroughItsInterface), typeof(InvoiceGuardControlCallsAnUnrelatedInterface),
            typeof(InvoiceGuardControlListsSubmissionsThroughTheSubmissionSeam),
            typeof(InvoiceGuardControlListsSubmissionsWithOnlyTheSourceDecision),
        };
        var scan = Scan(fixtures.SelectMany(IlCallScan.WithNested), inScope: _ => true);

        var flagged = scan.Surfaces.Keys.Where(k => !scan.Reaches.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                $"{typeof(InvoiceGuardControlCallsAFamilyReader).FullName}::ListAsync",
                $"{typeof(InvoiceGuardControlCallsAFamilyReaderThroughItsInterface).FullName}::ListAsync",
                $"{typeof(InvoiceGuardControlLineReadOnly).FullName}::ListAsync",
                $"{typeof(InvoiceGuardControlMentionOnly).FullName}::ListAsync",
                $"{typeof(InvoiceGuardControlListsSubmissionsWithOnlyTheSourceDecision).FullName}::ListAsync",
            }.Order(StringComparer.Ordinal).ToArray(),
            flagged);
        Assert.Contains($"{typeof(InvoiceGuardControlHidesInALambda).FullName}::ListAsync", scan.Reaches);
        Assert.Contains($"{typeof(InvoiceGuardControlHidesThroughAHelper).FullName}::ListAsync", scan.Reaches);
        Assert.Contains($"{typeof(InvoiceGuardControlListsSubmissionsThroughTheSubmissionSeam).FullName}::ListAsync", scan.Reaches);
        Assert.DoesNotContain(scan.Surfaces.Keys, k => k.StartsWith(typeof(InvoiceGuardControlNoInvoices).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Surfaces.Keys,
            k => k.StartsWith(typeof(InvoiceGuardControlCallsAnUnrelatedInterface).FullName!, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 098: the family-table token matches a whole table name, not a lookup column on another table")]
    public void FamilyTableToken_MustFireAndMustNotFire()
    {
        Assert.True(NamesAFamilyTable("sprk_invoices?$select=sprk_name"));
        Assert.True(NamesAFamilyTable("sprk_invoice"));
        Assert.True(NamesAFamilyTable("<entity name='sprk_billingevent'>"));
        Assert.True(NamesAFamilyTable("sprk_storedfiles"));
        Assert.False(NamesAFamilyTable("_sprk_invoice_value"));
        Assert.False(NamesAFamilyTable("sprk_invoiceid"));
        Assert.False(NamesAFamilyTable("sprk_regardinginvoice"));
        Assert.False(NamesAFamilyTable("sprk_documents"));
    }

    // ── The 093 / 130 / 152 entries (not on this branch): named so they fail until they reach the predicate ─────────────

    /// <summary>
    /// The model 9.1 surfaces later tasks build, each with what makes the guard see it. They are not on this branch; when they
    /// land they are external invoice-family surfaces by the primitives above, so they FAIL this guard until they reach the
    /// predicate (or a reviewer lists them with a reason).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> PendingSurfaces = new Dictionary<string, string>
    {
        ["093"] = "The firm file routes (GET /api/v1/external/invoices/{id}/files and …/content) in ExternalInvoiceSubmissionEndpoints / "
            + "ExternalInvoiceSubmissionAuthorizationFilter: external-plane namespace; they read sprk_storedfile through StoredFileService.",
        ["130"] = "The firm findings projection (FirmFindingsProjection, FirmOutcomeEndpoints): named external types; they read "
            + "sprk_signal / sprk_decisionrecord.",
        ["152"] = "External invoice-index retrieval: InvoiceSearchService is a family reader; a call from the external plane is a surface.",
    };

    [Fact(DisplayName = "Task 098: the 093, 130 and 152 surfaces are covered by the guard's primitives before they land")]
    public void TheLaterSurfaces_AreCoveredByThePrimitives()
    {
        Assert.Equal(new[] { "093", "130", "152" }, PendingSurfaces.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("Sprk.Bff.Api.Services.Finance.Files.StoredFileService", FamilyReaderTypes.Keys);
        Assert.Contains("Sprk.Bff.Api.Services.Finance.InvoiceSearchService", FamilyReaderTypes.Keys);
        Assert.Contains(typeof(Sprk.Bff.Api.Services.Finance.IInvoiceSearchService), FamilyReaderInterfaces.Value); // F3: 152 calls it so
        Assert.Contains("Sprk.Bff.Api.Services.Finance.FirmFindingsProjection", NamedExternalTypes.Keys);
        Assert.Contains("Sprk.Bff.Api.Api.Finance.FirmOutcomeEndpoints", NamedExternalTypes.Keys);
        Assert.True(InScope(typeof(ExternalInvoiceSubmissionEndpoints)), "093's routes live in the external-plane namespace.");
        Assert.True(InScope(typeof(ExternalInvoiceSubmissionAuthorizationFilter)), "093's gate lives in the external-plane namespace.");

        // The readers named before their owners land must name real types where they exist (a typo would hide a surface).
        foreach (var reader in FamilyReaderTypes.Keys.Where(r => !r.EndsWith(".FirmFindingsProjection", StringComparison.Ordinal)))
            Assert.True(typeof(Program).Assembly.GetType(reader) is not null, $"{reader} should exist in the BFF — check the name.");
    }

    // ── 090's flagged-external refusals (model 4.3, 5.3; uac-r2 F1-3, F1-e): listed and verified, not reimplemented ───────

    /// <summary>The internal Stored File routes refuse every caller step 0 does not admit (a flagged-external systemuser included),
    /// so the (a′) predicate is not needed there. Each must keep calling step 0.</summary>
    private static readonly IReadOnlyDictionary<string, string> RefusedToExternalCallers = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Api.Finance.StoredFileAccessFilter::InvokeAsync"] =
            "090: the internal file route's download filter refuses a caller step 0 refuses (flagged external, unresolvable, "
            + "fault) with 403 before any row read (StoredFileEndpointsTests, must-fire (a)-(c)).",
        ["Sprk.Bff.Api.Services.Finance.Files.StoredFileService::UploadForCallerAsync"] =
            "090: the upload entry refuses a caller step 0 refuses before any read or write (StoredFileServiceTests, upload must-fire).",
    };

    [Fact(DisplayName = "Task 098: 090's internal file route and upload entry keep refusing flagged-external callers (step 0)")]
    public void TheInternalFileRouteAndUpload_RefuseFlaggedExternalCallers()
    {
        var stepZeroCallers = StepZeroCallers(BffTypes.Value);

        foreach (var (method, reason) in RefusedToExternalCallers)
            Assert.True(stepZeroCallers.Contains(method), $"{method} no longer reaches FinanceAuthorizationFilter.EvaluateStepZeroAsync ({reason})");
    }

    [Fact(DisplayName = "Task 098: negative control — a method that calls step 0 is found; one that only names it is not")]
    public void StepZeroDetector_MustFireAndMustNotFire()
    {
        var callers = StepZeroCallers(new[] { typeof(InvoiceGuardControlCallsStepZero), typeof(InvoiceGuardControlNamesStepZero) }
            .SelectMany(IlCallScan.WithNested));

        Assert.Contains($"{typeof(InvoiceGuardControlCallsStepZero).FullName}::InvokeAsync", callers);
        Assert.Contains($"{typeof(InvoiceGuardControlCallsStepZero).FullName}::ThroughAHelperAsync", callers);
        Assert.DoesNotContain($"{typeof(InvoiceGuardControlNamesStepZero).FullName}::InvokeAsync", callers);
    }

    /// <summary>Every source method that REACHES step 0 (transitively over the BFF's own methods).</summary>
    private static IReadOnlySet<string> StepZeroCallers(IEnumerable<Type> types) =>
        ReachSet(types, target => target.DeclaringType?.FullName == "Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter"
            && target.Name == "EvaluateStepZeroAsync");

    /// <summary>Every source method whose compiled body references a method <paramref name="isTarget"/> accepts, or a BFF
    /// method that does (transitively).</summary>
    private static IReadOnlySet<string> ReachSet(IEnumerable<Type> types, Func<MethodBase, bool> isTarget)
    {
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reaches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var source = ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod);
            if (isTarget(target))
                reaches.Add(source);
            if (target.DeclaringType is { } declaring && declaring.Assembly == caller.Assembly)
            {
                if (!calls.TryGetValue(source, out var targets))
                    calls[source] = targets = new HashSet<string>(StringComparer.Ordinal);
                targets.Add($"{IlCallScan.Outermost(declaring).FullName}::{Normalize(target.Name)}");
            }
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (source, targets) in calls)
            {
                if (!reaches.Contains(source) && targets.Any(reaches.Contains))
                    changed = reaches.Add(source) || changed;
            }
        }

        return reaches;
    }

    // ── Review round 4b: the submission list and the single read decide through ONE function ────────────────────────────

    [Fact(DisplayName = "Task 098 review round 4b: the submission list and the single read call ONE decision function, and nothing else in their types decides")]
    public void TheSubmissionListAndRead_CallOneDecisionFunction()
    {
        var shared = $"{typeof(ExternalInvoiceSubmissionEndpoints).FullName}::{nameof(ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync)}";
        var (callers, deciders) = SubmissionDecisions(
            new[] { typeof(ExternalInvoiceSubmissionEndpoints), typeof(ExternalInvoiceSubmissionAuthorizationFilter) }
                .SelectMany(IlCallScan.WithNested),
            shared);

        Assert.Contains($"{typeof(ExternalInvoiceSubmissionEndpoints).FullName}::ListAsync", callers);
        Assert.Contains($"{typeof(ExternalInvoiceSubmissionAuthorizationFilter).FullName}::DecideReadOneAsync", callers);
        Assert.Equal(new[] { shared }, deciders.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact(DisplayName = "Task 098 review round 4b: negative control — a method that calls the shared decision is found; one that decides itself is flagged")]
    public void SubmissionDecisionDetector_MustFireAndMustNotFire()
    {
        var shared = $"{typeof(SubmissionDecisionControlShared).FullName}::{nameof(SubmissionDecisionControlShared.DecideAsync)}";
        var (callers, deciders) = SubmissionDecisions(
            new[] { typeof(SubmissionDecisionControlShared), typeof(SubmissionDecisionControlCallsTheShared),
                typeof(SubmissionDecisionControlDecidesItself), typeof(SubmissionDecisionControlNamesTheShared) }
                .SelectMany(IlCallScan.WithNested),
            shared);

        Assert.Equal(new[] { $"{typeof(SubmissionDecisionControlCallsTheShared).FullName}::ReadAsync" }, callers.ToArray());
        Assert.Equal(
            new[] { shared, $"{typeof(SubmissionDecisionControlDecidesItself).FullName}::ReadAsync" }.Order(StringComparer.Ordinal).ToArray(),
            deciders.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Among <paramref name="types"/>: the methods that call <paramref name="shared"/> directly, and the methods that apply a
    /// decision primitive themselves (an invoice or submission seam, the predicate's entries, or the source decision).
    /// </summary>
    private static (IReadOnlySet<string> Callers, IReadOnlySet<string> Deciders) SubmissionDecisions(IEnumerable<Type> types, string shared)
    {
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        var deciders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var source = ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod);
            if (target.DeclaringType is { } declaring && $"{IlCallScan.Outermost(declaring).FullName}::{Normalize(target.Name)}" == shared
                && source != shared)
                callers.Add(source);
            if (IsReach(target)
                || (target.DeclaringType == typeof(ExternalInvoiceVisibility)
                    && target.Name == nameof(ExternalInvoiceVisibility.DecideSourceDocumentsAsync)))
                deciders.Add(source);
        }

        return (callers, deciders);
    }

    // ── Review round 5 (F1): the assistant tools decide submission rows with the routes' function ─────────────────────────

    /// <summary>
    /// The functions the assistant tools' family seam decides through (<see cref="AppOnlyInvoiceFamilyHiding"/> is bound to the
    /// first by <c>ExternalAccessModule</c>, pinned by <see cref="TheAppOnlyInvoiceFamilySeam_HasOnlyListedConsumers"/>). Each must
    /// reach <see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>, so a submission row is decided exactly as on the
    /// routes (with an invoice, the invoice predicate; without one, its own source). Every other function that maps family rows
    /// to their invoices (<see cref="ExternalInvoiceVisibility.ReadInvoicesOfAsync"/>) is held to the same rule.
    /// </summary>
    private static readonly string[] ToolFamilyDecisionFunctions =
    {
        $"{typeof(ExternalDataService).FullName}::{nameof(ExternalDataService.HideInvoiceFamilyRowsAsync)}",
        $"{typeof(ExternalInvoiceVisibility).FullName}::{nameof(ExternalInvoiceVisibility.HiddenFamilyRowsAsync)}",
    };

    [Fact(DisplayName = "Task 098 review round 5: the assistant tools decide submission rows with DecideSubmissionsAsync, the routes' function")]
    public void TheAssistantTools_DecideSubmissionsWithTheRoutesFunction()
    {
        var (unreached, toolDeciders) = ToolSubmissionDecisions(
            BffTypes.Value, ToolFamilyDecisionFunctions,
            type => (IlCallScan.Outermost(type).Namespace ?? string.Empty).StartsWith("Sprk.Bff.Api.Services.Ai.Handlers", StringComparison.Ordinal));

        Assert.True(unreached.Count == 0 && toolDeciders.Count == 0,
            "The assistant tools must decide invoice-family rows only through AppOnlyInvoiceFamilyHiding, whose decision reaches " +
            "ExternalInvoiceVisibility.DecideSubmissionsAsync (a submission with no invoice is decided by its own source, as on the " +
            "routes):\n" + string.Join("\n",
                unreached.Select(f => $"DOES NOT REACH DecideSubmissionsAsync: {f}")
                    .Concat(toolDeciders.Select(d => $"TOOL DECIDES WITHOUT THE FAMILY SEAM: {d}"))));
    }

    [Fact(DisplayName = "Task 098 review round 5: negative control — a family decision without DecideSubmissionsAsync, or a tool deciding through another seam, is flagged; the real shape is not")]
    public void ToolSubmissionDecisionDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(ToolDecisionControlFamilyMappingWithoutTheSubmissionDecision),
            typeof(ToolDecisionControlFamilyMappingWithTheSubmissionDecision),
            typeof(ToolDecisionControlToolDecidesWithTheInvoiceSeam), typeof(ToolDecisionControlToolUsesTheFamilySeam),
        };

        var (unreached, toolDeciders) = ToolSubmissionDecisions(
            fixtures.SelectMany(IlCallScan.WithNested), Array.Empty<string>(),
            type => IlCallScan.Outermost(type).Name.StartsWith("ToolDecisionControlTool", StringComparison.Ordinal));

        Assert.Equal(new[] { $"{typeof(ToolDecisionControlFamilyMappingWithoutTheSubmissionDecision).FullName}::HiddenAsync" }, unreached.ToArray());
        Assert.Equal(new[] { $"{typeof(ToolDecisionControlToolDecidesWithTheInvoiceSeam).FullName}::ExecuteChatAsync" }, toolDeciders.ToArray());
    }

    /// <summary>
    /// Among <paramref name="types"/>: the family decision functions (<paramref name="required"/> plus every method that calls
    /// <see cref="ExternalInvoiceVisibility.ReadInvoicesOfAsync"/>) that do NOT reach
    /// <see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>; and the methods of tool types (<paramref name="isTool"/>)
    /// that apply an invoice decision other than the family seam or the submission decision.
    /// </summary>
    private static (IReadOnlyList<string> Unreached, IReadOnlyList<string> ToolDeciders) ToolSubmissionDecisions(
        IEnumerable<Type> types, IEnumerable<string> required, Func<Type, bool> isTool)
    {
        var list = types.ToList();
        var reaches = ReachSet(list, target => target.DeclaringType == typeof(ExternalInvoiceVisibility)
            && target.Name == nameof(ExternalInvoiceVisibility.DecideSubmissionsAsync));
        var mapping = $"{typeof(ExternalInvoiceVisibility).FullName}::{nameof(ExternalInvoiceVisibility.ReadInvoicesOfAsync)}";
        var functions = new SortedSet<string>(required, StringComparer.Ordinal);
        var toolDeciders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(list))
        {
            var source = ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod);
            if (target.DeclaringType == typeof(ExternalInvoiceVisibility)
                && target.Name == nameof(ExternalInvoiceVisibility.ReadInvoicesOfAsync) && source != mapping)
                functions.Add(source);
            if (isTool(caller) && DecidesOutsideTheToolSeam(target))
                toolDeciders.Add(source);
        }

        return (functions.Where(f => !reaches.Contains(f)).ToList(), toolDeciders.ToList());
    }

    /// <summary>An invoice decision a tool must not apply itself: every reach primitive except the family seam and the
    /// submission decision, and the source decision alone.</summary>
    private static bool DecidesOutsideTheToolSeam(MethodBase target) =>
        target.DeclaringType is { } declaring
        && (IsReach(target)
            || (declaring == typeof(ExternalInvoiceVisibility) && target.Name == nameof(ExternalInvoiceVisibility.DecideSourceDocumentsAsync)))
        && !(declaring == typeof(AppOnlyInvoiceFamilyHiding)
            || declaring == typeof(ExternalInvoiceSubmissionHiding)
            || (declaring == typeof(ExternalDataService)
                && target.Name is nameof(ExternalDataService.HideInvoiceFamilyRowsAsync) or nameof(ExternalDataService.HideSubmissionsForAsync))
            || (declaring == typeof(ExternalInvoiceVisibility)
                && target.Name is nameof(ExternalInvoiceVisibility.HiddenFamilyRowsAsync) or nameof(ExternalInvoiceVisibility.DecideSubmissionsAsync)));

    // ── The assistant tools' app-only seam is an allow-list (the second exception to the User-OBO rule) ───────────────────

    /// <summary>
    /// Every BFF type allowed to hold, resolve, invoke or build <see cref="AppOnlyInvoiceFamilyHiding"/>, or to call
    /// <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/> directly, with its reason. The seam reads app-only and returns
    /// a hide decision (an id set) only; a new consumer must be reviewed for that (escalated to uac-r2 with task 098).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedFamilySeamConsumers = new Dictionary<string, string>
    {
        ["Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule"] =
            "The ONE registrar: binds the seam to ExternalDataService.HideInvoiceFamilyRowsAsync (app-only).",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseReadQueryHandler"] =
            "Assistant tool (098 (d)): drops invoice-family rows of the user's own read.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler"] =
            "Assistant tool (098 (d)): drops invoice-family hits of the user's own search.",
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseDescribeHandler"] =
            "Assistant tool (098 (d)): answers a hidden invoice-family record like not-found.",
    };

    [Fact(DisplayName = "Task 098: the assistant's app-only invoice-family seam has only listed consumers")]
    public void TheAppOnlyInvoiceFamilySeam_HasOnlyListedConsumers()
    {
        var consumers = FamilySeamConsumers(BffTypes.Value);

        var problems = consumers.Keys.Where(k => !AllowedFamilySeamConsumers.ContainsKey(k)).Order(StringComparer.Ordinal)
            .Select(k => $"NEW consumer of AppOnlyInvoiceFamilyHiding: {k} ({string.Join("; ", consumers[k].Order())})")
            .Concat(AllowedFamilySeamConsumers.Keys.Where(k => !consumers.ContainsKey(k)).Order(StringComparer.Ordinal)
                .Select(k => $"STALE entry {k}: it no longer uses the seam — remove it (the ratchet)."))
            .ToList();
        Assert.True(problems.Count == 0,
            "AppOnlyInvoiceFamilyHiding reads app-only for the assistant's tools (a hide decision only; the second exception to " +
            "their User-OBO rule, escalated to uac-r2 with task 098). A new consumer must be reviewed and listed:\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 098: negative control — a type that takes, invokes, builds or names the family seam is found; one that does not is not")]
    public void FamilySeamConsumerDetector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(FamilySeamControlTakesTheSeam), typeof(FamilySeamControlCallsTheReaderDirectly),
            typeof(FamilySeamControlNamesTheSeam), typeof(InvoiceGuardControlNoInvoices),
        };

        var consumers = FamilySeamConsumers(fixtures.SelectMany(IlCallScan.WithNested));

        Assert.Equal(
            new[]
            {
                typeof(FamilySeamControlCallsTheReaderDirectly).FullName, typeof(FamilySeamControlNamesTheSeam).FullName,
                typeof(FamilySeamControlTakesTheSeam).FullName,
            }.Order(StringComparer.Ordinal).ToArray(),
            consumers.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyDictionary<string, HashSet<string>> FamilySeamConsumers(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var seam = typeof(AppOnlyInvoiceFamilyHiding);
        var list = types.Where(t => t != seam && IlCallScan.Outermost(t) != typeof(ExternalDataService)).ToList();
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(Type type, string how)
        {
            var key = IlCallScan.Outermost(type).FullName!;
            if (!found.TryGetValue(key, out var hows))
                found[key] = hows = new HashSet<string>(StringComparer.Ordinal);
            hows.Add(how);
        }

        bool Mentions(Type? type) =>
            type is not null && (type == seam || (type.IsGenericType && type.GetGenericArguments().Any(Mentions))
                || (type.HasElementType && Mentions(type.GetElementType())));

        foreach (var type in list)
        {
            foreach (var field in type.GetFields(all).Where(f => Mentions(f.FieldType)))
                Add(type, $"field {field.FieldType.Name}");
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.GetParameters().Any(p => Mentions(p.ParameterType)) || (method is MethodInfo m && Mentions(m.ReturnType)))
                    Add(type, "signature");
            }
        }

        foreach (var (caller, _, target) in IlCallScan.MethodReferences(list))
        {
            if (Mentions(target.DeclaringType)
                || (target is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().Any(Mentions))
                || target.GetParameters().Any(p => Mentions(p.ParameterType)))
                Add(caller, $"calls {target.DeclaringType?.Name}.{target.Name}");
            if (target.DeclaringType == typeof(ExternalDataService) && target.Name == nameof(ExternalDataService.HideInvoiceFamilyRowsAsync))
                Add(caller, "calls ExternalDataService.HideInvoiceFamilyRowsAsync");
        }

        foreach (var (caller, _, target) in IlCallScan.TypeReferences(list))
        {
            if (Mentions(target))
                Add(caller, $"type token {target.Name}");
        }

        foreach (var (caller, _, value) in IlCallScan.StringLoads(list))
        {
            if (value.Contains(seam.Name, StringComparison.Ordinal))
                Add(caller, "names the seam in a string");
        }

        return found;
    }

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ScanResult(IReadOnlyDictionary<string, HashSet<string>> Surfaces, IReadOnlySet<string> Reaches);

    /// <summary>The family tables' logical names and entity sets, as whole words.</summary>
    private static readonly Regex FamilyTableToken = new(
        @"(?<![A-Za-z0-9_])(" + string.Join("|", ExternalInvoiceVisibility.FamilyTables
            .SelectMany(t => new[] { t.Key, t.Value.EntitySet })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .Select(Regex.Escape)) + @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    internal static bool NamesAFamilyTable(string value) => FamilyTableToken.IsMatch(value);

    private static bool InScope(Type type)
    {
        var outermost = IlCallScan.Outermost(type);
        var name = outermost.FullName ?? string.Empty;
        return !PredicateOwners.Contains(outermost)
            && (NamedExternalTypes.ContainsKey(name)
                || ExternalPlaneNamespaces.Any(ns => (outermost.Namespace ?? string.Empty).StartsWith(ns, StringComparison.Ordinal)));
    }

    private static ScanResult Scan(IEnumerable<Type> types) => Scan(types, InScope);

    private static ScanResult Scan(IEnumerable<Type> types, Func<Type, bool> inScope)
    {
        var list = types.ToList();
        var surfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reaches = new HashSet<string>(StringComparer.Ordinal);
        var familyReaders = AppOnlyFamilyReaders();

        void Surface(string source, string primitive)
        {
            if (!surfaces.TryGetValue(source, out var used))
                surfaces[source] = used = new HashSet<string>(StringComparer.Ordinal);
            used.Add(primitive);
        }

        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(list))
        {
            var source = ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod);
            if (IsReach(target))
                reaches.Add(source);
            if (inScope(caller) && SurfacePrimitive(target, familyReaders) is { } primitive)
                Surface(source, primitive);

            if (target.DeclaringType is { } declaring && declaring.Assembly == caller.Assembly)
            {
                if (!calls.TryGetValue(source, out var targets))
                    calls[source] = targets = new HashSet<string>(StringComparer.Ordinal);
                targets.Add($"{IlCallScan.Outermost(declaring).FullName}::{Normalize(target.Name)}");
            }
        }

        foreach (var (caller, callerMethod, value) in IlCallScan.StringLoads(list))
        {
            if (inScope(caller) && FamilyTableToken.Match(value) is { Success: true } match)
                Surface(ExternalDocumentNarrowingGuardTests.SourceKey(caller, callerMethod), $"names {match.Value}");
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (source, targets) in calls)
            {
                if (!reaches.Contains(source) && targets.Any(reaches.Contains))
                    changed = reaches.Add(source) || changed;
            }
        }

        return new ScanResult(surfaces, reaches);
    }

    /// <summary>The <see cref="ExternalDataService"/> methods whose own body names a family table (its app-only family reads).</summary>
    private static IReadOnlySet<string> AppOnlyFamilyReaders() =>
        IlCallScan.StringLoads(IlCallScan.WithNested(typeof(ExternalDataService)))
            .Where(s => NamesAFamilyTable(s.Value))
            .Select(s => ExternalDocumentNarrowingGuardTests.SourceKey(s.Caller, s.CallerMethod))
            .Where(k => !k.EndsWith("::" + nameof(ExternalDataService.HideInvoicesForAsync), StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static string? SurfacePrimitive(MethodBase target, IReadOnlySet<string> familyReaders)
    {
        if (target.DeclaringType is not { } declaring)
            return null;
        var outermost = IlCallScan.Outermost(declaring);
        if (outermost == typeof(ExternalInvoiceVisibility) && target.Name == nameof(ExternalInvoiceVisibility.IsInvoiceFamilyTable))
            return "ExternalInvoiceVisibility.IsInvoiceFamilyTable";
        if (outermost == typeof(ExternalDataService) && familyReaders.Contains($"{outermost.FullName}::{Normalize(target.Name)}"))
            return $"ExternalDataService.{Normalize(target.Name)}";
        if (FamilyReaderTypes.ContainsKey(outermost.FullName ?? string.Empty) && !target.IsConstructor)
            return $"{outermost.Name}.{target.Name}";
        // Review round 1 (F3): consumers call a reader through its interface (IInvoiceSearchService), so the IL call target is
        // the interface; a call through an interface a listed reader implements is a surface too.
        if (declaring.IsInterface && FamilyReaderInterfaces.Value.Contains(declaring.IsConstructedGenericType ? declaring.GetGenericTypeDefinition() : declaring))
            return $"{declaring.Name}.{target.Name}";
        return null;
    }

    /// <summary>The BFF's own interfaces a listed family reader type implements (a call through one reaches the reader).</summary>
    private static readonly Lazy<IReadOnlySet<Type>> FamilyReaderInterfaces = new(() =>
        FamilyReaderTypes.Keys
            .Select(name => typeof(Program).Assembly.GetType(name))
            .OfType<Type>()
            .SelectMany(reader => reader.GetInterfaces())
            .Where(i => i.Assembly == typeof(Program).Assembly)
            .Select(i => i.IsConstructedGenericType ? i.GetGenericTypeDefinition() : i)
            .ToHashSet());

    private static bool IsReach(MethodBase method) =>
        method.DeclaringType is { } declaring
        && ((declaring == typeof(ExternalInvoiceHiding) && method.Name == nameof(ExternalInvoiceHiding.Invoke))
            || (declaring == typeof(ExternalInvoiceSubmissionHiding) && method.Name == nameof(ExternalInvoiceSubmissionHiding.Invoke))
            || (declaring == typeof(AppOnlyInvoiceFamilyHiding) && method.Name == nameof(AppOnlyInvoiceFamilyHiding.Invoke))
            || (declaring == typeof(ExternalDataService)
                && method.Name is nameof(ExternalDataService.HideInvoicesForAsync) or nameof(ExternalDataService.HideSubmissionsForAsync)
                    or nameof(ExternalDataService.HideInvoiceFamilyRowsAsync))
            || (declaring == typeof(ExternalInvoiceVisibility)
                && method.Name is nameof(ExternalInvoiceVisibility.DecideAsync) or nameof(ExternalInvoiceVisibility.DecideSubmissionsAsync)
                    or nameof(ExternalInvoiceVisibility.HiddenFamilyRowsAsync)));

    private static readonly Regex CompilerPrefix = new(@"^<+([^<>]+)>", RegexOptions.Compiled);

    private static string Normalize(string name) => CompilerPrefix.Match(name) is { Success: true } m ? m.Groups[1].Value : name;
}

// ── Controls (fixtures; never called) ─────────────────────────────────────────────────────────────────────────────

/// <summary>MUST FIRE: reads invoices; the predicate appears only in a comment and a nameof.</summary>
internal sealed class InvoiceGuardControlMentionOnly
{
    internal string ListAsync(Guid matter)
    {
        // ExternalInvoiceHiding runs after this — in this comment only.
        return $"sprk_invoices?$filter=_sprk_matter_value eq {matter}" + nameof(ExternalInvoiceHiding);
    }
}

/// <summary>MUST FIRE: reads invoice LINES (a taint-closure table) without the predicate.</summary>
internal sealed class InvoiceGuardControlLineReadOnly
{
    internal string ListAsync(Guid invoice) => $"<fetch><entity name='sprk_billingevent'><filter><condition value='{invoice}'/></filter></entity></fetch>";
}

/// <summary>MUST FIRE: lists submissions through a family reader service without the predicate.</summary>
internal sealed class InvoiceGuardControlCallsAFamilyReader(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService service)
{
    internal Task ListAsync(Guid firm, CancellationToken ct) =>
        service.ListAsync(new[] { firm }, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(), ct);
}

/// <summary>MUST FIRE (review round 1, F3): searches invoices through the reader's INTERFACE without the predicate.</summary>
internal sealed class InvoiceGuardControlCallsAFamilyReaderThroughItsInterface(Sprk.Bff.Api.Services.Finance.IInvoiceSearchService search)
{
    internal Task ListAsync(string query, CancellationToken ct) => search.SearchAsync(query, null, 10, ct);
}

/// <summary>MUST NOT BE A SURFACE: calls through a BFF interface no listed family reader implements.</summary>
internal sealed class InvoiceGuardControlCallsAnUnrelatedInterface(Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver identity)
{
    internal Task<bool> ListAsync(Guid user, CancellationToken ct) => identity.IsExternalAsync(user, ct);
}

/// <summary>MUST NOT FIRE: reads invoices, then applies the predicate inside a lambda.</summary>
internal sealed class InvoiceGuardControlHidesInALambda(ExternalInvoiceHiding hide)
{
    internal async Task<string> ListAsync(CallerPrincipal caller, Guid invoice, CancellationToken ct)
    {
        var url = $"sprk_invoices({invoice})";
        Func<Task<InvoiceVisibilityResult>> decide = () => hide(caller, new[] { invoice }, ct);
        await decide();
        return url;
    }
}

/// <summary>MUST NOT FIRE: reads invoices and calls a helper that applies the predicate.</summary>
internal sealed class InvoiceGuardControlHidesThroughAHelper(ExternalInvoiceHiding hide)
{
    internal async Task<string> ListAsync(CallerPrincipal caller, Guid invoice, CancellationToken ct)
    {
        await Helper(caller, invoice, ct);
        return $"sprk_invoices({invoice})";
    }

    private Task<InvoiceVisibilityResult> Helper(CallerPrincipal caller, Guid invoice, CancellationToken ct) => hide(caller, new[] { invoice }, ct);
}

/// <summary>MUST NOT BE A SURFACE: names only a lookup column and an unrelated table.</summary>
internal sealed class InvoiceGuardControlNoInvoices
{
    internal string ListAsync(Guid id) => $"sprk_documents?$select=_sprk_invoice_value&$filter=sprk_documentid eq {id}";
}

/// <summary>MUST FIRE (step 0): calls FinanceAuthorizationFilter.EvaluateStepZeroAsync.</summary>
internal sealed class InvoiceGuardControlCallsStepZero
{
    internal async Task InvokeAsync(System.Security.Claims.ClaimsPrincipal user, IServiceProvider services, CancellationToken ct) =>
        await Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.EvaluateStepZeroAsync(user, services, null, ct);

    internal Task ThroughAHelperAsync(System.Security.Claims.ClaimsPrincipal user, IServiceProvider services, CancellationToken ct) =>
        InvokeAsync(user, services, ct);
}

/// <summary>MUST NOT FIRE (step 0): names it only.</summary>
internal sealed class InvoiceGuardControlNamesStepZero
{
    internal string InvokeAsync() => nameof(Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilter.EvaluateStepZeroAsync);
}

/// <summary>MUST FIRE (family seam): takes the seam.</summary>
internal sealed class FamilySeamControlTakesTheSeam(AppOnlyInvoiceFamilyHiding hide)
{
    internal Task<IReadOnlySet<Guid>> HideAsync(Guid id, CancellationToken ct) => hide("sprk_invoice", new[] { id }, ct);
}

/// <summary>MUST FIRE (family seam): calls the app-only reader's entry directly.</summary>
internal sealed class FamilySeamControlCallsTheReaderDirectly(ExternalDataService data)
{
    internal Task<IReadOnlySet<Guid>> HideAsync(Guid id, EffectiveRootFlagReader flags, CancellationToken ct) =>
        data.HideInvoiceFamilyRowsAsync("sprk_invoice", new[] { id }, flags, ct);
}

/// <summary>MUST FIRE (family seam): resolves the seam by name.</summary>
internal sealed class FamilySeamControlNamesTheSeam
{
    internal Type? Resolve() => Type.GetType("Sprk.Bff.Api.Infrastructure.ExternalAccess.AppOnlyInvoiceFamilyHiding, Sprk.Bff.Api");
}

/// <summary>MUST NOT FIRE (review round 4b): lists submissions and applies the submission seam through a helper.</summary>
internal sealed class InvoiceGuardControlListsSubmissionsThroughTheSubmissionSeam(
    Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService service, ExternalInvoiceSubmissionHiding hide)
{
    internal async Task<InvoiceVisibilityResult> ListAsync(CallerPrincipal caller, Guid firm, CancellationToken ct)
    {
        var views = await service.ListAsync(new[] { firm }, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(), ct);
        return await Helper(caller, views, ct);
    }

    private Task<InvoiceVisibilityResult> Helper(
        CallerPrincipal caller, IReadOnlyList<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView> views, CancellationToken ct) =>
        hide(caller, views, ct);
}

/// <summary>MUST FIRE (review round 4b): lists submissions and applies only the source decision (it decides no invoice).</summary>
internal sealed class InvoiceGuardControlListsSubmissionsWithOnlyTheSourceDecision(
    Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService service)
{
    internal async Task<SourceDocumentDecision> ListAsync(Guid firm, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        var views = await service.ListAsync(new[] { firm }, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(), ct);
        return await ExternalInvoiceVisibility.DecideSourceDocumentsAsync(
            views.Select(v => v.DocumentId ?? Guid.Empty).ToList(), readers, cost: null, ct);
    }
}

/// <summary>Control (round 5), MUST FIRE: maps family rows to invoices and decides them with the invoice predicate only.</summary>
internal static class ToolDecisionControlFamilyMappingWithoutTheSubmissionDecision
{
    internal static async Task<InvoiceVisibilityResult> HiddenAsync(
        IReadOnlyCollection<Guid> rows, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        var invoices = await ExternalInvoiceVisibility.ReadInvoicesOfAsync("sprk_invoicesubmission", rows, readers.Query, ct);
        return await ExternalInvoiceVisibility.DecideAsync(invoices.Values.SelectMany(v => v).ToList(), readers, ct);
    }
}

/// <summary>Control (round 5), MUST NOT FIRE: maps family rows, and decides submission rows through a helper that calls
/// DecideSubmissionsAsync.</summary>
internal static class ToolDecisionControlFamilyMappingWithTheSubmissionDecision
{
    internal static async Task<InvoiceVisibilityResult> HiddenAsync(
        IReadOnlyCollection<Guid> rows, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        await ExternalInvoiceVisibility.ReadInvoicesOfAsync("sprk_invoicesubmission", rows, readers.Query, ct);
        return await Submissions(rows, readers, ct);
    }

    private static Task<InvoiceVisibilityResult> Submissions(IReadOnlyCollection<Guid> rows, InvoiceVisibilityReaders readers, CancellationToken ct) =>
        ExternalInvoiceVisibility.DecideSubmissionsAsync(
            rows.Select(id => new Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView { SubmissionId = id }).ToList(), readers, ct);
}

/// <summary>Control (round 5), MUST FIRE: a tool that decides family rows with the external routes' invoice seam.</summary>
internal sealed class ToolDecisionControlToolDecidesWithTheInvoiceSeam(ExternalInvoiceHiding hide)
{
    internal async Task<bool> ExecuteChatAsync(CallerPrincipal caller, Guid row, CancellationToken ct) =>
        (await hide(caller, new[] { row }, ct)).IsVisible(row);
}

/// <summary>Control (round 5), MUST NOT FIRE: a tool that decides family rows through the family seam.</summary>
internal sealed class ToolDecisionControlToolUsesTheFamilySeam(AppOnlyInvoiceFamilyHiding hide)
{
    internal async Task<bool> ExecuteChatAsync(Guid row, CancellationToken ct) =>
        !(await hide("sprk_invoicesubmission", new[] { row }, ct)).Contains(row);
}

/// <summary>Control (one function): the shared decision; it invokes the seam.</summary>
internal static class SubmissionDecisionControlShared
{
    internal static Task<InvoiceVisibilityResult> DecideAsync(
        CallerPrincipal caller, ExternalInvoiceSubmissionHiding hide, CancellationToken ct) =>
        hide(caller, Array.Empty<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView>(), ct);
}

/// <summary>Control (one function), MUST NOT FIRE: calls the shared decision.</summary>
internal sealed class SubmissionDecisionControlCallsTheShared(ExternalInvoiceSubmissionHiding hide)
{
    internal async Task<bool> ReadAsync(CallerPrincipal caller, CancellationToken ct) =>
        (await SubmissionDecisionControlShared.DecideAsync(caller, hide, ct)).Faulted;
}

/// <summary>Control (one function), MUST FIRE: decides itself (a second copy of the decision).</summary>
internal sealed class SubmissionDecisionControlDecidesItself(ExternalInvoiceHiding hide)
{
    internal async Task<bool> ReadAsync(CallerPrincipal caller, Guid invoice, CancellationToken ct) =>
        (await hide(caller, new[] { invoice }, ct)).IsVisible(invoice);
}

/// <summary>Control (one function), MUST NOT BE FOUND: names the shared decision only.</summary>
internal sealed class SubmissionDecisionControlNamesTheShared
{
    internal string ReadAsync() => nameof(SubmissionDecisionControlShared.DecideAsync);
}
