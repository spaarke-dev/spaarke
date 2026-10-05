using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The forcing function for BFF route authorization (task 074, extended to EVERY route by task 167):
/// <b>every route in every BFF endpoint file must declare how it is authorized</b> — a per-resource
/// decision the guard can see, a verified in-handler decision, an admin policy on a pinned operator route,
/// or a written waiver. A route carrying only "are you signed in?" fails the build.
///
/// <para><b>Why every route, as of task 167 (owner round 9 item 3, 2026-10-03).</b> The 2026-10-02 route
/// authorization sweep (<c>projects/unified-access-control-r2/notes/route-authorization-sweep-2026-10-02.md</c>)
/// found <b>82 routes</b> where any signed-in caller acts, as the BFF's own identity, on a record they have no
/// rights to — 17 critical. Task 074's guard saw almost none of them, for three structural reasons this
/// version removes: it read 25 of 120 endpoint files; it exempted whole files as "HandlerAuthorized"; and it
/// credited any filter whose NAME contained "Authorization", including six that decide nothing. Each was a
/// blind spot that kept itself out of the guard.</para>
///
/// <para><b>The four credit kinds.</b> A route passes Rule A with exactly one of:</para>
/// <list type="number">
///   <item><b>Per-resource filter</b> — an attachment form on the explicit credited allow-list
///   (<see cref="CreditedForms"/>), each entry read and found to decide. (A
///   <c>ResourceAccessRequirement</c> policy, <see cref="ResourcePolicies"/>, also counts and is pinned in
///   <see cref="PolicyOnlyRoutes"/>.)</item>
///   <item><b>HandlerDecision</b> — a per-route declaration (<see cref="HandlerDecisions"/>) whose handler BODY
///   (never its signature), followed through at most two named hops, reaches a decision seam. Verified in source,
///   not trusted.</item>
///   <item><b>Admin</b> — one of exactly four admin mechanisms (<see cref="AdminMechanisms"/>), on a route in
///   the pinned <see cref="AdminOnlyRoutes"/> set.</item>
///   <item><b>Waiver</b> — Permanent with a basis from a closed set, or Pending with an owning task
///   (<see cref="Waivers"/>).</item>
/// </list>
///
/// <para><b>Sign-in is declared, never implied</b> (task 167 r2, f1). Credit says which record a caller may act on; it
/// presumes a signed-in caller. So every live route also carries <c>.RequireAuthorization(...)</c> on its route or
/// group chain — with a policy the guard PROVES requires an authenticated user — or a declared
/// <c>.AllowAnonymous()</c> that Rule A then sends to an AnonymousByDesign or Pending waiver; no waiver excuses a
/// route with neither (<see cref="NoRouteIsAnonymousByOmission"/>). Since owner round 14 the RUNTIME fails closed as
/// well: the authorization FallbackPolicy requires an authenticated user for any endpoint that declares nothing
/// (<see cref="TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser"/>), and the public surface is a pinned set
/// (<see cref="TheExplicitlyAnonymousSurfaceIsPinned"/>).</para>
///
/// <para><b>The attachment-form census.</b> Every authorization-shaped call on any route chain — any
/// <c>.Add*Filter</c>, <c>.AddEndpointFilter</c>, <c>.Require*</c>, <c>.WithMetadata</c> — must sit in exactly
/// one list: credited, admin, <see cref="NonDecidingAttachments"/> (pass-throughs, each pinned to its source
/// evidence and owned by its fix task), or <see cref="NotAuthorizationForms"/>. An unclassified form fails the
/// build naming file:line, so the next new filter is classified before it can merge.</para>
///
/// <para><b>The sweep ledger.</b> The 82 findings (90 route keys) are pinned in <see cref="SweepFindings"/>,
/// each a Pending waiver owned by its fix task 159-166. A fix task resolves its entry ONLY by credit: make the
/// route pass Rule A by a credited filter, a HandlerDecision or a SystemAdmin / SPE-admin AdminOnlyRoutes entry, delete
/// its waiver, and set <c>ResolvedBy</c> to the task id and <c>ProofTest</c> to its behavioural deny test. A route the fix
/// task DELETED resolves with <c>ResolvedBy</c> and a <c>ProofTest</c> that pins its absence (task 167 f2, main-session
/// round 34 items 4-5).</para>
///
/// <para><b>Why source analysis and not endpoint reflection</b> (task 074, still true).
/// <c>AddEndpointFilter</c> adds NOTHING to <c>EndpointBuilder.Metadata</c> — reflection yields only
/// <c>IAuthorizeData</c> and <c>IAllowAnonymous</c>, exactly the authenticated-vs-anonymous distinction that
/// produced every finding — and an app built in a test would not register config-gated routes at all.</para>
///
/// <para><b>What this rule does NOT catch, stated plainly.</b> Presence is not correctness. A credited filter
/// that authorizes the wrong id, a HandlerDecision seam that is called on the wrong record, and a Read check
/// guarding a write all pass. The behavioural deny tests of tasks 159-166 are the proof; this guard makes the
/// absence of any mechanism impossible to merge, and makes every exemption a written, owned, reviewable line.
/// The history of the four historical misses (task 074) is preserved in
/// <see cref="Detector_NegativeControl_FiresOnEachHistoricalMiss"/>.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // =============================================================================================
    // THE SCOPE BOUNDARY
    // ---------------------------------------------------------------------------------------------
    // EVERY file the census finds (EndpointFiles(): every non-obj/bin .cs file under Sprk.Bff.Api that
    // registers a route with Map{Verb}, MapMethods or MapHealthChecks) is in GovernedFiles, and the two sets
    // are equal — TheEndpointFileCensusIsPinned asserts it. Rule A applies to every live route of every one
    // of them. There is no "NotGoverned" and no file-level exemption any more: task 074's HandlerAuthorized
    // scope is RETIRED, because it is how eight Compose files and the external data plane sat outside Rule A
    // while serving document content.
    //
    // A file's Scope now records only how its routes are BOUND, and the scanner verifies it:
    //   RouteLevelGate — routes registered on the application root or on the file's own MapGroup.
    //   GroupGated     — routes registered on a group an AGGREGATOR passes in (see Aggregators); the group's
    //                    prefix and chain are inherited and Rule E forbids absolute paths on it.
    //   NotMapped      — the file registers routes in a method nothing calls (dead code). Its routes are
    //                    not live; TheDeadClosureRegistrationIsNotMapped fails if it gains a caller.
    // =============================================================================================

    private const int MaxHandlerDecisionHops = 2;

    private static readonly IReadOnlySet<string> PendingOwners = new HashSet<string>(StringComparer.Ordinal)
    {
        "159", "160", "161", "162", "163", "164", "165", "166", "UNOWNED-NEW",
    };

    // =============================================================================================
    // CLASSIFICATION
    // =============================================================================================

    /// <summary>How a route is credited, in precedence order.</summary>
    private enum Credit
    {
        /// <summary>Only "are you signed in?" (or nothing). Needs a waiver.</summary>
        None,

        /// <summary>A form on the credited allow-list, at route or group level.</summary>
        PerResource,

        /// <summary>A <c>ResourceAccessRequirement</c> policy. Real, but pinned — see PolicyOnlyRoutes.</summary>
        ResourcePolicy,

        /// <summary>A verified HandlerDecision declaration.</summary>
        HandlerDecision,

        /// <summary>Only an admin mechanism. Pinned — see AdminOnlyRoutes.</summary>
        AdminOnly,

        /// <summary>Explicitly public. Needs an AnonymousByDesign or a Pending waiver.</summary>
        Anonymous,
    }

    private enum FormCategory
    {
        Credited,
        Admin,
        NonDeciding,
        NotAuthorization,
        ResourcePolicy,
    }

    private static readonly Regex NamedPolicyForm = new(@"^RequireAuthorization\(""([^""]+)""\)$", RegexOptions.Compiled);

    private static bool IsResourcePolicyForm(string form)
    {
        var m = NamedPolicyForm.Match(form);
        return m.Success && ResourcePolicies.Contains(m.Groups[1].Value);
    }

    private static List<FormCategory> CategoriesOf(string form)
    {
        var categories = new List<FormCategory>();
        if (CreditedForms.Any(c => c.Form == form)) categories.Add(FormCategory.Credited);
        if (AdminMechanisms.Any(a => a.Form == form)) categories.Add(FormCategory.Admin);
        if (NonDecidingAttachments.Any(n => n.Form == form)) categories.Add(FormCategory.NonDeciding);
        if (NotAuthorizationForms.Any(n => n.Form == form)) categories.Add(FormCategory.NotAuthorization);
        if (IsResourcePolicyForm(form)) categories.Add(FormCategory.ResourcePolicy);
        return categories;
    }

    private sealed record Assessment(
        RouteRegistration Route,
        Credit Credit,
        string Fingerprint,
        IReadOnlyList<ChainCall> UnclassifiedCalls,
        string? HandlerProblem)
    {
        public string Key => Route.Key;
    }

    /// <summary>
    /// Classifies one route. <paramref name="decisions"/> defaults to the real <see cref="HandlerDecisions"/>;
    /// controls pass their own list so a fixture cannot borrow a real declaration by accident.
    /// </summary>
    private static Assessment Assess(RouteRegistration route, IReadOnlyList<HandlerDecision>? decisions = null,
        IReadOnlyList<SourceUnit>? extraTypeSources = null)
    {
        decisions ??= HandlerDecisions;
        var forms = route.Forms;
        var unclassified = route.Chain
            .Where(c => c.IsAuthorizationShaped && c.Name != "AllowAnonymous" && CategoriesOf(c.Form).Count == 0)
            .ToList();

        var credited = forms.Any(f => CreditedForms.Any(c => c.Form == f));
        var policy = forms.Any(IsResourcePolicyForm);
        var admin = forms.Any(f => AdminMechanisms.Any(a => a.Form == f));

        string? seam = null;
        string? problem = null;
        var declared = decisions.Where(d => d.Route == route.Key).ToList();
        if (declared.Count > 1)
        {
            problem = $"declared {declared.Count} times";
        }
        else if (declared.Count == 1)
        {
            var (ok, why) = VerifyHandlerDecision(declared[0], route, extraTypeSources);
            if (ok)
            {
                seam = declared[0].Seam;
            }
            else
            {
                problem = why;
            }
        }

        var credit = credited ? Credit.PerResource
            : policy ? Credit.ResourcePolicy
            : seam is not null ? Credit.HandlerDecision
            : admin ? Credit.AdminOnly
            : route.Anonymous ? Credit.Anonymous
            : Credit.None;

        return new Assessment(route, credit, Fingerprint(route, seam), unclassified, problem);
    }

    /// <summary>
    /// The normalized authorization fingerprint an InsufficientDecision waiver records: sorted distinct credited,
    /// admin and non-deciding forms, "policy:NAME" for a resource policy, "anonymous", and "handler:SEAM".
    /// NotAuthorization forms are EXCLUDED — adding a rate limiter must not make a waiver stale.
    /// </summary>
    private static string Fingerprint(RouteRegistration route, string? handlerSeam)
    {
        var parts = new List<string>();
        foreach (var form in route.Forms)
        {
            var categories = CategoriesOf(form);
            if (categories.Contains(FormCategory.Credited) || categories.Contains(FormCategory.Admin)
                || categories.Contains(FormCategory.NonDeciding))
            {
                parts.Add(form);
            }
            else if (categories.Contains(FormCategory.ResourcePolicy))
            {
                parts.Add("policy:" + NamedPolicyForm.Match(form).Groups[1].Value);
            }
        }

        if (route.Anonymous)
        {
            parts.Add("anonymous");
        }

        if (handlerSeam is not null)
        {
            parts.Add("handler:" + handlerSeam);
        }

        return string.Join(" + ", parts.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal));
    }

    private static readonly Lazy<IReadOnlyList<Assessment>> RealAssessments =
        new(() => Real.Live.Select(r => Assess(r)).ToList(), LazyThreadSafetyMode.ExecutionAndPublication);

    private static Credit CreditOf(RouteRegistration route) => Assess(route, Array.Empty<HandlerDecision>()).Credit;

    // =============================================================================================
    // EVALUATION — pure functions over (assessments, data), so every negative control can perturb the
    // REAL data instead of a copy of it
    // =============================================================================================

    private static List<string> RuleAViolations(IEnumerable<Assessment> routes, IReadOnlyList<Waiver> waivers)
    {
        var byRoute = waivers.GroupBy(w => w.Route, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var violations = new List<string>();

        foreach (var a in routes)
        {
            var waived = byRoute.GetValueOrDefault(a.Key) ?? new List<Waiver>();
            switch (a.Credit)
            {
                case Credit.None when waived.Count == 0:
                    violations.Add($"{a.Key}\n      at {a.Route.File}:{a.Route.Line} — only signed-in (or nothing): "
                                   + $"[{string.Join(", ", a.Route.Forms)}]");
                    break;
                case Credit.Anonymous when !waived.Any(w => w.Kind == WaiverKind.Pending
                                                           || w.Basis == PermanentBasis.AnonymousByDesign):
                    violations.Add($"{a.Key}\n      at {a.Route.File}:{a.Route.Line} — ANONYMOUS with no AnonymousByDesign "
                                   + "or Pending waiver");
                    break;
            }
        }

        return violations;
    }

    private static List<string> WaiverViolations(IReadOnlyList<Assessment> routes, IReadOnlyList<Waiver> waivers)
    {
        var byKey = routes.GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        var violations = new List<string>();

        foreach (var dup in waivers.GroupBy(w => w.Route, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            violations.Add($"{dup.Key}: {dup.Count()} waivers — two entries for one route means one is unreviewed");
        }

        foreach (var w in waivers)
        {
            var label = $"{w.Route} ({w.Kind}{(w.Kind == WaiverKind.Pending ? $" {w.Gap}, owner {w.OwningTask}" : $" {w.Basis}")})";

            if (string.IsNullOrWhiteSpace(w.Reason) || w.Reason.Trim().Length < 60)
            {
                violations.Add($"{label}: the reason is under 60 characters — a reviewer two years from now must be "
                               + "able to evaluate it");
            }

            if (w.Kind == WaiverKind.Permanent)
            {
                if (w.Basis == PermanentBasis.None || !Enum.IsDefined(w.Basis))
                {
                    violations.Add($"{label}: a Permanent waiver needs a PermanentBasis from the closed set");
                }

                if (w.Gap != Gap.None || w.ObservedMechanisms is not null)
                {
                    violations.Add($"{label}: Gap and ObservedMechanisms belong to Pending waivers only");
                }

                if (!Regex.IsMatch(w.Reason, @"\w\.cs:\d+"))
                {
                    violations.Add($"{label}: a Permanent reason must cite the handler file:line that proves its basis");
                }

                if (string.IsNullOrWhiteSpace(w.OwningTask))
                {
                    violations.Add($"{label}: a Permanent waiver names who verified it ('-' is allowed for history)");
                }
            }
            else
            {
                if (!PendingOwners.Contains(w.OwningTask))
                {
                    violations.Add($"{label}: a Pending owner must be one of {string.Join(", ", PendingOwners)}");
                }

                if (w.Basis != PermanentBasis.None)
                {
                    violations.Add($"{label}: a Pending waiver has no PermanentBasis");
                }

                if (w.Gap is not (Gap.NoDecision or Gap.InsufficientDecision))
                {
                    violations.Add($"{label}: a Pending waiver needs Gap NoDecision or InsufficientDecision");
                }

                if (w.Gap == Gap.InsufficientDecision && string.IsNullOrWhiteSpace(w.ObservedMechanisms))
                {
                    violations.Add($"{label}: an InsufficientDecision waiver records the ObservedMechanisms fingerprint");
                }

                if (w.Gap == Gap.NoDecision && w.ObservedMechanisms is not null)
                {
                    violations.Add($"{label}: a NoDecision waiver has no ObservedMechanisms");
                }
            }

            // ---- the stale rules ----
            if (!byKey.TryGetValue(w.Route, out var a))
            {
                violations.Add($"{label}: STALE — the route no longer exists. Delete the waiver; if the route was RE-KEYED, "
                               + "the new key needs its own decision.");
                continue;
            }

            var credited = a.Credit is Credit.PerResource or Credit.ResourcePolicy or Credit.HandlerDecision or Credit.AdminOnly;

            if (w.Kind == WaiverKind.Pending && w.Gap == Gap.NoDecision && credited)
            {
                violations.Add($"{label}: STALE — the route is credited ({a.Credit}: {a.Fingerprint}). If a fix landed, "
                               + "DELETE this waiver (and, for a sweep route, set ResolvedBy + ProofTest); if the credit "
                               + "predates the waiver, the gap is InsufficientDecision, not NoDecision.");
            }

            if (w.Kind == WaiverKind.Pending && w.Gap == Gap.InsufficientDecision)
            {
                if (!credited)
                {
                    violations.Add($"{label}: InsufficientDecision needs a credited route; this one has {a.Credit} — use "
                                   + "NoDecision");
                }

                if (!string.Equals(w.ObservedMechanisms, a.Fingerprint, StringComparison.Ordinal))
                {
                    violations.Add($"{label}: STALE — the route's authorization fingerprint changed from "
                                   + $"'{w.ObservedMechanisms}' to '{a.Fingerprint}'. Re-read the route: delete the waiver if "
                                   + "the fix landed, or record the new fingerprint if it did not.");
                }
            }

            if (w.Kind == WaiverKind.Permanent && credited)
            {
                violations.Add($"{label}: REDUNDANT — the route is credited ({a.Credit}). A Permanent waiver on a "
                               + "credited route is a note nobody needs; delete it.");
            }

            if (w.Kind == WaiverKind.Permanent && w.Basis == PermanentBasis.AnonymousByDesign && a.Credit != Credit.Anonymous)
            {
                violations.Add($"{label}: AnonymousByDesign on a route that is not anonymous ({a.Credit})");
            }

            if (w.Kind == WaiverKind.Permanent && w.Basis != PermanentBasis.AnonymousByDesign && a.Credit == Credit.Anonymous)
            {
                violations.Add($"{label}: the route is ANONYMOUS; only AnonymousByDesign (with its mandatory control) or "
                               + "Pending may waive it");
            }
        }

        return violations;
    }

    private static List<string> LedgerViolations(
        IReadOnlyList<Assessment> routes, IReadOnlyList<Waiver> waivers, IReadOnlyList<SweepFinding> ledger,
        Func<string, string?>? readTestFile = null, IReadOnlyList<AdminOnlyGroup>? adminOnly = null)
    {
        readTestFile ??= ReadRepoFile;
        adminOnly ??= AdminOnlyRoutes;
        var byKey = routes.GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        var violations = new List<string>();

        foreach (var dup in ledger.GroupBy(e => e.Route, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            violations.Add($"{dup.Key}: appears {dup.Count()} times in SweepFindings");
        }

        foreach (var entry in ledger)
        {
            var label = $"{entry.SweepId} {entry.Route} (owner {entry.OwningTask})";
            var waived = waivers.Where(w => w.Route == entry.Route).ToList();

            if (entry.OwningTask is not ("159" or "160" or "161" or "162" or "163" or "164" or "165" or "166"))
            {
                violations.Add($"{label}: a sweep entry is owned by one of tasks 159-166");
            }

            if (waived.Any(w => w.Kind == WaiverKind.Permanent))
            {
                violations.Add($"{label}: a sweep route may NEVER carry a Permanent waiver — the sweep proved it unsafe. "
                               + "Resolve it by credit.");
            }

            if (!byKey.TryGetValue(entry.Route, out var a))
            {
                violations.AddRange(RetiredEntryViolations(entry, label, routes, waived, readTestFile));
                continue;
            }

            if (entry.ResolvedBy is null)
            {
                var pending = waived.Where(w => w.Kind == WaiverKind.Pending).ToList();
                if (pending.Count != 1 || pending[0].OwningTask != entry.OwningTask || pending[0].Gap != entry.Gap)
                {
                    violations.Add($"{label}: an unresolved sweep entry carries exactly one Pending waiver with owner "
                                   + $"{entry.OwningTask} and Gap {entry.Gap}; found "
                                   + (pending.Count == 0
                                       ? "none. REMEDY for a fix task: make the route pass Rule A by credit, delete its "
                                         + "waiver, and set ResolvedBy to your task id and ProofTest to your deny test."
                                       : string.Join("; ", pending.Select(p => $"owner {p.OwningTask} gap {p.Gap}"))));
                }
            }
            else
            {
                if (waived.Count > 0)
                {
                    violations.Add($"{label}: ResolvedBy is set, so the route carries no waiver of any kind — delete it");
                }

                // Admin credit resolves a sweep entry only through the pinned AdminOnlyRoutes set, in a group whose
                // declared mechanism is an ADMIN POLICY — SystemAdmin or the SPE admin filter (owner round 9 item 3) — and
                // only when the route really carries it (main-session round 34 item 5; before, only "/api/spe/" routes).
                var adminGroup = adminOnly.FirstOrDefault(g => g.Routes.Contains(entry.Route, StringComparer.Ordinal));
                var resolvedByAdminPolicy = a.Credit == Credit.AdminOnly
                                            && adminGroup is not null
                                            && SweepAdminPolicies.Contains(adminGroup.Mechanism)
                                            && a.Route.Forms.Contains(adminGroup.Mechanism, StringComparer.Ordinal);
                var resolvedByCredit = a.Credit is Credit.PerResource or Credit.HandlerDecision || resolvedByAdminPolicy;
                if (!resolvedByCredit)
                {
                    violations.Add($"{label}: ResolvedBy is set but the route does not pass Rule A by credit ({a.Credit}"
                                   + (a.Credit == Credit.AdminOnly
                                       ? adminGroup is null
                                           ? "; it is not in AdminOnlyRoutes"
                                           : !SweepAdminPolicies.Contains(adminGroup.Mechanism)
                                               ? $"; its AdminOnlyRoutes group's mechanism is {adminGroup.Mechanism}, which is not an admin policy for a sweep entry"
                                               : $"; its AdminOnlyRoutes group is gated by {adminGroup.Mechanism}, which the route does not carry"
                                       : string.Empty)
                                   + "). An entry resolves ONLY by a credited filter, a HandlerDecision, or an AdminOnlyRoutes "
                                   + "entry whose group is gated by RequireAuthorization(\"SystemAdmin\") or the SPE admin "
                                   + "policy (main-session round 34 item 5) — or, for a DELETED route, by a ProofTest that pins "
                                   + "its absence (round 34 item 4).");
                }

                violations.AddRange(ProofTestViolations(entry, readTestFile));
            }
        }

        return violations;
    }

    /// <summary>
    /// The admin mechanisms a SWEEP entry may resolve through (main-session round 34 item 5, owner round 9 item 3:
    /// "an admin policy"). The RAG machine credential and the registration approver role stay admin credit for their
    /// own pinned routes, but neither resolves a sweep finding.
    /// </summary>
    private static readonly IReadOnlySet<string> SweepAdminPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        "RequireAuthorization(\"SystemAdmin\")",
        "AddSpeAdminAuthorizationFilter",
    };

    private static string? ReadRepoFile(string repoRelative)
    {
        var path = Path.Combine(SourceScan.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static IEnumerable<string> ProofTestViolations(SweepFinding entry, Func<string, string?> readTestFile)
    {
        var label = $"{entry.SweepId} {entry.Route}";
        var parts = (entry.ProofTest ?? string.Empty).Split("::");
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            yield return $"{label}: ProofTest must be '{{repo-relative test file}}::{{test method}}', got '{entry.ProofTest}'";
            yield break;
        }

        var raw = readTestFile(parts[0]);
        if (raw is null)
        {
            yield return $"{label}: ProofTest file '{parts[0]}' does not exist";
            yield break;
        }

        if (!ContainsToken(CodeOf(raw), parts[1]))
        {
            yield return $"{label}: ProofTest method '{parts[1]}' is not in {parts[0]}";
        }
    }

    // =============================================================================================
    // RETIRED SWEEP ROUTES (main-session round 34 item 4)
    // ---------------------------------------------------------------------------------------------
    // Owner round 10 item 1 DELETES a route with no caller and no published description (tasks 159, 160, 164). Its sweep
    // entry then has no live route to be credited. The entry passes ONLY when all of these hold — anything else absent
    // still fails ("do not drop or re-key", task 167 trigger 4):
    //   1. ResolvedBy is set (the deleting task) and the route carries no waiver;
    //   2. no live route has the SAME verb and path with only a parameter name or constraint changed — that is a
    //      RE-KEY, not a retirement;
    //   3. ProofTest names an existing test method that pins the route's ABSENCE: its scope (the method, its attributes,
    //      and the same-type members it names, e.g. a RetiredRoutes array or a MemberData source) pairs the retired VERB
    //      with a PATH the retired template matches, and asserts absence — 404 / 405 on the request, or an empty / false
    //      answer from the endpoint table. A test that names the route only to assert it is PRESENT (NotBe(404)), a
    //      test of a different route, or one that never asserts absence does not pass.
    // =============================================================================================

    private static IEnumerable<string> RetiredEntryViolations(SweepFinding entry, string label, IReadOnlyList<Assessment> routes,
        IReadOnlyList<Waiver> waived, Func<string, string?> readTestFile)
    {
        if (entry.ResolvedBy is null)
        {
            yield return $"{label}: the route key is not on this branch (renamed, deleted or re-keyed). Do not drop or re-key "
                         + "the entry. A DELETED route resolves only with ResolvedBy (the deleting task) and a ProofTest that pins "
                         + "its ABSENCE (main-session round 34 item 4); a rename or re-key escalates for reconciliation (task 167 "
                         + "trigger 4).";
            yield break;
        }

        if (waived.Count > 0)
        {
            yield return $"{label}: ResolvedBy is set, so the route carries no waiver of any kind — delete it";
        }

        var shape = RouteShape(entry.Route);
        var twin = routes.FirstOrDefault(r => RouteShape(r.Key) == shape);
        if (twin is not null)
        {
            yield return $"{label}: the key is absent but '{twin.Key}' (at {twin.Route.File}:{twin.Route.Line}) is the same verb "
                         + "and path with only a parameter name or constraint changed — the route was RE-KEYED, not retired. Do not "
                         + "re-key the entry: escalate for reconciliation (task 167 trigger 4).";
            yield break;
        }

        var proofProblems = ProofTestViolations(entry, readTestFile).ToList();
        if (proofProblems.Count > 0)
        {
            foreach (var problem in proofProblems)
            {
                yield return problem;
            }

            yield break;
        }

        var parts = entry.ProofTest!.Split("::");
        var why = AbsencePinProblem(readTestFile(parts[0])!, parts[1], entry.Route);
        if (why is not null)
        {
            yield return $"{label}: the route is deleted, but ProofTest {entry.ProofTest} does not pin its ABSENCE — {why}. A "
                         + "retired route's proof pairs its verb with a path its template matches and asserts 404/405 (signed in) "
                         + "or an empty endpoint-table answer (main-session round 34 item 4).";
        }
    }

    /// <summary>"VERB /a/{}/b/{*}" — the route key with every parameter's name and constraint erased.</summary>
    private static string RouteShape(string key)
    {
        var space = key.IndexOf(' ');
        var verb = space < 0 ? key : key[..space];
        var path = space < 0 ? string.Empty : key[(space + 1)..];
        var segments = path.Trim('/').Split('/').Select(s =>
            s.StartsWith("{*", StringComparison.Ordinal) || s.StartsWith("{**", StringComparison.Ordinal) ? "{*}"
            : s.StartsWith('{') ? "{}"
            : s.ToLowerInvariant());
        return verb.ToUpperInvariant() + " /" + string.Join("/", segments);
    }

    private static readonly IReadOnlyDictionary<string, string> VerbByClientCall = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GetAsync"] = "GET", ["GetFromJsonAsync"] = "GET", ["GetStringAsync"] = "GET", ["GetByteArrayAsync"] = "GET",
        ["GetStreamAsync"] = "GET", ["PostAsync"] = "POST", ["PostAsJsonAsync"] = "POST", ["PutAsync"] = "PUT",
        ["PutAsJsonAsync"] = "PUT", ["PatchAsync"] = "PATCH", ["PatchAsJsonAsync"] = "PATCH", ["DeleteAsync"] = "DELETE",
        ["DeleteFromJsonAsync"] = "DELETE",
    };

    private static readonly Regex AbsenceAssertion = new(
        @"\.\s*Be\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*(?:NotFound|MethodNotAllowed)\b"
        + @"|\.\s*BeOneOf\s*\([^;]*HttpStatusCode\s*\.\s*NotFound\b"
        + @"|Assert\s*\.\s*Equal\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*(?:NotFound|MethodNotAllowed)\b"
        + @"|\.\s*Be\s*\(\s*(?:404|405)\s*\)|Assert\s*\.\s*Equal\s*\(\s*(?:404|405)\s*,"
        + @"|StatusCodes\s*\.\s*Status40[45]\w*"
        + @"|EndpointTable\s*\.\s*AssertNotMapped\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex EndpointTableRead = new(@"(?<![\w])(?:EndpointDataSource|RouteEndpoint|EndpointTable\s*\.\s*Maps)\b", RegexOptions.Compiled);

    private static readonly Regex EmptinessAssertion = new(
        @"\.\s*(?:BeEmpty|BeFalse|NotContain)\s*\(|Assert\s*\.\s*(?:Empty|False|DoesNotContain)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Null when test <paramref name="methodName"/> in <paramref name="raw"/> pins the absence of
    /// <paramref name="routeKey"/>; otherwise why not. Reads the method's attributes, signature and body plus the
    /// initializers of the same-type members it names.
    /// </summary>
    private static string? AbsencePinProblem(string raw, string methodName, string routeKey)
    {
        var unit = new SourceUnit("proof-test", raw);
        var methods = unit.Methods.Where(m => m.Name == methodName).ToList();
        if (methods.Count == 0)
        {
            return $"no method '{methodName}' is declared there";
        }

        var space = routeKey.IndexOf(' ');
        var verb = routeKey[..space];
        var template = routeKey[(space + 1)..];
        var problems = new List<string>();

        foreach (var method in methods)
        {
            // Scope: from the method's attribute lines ([Theory], [InlineData(...)], [MemberData(...)]) to the end of its
            // body, plus the same-type members it names.
            var start = unit.Text.LastIndexOf('\n', Math.Max(0, method.NameIndex - 1)) + 1;
            while (start > 1)
            {
                var previousLineEnd = start - 1;
                var previousLineStart = unit.Text.LastIndexOf('\n', previousLineEnd - 1) + 1;
                if (!unit.Text[previousLineStart..previousLineEnd].TrimStart().StartsWith('['))
                {
                    break;
                }

                start = previousLineStart;
            }

            var scopes = new List<(int Start, int End)> { (start, method.BodyEnd) };
            var type = unit.Types.Where(t => t.BodyStart <= method.NameIndex && method.NameIndex < t.BodyEnd)
                .OrderBy(t => t.BodyEnd - t.BodyStart).FirstOrDefault();
            if (type is not null)
            {
                var top = TopLevelOf(unit.Code, type.BodyStart, type.BodyEnd);
                var named = Regex.Matches(unit.Code[start..method.BodyEnd], @"(?<![\w.])[A-Za-z_]\w*").Select(m => m.Value)
                    .Distinct(StringComparer.Ordinal);
                foreach (var name in named)
                {
                    var declaration = Regex.Match(top, $@"(?<![\w.]){Regex.Escape(name)}\s*(?:=>|=(?![=>])|\{{)");
                    if (!declaration.Success)
                    {
                        continue;
                    }

                    var memberStart = type.BodyStart + 1 + declaration.Index;
                    if (memberStart >= start && memberStart < method.BodyEnd)
                    {
                        continue;
                    }

                    var memberEnd = StatementEnd(unit.Code, memberStart);
                    var braceEnd = unit.Code[memberStart] == '{' ? MatchClose(unit.Code, memberStart) : -1;
                    var end = Math.Max(memberEnd, braceEnd);
                    if (end > memberStart)
                    {
                        scopes.Add((memberStart, end + 1));
                    }
                }
            }

            var pathMatched = false;
            foreach (var (from, to) in scopes)
            {
                pathMatched |= ScopePairsVerbWithPath(unit, from, to, verb, template);
            }

            var scopeCode = string.Join("\n", scopes.Select(s => unit.Code[s.Start..s.End]));
            var asserted = AbsenceAssertion.IsMatch(scopeCode)
                           || (EndpointTableRead.IsMatch(scopeCode) && EmptinessAssertion.IsMatch(scopeCode));

            if (!pathMatched)
            {
                problems.Add($"it names no request or table row pairing {verb} with a path '{template}' matches");
            }
            else if (!asserted)
            {
                problems.Add("it never asserts absence (404/405, or an empty endpoint-table answer)");
            }
            else
            {
                return null;
            }
        }

        return string.Join("; ", problems.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// True when a string literal starting with '/' in <c>[from, to)</c> is a path the retired template matches AND the
    /// literal is paired with the retired verb: another argument of the same argument list or tuple is the verb (a
    /// <c>"PUT"</c> literal or <c>HttpMethod.Put</c>), or the literal is the first argument of a verb-named client call
    /// (<c>client.PostAsJsonAsync("/api/…", …)</c>). An interpolation hole counts as one path segment.
    /// </summary>
    private static bool ScopePairsVerbWithPath(SourceUnit unit, int from, int to, string verb, string template)
    {
        var raw = unit.Text;
        var code = unit.Code;
        var i = from;
        while (i < to)
        {
            if (raw[i] != '"' || code[i] != '"')
            {
                i++;
                continue;
            }

            var quote = i;
            var end = StringLiteralEnd(raw, quote);
            i = Math.Max(end, quote + 1);

            var literalStart = quote;
            while (literalStart > 0 && raw[literalStart - 1] is '$' or '@')
            {
                literalStart--;
            }

            var interpolated = raw[literalStart..quote].Contains('$');
            var content = raw[(quote + 1)..Math.Max(quote + 1, end - 1)];
            if (interpolated)
            {
                content = Regex.Replace(content, @"\{[^{}]*\}", "x");
            }

            if (!content.StartsWith('/') || !TemplateMatchesPath(template, content))
            {
                continue;
            }

            var open = EnclosingOpen(code, literalStart);
            var close = open < 0 ? -1 : MatchClose(code, open);
            if (close < 0)
            {
                continue;
            }

            var spans = SplitTopLevel(code, open + 1, close);
            var siblings = spans.Select(s => raw[s.Start..s.End].Trim()).ToList();
            if (siblings.Any(s => (s.StartsWith('"') && string.Equals(s.Trim('"'), verb, StringComparison.OrdinalIgnoreCase))
                                  || Regex.IsMatch(s, $@"^(?:System\.Net\.Http\.)?HttpMethod\s*\.\s*{verb}$", RegexOptions.IgnoreCase)))
            {
                return true;
            }

            var (callName, _) = IdentifierBefore(code, open);
            if (VerbByClientCall.TryGetValue(callName, out var callVerb) && callVerb == verb
                && spans.Count > 0 && spans[0].Start <= literalStart && literalStart < spans[0].End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The '(' or '[' or '{' whose bracketed region most tightly encloses <paramref name="interpolatedStart"/>.</summary>
    private static int EnclosingOpen(string code, int interpolatedStart)
    {
        if (interpolatedStart < 0)
        {
            return -1;
        }

        var depth = 0;
        for (var j = interpolatedStart - 1; j >= 0; j--)
        {
            var c = code[j];
            if (c is ')' or ']' or '}')
            {
                depth++;
            }
            else if (c is '(' or '[' or '{')
            {
                if (depth == 0)
                {
                    return j;
                }

                depth--;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when the route TEMPLATE (constraints and parameter names ignored) matches the concrete or template-style PATH
    /// a test sends: literal segments compare case-insensitively; a template parameter matches any non-empty segment; a
    /// catch-all matches the rest; a '{…}' segment in the test path matches a template parameter. Query string and a
    /// trailing '/' are ignored.
    /// </summary>
    private static bool TemplateMatchesPath(string template, string path)
    {
        var t = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var p = path.Split('?')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var k = 0; k < t.Length; k++)
        {
            if (t[k].StartsWith("{*", StringComparison.Ordinal))
            {
                return true;
            }

            if (k >= p.Length)
            {
                return false;
            }

            if (t[k].StartsWith('{'))
            {
                continue;
            }

            if (p[k].StartsWith('{') || !string.Equals(t[k], p[k], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return t.Length == p.Length;
    }

    private static (List<string> Added, List<string> Removed) PinDiff(IEnumerable<string> actual, IEnumerable<string> pinned)
    {
        var a = actual.ToHashSet(StringComparer.Ordinal);
        var p = pinned.ToHashSet(StringComparer.Ordinal);
        return (a.Except(p).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            p.Except(a).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    private static List<string> CensusViolations(IEnumerable<string> census, IReadOnlyList<GovernedFile> governed)
    {
        var censusSet = census.ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var file in censusSet.Where(f => governed.All(g => g.RelativePath != f)).OrderBy(f => f, StringComparer.Ordinal))
        {
            violations.Add($"{file}: registers routes but is NOT in GovernedFiles — classify it (RouteLevelGate, "
                           + "GroupGated or NotMapped) with a reason");
        }

        foreach (var g in governed.Where(g => !censusSet.Contains(g.RelativePath)))
        {
            violations.Add($"{g.RelativePath}: is in GovernedFiles but the census does not find it (renamed, deleted, or it "
                           + "no longer registers a route) — update the entry");
        }

        foreach (var dup in governed.GroupBy(g => g.RelativePath, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            violations.Add($"{dup.Key}: listed {dup.Count()} times in GovernedFiles");
        }

        return violations;
    }

    // =============================================================================================
    // HANDLER DECISION VERIFICATION
    // ---------------------------------------------------------------------------------------------
    // A PRESENCE check, the same limit Rule B has: it proves the handler (or a declared hop) reaches a decision
    // seam, not that the seam is applied to the right id. The 159-166 deny tests are the proof of behaviour.
    //
    // A "body" is a method's BODY — the code between its braces, or after its arrow; never its signature — PLUS
    // the bodies of methods of the SAME type that it calls directly (one level, owner round 12 item 3) — so a
    // decision factored into a private helper of the same class is found, while a hop across a type boundary must
    // be declared. An interface in a hop is refused (name the implementation); every overload of a hop method
    // must reach the next hop / the seam.
    //
    // The seam is reached when the body contains it as a whole identifier, or when the body USES a parameter (of
    // the method whose body it is) or a top-level field/property of the declaring type whose declared type is the
    // seam. Task 167 r1 closed two false-credit paths here: the signature used to count, so an unused DI parameter
    // of the seam type credited a handler with no decision in its body; and the field rule scanned the whole TYPE,
    // so a sibling method's parameter named like a local of the handler lent the handler its seam.
    // =============================================================================================

    /// <summary>One body (a method's or a lambda's) and the parameters in scope for it.</summary>
    private sealed record BodyPart(string Code, IReadOnlyList<ParamDecl> Params);

    /// <summary>A declared body plus the same-type helper bodies it calls directly. Code only — comments and
    /// literals blanked, no signature.</summary>
    private sealed record Body(SourceUnit Unit, string? DeclaringType, IReadOnlyList<BodyPart> Parts)
    {
        public string Text => string.Join("\n", Parts.Select(p => p.Code));
    }

    private sealed class TypeIndex
    {
        private readonly Dictionary<string, List<(SourceUnit Unit, TypeDecl Type)>> _types = new(StringComparer.Ordinal);

        public TypeIndex(IEnumerable<SourceUnit> units)
        {
            foreach (var unit in units)
            {
                foreach (var type in unit.Types)
                {
                    if (!_types.TryGetValue(type.Name, out var list))
                    {
                        _types[type.Name] = list = new List<(SourceUnit, TypeDecl)>();
                    }

                    list.Add((unit, type));
                }
            }
        }

        public IReadOnlyList<(SourceUnit Unit, TypeDecl Type)> Named(string name)
            => _types.GetValueOrDefault(name) ?? new List<(SourceUnit, TypeDecl)>();
    }

    /// <summary>Every source unit under src/server/** — the BFF's from the real scan, the shared projects' lexed
    /// once here — so a hop into Spaarke.Core or Spaarke.Dataverse resolves.</summary>
    private static readonly Lazy<IReadOnlyList<SourceUnit>> ServerUnits = new(() =>
    {
        var bff = Real.Set.Units;
        var others = SourceScan.ServerSourceFiles()
            .Where(f => !Path.GetFullPath(f).StartsWith(Path.GetFullPath(BffRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new SourceUnit(SourceScan.Relative(f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f)));
        return bff.Concat(others).ToList();
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<TypeIndex> ServerTypes =
        new(() => new TypeIndex(ServerUnits.Value), LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyList<string> AllSeams()
        => DecisionServices.Concat(CallerContextSeams.Keys).ToList();

    /// <summary>Signature AND body — used only by the pass-through pins (<see cref="MethodBody"/>), where reading
    /// more text can only make a pin fail, never credit a route.</summary>
    private static string SignatureAndBody(MethodDecl m) => m.Unit.Code[m.NameIndex..m.BodyEnd];

    private static BodyPart PartOf(MethodDecl m) => new(m.Body, m.Params);

    private static Body Expand(SourceUnit unit, string? declaringType, BodyPart root, string? selfName = null)
    {
        if (declaringType is null)
        {
            return new Body(unit, null, new[] { root });
        }

        var sameType = unit.Methods.Where(m => m.DeclaringType == declaringType).ToList();
        var parts = new List<BodyPart> { root };
        foreach (var name in sameType.Select(m => m.Name).Distinct(StringComparer.Ordinal))
        {
            // Never expand the body's OWN name (a recursive call): that would pull its sibling overloads in and let
            // one overload borrow another's seam — the exact case the overload rule exists to catch.
            if (name == selfName)
            {
                continue;
            }

            if (Regex.IsMatch(root.Code, $@"(?<![\w.]){Regex.Escape(name)}\s*\(") || Regex.IsMatch(root.Code, $@"\bthis\.{Regex.Escape(name)}\s*\("))
            {
                parts.AddRange(sameType.Where(m => m.Name == name).Select(PartOf));
            }
        }

        return new Body(unit, declaringType, parts);
    }

    private static bool BodyHasSeam(Body body, string seam)
    {
        var members = SeamTypedMembers(body.Unit, body.DeclaringType, seam);
        foreach (var part in body.Parts)
        {
            if (ContainsToken(part.Code, seam))
            {
                return true;
            }

            // A parameter of THIS body's method whose declared type is the seam, actually used in the body.
            if (part.Params.Any(p => IsSeamType(p.Type, seam) && UsesName(part.Code, p.Name)))
            {
                return true;
            }

            // A top-level field or property of the declaring type whose declared type is the seam, used in the body.
            if (members.Any(name => UsesName(part.Code, name)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="type"/> (as written in a declaration) names the seam: a simple or
    /// qualified name (Spaarke.Core.Auth.AuthorizationService, global::...), optionally nullable.</summary>
    private static bool IsSeamType(string type, string seam)
    {
        var t = Regex.Replace(type, @"\s+", string.Empty).TrimEnd('?');
        if (t.StartsWith("global::", StringComparison.Ordinal))
        {
            t = t["global::".Length..];
        }

        return t[(t.LastIndexOf('.') + 1)..] == seam;
    }

    /// <summary>True when <paramref name="name"/> is USED in <paramref name="code"/> as a variable: a whole identifier
    /// that is not another object's member (<c>x.name</c>) and not a named-argument label (<c>f(name: v)</c>).
    /// <c>this.name</c> counts.</summary>
    private static bool UsesName(string code, string name)
    {
        foreach (Match m in Regex.Matches(code, $@"(?<![\w]){Regex.Escape(name)}(?!\w)"))
        {
            var p = m.Index - 1;
            while (p >= 0 && char.IsWhiteSpace(code[p]))
            {
                p--;
            }

            if (p >= 0 && code[p] == '.')
            {
                var (owner, _) = IdentifierBefore(code, p);
                if (owner != "this")
                {
                    continue;   // another object's member that merely shares the name
                }
            }

            var label = Regex.Match(code[(m.Index + m.Length)..], @"^\s*:(?!:)");
            if (label.Success && p >= 0 && code[p] is '(' or ',')
            {
                continue;   // f(name: value) — a label, not a use
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Names of the fields and properties declared at the TOP LEVEL of <paramref name="declaringType"/> whose declared
    /// type is the seam. Method parameters, locals and nested types' members are excluded: every bracketed region's
    /// contents are blanked before matching, so a sibling method's <c>Other(AccessRights rights)</c> parameter is not
    /// a member of the type and lends nothing to a handler that happens to use a <c>string rights</c>.
    /// </summary>
    private static IReadOnlyList<string> SeamTypedMembers(SourceUnit unit, string? declaringType, string seam)
    {
        var type = unit.Types.FirstOrDefault(t => t.Name == declaringType);
        if (type is null)
        {
            return Array.Empty<string>();
        }

        var top = TopLevelOf(unit.Code, type.BodyStart, type.BodyEnd);
        return Regex.Matches(top, $@"(?<![\w.])(?:global::)?(?:[A-Za-z_]\w*\s*\.\s*)*{Regex.Escape(seam)}\s*\??\s+([A-Za-z_]\w*)\s*[;={{,]")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The inside of a type body with the CONTENTS of every bracketed region blanked (the brackets are
    /// kept), so only depth-one member declarations stay readable. Offsets and newlines are preserved.</summary>
    private static string TopLevelOf(string code, int bodyStart, int bodyEnd)
    {
        var chars = code[(bodyStart + 1)..Math.Max(bodyStart + 1, bodyEnd - 1)].ToCharArray();
        var depth = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    chars[i] = ' ';
                }

                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth > 0)
                {
                    chars[i] = ' ';
                }
            }
            else if (depth > 0 && c != '\n')
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Splits an inline lambda handler (code, literals blanked) into its parameters and its BODY — everything after
    /// the top-level arrow. Handles attributes, <c>static</c>/<c>async</c>, an explicit return type and a single
    /// untyped parameter. Null when the text has no top-level arrow (not a lambda this verifier can read).
    /// </summary>
    private static BodyPart? LambdaPart(string code)
    {
        var depth = 0;
        for (var i = 0; i + 1 < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (depth == 0 && c == '=' && code[i + 1] == '>')
            {
                var head = code[..i].TrimEnd();
                IReadOnlyList<ParamDecl> parameters;
                if (head.EndsWith(')'))
                {
                    var close = head.Length - 1;
                    var open = close;
                    var d = 0;
                    for (; open >= 0; open--)
                    {
                        if (head[open] is ')' or ']' or '}')
                        {
                            d++;
                        }
                        else if (head[open] is '(' or '[' or '{' && --d == 0)
                        {
                            break;
                        }
                    }

                    if (open < 0)
                    {
                        return null;
                    }

                    parameters = ParseParams(head[(open + 1)..close]);
                }
                else
                {
                    var single = Regex.Match(head, @"([A-Za-z_]\w*)$");
                    if (!single.Success)
                    {
                        return null;
                    }

                    parameters = new[] { new ParamDecl(single.Groups[1].Value, string.Empty, false) };
                }

                return new BodyPart(code[(i + 2)..], parameters);
            }
        }

        return null;
    }

    private static readonly Regex LambdaHandler = new(@"^(?:\[[^\]]*\]\s*)?(?:static\s+)?(?:async\s+)?(?:\([^)]*\)|[A-Za-z_]\w*)\s*=>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static (bool Ok, string? Problem) VerifyHandlerDecision(
        HandlerDecision decision, RouteRegistration route, IReadOnlyList<SourceUnit>? extraTypeSources = null)
    {
        if (!AllSeams().Contains(decision.Seam))
        {
            return (false, $"the seam '{decision.Seam}' is in neither DecisionServices nor CallerContextSeams — a seam must "
                           + "be an authorization decision or a caller-context mechanism, listed with its reason");
        }

        if (decision.Hops.Count > MaxHandlerDecisionHops)
        {
            return (false, $"{decision.Hops.Count} hops declared; at most {MaxHandlerDecisionHops}");
        }

        if (route.Unit is null || route.HandlerEnd <= route.HandlerStart)
        {
            return (false, "the registration has no handler argument to follow");
        }

        var unit = route.Unit;
        var handlerText = Squash(unit.Text[route.HandlerStart..route.HandlerEnd]);
        var types = extraTypeSources is null ? ServerTypes.Value : new TypeIndex(extraTypeSources.Concat(ServerUnits.Value));
        List<Body> bodies;

        if (decision.Handler == "inline")
        {
            if (!LambdaHandler.IsMatch(handlerText))
            {
                return (false, $"declared 'inline' but the registration's handler is '{Abbreviate(handlerText)}'");
            }

            var lambda = LambdaPart(unit.Code[route.HandlerStart..route.HandlerEnd]);
            if (lambda is null)
            {
                return (false, "the inline handler cannot be split into its parameters and its body");
            }

            bodies = new List<Body> { Expand(unit, unit.TypeAt(route.HandlerStart)?.Name, lambda) };
        }
        else
        {
            var named = handlerText == decision.Handler || handlerText.EndsWith("." + decision.Handler, StringComparison.Ordinal);
            if (!named)
            {
                return (false, $"the registration's handler is '{Abbreviate(handlerText)}', not '{decision.Handler}'");
            }

            List<MethodDecl> methods;
            if (decision.Handler.Contains('.'))
            {
                var (found, problem) = ResolveMethods(types, decision.Handler);
                if (problem is not null)
                {
                    return (false, problem);
                }

                methods = found;
            }
            else
            {
                var registrationType = unit.TypeAt(route.HandlerStart)?.Name;
                methods = unit.Methods.Where(m => m.Name == decision.Handler && m.DeclaringType == registrationType).ToList();
                if (methods.Count == 0)
                {
                    return (false, $"the handler method '{decision.Handler}' is not declared in {route.File}");
                }
            }

            bodies = methods.Select(m => Expand(m.Unit, m.DeclaringType, PartOf(m), m.Name)).ToList();
        }

        foreach (var hop in decision.Hops)
        {
            var (methods, problem) = ResolveMethods(types, hop);
            if (problem is not null)
            {
                return (false, problem);
            }

            var hopName = hop.Split('.')[1];
            var notCalling = bodies.Where(b => !Regex.IsMatch(b.Text, $@"(?<![\w]){Regex.Escape(hopName)}\s*(?:<[^;(){{}}]*>)?\s*\(")).ToList();
            if (notCalling.Count > 0)
            {
                return (false, $"the body before hop '{hop}' does not call {hopName} (in {notCalling.Count} of {bodies.Count} "
                               + "overload(s))");
            }

            bodies = methods.Select(m => Expand(m.Unit, m.DeclaringType, PartOf(m), m.Name)).ToList();
        }

        var missing = bodies.Count(b => !BodyHasSeam(b, decision.Seam));
        if (missing > 0)
        {
            return (false, $"the seam '{decision.Seam}' is not reached in {missing} of {bodies.Count} final bod"
                           + (bodies.Count == 1 ? "y" : "ies (every overload must reach it)"));
        }

        return (true, null);
    }

    private static (List<MethodDecl> Methods, string? Problem) ResolveMethods(TypeIndex types, string qualified)
    {
        var parts = qualified.Split('.');
        if (parts.Length != 2)
        {
            return (new List<MethodDecl>(), $"'{qualified}' is not 'ConcreteType.Method'");
        }

        var declarations = types.Named(parts[0]);
        if (declarations.Count == 0)
        {
            return (new List<MethodDecl>(), $"no type '{parts[0]}' is declared under src/server/**");
        }

        if (declarations.Any(d => d.Type.Kind == "interface"))
        {
            return (new List<MethodDecl>(), $"'{parts[0]}' is an INTERFACE — name the implementation, so the body the "
                                             + "decision lives in is the one that runs");
        }

        if (declarations.Count > 1 && !declarations.All(d => d.Type.IsPartial))
        {
            return (new List<MethodDecl>(), $"'{parts[0]}' is declared {declarations.Count} times — ambiguous");
        }

        var methods = declarations
            .SelectMany(d => d.Unit.Methods.Where(m => m.Name == parts[1] && m.DeclaringType == parts[0]))
            .ToList();
        return methods.Count == 0
            ? (methods, $"'{parts[0]}' declares no method '{parts[1]}'")
            : (methods, null);
    }

    private static string Abbreviate(string s) => s.Length <= 60 ? s : s[..57] + "...";

    // =============================================================================================
    // NON-DECIDING PASS-THROUGH PINS — the evidence that each NonDecidingAttachments entry is still true
    // =============================================================================================

    /// <summary>Record-level decision tokens: a filter consulting any of these is no longer an entity-level or
    /// identity-only check.</summary>
    private static readonly string[] RecordLevelTokens =
    {
        "AuthorizationService", "IAiAuthorizationService", "IAccessDataSource", "RetrievePrincipalAccess",
        "CallerRecordAccessProbe", "IDataverseUserClient", "DataverseImpersonation", "IImpersonatedCommunicationQuery",
        "ICommunicationAccessFilter", "IAccessibleRecordSetService",
    };

    private static bool ConsultsAny(string code, IEnumerable<string> tokens) => tokens.Any(t => ContainsToken(code, t));

    private static string MethodBody(string code, string name)
    {
        var unit = new SourceUnit("pin", code);
        var method = unit.Methods.FirstOrDefault(m => m.Name == name);
        return method is null ? string.Empty : SignatureAndBody(method);
    }

    /// <summary>
    /// True while the named pass-through is still in <paramref name="raw"/> (the filter's source). When this
    /// turns false the filter was fixed: delete the NonDecidingAttachments entry, add the form to CreditedForms
    /// if it now decides, and re-run Rule A.
    /// </summary>
    private static bool PassThroughStillPresent(string form, string raw)
    {
        var code = CodeOf(raw);
        var decisions = DecisionServices.Concat(CallerContextSeams.Keys).ToList();
        switch (form)
        {
            case "AddAnalysisRecordAuthorizationFilter":
                {
                    var body = MethodBody(raw, "AuthorizeAnalysisAccessAsync");
                    return body.Length > 0 && Regex.IsMatch(body, @"return\s+await\s+next\s*\(")
                           && !Regex.IsMatch(body, @"\.\s*AuthorizeAsync\s*\(") && !ConsultsAny(body, RecordLevelTokens);
                }

            case "AddAiAuthorizationFilter":
            case "AddEndpointFilter<AiAuthorizationFilter>":
                return Regex.IsMatch(code, @"if\s*\(\s*documentIds\.Count\s*==\s*0\s*\)\s*\{?\s*return\s+await\s+next\s*\(")
                       && NoEndpointHandlerBindsDocumentAnalysisRequest();

            case "AddDataverseAuthorizationFilter(EntitySource.FromRouteValueWithRecord)":
                return Regex.IsMatch(code, @"case\s+EntitySource\.FromRouteValue\s*:\s*case\s+EntitySource\.FromRouteValueWithRecord\s*:")
                       && !ConsultsAny(code, RecordLevelTokens);

            case "AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody)":
                return Regex.IsMatch(MethodBody(raw, "ResolveEntities"), @"case\s+EntitySource\.FromFetchXmlBody\s*:")
                       && !ConsultsAny(code, RecordLevelTokens);

            case "AddTenantAuthorizationFilter":
                return Regex.IsMatch(code, @"if\s*\(\s*string\.IsNullOrEmpty\(\s*requestedTenantId\s*\)\s*\)\s*\{\s*(?:[^{}]*?)return\s+await\s+next\s*\(");

            case "AddSpeAdminTenantScopeFilter":
                return Regex.IsMatch(code, @"if\s*\(\s*!\s*TryReadConfigId\s*\([^)]*\)\s*\)\s*\{\s*return\s+await\s+next\s*\(");

            case "AddReportingAuthorizationFilter":
                return Regex.IsMatch(code, @"\.IsInRole\s*\(") && !ConsultsAny(code, decisions);

            case "AddEndpointFilter<CommunicationAuthorizationFilter>":
            case "AddEndpointFilter<WorkspaceAuthorizationFilter>":
            case "AddAgentAuthorizationFilter":
            case "AddCallerPrincipalAuthorizationFilter":
                return !ConsultsAny(code, decisions);

            default:
                return false;   // an entry with no pin fails — every NonDeciding entry is pinned to its evidence
        }
    }

    private static bool NoEndpointHandlerBindsDocumentAnalysisRequest()
        => Real.CensusFiles.All(f => !ContainsToken(Real.Set.Get(f)!.Code, "DocumentAnalysisRequest"));

    // =============================================================================================
    // RULE A — every route declares how it is authorized
    // =============================================================================================

    [Fact(DisplayName = "Task 167 Rule A: every route in every BFF endpoint file declares how it is authorized")]
    public void EveryRouteDeclaresHowItIsAuthorized()
    {
        var scan = Real;

        var unparseable = scan.Routes.Where(r => r.Unparseable)
            .Select(r => $"{r.File}:{r.Line}: {r.Problem}")
            .Concat(scan.Set.Problems)
            .ToList();
        Assert.True(
            unparseable.Count == 0,
            "The route scanner could not resolve one or more registrations. This is a FAILURE, never a skip: an "
            + "unreadable registration is the helper-wrapped blind spot that would otherwise let a route through "
            + "silently. Fix the scanner (RouteAuthorizationGuardTests.Scanner.cs) — do not narrow the census.\n\n"
            + string.Join("\n", unparseable));

        var violations = RuleAViolations(RealAssessments.Value, Waivers);
        Assert.True(
            violations.Count == 0,
            "These BFF routes declare no authorization: no credited per-resource filter, no verified HandlerDecision, "
            + "no admin mechanism, and no waiver. A route that only asks \"are you signed in?\" while the BFF reads or "
            + "writes as its OWN identity is how the 82 findings of the 2026-10-02 sweep were made.\n\n"
            + "REMEDY — in this order of preference:\n"
            + "  1. Attach a credited per-resource filter (CreditedForms) — e.g. .AddDocumentAuthorizationFilter(\"read\").\n"
            + "  2. If the decision lives in the handler, add a HandlerDecisions entry naming the handler, its seam and\n"
            + "     up to two hops; the guard verifies the seam is reached.\n"
            + "  3. If the route is an operator surface, gate it with one of the four AdminMechanisms and pin it in\n"
            + "     AdminOnlyRoutes with the group's reason.\n"
            + "  4. If the route should not exist, DELETE it (owner round 10 item 1: no caller and not published).\n"
            + "  5. Otherwise a Waiver: Permanent with a PermanentBasis whose reason cites the handler file:line that\n"
            + "     proves it, or Pending with an owning task. Never Permanent to make a build green.\n\n"
            + "Do NOT make this pass by removing a file from GovernedFiles or a form from a list.\n\n"
            + "Undeclared routes:\n    " + string.Join("\n    ", violations));
    }

    [Fact(DisplayName = "Task 167: every waiver is well formed and none is stale or redundant")]
    public void NoWaiverIsStaleAndEveryWaiverIsWellFormed()
    {
        var violations = WaiverViolations(RealAssessments.Value, Waivers);
        Assert.True(
            violations.Count == 0,
            "The waiver list is the part of this mechanism that decays, so it is checked rather than trusted. A waiver "
            + "on a route that is now credited, gone, or changed is STALE: delete it (that deletion is how the work "
            + "list visibly shrinks). See the maintenance procedure above the Waivers list.\n\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Task 167: the sweep ledger is pinned, owned by 159-166, and resolves only by credit")]
    public void TheSweepLedgerIsPinnedAndResolvesOnlyByCredit()
    {
        Assert.Equal(90, SweepFindings.Count);
        Assert.Equal(82, SweepFindings.Select(e => e.SweepId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(74, SweepFindings.Count(e => e.Gap == Gap.NoDecision));
        Assert.Equal(16, SweepFindings.Count(e => e.Gap == Gap.InsufficientDecision));

        var perTask = SweepFindings.GroupBy(e => e.OwningTask).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int>
        {
            ["159"] = 8,
            ["160"] = 2,
            ["161"] = 12,
            ["162"] = 6,
            ["163"] = 17,
            ["164"] = 19,
            ["165"] = 9,
            ["166"] = 17,
        }, perTask);

        var bySeverity = SweepFindings.GroupBy(e => e.SweepId).Select(g => g.First().Severity)
            .GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(17, bySeverity[Severity.Critical]);
        Assert.Equal(29, bySeverity[Severity.High]);
        Assert.Equal(30, bySeverity[Severity.Medium]);
        Assert.Equal(6, bySeverity[Severity.Low]);

        var violations = LedgerViolations(RealAssessments.Value, Waivers, SweepFindings);
        Assert.True(
            violations.Count == 0,
            "The sweep ledger is the closed set of the 82 findings (90 route keys). A fix task resolves its entry ONLY "
            + "by credit: make the route pass Rule A by a credited filter, a HandlerDecision, or an AdminOnlyRoutes entry in a "
            + "SystemAdmin / SPE-admin group (round 34 item 5), delete its waiver, and set ResolvedBy to the task id and "
            + "ProofTest to its behavioural deny test. A route DELETED under owner round 10 item 1 resolves with ResolvedBy and "
            + "a ProofTest that pins its ABSENCE (round 34 item 4). For an InsufficientDecision "
            + "entry the guard cannot see a fix made inside an already-credited filter or handler — that resolution is by "
            + "declaration (ResolvedBy + ProofTest), reviewed at code review.\n\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Task 167: the set of admin-only routes is pinned, by file, with a reason per surface")]
    public void TheSetOfAdminOnlyRoutesIsPinned()
    {
        var actual = RealAssessments.Value.Where(a => a.Credit == Credit.AdminOnly).Select(a => a.Key);
        var (added, removed) = PinDiff(actual, AdminOnlyRoutes.SelectMany(g => g.Routes));

        Assert.True(
            added.Count == 0,
            "These routes are credited ONLY by an admin mechanism and are not in AdminOnlyRoutes. Admin credit is "
            + "reserved for operator surfaces: confirm the audience is operators (not users acting on their own "
            + "records), then add the route to its file's group — or give it a per-resource decision instead.\n\n  "
            + string.Join("\n  ", added));
        Assert.True(
            removed.Count == 0,
            "These AdminOnlyRoutes entries are no longer admin-only (gone, renamed, or now carrying a per-resource "
            + "decision). Delete them from the pin:\n\n  " + string.Join("\n  ", removed));

        Assert.All(AdminOnlyRoutes, g => Assert.True(g.Reason.Trim().Length >= 60,
            $"{g.File}: the group reason must say why the surface is an operator surface"));
        Assert.Equal(AdminOnlyRoutes.Count, AdminOnlyRoutes.Select(g => (g.File, g.Mechanism)).Distinct().Count());
    }

    /// <summary>Every route of an <see cref="AdminOnlyRoutes"/> group carries the group's declared mechanism, which is one
    /// of the four <see cref="AdminMechanisms"/>; a route appears in one group only.</summary>
    private static List<string> AdminOnlyMechanismViolations(IReadOnlyList<Assessment> routes, IReadOnlyList<AdminOnlyGroup> groups)
    {
        var byKey = routes.GroupBy(a => a.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var group in groups)
        {
            if (!AdminMechanisms.Any(m => m.Form == group.Mechanism))
            {
                violations.Add($"{group.File}: '{group.Mechanism}' is not one of the four admin mechanisms");
            }

            foreach (var route in group.Routes)
            {
                if (byKey.TryGetValue(route, out var a) && !a.Route.Forms.Contains(group.Mechanism, StringComparer.Ordinal))
                {
                    violations.Add($"{route} is in the {group.File} group gated by {group.Mechanism}, but its chain carries "
                                   + $"[{string.Join(", ", a.Route.Forms)}] — add the mechanism to the route, or move the route "
                                   + "to the group of the mechanism it really has");
                }
            }
        }

        foreach (var dup in groups.SelectMany(g => g.Routes).GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            violations.Add($"{dup.Key} is listed in {dup.Count()} AdminOnlyRoutes groups");
        }

        return violations;
    }

    [Fact(DisplayName = "Task 167 f2: every admin-only route carries its group's declared admin mechanism (round 34 item 5)")]
    public void EveryAdminOnlyRouteCarriesItsGroupsMechanism()
    {
        var violations = AdminOnlyMechanismViolations(RealAssessments.Value, AdminOnlyRoutes);
        Assert.True(
            violations.Count == 0,
            "An AdminOnlyRoutes group declares the admin mechanism its routes are gated by; a sweep entry resolves through the "
            + "set only when that mechanism is RequireAuthorization(\"SystemAdmin\") or the SPE admin policy (main-session "
            + "round 34 item 5). A route listed under a mechanism it does not carry would let a sweep finding resolve on a "
            + "gate that is not there.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: both sweep-eligible policies are in use, and the RAG machine credential is its own group.
        Assert.Contains(AdminOnlyRoutes, g => g.Mechanism == SystemAdminPolicy);
        Assert.Contains(AdminOnlyRoutes, g => g.Mechanism == SpeAdminPolicy);
        Assert.Equal(new[] { "POST /api/ai/rag/enqueue-indexing" },
            AdminOnlyRoutes.Where(g => g.Mechanism == RagApiKeyCredential).SelectMany(g => g.Routes).ToArray());
    }

    [Fact(DisplayName = "Task 074 Rule A: the set of policy-only routes is pinned")]
    public void TheSetOfPolicyOnlyRoutesIsPinned()
    {
        // A ResourceAccessRequirement policy IS a real resource decision, but finding #4 was a route whose policy
        // authorized a CONTAINER id against DOCUMENT rights. Pinning the set means a new route cannot quietly join
        // the one category whose correctness this guard cannot verify. The set is EMPTY and stays pinned.
        var actual = RealAssessments.Value.Where(a => a.Credit == Credit.ResourcePolicy).Select(a => a.Key);
        var (added, removed) = PinDiff(actual, PolicyOnlyRoutes);
        Assert.True(added.Count == 0 && removed.Count == 0,
            "The policy-only set changed. ResourceAccessHandler.ExtractResourceId accepts containerId / driveId / "
            + "documentId / id INTERCHANGEABLY — confirm the route key matches the policy's resource domain.\n  added: "
            + string.Join(", ", added) + "\n  removed: " + string.Join(", ", removed));
    }

    [Fact(DisplayName = "Task 167: every HandlerDecision is verified in source and none is redundant")]
    public void EveryHandlerDecisionIsVerified()
    {
        var byKey = RealAssessments.Value.ToDictionary(a => a.Key, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var d in HandlerDecisions)
        {
            if (d.Reason.Trim().Length < 60 || !Regex.IsMatch(d.Reason, @"\w\.cs:\d+"))
            {
                violations.Add($"{d.Route}: the reason must be at least 60 characters and cite file:line");
            }

            if (!byKey.TryGetValue(d.Route, out var a))
            {
                violations.Add($"{d.Route}: no live route has this key — delete or re-key the declaration");
                continue;
            }

            if (a.HandlerProblem is not null)
            {
                violations.Add($"{d.Route}: {a.HandlerProblem}");
            }
            else if (a.Credit is Credit.PerResource or Credit.ResourcePolicy)
            {
                violations.Add($"{d.Route}: REDUNDANT — the route is already credited by a filter ({a.Fingerprint})");
            }
        }

        foreach (var dup in HandlerDecisions.GroupBy(d => d.Route).Where(g => g.Count() > 1))
        {
            violations.Add($"{dup.Key}: declared {dup.Count()} times");
        }

        foreach (var (seam, entry) in CallerContextSeams)
        {
            if (entry.Reason.Trim().Length < 60)
            {
                violations.Add($"CallerContextSeams[{seam}]: the reason must be at least 60 characters");
            }

            if (!File.Exists(Path.Combine(BffRoot, entry.TypeFile.Replace('/', Path.DirectorySeparatorChar))))
            {
                violations.Add($"CallerContextSeams[{seam}]: the type file '{entry.TypeFile}' does not exist");
            }
        }

        Assert.True(
            violations.Count == 0,
            "A HandlerDecision credits a route only when the declared handler — followed through at most two named "
            + "hops — reaches the declared seam. This is a PRESENCE check, the same limit Rule B has; the behavioural "
            + "deny tests are the proof.\n\n  " + string.Join("\n  ", violations));
    }

    // =============================================================================================
    // THE ATTACHMENT-FORM CENSUS AND THE CREDIT LISTS
    // =============================================================================================

    [Fact(DisplayName = "Task 167: every authorization-shaped attachment on any route is classified in exactly one list")]
    public void EveryAttachmentFormIsClassifiedExactlyOnce()
    {
        var unclassified = RealAssessments.Value
            .SelectMany(a => a.UnclassifiedCalls.Select(c => $"{c.File}:{c.Line}: .{c.Form} (on {a.Key})"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            unclassified.Count == 0,
            "These authorization-shaped calls are on a route chain but in NO classification list. Read the filter, then "
            + "add the form to exactly one of CreditedForms (it decides per resource), AdminMechanisms (it is one of the "
            + "four operator gates), NonDecidingAttachments (it passes through — pin the evidence, name the owner) or "
            + "NotAuthorizationForms (it is not authorization).\n\n  " + string.Join("\n  ", unclassified));

        Assert.True(ListOverlapViolations().Count == 0, string.Join("\n", ListOverlapViolations()));

        var used = RealAssessments.Value.SelectMany(a => a.Route.Forms).ToHashSet(StringComparer.Ordinal);
        var unused = UnusedEntryViolations(used, CreditedForms.Select(c => c.Form), AdminMechanisms.Select(a => a.Form),
            NonDecidingAttachments.Select(n => n.Form), NotAuthorizationForms.Select(n => n.Form));
        Assert.True(unused.Count == 0,
            "These list entries name a form NO route attaches. An unused allow-list entry is a credit waiting for a "
            + "route nobody reviewed; delete it.\n  " + string.Join("\n  ", unused));

        var thin = CreditedForms.Where(c => c.Reason.Trim().Length < 60 || !Regex.IsMatch(c.Reason, @"\w\.cs:\d+")).Select(c => c.Form)
            .Concat(AdminMechanisms.Where(a => a.Reason.Trim().Length < 60).Select(a => a.Form))
            .Concat(NonDecidingAttachments.Where(n => n.Reason.Trim().Length < 60 || n.Evidence.Trim().Length < 20).Select(n => n.Form))
            .Concat(NotAuthorizationForms.Where(n => n.Reason.Trim().Length < 30).Select(n => n.Form))
            .ToList();
        Assert.True(thin.Count == 0, "These list entries lack a substantive written reason (credited entries must cite "
                                     + "file:line):\n  " + string.Join("\n  ", thin));

        var ownerless = NonDecidingAttachments
            .Where(n => n.OwningTask is not ("-" or "160" or "161" or "162" or "163" or "164" or "165"))
            .Select(n => n.Form).ToList();
        Assert.True(ownerless.Count == 0, "Every NonDecidingAttachments entry names the fix task whose POML modifies the "
                                          + "filter, or '-' for an identity/role precondition:\n  " + string.Join("\n  ", ownerless));

        var missingFiles = CreditedForms.Select(c => c.FilterFile).Concat(NonDecidingAttachments.Select(n => n.FilterFile))
            .Where(f => !File.Exists(Path.Combine(BffRoot, f.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
        Assert.True(missingFiles.Count == 0, "Filter files named by the lists do not exist:\n  " + string.Join("\n  ", missingFiles));
    }

    private static List<string> ListOverlapViolations()
    {
        var all = CreditedForms.Select(c => (c.Form, "CreditedForms"))
            .Concat(AdminMechanisms.Select(a => (a.Form, "AdminMechanisms")))
            .Concat(NonDecidingAttachments.Select(n => (n.Form, "NonDecidingAttachments")))
            .Concat(NotAuthorizationForms.Select(n => (n.Form, "NotAuthorizationForms")))
            .Concat(ResourcePolicies.Select(p => ($"RequireAuthorization(\"{p}\")", "ResourcePolicies")));
        return all.GroupBy(x => x.Item1, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: listed in {string.Join(" AND ", g.Select(x => x.Item2))} — a form belongs to exactly one list")
            .ToList();
    }

    private static List<string> UnusedEntryViolations(IReadOnlySet<string> used, params IEnumerable<string>[] lists)
        => lists.SelectMany(l => l).Where(f => !used.Contains(f)).Select(f => $"{f}: attached by no route").ToList();

    public static IEnumerable<object[]> NonDecidingEntries()
        => NonDecidingAttachments.Select(n => new object[] { n.Form });

    [Theory(DisplayName = "Task 167: each non-deciding attachment still passes through, as its entry claims")]
    [MemberData(nameof(NonDecidingEntries))]
    public void NonDecidingAttachmentStillPassesThrough(string form)
    {
        var entry = NonDecidingAttachments.Single(n => n.Form == form);
        var raw = File.ReadAllText(Path.Combine(BffRoot, entry.FilterFile.Replace('/', Path.DirectorySeparatorChar)));

        Assert.True(
            PassThroughStillPresent(form, raw),
            $"The pass-through recorded for .{form} ({entry.FilterFile}) is no longer in the source: {entry.Evidence}\n\n"
            + "If the filter was FIXED, that is the outcome this entry exists for. REMEDY: delete the "
            + "NonDecidingAttachments entry, add the form to CreditedForms if it now decides per resource (with a reason "
            + "citing file:line), and re-run Rule A — the routes it guards then need no waiver, and their Pending "
            + "waivers go stale.");
    }

    // =============================================================================================
    // RULE B — an authorization filter must actually decide something
    // =============================================================================================

    [Fact(DisplayName = "Task 074 Rule B: no authorization filter is decorative — each consults a decision service")]
    public void NoAuthorizationFilterIsDecorative()
    {
        // THE RULE THAT CATCHES FINDING #1 (task 074): SemanticSearchAuthorizationFilter was attached to
        // /api/ai/search from the start, returned allow from every branch, and read as a gate at the call site.
        // A type that consults no authorization decision service decides nothing.
        //
        // Task 167: tokens match WHOLE identifiers in code (comments and literals blanked), so
        // "IAiAuthorizationService" no longer satisfies "AuthorizationService" by substring — it is its own entry.
        // The subject set is every *AuthorizationFilter.cs plus the file of every credited form.
        var violations = new List<string>();

        foreach (var file in AuthorizationFilterFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (ClaimOnlyFilters.ContainsKey(name) || KnownDecorativeFilters.ContainsKey(name))
            {
                continue;
            }

            if (ConsultsDecisionService(CodeOf(File.ReadAllText(file))))
            {
                continue;
            }

            // THE PAIR MAY DECIDE (task 077): the filter authorizes the request and publishes an obligation; the
            // endpoint authorizes the rows. Acceptable only if EVERY endpoint file attaching it consults one.
            var attaching = EndpointFilesAttaching(file);
            if (attaching.Count > 0 && attaching.All(f => ConsultsDecisionService(Real.Set.Get(f)!.Code)))
            {
                continue;
            }

            violations.Add($"{SourceScan.Relative(file)}: references none of the decision services"
                           + (attaching.Count == 0
                               ? " (and no route attaches it)"
                               : ", and neither do all of the endpoint files attaching it: " + string.Join(", ", attaching)));
        }

        Assert.True(
            violations.Count == 0,
            "These filters consult NO authorization decision service. A filter that consults nothing decides nothing — "
            + "it produces an audit log while reading as a gate at the call site.\n\nREMEDY: consult one of "
            + string.Join(" / ", DecisionServices) + " and deny on refusal; or, if the filter legitimately decides from "
            + "claims, a signature or an owner comparison, add it to ClaimOnlyFilters WITH a written reason.\n\n"
            + string.Join("\n", violations));
    }

    private static bool ConsultsDecisionService(string code) => DecisionServices.Any(s => ContainsToken(code, s));

    /// <summary>Rule B's subject: every *AuthorizationFilter.cs, plus the file of every credited form — so a
    /// credited filter is always inspected, whatever it is named.</summary>
    private static IReadOnlyList<string> AuthorizationFilterFiles()
    {
        var credited = CreditedForms.Select(c => Path.GetFullPath(Path.Combine(BffRoot, c.FilterFile.Replace('/', Path.DirectorySeparatorChar))));
        return Directory.EnumerateFiles(BffRoot, "*AuthorizationFilter.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Select(Path.GetFullPath)
            .Concat(credited)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Endpoint files whose routes attach a form mapped to <paramref name="filterFile"/>.</summary>
    private static IReadOnlyList<string> EndpointFilesAttaching(string filterFile)
    {
        var relative = BffRelative(filterFile);
        var forms = CreditedForms.Where(c => c.FilterFile == relative).Select(c => c.Form)
            .Concat(NonDecidingAttachments.Where(n => n.FilterFile == relative).Select(n => n.Form))
            .Concat(AdminMechanisms.Where(a => a.Form == $"Add{Path.GetFileNameWithoutExtension(filterFile)}").Select(a => a.Form))
            .ToHashSet(StringComparer.Ordinal);

        return Real.Live.Where(r => r.Forms.Any(forms.Contains)).Select(r => r.File)
            .Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    [Fact(DisplayName = "Task 120/167: Rule B inspects every filter Rule A credits — the two lists cannot drift apart")]
    public void RuleBCoversEveryFilterRuleACredits()
    {
        // One list, two consumers: CreditedForms is what Rule A credits AND what Rule B's subject set is derived
        // from, so a credited-but-never-inspected filter (the SemanticSearchAuthorizationFilter shape reached from
        // the opposite direction) cannot exist. This pins that derivation.
        var subjects = AuthorizationFilterFiles().Select(BffRelative).ToHashSet(StringComparer.Ordinal);
        var uninspected = CreditedForms.Where(c => !subjects.Contains(c.FilterFile)).Select(c => c.Form).ToList();
        Assert.True(uninspected.Count == 0, "Credited forms whose filter Rule B never inspects:\n  " + string.Join("\n  ", uninspected));

        Assert.Contains("Api/Filters/SessionOwnershipFilter.cs", subjects);
        Assert.Contains("Api/ExternalAccess/DelegationRuleFilter.cs", subjects);
    }

    // =============================================================================================
    // CENSUS, BINDING AND SCANNER COMPLETENESS
    // =============================================================================================

    [Fact(DisplayName = "Task 074/167 census: the set of BFF endpoint files is pinned and EQUAL to GovernedFiles")]
    public void TheEndpointFileCensusIsPinned()
    {
        // MAINTENANCE PROCEDURE. The census changed because a file started or stopped registering routes.
        //   - ADDED: add a GovernedFiles entry (RouteLevelGate, GroupGated or NotMapped) with a reason, then give
        //     every route in it a decision (Rule A), and bump ExpectedEndpointFileCount with a history line.
        //   - REMOVED: delete its entry and every waiver / declaration / pin naming its routes, then bump.
        var files = EndpointFiles();
        Assert.True(
            files.Count == ExpectedEndpointFileCount,
            $"The number of BFF files registering HTTP routes changed: expected {ExpectedEndpointFileCount}, found "
            + $"{files.Count}. Classify the change (see the maintenance procedure in this test) before bumping the count.");

        var violations = CensusViolations(files, GovernedFiles);
        Assert.True(violations.Count == 0, string.Join("\n", violations));

        var thin = GovernedFiles.Where(g => g.Reason.Trim().Length < 20).Select(g => g.RelativePath).ToList();
        Assert.True(thin.Count == 0, "GovernedFiles entries need a reason:\n  " + string.Join("\n  ", thin));

        var scopeMismatches = new List<string>();
        foreach (var g in GovernedFiles)
        {
            var routes = ScanFile(g.RelativePath).Where(r => !r.Unparseable).ToList();
            var derived = routes.Count > 0 && routes.All(r => r.Unbound) ? Scope.NotMapped
                : routes.Any(r => r.Aggregator is not null) ? Scope.GroupGated
                : Scope.RouteLevelGate;
            if (derived != g.Scope)
            {
                scopeMismatches.Add($"{g.RelativePath}: declared {g.Scope}, the scanner binds it as {derived}");
            }

            if (g.Scope != Scope.NotMapped && routes.Any(r => r.Unbound))
            {
                scopeMismatches.Add($"{g.RelativePath}: a registration in a method nothing calls — delete it, or classify "
                                    + "the file NotMapped if ALL of it is dead");
            }
        }

        Assert.True(scopeMismatches.Count == 0, string.Join("\n", scopeMismatches));
    }

    [Fact(DisplayName = "Task 167: the census selects files by the scanner's own registration vocabulary")]
    public void Census_CountsAFileThatRegistersOnlyMapMethodsOrMapHealthChecks()
    {
        // A file registering only MapMethods (or only MapHealthChecks) is a route surface; the census must see it.
        var methodsOnly = CodeOf("public static class X { public static void M(this RouteGroupBuilder g) { "
                                 + "g.MapMethods(\"/x\", [\"PATCH\"], H); } }");
        var healthOnly = CodeOf("app.MapHealthChecks(\"/h\").AllowAnonymous();");
        var commentOnly = CodeOf("// app.MapGet(\"/x\", H);\nvar s = \".MapGet(\";");
        Assert.Matches(RegistrationCall, methodsOnly);
        Assert.Matches(RegistrationCall, healthOnly);
        Assert.DoesNotMatch(RegistrationCall, commentOnly);

        // Negative: a GovernedFiles entry naming a missing file fails naming the file.
        var withMissing = GovernedFiles.Append(new GovernedFile("Api/NoSuchEndpoints.cs", Scope.RouteLevelGate, "seeded by the negative control")).ToList();
        Assert.Contains(CensusViolations(EndpointFiles(), withMissing), v => v.StartsWith("Api/NoSuchEndpoints.cs", StringComparison.Ordinal));

        // Negative (task 167 r1 — the census ITSELF, not a name appended to its output): a TEMPORARY scan root holding
        // a MapMethods-only endpoint file, a file with no route, a commented-out route and an obj/ file is read by the
        // same LoadUnits + CensusOf the real census uses. Exactly the seeded endpoint file is selected.
        const string seeded = """
            public static class SeededExtraEndpoints
            {
                public static void MapSeeded(this IEndpointRouteBuilder app)
                {
                    app.MapMethods("/api/zzseed/{id}", ["PATCH"], (string id) => Results.Ok()).RequireAuthorization();
                }
            }
            """;
        var root = Path.Combine(Path.GetTempPath(), "uac167-census-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Api"));
            Directory.CreateDirectory(Path.Combine(root, "obj", "Debug"));
            File.WriteAllText(Path.Combine(root, "Api", "SeededExtraEndpoints.cs"), seeded);
            File.WriteAllText(Path.Combine(root, "Api", "Plain.cs"), "public class Plain { }");
            File.WriteAllText(Path.Combine(root, "Api", "Commented.cs"), "// app.MapGet(\"/x\", H);\npublic class C { string s = \".MapPost(\"; }");
            File.WriteAllText(Path.Combine(root, "obj", "Debug", "Generated.cs"), "app.MapGet(\"/generated\", H);");

            var tempUnits = LoadUnits(root);
            Assert.Equal(new[] { "Api/SeededExtraEndpoints.cs" }, CensusOf(tempUnits));

            // The seeded file joined to the REAL units: the census count moves off the pin and the set check names it.
            var census = CensusOf(Real.Set.Units.Concat(tempUnits));
            Assert.Equal(ExpectedEndpointFileCount + 1, census.Count);
            Assert.Contains(CensusViolations(census, GovernedFiles), v => v.StartsWith("Api/SeededExtraEndpoints.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Task 167: no two registrations produce the same route key")]
    public void NoTwoRegistrationsProduceTheSameKey()
    {
        var duplicates = DuplicateKeys(Real.Live);
        Assert.True(duplicates.Count == 0,
            "Two registrations produce the same route key. A waiver or ledger entry written for one would silently "
            + "cover the other, and NoWaiverIsStaleAndEveryWaiverIsWellFormed would compare the wrong routes.\n  "
            + string.Join("\n  ", duplicates));
    }

    private static List<string> DuplicateKeys(IEnumerable<RouteRegistration> routes)
        => routes.GroupBy(r => r.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: " + string.Join(" AND ", g.Select(r => $"{r.File}:{r.Line}")))
            .ToList();

    [Fact(DisplayName = "Task 167: the dead ProjectClosureEndpoint registration is NotMapped and has no caller")]
    public void TheDeadClosureRegistrationIsNotMapped()
    {
        // ProjectClosureEndpoint.MapProjectClosureEndpoint registers POST /close-project on a group parameter, but
        // nothing calls it — the live route is mapped inline in ExternalAccessEndpoints.cs. If someone maps it, the
        // route would arrive WITHOUT the DelegationRuleFilter the inline one inherits; this fails first.
        var method = Real.Set.Get("Api/ExternalAccess/ProjectClosureEndpoint.cs")!.Methods
            .Single(m => m.Name == "MapProjectClosureEndpoint");
        Assert.Empty(Real.Set.CallSitesOf(method));
        Assert.All(ScanFile("Api/ExternalAccess/ProjectClosureEndpoint.cs"), r => Assert.True(r.Unbound));
        Assert.Equal(Scope.NotMapped, GovernedFiles.Single(g => g.RelativePath == "Api/ExternalAccess/ProjectClosureEndpoint.cs").Scope);
        Assert.Single(Real.Live, r => r.Key == "POST /api/v1/external-access/close-project");
    }

    [Fact(DisplayName = "Task 091/167 Rule E: a route on an aggregator-bound group declares no absolute path")]
    public void GroupGatedFilesRegisterNoAbsolutePaths()
    {
        var offenders = RuleEViolations(Real.Live);
        Assert.True(
            offenders.Count == 0,
            "These routes are registered on a group an AGGREGATOR binds, yet declare an absolute \"/api/...\" path. Make "
            + "them group-relative. Task 091's nine routes had this exact shape and were registered on the root app for "
            + "it — they answered on /api/spe URLs while inheriting neither the admin filter nor the tenant scope.\n\n  "
            + string.Join("\n  ", offenders));

        Assert.Contains(Real.Live, r => r.Aggregator is not null);   // non-vacuous
    }

    private static List<string> RuleEViolations(IEnumerable<RouteRegistration> routes)
        => routes.Where(r => r.Aggregator is not null && r.RouteLiteral.StartsWith("/api/", StringComparison.Ordinal))
            .Select(r => $"{r.File}:{r.Line}: \"{r.RouteLiteral}\" (bound by {r.Aggregator})")
            .ToList();

    [Fact(DisplayName = "Task 074/167: the scanner reads every governed file and finds every registration in it")]
    public void ScannerAccountsForEveryRegistrationInTheGovernedFiles()
    {
        // The vacuous-pass guard: an independent count of registration call sites per file must equal what the
        // scanner returns, so a parser regression shows up as a mismatch rather than a green run. MapMethods
        // registers one route per verb; every array in this codebase names one verb.
        var mismatches = new List<string>();
        foreach (var file in GovernedFiles)
        {
            var code = Real.Set.Get(file.RelativePath)!.Code;
            var expected = Regex.Matches(code, @"\.\s*Map(?:Get|Post|Put|Patch|Delete|Methods|HealthChecks)\s*\(").Count;
            var actual = ScanFile(file.RelativePath).Select(r => (r.Line, r.RouteLiteral)).Distinct().Count();
            if (expected != actual)
            {
                mismatches.Add($"{file.RelativePath}: an independent count found {expected} registrations, the scanner {actual}");
            }
        }

        Assert.True(mismatches.Count == 0, "The scanner did not account for every registration:\n" + string.Join("\n", mismatches));

        // 3 → 1 → 3 → 4 → 3 history in task 074; still 3 (two record-keyed uploads + the record-less one).
        Assert.True(ScanFile("Api/OBOEndpoints.cs").Count == 3,
            "Expected 3 registrations in OBOEndpoints.cs. A RISE means a new upload route needs its decision stated — a "
            + "route that writes bytes to a CALLER-NAMED destination needs a per-resource decision, not a waiver.");
    }

    [Fact(DisplayName = "Task 167: the scanner resolves MapMethods, MapHealthChecks, const paths and aggregator bindings to full keys")]
    public void ScannerResolvesEveryRegistrationFormToAFullKey()
    {
        var keys = Real.Live.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[]
                 {
                     "PATCH /api/ai/chat/sessions/{sessionId}/context",          // MapMethods ["PATCH"]
                     "PATCH /api/ai/chat/sessions/{sessionId}",
                     "PATCH /api/ai/chat/sessions/{sessionId}/tabs",
                     "GET /healthz",                                              // MapHealthChecks
                     "GET /healthz/catalog",
                     "GET /api/diagnostics/tenant-container-resolver",            // const path
                     "POST /api/onboarding/consent-callback",
                     "GET /api/spe/configs/{id:guid}",                            // aggregator, extension form + nested group
                     "GET /api/spe/containers/{containerId}",                     // aggregator, static form
                     "POST /api/compose/upload",
                     "GET /api/v1/external/projects/{id:guid}",
                     "POST /api/v1/external-access/grant",
                     "POST /api/v1/external/api/dataverse/fetch",                 // nested group under the aggregator group
                     "GET /api/v1/external/api/dataverse/record/{entityLogicalName}/{id:guid}",
                     "GET /api/v1/external/api/dataverse/metadata/{entityLogicalName}",
                     "GET /api/v1/external/api/dataverse/savedquery/{savedQueryId:guid}",
                     "GET /api/v1/external/api/dataverse/savedqueries/{entityLogicalName}",
                     "POST /api/compose/webhooks/spe-doc-changed",               // the ROOT builder parameter, no doubled prefix
                 })
        {
            Assert.True(keys.Contains(key), $"Expected the scanner to resolve '{key}'");
        }

        var webhook = Real.Live.Single(r => r.Key == "POST /api/compose/webhooks/spe-doc-changed");
        Assert.Null(webhook.Aggregator);   // registered on `routes`, the root — not on the bound group
        Assert.NotNull(Real.Live.Single(r => r.Key == "POST /api/compose/upload").Aggregator);
        Assert.DoesNotContain("POST /api/dataverse/fetch", Real.Live.Where(r => r.File.Contains("ExternalAccess")).Select(r => r.Key));
    }

    // =============================================================================================
    // THE THREE SHAPES THAT WOULD FAIL OPEN — refused outright (task 167 r1 findings 3 and 4; r2 finding 2)
    // ---------------------------------------------------------------------------------------------
    // The scanner reads fluent chains of Map{Verb} / MapMethods / MapHealthChecks / MapGroup. Three things could
    // put an anonymous or unread route surface beside it without it noticing, and all three fail OPEN, so all
    // three are refused rather than stated as residuals:
    //   - Anonymity carried by an ATTRIBUTE ([AllowAnonymous] on a lambda or a handler method,
    //     WithMetadata(new AllowAnonymousAttribute()), an IAllowAnonymous implementation) or by an .AllowAnonymous()
    //     call hidden in a wrapper extension. Such a route would scan as signed-in and could carry any Permanent
    //     basis, escaping the AnonymousByDesign rule. (AnonymityIsDeclaredOnlyOnAScannedChain)
    //   - Anonymity by OMISSION (r2): a route with neither .RequireAuthorization(...) nor .AllowAnonymous() on its
    //     route or group chain. Until owner round 14 the BFF set no FallbackPolicy, so such a route was callable
    //     WITHOUT SIGNING IN — yet it carried no AllowAnonymous, so it scanned as signed-in and passed under a
    //     ReferenceData waiver with no mandatory control (the r2 verifier seeded exactly that, GET /api/zzref, and
    //     the guard stayed green). (NoRouteIsAnonymousByOmission — no waiver exempts it.) Since f1 the runtime is
    //     closed too (AuthorizationModule.ApplyFallbackPolicy: an authenticated user; pinned by
    //     TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser), and the build rule is KEPT (owner round 14
    //     item 2): it keeps every route's intent declared, and a RequireAuthorization counts only when its policy is
    //     PROVEN to require an authenticated user (SignInVerdict) — a permissive named policy defeats the fallback,
    //     which never applies to an endpoint that declares authorization.
    //   - A registration API outside the vocabulary: .Map(...) (all verbs), MapFallback*, MapHub<T>,
    //     MapControllers and the like, or any Map* call that is not a method declared under src/server/**.
    //     (NoRouteIsRegisteredInAFormTheScannerCannotRead)
    // Today every anonymity in the BFF is an .AllowAnonymous() call on a chain the scanner reads, every other live
    // route carries .RequireAuthorization(...) on its route or group chain, and no unread registration form exists.
    // MAINTENANCE: if one of these is genuinely needed, teach the scanner the shape (and its census) first, with a
    // control — do not add an exemption list.
    // =============================================================================================

    /// <summary>Any identifier containing "AllowAnonymous", in code (comments and literals blanked).</summary>
    private static readonly Regex AnonymityIdentifier = new(@"(?<![\w])\w*AllowAnonymous\w*", RegexOptions.Compiled);

    private static List<string> AnonymityViolations(IEnumerable<SourceUnit> units, IEnumerable<RouteRegistration> routes)
    {
        var scanned = routes.SelectMany(r => r.Chain)
            .Where(c => c.Name == "AllowAnonymous")
            .Select(c => (c.File, c.Line))
            .ToHashSet();
        var violations = new List<string>();

        foreach (var unit in units)
        {
            var code = unit.Code;
            foreach (Match m in AnonymityIdentifier.Matches(code))
            {
                var at = $"{unit.Path}:{unit.LineOf(m.Index)}";
                if (m.Value != "AllowAnonymous")
                {
                    violations.Add($"{at}: '{m.Value}' — anonymity carried by metadata or a type is invisible to the scanner");
                    continue;
                }

                var p = m.Index - 1;
                while (p >= 0 && char.IsWhiteSpace(code[p]))
                {
                    p--;
                }

                var next = SkipWs(code, m.Index + m.Length);
                var fluentCall = p >= 0 && code[p] == '.' && next < code.Length && code[next] == '(';
                if (!fluentCall)
                {
                    violations.Add($"{at}: AllowAnonymous used as an ATTRIBUTE (or other non-call) — the route would scan as "
                                   + "signed-in and escape the AnonymousByDesign rule");
                }
                else if (!scanned.Contains((unit.Path, unit.LineOf(m.Index))))
                {
                    violations.Add($"{at}: an .AllowAnonymous() call on no route chain the scanner reads (a wrapper "
                                   + "extension?) — every route it reaches would scan as signed-in");
                }
            }
        }

        return violations;
    }

    /// <summary>A member call whose name starts with "Map" (generic arguments allowed).</summary>
    private static readonly Regex AnyMapCall = new(@"\.\s*(?<name>Map[A-Za-z0-9_]*)\s*(?:<[^;(){}]*>)?\s*\(", RegexOptions.Compiled);

    /// <summary>The registration vocabulary the scanner reads (plus MapGroup, which it follows).</summary>
    private static readonly IReadOnlySet<string> ReadRegistrationVocabulary = new HashSet<string>(StringComparer.Ordinal)
    {
        "MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods", "MapHealthChecks", "MapGroup",
    };

    /// <summary>ASP.NET Core registration APIs the scanner does not read — refused even if something in src/server
    /// happens to declare a method of the same name.</summary>
    private static readonly IReadOnlySet<string> UnreadFrameworkRegistrations = new HashSet<string>(StringComparer.Ordinal)
    {
        "Map", "MapFallback", "MapFallbackToFile", "MapFallbackToPage", "MapFallbackToController",
        "MapFallbackToAreaController", "MapHub", "MapControllers", "MapControllerRoute", "MapDefaultControllerRoute",
        "MapAreaControllerRoute", "MapDynamicControllerRoute", "MapDynamicPageRoute", "MapRazorPages",
        "MapRazorComponents", "MapBlazorHub", "MapGrpcService", "MapConnectionHandler", "MapConnections",
        "MapIdentityApi", "MapOpenApi", "MapSwagger", "MapStaticAssets", "MapWhen",
    };

    private static List<string> UnreadRegistrationViolations(IEnumerable<SourceUnit> units, IReadOnlySet<string> declaredMethodNames)
    {
        var violations = new List<string>();
        foreach (var unit in units)
        {
            foreach (Match m in AnyMapCall.Matches(unit.Code))
            {
                var name = m.Groups["name"].Value;
                if (ReadRegistrationVocabulary.Contains(name))
                {
                    continue;
                }

                if (UnreadFrameworkRegistrations.Contains(name) || !declaredMethodNames.Contains(name))
                {
                    violations.Add($"{unit.Path}:{unit.LineOf(m.Index)}: .{name}(...) — a route registration form the scanner "
                                   + "does not read (or an undeclared Map* call); its routes would be invisible to the census "
                                   + "and to Rule A");
                }
            }
        }

        return violations;
    }

    /// <summary>The BFF's own units plus src/server/shared/** (a wrapper there would reach BFF builders). The other
    /// server apps (provisioning control plane) have their own routes and are not the BFF.</summary>
    private static IEnumerable<SourceUnit> BffAndSharedUnits()
        => Real.Set.Units.Concat(ServerUnits.Value.Where(u => u.Path.StartsWith("src/server/shared/", StringComparison.Ordinal)));

    private static readonly Lazy<IReadOnlySet<string>> DeclaredServerMethodNames = new(
        () => ServerUnits.Value.SelectMany(u => u.Methods).Select(m => m.Name).ToHashSet(StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact(DisplayName = "Task 167 r1: anonymity is declared only by an .AllowAnonymous() call on a chain the scanner reads")]
    public void AnonymityIsDeclaredOnlyOnAScannedChain()
    {
        var violations = AnonymityViolations(BffAndSharedUnits(), Real.Routes);
        Assert.True(
            violations.Count == 0,
            "An anonymity the scanner cannot see fails OPEN: the route scans as signed-in, escapes the AnonymousByDesign "
            + "rule, and can carry any Permanent basis. Write .AllowAnonymous() on the registration (or its MapGroup) chain, "
            + "or teach the scanner the new shape with a control.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the anonymous routes ARE seen — exactly the pinned explicit surface (16 today).
        Assert.Equal(ExplicitlyAnonymousRoutes.Count, Real.Live.Count(r => r.Anonymous));
    }

    [Fact(DisplayName = "Task 167 r1: no route is registered in a form the scanner and the census cannot read")]
    public void NoRouteIsRegisteredInAFormTheScannerCannotRead()
    {
        var violations = UnreadRegistrationViolations(BffAndSharedUnits(), DeclaredServerMethodNames.Value);
        Assert.True(
            violations.Count == 0,
            "These calls register routes (or may) in a form outside the scanner's vocabulary (Map{Verb}, MapMethods, "
            + "MapHealthChecks, MapGroup). Their routes would be invisible to the census and to Rule A. Teach the scanner "
            + "the form (RouteAuthorizationGuardTests.Scanner.cs) and the census vocabulary first, with a control.\n\n  "
            + string.Join("\n  ", violations));
    }

    // =============================================================================================
    // SIGN-IN IS VERIFIED, NOT ASSUMED (task 167 f1 — the r2 verifier's items 6 and 2)
    // ---------------------------------------------------------------------------------------------
    // Until f1, ANY .RequireAuthorization(...) on the chain counted as "this route requires sign-in". That rested
    // on census review: a reviewer who classified a new, PERMISSIVE policy (one with no RequireAuthenticatedUser)
    // as NotAuthorization would have made a public route count as signed-in. The FallbackPolicy cannot catch that
    // either — ASP.NET Core applies the fallback only to an endpoint that declares NO authorization metadata.
    //
    // Now a RequireAuthorization form counts as sign-in only when the guard PROVES, from source, that its policy
    // requires an authenticated user:
    //   - bare .RequireAuthorization()        — the DEFAULT policy: the framework's (an authenticated user) when no
    //                                           DefaultPolicy override exists; otherwise every override must call
    //                                           RequireAuthenticatedUser();
    //   - .RequireAuthorization("X")          — "X" (a string literal, or an AuthPolicies constant resolved from
    //     / (AuthPolicies.X)                   Infrastructure/Authentication/AuthPolicies.cs) must be registered
    //                                           EXACTLY ONCE by an AddPolicy(...) call, and that registration must call
    //                                           RequireAuthenticatedUser() UNCONDITIONALLY. Requirements AND together,
    //                                           so one such policy among several arguments suffices; every argument
    //                                           must still resolve.
    //   - anything else — an inline lambda, a policy object, an unresolvable name — is NOT sign-in, whatever the
    //     census says about the form.
    //
    // Task 167 f2 (the f1 verifier's items 5 and 6) closed three fail-opens in that proof:
    //   - "calls RequireAuthenticatedUser()" was a TEXT match anywhere in the AddPolicy arguments, so
    //     `p => { if (false) p.RequireAuthenticatedUser(); p.RequireAssertion(_ => true); }` proved a permissive
    //     SystemAdmin. Now the call must be in the unbroken opening run of the builder lambda (or be the expression
    //     body's chain) — PolicyBuilderRequiresAuthenticatedUser;
    //   - "registered exactly once" ignored a registration whose NAME the guard could not resolve, so
    //     `AddPolicy(AdminAlias, permissive)` with `const string AdminAlias = "SystemAdmin"` silently replaced
    //     SystemAdmin. Now same-file and Type.X const strings resolve (the alias is then a second registration), and
    //     any name still unresolvable fails EVERY named verdict closed (PolicyCatalog.Unresolved);
    //   - only AddPolicy calls INSIDE AddAuthorization(...) were catalogued, so an AddAuthorizationBuilder().AddPolicy
    //     or Configure<AuthorizationOptions> re-registration was invisible. Now every AddPolicy outside the rate
    //     limiter's and CORS's own registration calls is catalogued.
    // The runtime half is the FallbackPolicy (AuthorizationModule.ApplyFallbackPolicy, owner round 14 item 2),
    // pinned by TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser and proven over HTTP by
    // tests/integration/auth/UnifiedAccessControl/AuthorizationFallbackPolicyTests.cs.
    // =============================================================================================

    private sealed record PolicyRegistration(string Name, string File, int Line, bool RequiresAuthenticatedUser);

    /// <summary>The authorization policies the BFF registers, read from source. <see cref="Unresolved"/> holds every
    /// <c>AddPolicy</c> registration whose NAME the guard cannot resolve (task 167 f2): such a registration could be an
    /// alias that re-registers — and, the last one winning, replaces — a policy the guard believes is registered once.</summary>
    private sealed record PolicyCatalog(
        IReadOnlyDictionary<string, List<PolicyRegistration>> Named,
        IReadOnlyList<PolicyRegistration> DefaultOverrides,
        IReadOnlyList<PolicyRegistration> FallbackAssignments,
        IReadOnlyDictionary<string, string> AuthPolicyConstants,
        IReadOnlyList<PolicyRegistration> Unresolved);

    private static readonly Regex AddAuthorizationCall = new(@"(?<![\w.])(?:\w+\s*\.\s*)?AddAuthorization\s*\(", RegexOptions.Compiled);

    private static readonly Regex AddPolicyCall = new(@"\.\s*AddPolicy\s*\(", RegexOptions.Compiled);

    /// <summary>Registration calls whose own <c>AddPolicy</c> is NOT an authorization policy: the rate limiter's and
    /// CORS's. Every other <c>.AddPolicy(</c> in BFF + shared code is catalogued as an authorization registration —
    /// inside <c>AddAuthorization(...)</c> or not: an <c>AddAuthorizationBuilder().AddPolicy(...)</c> or a
    /// <c>Configure&lt;AuthorizationOptions&gt;</c> re-registration replaces a policy just the same (task 167 f2).</summary>
    private static readonly Regex NonAuthorizationPolicyHost = new(@"(?<![\w])(?:AddRateLimiter|AddCors)\s*\(", RegexOptions.Compiled);

    /// <summary><c>DefaultPolicy = …</c> / <c>FallbackPolicy = …</c> (an assignment, not <c>==</c> or <c>=&gt;</c>), and the
    /// AuthorizationBuilder forms <c>SetDefaultPolicy(…)</c> / <c>SetFallbackPolicy(…)</c>.</summary>
    private static readonly Regex PolicySlotAssignment = new(
        @"(?<![\w])(?:(?<which>Default|Fallback)Policy\s*=(?![=>])|Set(?<setwhich>Default|Fallback)Policy\s*\()",
        RegexOptions.Compiled);

    private static readonly Regex AuthPolicyConstant = new("const\\s+string\\s+(?<name>\\w+)\\s*=\\s*\"(?<value>[^\"]*)\"\\s*;", RegexOptions.Compiled);

    /// <summary>A <c>const string NAME = "value";</c> anywhere in the units — the index registration names resolve
    /// through (task 167 f2).</summary>
    private sealed record StringConstant(string Type, string Name, string Value, SourceUnit Unit);

    private static PolicyCatalog PolicyCatalogOf(IEnumerable<SourceUnit> units)
    {
        var list = units.ToList();

        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        var allConstants = new List<StringConstant>();
        foreach (var unit in list)
        {
            foreach (var type in unit.Types.Where(t => t.Name == "AuthPolicies"))
            {
                foreach (Match c in AuthPolicyConstant.Matches(unit.Text[type.BodyStart..type.BodyEnd]))
                {
                    constants[c.Groups["name"].Value] = c.Groups["value"].Value;
                }
            }

            foreach (Match c in AuthPolicyConstant.Matches(unit.Text))
            {
                allConstants.Add(new StringConstant(unit.TypeAt(c.Index)?.Name ?? string.Empty, c.Groups["name"].Value,
                    c.Groups["value"].Value, unit));
            }
        }

        var named = new Dictionary<string, List<PolicyRegistration>>(StringComparer.Ordinal);
        var unresolved = new List<PolicyRegistration>();
        var defaults = new List<PolicyRegistration>();
        var fallbacks = new List<PolicyRegistration>();

        foreach (var unit in list)
        {
            var code = unit.Code;

            var excludedSpans = new List<(int Open, int Close)>();
            foreach (Match host in NonAuthorizationPolicyHost.Matches(code))
            {
                var open = host.Index + host.Length - 1;
                var close = MatchClose(code, open);
                if (close > open)
                {
                    excludedSpans.Add((open, close));
                }
            }

            foreach (Match p in AddPolicyCall.Matches(code))
            {
                if (excludedSpans.Any(s => s.Open < p.Index && p.Index < s.Close))
                {
                    continue;   // e.g. the rate limiter's options.AddPolicy("anonymous", ...) — not an authorization policy
                }

                var open = p.Index + p.Length - 1;
                var close = MatchClose(code, open);
                var args = close < 0 ? new List<(int Start, int End)>() : SplitTopLevel(code, open + 1, close);
                var line = unit.LineOf(p.Index);
                if (args.Count < 2)
                {
                    unresolved.Add(new PolicyRegistration($"<unreadable AddPolicy at {unit.Path}:{line}>", unit.Path, line, false));
                    continue;
                }

                var nameText = unit.Text[args[0].Start..args[0].End];
                var requires = PolicyBuilderRequiresAuthenticatedUser(unit.Text[args[1].Start..args[1].End]);
                var name = ResolveRegistrationName(nameText, unit, constants, allConstants);
                if (name is null)
                {
                    unresolved.Add(new PolicyRegistration($"<unresolved {Squash(nameText)}>", unit.Path, line, requires));
                    continue;
                }

                if (!named.TryGetValue(name, out var registrations))
                {
                    named[name] = registrations = new List<PolicyRegistration>();
                }

                registrations.Add(new PolicyRegistration(name, unit.Path, line, requires));
            }

            foreach (Match s in PolicySlotAssignment.Matches(code))
            {
                int end;
                if (s.Groups["setwhich"].Success)
                {
                    end = MatchClose(code, s.Index + s.Length - 1);
                }
                else
                {
                    end = AssignmentEnd(code, s.Index + s.Length);
                }

                var rhs = end < 0 ? string.Empty : unit.Text[(s.Index + s.Length)..end];
                var which = s.Groups["which"].Success ? s.Groups["which"].Value : s.Groups["setwhich"].Value;
                var registration = new PolicyRegistration(which, unit.Path, unit.LineOf(s.Index), BuiltPolicyRequiresAuthenticatedUser(rhs));
                (which == "Default" ? defaults : fallbacks).Add(registration);
            }
        }

        return new PolicyCatalog(named, defaults, fallbacks, constants, unresolved);
    }

    /// <summary>
    /// The NAME an <c>AddPolicy(name, …)</c> registers: a string literal, an <c>AuthPolicies.X</c> constant, a bare
    /// <c>const string</c> declared in the same file, or <c>Type.X</c> naming a <c>const string</c> of a type declared in
    /// BFF + shared code. Null for anything else — a computed name, <c>nameof</c>, an interpolation, a variable, an
    /// ambiguous constant. Null is NOT "some other policy": the caller records it as unresolved, and every NAMED sign-in
    /// verdict then fails closed (task 167 f2, the f1 verifier's item 6 — the aliased re-registration).
    /// </summary>
    private static string? ResolveRegistrationName(string expression, SourceUnit unit,
        IReadOnlyDictionary<string, string> authPolicies, IReadOnlyList<StringConstant> constants)
    {
        var direct = ResolvePolicyName(expression, authPolicies);
        if (direct is not null)
        {
            return direct;
        }

        var e = expression.Trim();
        var bare = Regex.Match(e, @"^(?<name>[A-Za-z_]\w*)$");
        if (bare.Success)
        {
            var values = constants.Where(c => ReferenceEquals(c.Unit, unit) && c.Name == bare.Groups["name"].Value)
                .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
            return values.Count == 1 ? values[0] : null;
        }

        var qualified = Regex.Match(e, @"^(?:(?:global::)?[A-Za-z_][\w.]*\.)?(?<type>[A-Za-z_]\w*)\.(?<name>[A-Za-z_]\w*)$");
        if (qualified.Success)
        {
            var values = constants.Where(c => c.Type == qualified.Groups["type"].Value && c.Name == qualified.Groups["name"].Value)
                .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
            return values.Count == 1 ? values[0] : null;
        }

        return null;
    }

    /// <summary>
    /// True only when the policy-builder lambda passed to <c>AddPolicy(name, p =&gt; …)</c> calls
    /// <c>p.RequireAuthenticatedUser()</c> UNCONDITIONALLY (task 167 f2, the f1 verifier's item 5):
    /// <list type="bullet">
    ///   <item>expression body — a call chain on the parameter (<c>p =&gt; p.RequireRole("x").RequireAuthenticatedUser()</c>)
    ///   that IS the whole body and contains the call; or</item>
    ///   <item>block body — the call is in a chain statement of the unbroken run of top-level statements that opens the
    ///   block, each of them <c>p.Chain(…);</c> or <c>p.Member = …;</c>. An <c>if</c>, a loop, a <c>return</c>, a lambda or
    ///   any other statement before it ends the run, so <c>if (x) p.RequireAuthenticatedUser();</c> — or the call after an
    ///   early return — proves nothing.</item>
    /// </list>
    /// A body that touches its requirement list other than by <c>.Requirements.Add(…)</c> (a <c>Clear</c>, a
    /// <c>Remove</c>) proves nothing either: it could undo the call. A non-lambda second argument (a policy object, a
    /// method group) is not read. Before f2 the TEXT <c>RequireAuthenticatedUser()</c> anywhere in the arguments counted,
    /// so a dead or conditional call made a permissive SystemAdmin "sign-in" and the full ArchTests stayed green.
    /// </summary>
    private static bool PolicyBuilderRequiresAuthenticatedUser(string argumentText)
    {
        var unit = new SourceUnit("policy-lambda", argumentText);
        var code = unit.Code;
        var head = Regex.Match(code, @"^\s*(?:static\s+)?(?:\(\s*(?:[A-Za-z_][\w.<>?]*\s+)?(?<p>[A-Za-z_]\w*)\s*\)|(?<p>[A-Za-z_]\w*))\s*=>");
        if (!head.Success || Regex.IsMatch(code, @"\.\s*Requirements\b(?!\s*\.\s*Add\s*\()"))
        {
            return false;
        }

        var parameter = head.Groups["p"].Value;
        bool StartsWithParameter(int at)
            => at + parameter.Length <= code.Length
               && string.CompareOrdinal(code, at, parameter, 0, parameter.Length) == 0
               && (at + parameter.Length == code.Length || !IsIdentChar(code[at + parameter.Length]));

        var i = SkipWs(code, head.Index + head.Length);
        if (i < code.Length && code[i] == '{')
        {
            var close = MatchClose(code, i);
            if (close < 0)
            {
                return false;
            }

            var s = i + 1;
            while (true)
            {
                s = SkipWs(code, s);
                if (s >= close || !StartsWithParameter(s))
                {
                    return false;
                }

                var dot = SkipWs(code, s + parameter.Length);
                if (dot >= close || code[dot] != '.')
                {
                    return false;
                }

                var assignment = Regex.Match(code[dot..close], @"^\.\s*[A-Za-z_]\w*\s*=(?![=>])");
                if (assignment.Success)
                {
                    var end = StatementEnd(code, dot + assignment.Length);
                    if (end < 0 || end > close)
                    {
                        return false;
                    }

                    s = end + 1;
                    continue;
                }

                // p.Requirements.Add(...); — adds a requirement; it cannot skip the statements after it.
                var add = Regex.Match(code[dot..close], @"^\.\s*Requirements\s*\.\s*Add\s*\(");
                if (add.Success)
                {
                    var addClose = MatchClose(code, dot + add.Length - 1);
                    var semicolon = addClose < 0 ? -1 : SkipWs(code, addClose + 1);
                    if (semicolon < 0 || semicolon >= close || code[semicolon] != ';')
                    {
                        return false;
                    }

                    s = semicolon + 1;
                    continue;
                }

                var (calls, chainEnd, problem) = ParseChain(unit, dot);
                if (problem is not null || chainEnd >= close || code[chainEnd] != ';')
                {
                    return false;
                }

                if (calls.Any(c => c.Name == "RequireAuthenticatedUser" && c.Args.Trim().Length == 0))
                {
                    return true;
                }

                s = chainEnd + 1;
            }
        }

        if (!StartsWithParameter(i))
        {
            return false;
        }

        var (exprCalls, exprEnd, exprProblem) = ParseChain(unit, SkipWs(code, i + parameter.Length));
        return exprProblem is null
               && SkipWs(code, exprEnd) == code.Length
               && exprCalls.Any(c => c.Name == "RequireAuthenticatedUser" && c.Args.Trim().Length == 0);
    }

    /// <summary>
    /// True only when the right-hand side of a <c>DefaultPolicy</c> / <c>FallbackPolicy</c> slot is ONE builder chain —
    /// <c>new AuthorizationPolicyBuilder(…)</c> followed by calls only, ending in <c>.Build()</c> — that calls
    /// <c>RequireAuthenticatedUser()</c> and does not <c>Combine</c> another policy in. A conditional
    /// (<c>c ? permissive : strict</c>), a variable or a policy passed through proves nothing (task 167 f2; before, the
    /// text anywhere on the right-hand side counted).
    /// </summary>
    private static bool BuiltPolicyRequiresAuthenticatedUser(string rhs)
    {
        var unit = new SourceUnit("policy-rhs", rhs);
        var code = unit.Code;
        var builder = Regex.Match(code, @"^\s*new\s+(?:(?:global::)?[A-Za-z_][\w.]*\.)?AuthorizationPolicyBuilder\s*\(");
        if (!builder.Success)
        {
            return false;
        }

        var close = MatchClose(code, builder.Index + builder.Length - 1);
        if (close < 0)
        {
            return false;
        }

        var (calls, end, problem) = ParseChain(unit, close + 1);
        return problem is null
               && SkipWs(code, end) == code.Length
               && calls.Count > 0
               && calls[^1].Name == "Build"
               && calls.Any(c => c.Name == "RequireAuthenticatedUser" && c.Args.Trim().Length == 0)
               && !calls.Any(c => c.Name == "Combine");
    }

    /// <summary>A string literal, or an <c>AuthPolicies.X</c> constant (optionally namespace-qualified); else null.</summary>
    private static string? ResolvePolicyName(string expression, IReadOnlyDictionary<string, string> constants)
    {
        var e = expression.Trim();
        var literal = Regex.Match(e, "^\"([^\"\\\\]*)\"$");
        if (literal.Success)
        {
            return literal.Groups[1].Value;
        }

        var constant = Regex.Match(e, @"^(?:[A-Za-z_][\w.]*\.)?AuthPolicies\.(?<name>[A-Za-z_]\w*)$");
        return constant.Success && constants.TryGetValue(constant.Groups["name"].Value, out var value) ? value : null;
    }

    /// <summary>Whether one <c>.RequireAuthorization(...)</c> call provably requires an authenticated user, and why not.</summary>
    private static (bool Verified, string Why) SignInVerdict(ChainCall call, PolicyCatalog catalog)
    {
        var args = SplitTopLevel(call.Args, 0, call.Args.Length).Select(s => call.Args[s.Start..s.End].Trim()).ToList();
        if (args.Count == 0)
        {
            var permissive = catalog.DefaultOverrides.Where(d => !d.RequiresAuthenticatedUser).ToList();
            return permissive.Count == 0
                ? (true, "the default policy (an authenticated user)")
                : (false, "bare RequireAuthorization() uses the DefaultPolicy, and the override at "
                          + string.Join(", ", permissive.Select(d => $"{d.File}:{d.Line}")) + " does not call RequireAuthenticatedUser()");
        }

        // A registration whose name the guard cannot resolve may be an alias of ANY named policy — and the last
        // AddPolicy wins at runtime — so no named policy is proven registered once while one exists (task 167 f2).
        if (catalog.Unresolved.Count > 0)
        {
            return (false, "an AddPolicy registration has a name the guard cannot resolve ("
                           + string.Join(", ", catalog.Unresolved.Select(u => $"{u.Name} at {u.File}:{u.Line}"))
                           + ") — it could re-register (and replace) this policy, so no named policy is proven. Name every "
                           + "authorization policy with a string literal or a const string the guard resolves");
        }

        var anyRequires = false;
        foreach (var arg in args)
        {
            var name = ResolvePolicyName(arg, catalog.AuthPolicyConstants);
            if (name is null)
            {
                return (false, $"'{Squash(arg)}' is not a policy NAME the guard can resolve (a string literal or an AuthPolicies "
                               + "constant) — an inline lambda or a policy object is never read as sign-in");
            }

            var registrations = catalog.Named.GetValueOrDefault(name) ?? new List<PolicyRegistration>();
            if (registrations.Count != 1)
            {
                return (false, registrations.Count == 0
                    ? $"no AddPolicy(\"{name}\", ...) registration inside AddAuthorization(...) was found"
                    : $"policy '{name}' is registered {registrations.Count} times ({string.Join(", ", registrations.Select(r => $"{r.File}:{r.Line}"))})");
            }

            anyRequires |= registrations[0].RequiresAuthenticatedUser;
        }

        return anyRequires
            ? (true, "a named policy that calls RequireAuthenticatedUser()")
            : (false, $"none of the policies {string.Join(", ", args)} calls RequireAuthenticatedUser() in its registration — a "
                      + "permissive policy is public, whatever list its form is classified in");
    }

    private static readonly Lazy<PolicyCatalog> RealPolicyCatalog = new(
        () => PolicyCatalogOf(BffAndSharedUnits()), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// True when the route's EFFECTIVE chain — its own fluent calls plus every enclosing group's, aggregator groups
    /// and UNCONDITIONAL group-continuation statements included — carries a <c>.RequireAuthorization(...)</c> whose
    /// policy the guard PROVES requires an authenticated user (<see cref="SignInVerdict"/>).
    /// </summary>
    private static bool RequiresSignIn(RouteRegistration route, PolicyCatalog? catalog = null)
    {
        catalog ??= RealPolicyCatalog.Value;
        return route.Chain.Any(c => c.Name == "RequireAuthorization" && SignInVerdict(c, catalog).Verified);
    }

    /// <summary>Live routes that are anonymous by OMISSION: no .AllowAnonymous() and no PROVEN sign-in requirement
    /// anywhere on the effective chain (task 167 r2, verifier finding 2; f1, verifier item 6).</summary>
    private static List<string> AnonymousByOmissionViolations(IEnumerable<RouteRegistration> routes, PolicyCatalog? catalog = null)
    {
        catalog ??= RealPolicyCatalog.Value;
        return routes
            .Where(r => !r.Anonymous && !RequiresSignIn(r, catalog))
            .Select(r =>
            {
                var unproven = r.Chain.Where(c => c.Name == "RequireAuthorization")
                    .Select(c => $"{c.Form}: {SignInVerdict(c, catalog).Why}")
                    .ToList();
                var detail = unproven.Count == 0
                    ? "neither .RequireAuthorization(...) nor .AllowAnonymous() on its route or group chain"
                    : "its .RequireAuthorization(...) is not shown to require an authenticated user ("
                      + string.Join("; ", unproven) + ")";
                return $"{r.Key}\n      at {r.File}:{r.Line} — {detail} [{string.Join(", ", r.Forms)}]: it is callable "
                       + "WITHOUT SIGNING IN";
            })
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "Task 167 r2/f1: no route is anonymous by omission — each carries a PROVEN sign-in requirement or a declared .AllowAnonymous()")]
    public void NoRouteIsAnonymousByOmission()
    {
        var violations = AnonymousByOmissionViolations(Real.Live);
        Assert.True(
            violations.Count == 0,
            "A route with neither a sign-in requirement nor .AllowAnonymous() on its route or group chain declares no "
            + "intent. At runtime the authorization FallbackPolicy now answers it 401 (owner round 14 item 2), but the "
            + "BUILD still refuses it: the intent must be written where a reviewer reads it, and a route that does not "
            + "scan as anonymous could otherwise pass under any Permanent basis with no AnonymousByDesign waiver and no "
            + "mandatory control. No waiver exempts this rule. Add .RequireAuthorization() — bare, or naming a policy whose "
            + "AddPolicy registration calls RequireAuthenticatedUser() — to the route or its MapGroup (an [Authorize] "
            + "attribute is not read — declare it on the chain); if the route is public BY DESIGN, write .AllowAnonymous() "
            + "on the chain and give it an AnonymousByDesign waiver naming its MANDATORY control.\n\n  "
            + string.Join("\n  ", violations));

        // Non-vacuous: the real code has routes the predicate WOULD catch were their anonymity not declared — the
        // explicitly anonymous probes carry no RequireAuthorization — and they are exempt only because they say so.
        Assert.Contains(Real.Live, r => r.Anonymous && !RequiresSignIn(r));
        Assert.Contains(Real.Live, r => !r.Anonymous && RequiresSignIn(r));
    }

    [Fact(DisplayName = "Task 167 f1: every RequireAuthorization form on a live route is PROVEN to require an authenticated user")]
    public void EveryRequireAuthorizationFormOnALiveRouteRequiresAnAuthenticatedUser()
    {
        var catalog = RealPolicyCatalog.Value;
        var forms = Real.Live.SelectMany(r => r.Chain).Where(c => c.Name == "RequireAuthorization")
            .GroupBy(c => c.Form, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var unproven = forms
            .Select(f => (f.Key, Verdict: SignInVerdict(f.Value, catalog)))
            .Where(f => !f.Verdict.Verified)
            .Select(f => $"{f.Key}: {f.Verdict.Why}")
            .ToList();
        Assert.True(
            unproven.Count == 0,
            "Each of these RequireAuthorization forms is used on a live route but is not PROVEN to require an "
            + "authenticated user. A permissive policy makes a route public even though it declares authorization, and "
            + "the FallbackPolicy does not reach an endpoint that declares any. Give the policy's AddPolicy registration "
            + "p.RequireAuthenticatedUser(), or name a policy that has it.\n\n  " + string.Join("\n  ", unproven));

        // Non-vacuous, and the four forms in use today, each proven from its own registration.
        Assert.Equal(
            new[]
            {
                "RequireAuthorization()",
                "RequireAuthorization(\"SystemAdmin\")",
                "RequireAuthorization(AuthPolicies.ExternalCollaboration)",
                "RequireAuthorization(AuthPolicies.RagApiKey)",
            }.OrderBy(k => k, StringComparer.Ordinal),
            forms.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Empty(catalog.DefaultOverrides);   // so bare RequireAuthorization() is the framework default: an authenticated user
        foreach (var name in new[] { "SystemAdmin", "RagApiKey", "ExternalCollaboration", "CiamExternal" })
        {
            var registration = Assert.Single(catalog.Named[name]);
            Assert.True(registration.RequiresAuthenticatedUser, $"{name} ({registration.File}:{registration.Line})");
            Assert.Equal("Infrastructure/DI/AuthorizationModule.cs", registration.File);
        }

        // The resource policies rely on ResourceAccessHandler failing without a user id; they are NOT proven sign-in, so
        // a future route naming one fails this rule (and the PolicyOnlyRoutes pin) until its registration says so.
        Assert.False(Assert.Single(catalog.Named["canpreviewfiles"]).RequiresAuthenticatedUser);
        Assert.DoesNotContain("anonymous", catalog.Named.Keys);   // the rate limiter's AddPolicy is not an authorization policy

        // Task 167 f2: every authorization registration's NAME resolves, so none can be an unseen alias that replaces
        // one of the policies above (the last AddPolicy for a name wins at runtime).
        Assert.True(catalog.Unresolved.Count == 0,
            "These AddPolicy registrations have a name the guard cannot resolve — each could re-register, and so replace, "
            + "a policy proven above:\n  " + string.Join("\n  ", catalog.Unresolved.Select(u => $"{u.Name} at {u.File}:{u.Line}")));
    }

    [Fact(DisplayName = "Task 167 f2 controls: a conditional, dead or undone RequireAuthenticatedUser and an aliased or re-registered policy are not sign-in")]
    public void SignInVerdict_NegativeControl_ConditionalCallsAndAliasesAreNotSignIn()
    {
        static PolicyCatalog Catalog(string registrations, string extra = "")
            => PolicyCatalogOf(new[] { new SourceUnit("Infrastructure/DI/AuthorizationModule.cs",
                "public static class AuthorizationModule\n{\n    private const string AdminAlias = \"SystemAdmin\";\n"
                + "    public static void Add(IServiceCollection services)\n    {\n"
                + "        services.AddAuthorization(options =>\n        {\n" + registrations + "\n        });\n" + extra
                + "    }\n}") });

        static bool SystemAdminIsSignIn(PolicyCatalog catalog)
            => AnonymousByOmissionViolations(ScanText("Api/Fake/Admin.cs",
                new[] { "        app.MapGet(\"/api/zz/admin\", Get).RequireAuthorization(\"SystemAdmin\");" }), catalog).Count == 0;

        const string proven = "            options.AddPolicy(\"SystemAdmin\", p => { p.RequireAuthenticatedUser(); p.RequireAssertion(_ => true); });";

        // NEGATIVE (the f1 verifier's item 5 seed and its relatives): the call is conditional, dead, nested in a lambda,
        // after an early return, after an unrelated statement, or undone by a requirement-list edit.
        foreach (var body in new[]
                 {
                     "p => { if (DateTime.UtcNow.Year < 0) p.RequireAuthenticatedUser(); p.RequireAssertion(_ => true); }",
                     "p => { if (flag) { p.RequireAuthenticatedUser(); } p.RequireAssertion(_ => true); }",
                     "p => { p.RequireAssertion(_ => true); return; p.RequireAuthenticatedUser(); }",
                     "p => { Action a = () => p.RequireAuthenticatedUser(); p.RequireAssertion(_ => true); }",
                     "p => { var x = 1; p.RequireAuthenticatedUser(); }",
                     "p => { p.RequireAuthenticatedUser(); p.Requirements.Clear(); }",
                     "p => p.RequireAssertion(ctx => { p.RequireAuthenticatedUser(); return true; })",
                     "p => flag ? p.RequireAuthenticatedUser() : p.RequireAssertion(_ => true)",
                     "policyObject",
                 })
        {
            var catalog = Catalog($"            options.AddPolicy(\"SystemAdmin\", {body});");
            Assert.False(Assert.Single(catalog.Named["SystemAdmin"]).RequiresAuthenticatedUser, body);
            Assert.False(SystemAdminIsSignIn(catalog), body);
        }

        // NEGATIVE (the f1 verifier's item 6 seed): an alias through a same-file const re-registers SystemAdmin — now a
        // second registration, so the name is ambiguous; through a name the guard cannot resolve at all — every named
        // verdict fails closed; and a re-registration OUTSIDE AddAuthorization(...) is seen too.
        var aliased = Catalog(proven + "\n            options.AddPolicy(AdminAlias, p => p.RequireAssertion(_ => true));");
        Assert.Equal(2, aliased.Named["SystemAdmin"].Count);
        Assert.False(SystemAdminIsSignIn(aliased));

        foreach (var name in new[] { "\"System\" + \"Admin\"", "nameof(SystemAdmin)", "$\"{prefix}Admin\"", "policyName", "Unknown.Name" })
        {
            var unresolvable = Catalog(proven + $"\n            options.AddPolicy({name}, p => p.RequireAssertion(_ => true));");
            Assert.Single(unresolvable.Unresolved);
            Assert.False(SystemAdminIsSignIn(unresolvable), name);
        }

        var outside = Catalog(proven,
            "        services.AddAuthorizationBuilder().AddPolicy(\"SystemAdmin\", p => p.RequireAssertion(_ => true));\n");
        Assert.Equal(2, outside.Named["SystemAdmin"].Count);
        Assert.False(SystemAdminIsSignIn(outside));

        // NEGATIVE: a DefaultPolicy override proven only by text — a conditional right-hand side — is not proven.
        var conditionalDefault = Catalog(proven
            + "\n            options.DefaultPolicy = flag ? new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build() "
            + ": new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();");
        Assert.False(Assert.Single(conditionalDefault.DefaultOverrides).RequiresAuthenticatedUser);
        Assert.Single(AnonymousByOmissionViolations(
            ScanText("Api/Fake/Default.cs", new[] { "        app.MapGet(\"/api/zz\", Get).RequireAuthorization();" }), conditionalDefault));

        // POSITIVE: the real shapes — first statement, after member assignments (the scheme-pinned policies), an
        // expression-bodied chain, a typed parameter, a static lambda, a Requirements.Add before it — and the alias
        // resolves to the SAME policy when it is the only registration.
        foreach (var body in new[]
                 {
                     "p => { p.RequireAuthenticatedUser(); p.RequireAssertion(_ => true); }",
                     "p => { p.AuthenticationSchemes = new[] { \"A\", \"B\" }; p.RequireAuthenticatedUser(); }",
                     "p => p.RequireAuthenticatedUser()",
                     "p => p.RequireRole(\"Admin\").RequireAuthenticatedUser()",
                     "(AuthorizationPolicyBuilder b) => b.RequireAuthenticatedUser()",
                     "static p => { p.Requirements.Add(new X()); p.RequireAuthenticatedUser(); }",
                 })
        {
            var catalog = Catalog($"            options.AddPolicy(\"SystemAdmin\", {body});");
            Assert.True(Assert.Single(catalog.Named["SystemAdmin"]).RequiresAuthenticatedUser, body);
            Assert.True(SystemAdminIsSignIn(catalog), body);
        }

        var aliasOnly = Catalog("            options.AddPolicy(AdminAlias, p => p.RequireAuthenticatedUser());");
        Assert.True(Assert.Single(aliasOnly.Named["SystemAdmin"]).RequiresAuthenticatedUser);
        Assert.Empty(aliasOnly.Unresolved);
        Assert.True(SystemAdminIsSignIn(aliasOnly));

        // The rate limiter's and CORS's own AddPolicy are not authorization registrations — not catalogued, not unresolved.
        var hosts = Catalog(proven,
            "        services.AddRateLimiter(o => o.AddPolicy(LimiterName, c => RateLimitPartition.GetNoLimiter(\"x\")));\n"
            + "        services.AddCors(o => o.AddPolicy(CorsName, b => b.AllowAnyOrigin()));\n");
        Assert.Empty(hosts.Unresolved);
        Assert.True(SystemAdminIsSignIn(hosts));
    }

    [Fact(DisplayName = "Task 167 f1 controls: a permissive or unresolvable policy is not sign-in, whatever its census list; proven forms are")]
    public void SignInVerdict_NegativeControl_PermissivePoliciesAreNotSignIn()
    {
        const string module =
            "public static class AuthPolicies { public const string Open = \"Open\"; public const string Locked = \"Locked\"; }\n"
            + "public static class M\n{\n    public static void Add(IServiceCollection services)\n    {\n"
            + "        services.AddAuthorization(options =>\n        {\n"
            + "            options.AddPolicy(\"Open\", p => p.RequireAssertion(_ => true));\n"
            + "            options.AddPolicy(AuthPolicies.Locked, p => { p.AuthenticationSchemes = new[] { \"X\" }; p.RequireAuthenticatedUser(); });\n"
            + "            options.AddPolicy(\"Twice\", p => p.RequireAuthenticatedUser());\n"
            + "            options.AddPolicy(\"Twice\", p => p.RequireAuthenticatedUser());\n"
            + "        });\n"
            + "        services.AddRateLimiter(o => o.AddPolicy(\"Limiter\", c => RateLimitPartition.GetNoLimiter(\"x\")));\n"
            + "    }\n}";
        var catalog = PolicyCatalogOf(new[] { new SourceUnit("Infrastructure/DI/M.cs", module) });

        List<string> Omission(string chain) => AnonymousByOmissionViolations(
            ScanText("Api/Fake/Policy.cs", new[] { $"        app.MapGet(\"/api/zz/{{id}}\", Get){chain};" }), catalog);

        // NEGATIVE: each of these declares RequireAuthorization, and none is proven sign-in — the route is named.
        foreach (var chain in new[]
                 {
                     ".RequireAuthorization(\"Open\")",                       // permissive: no RequireAuthenticatedUser
                     ".RequireAuthorization(AuthPolicies.Open)",              // the same, through the constant
                     ".RequireAuthorization(\"Limiter\")",                    // a RATE-LIMIT policy name, not an authorization one
                     ".RequireAuthorization(\"Unregistered\")",               // registered nowhere
                     ".RequireAuthorization(\"Twice\")",                      // ambiguous: registered twice
                     ".RequireAuthorization(p => p.RequireAssertion(_ => true))", // inline lambda — never read as sign-in
                     ".RequireAuthorization(somePolicyObject)",               // a variable — never read as sign-in
                     ".RequireAuthorization(\"Locked\", someOther)",          // one argument unresolvable
                 })
        {
            var violation = Assert.Single(Omission(chain));
            Assert.Contains("is not shown to require an authenticated user", violation);
        }

        // A DefaultPolicy override that does not require a user makes the BARE form unproven too.
        var permissiveDefault = PolicyCatalogOf(new[] { new SourceUnit("Infrastructure/DI/D.cs",
            "public static class D { public static void A(IServiceCollection s) { s.AddAuthorization(o => { "
            + "o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build(); }); } }") });
        Assert.Single(AnonymousByOmissionViolations(
            ScanText("Api/Fake/Default.cs", new[] { "        app.MapGet(\"/api/zz\", Get).RequireAuthorization();" }), permissiveDefault));

        // POSITIVE: the proven forms — bare (no override), a literal name, a constant, a qualified constant, and a
        // permissive policy combined with a proven one (requirements AND together).
        foreach (var chain in new[]
                 {
                     ".RequireAuthorization()",
                     ".RequireAuthorization(\"Locked\")",
                     ".RequireAuthorization(AuthPolicies.Locked)",
                     ".RequireAuthorization(Sprk.Bff.Api.Infrastructure.Authentication.AuthPolicies.Locked)",
                     ".RequireAuthorization(\"Open\", \"Locked\")",
                 })
        {
            Assert.Empty(Omission(chain));
        }

        // The rate limiter's AddPolicy is outside AddAuthorization(...), so it is not catalogued as an authorization policy.
        Assert.False(catalog.Named.ContainsKey("Limiter"));
        Assert.True(Assert.Single(catalog.Named["Locked"]).RequiresAuthenticatedUser);
    }

    // =============================================================================================
    // THE RUNTIME HALF — the authorization FallbackPolicy (owner round 14 item 2)
    // ---------------------------------------------------------------------------------------------
    // The build-time rule above keeps every route's intent declared. The FallbackPolicy keeps the RUNTIME closed if
    // a route ever slips past it: ASP.NET Core applies the fallback to an endpoint with no authorization metadata
    // (and to a request that matches no endpoint), so the omission answers 401 instead of serving anyone. This rule
    // pins its SOURCE — exactly one assignment, exactly the authenticated-user policy, applied inside
    // AddAuthorization(...). tests/integration/auth/UnifiedAccessControl/AuthorizationFallbackPolicyTests.cs proves
    // its BEHAVIOUR over HTTP in the real app and in a host built on AuthorizationModule.ApplyFallbackPolicy.
    // MAINTENANCE: a deliberate change to the fallback (a scheme list, say) changes RequiredFallbackPolicy here in the
    // same diff, and must still call RequireAuthenticatedUser().
    // =============================================================================================

    private const string RequiredFallbackPolicy = "new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()";

    private static List<string> FallbackPolicyViolations(IReadOnlyList<SourceUnit> units)
    {
        var violations = new List<string>();
        var assignments = new List<(SourceUnit Unit, int Index, string Rhs)>();

        foreach (var unit in units)
        {
            var code = unit.Code;
            foreach (Match s in PolicySlotAssignment.Matches(code))
            {
                var isSetter = s.Groups["setwhich"].Success;
                var which = isSetter ? s.Groups["setwhich"].Value : s.Groups["which"].Value;
                if (which != "Fallback")
                {
                    continue;
                }

                var end = isSetter ? MatchClose(code, s.Index + s.Length - 1) : AssignmentEnd(code, s.Index + s.Length);
                assignments.Add((unit, s.Index, end < 0 ? "<unreadable>" : Squash(code[(s.Index + s.Length)..end])));
            }
        }

        if (assignments.Count == 0)
        {
            violations.Add("no authorization FallbackPolicy is set: an endpoint that declares neither RequireAuthorization nor "
                           + "AllowAnonymous is callable by ANYONE at runtime (owner round 14 item 2 requires a FallbackPolicy "
                           + "requiring an authenticated user)");
            return violations;
        }

        if (assignments.Count > 1)
        {
            violations.Add($"the FallbackPolicy is set {assignments.Count} times ("
                           + string.Join(", ", assignments.Select(a => $"{a.Unit.Path}:{a.Unit.LineOf(a.Index)}"))
                           + ") — the last one wins, so one of them is unreviewed; keep exactly one");
        }

        foreach (var (unit, index, rhs) in assignments)
        {
            var at = $"{unit.Path}:{unit.LineOf(index)}";
            if (rhs != RequiredFallbackPolicy)
            {
                violations.Add($"{at}: FallbackPolicy = {rhs} — it must be exactly {RequiredFallbackPolicy}");
            }

            // Applied, not merely written — and applied UNCONDITIONALLY (task 167 f2, the f1 verifier's item 9): the
            // assignment is a statement of the unbroken opening run of an AddAuthorization(...) lambda; or it is a
            // statement of the unbroken opening run of a method (or that method's expression body) whose CALL is such a
            // statement. `if (cond) ApplyFallbackPolicy(options);`, an assignment after an early return, or a call
            // inside a lambda that never runs, is not applied — before f2 any call inside AddAuthorization(...) counted.
            if (AppliedUnconditionally(units, unit, index))
            {
                continue;
            }

            var method = unit.MethodAt(index);
            violations.Add($"{at}: the FallbackPolicy is assigned{(method is null ? string.Empty : $" in {method.Name}")}, but "
                           + "nothing applies it UNCONDITIONALLY inside AddAuthorization(...) — a statement of the unbroken run "
                           + "that opens the AddAuthorization lambda (or of the helper that such a statement calls). Otherwise the "
                           + "runtime could stay open");
        }

        return violations;
    }

    /// <summary>The end (exclusive) of the right-hand side of an assignment that starts at <paramref name="start"/>: the
    /// depth-zero <c>;</c>, or — for an expression-bodied lambda such as <c>AddAuthorization(o =&gt; o.FallbackPolicy = …)</c>
    /// — the bracket or comma that closes the enclosing argument. -1 when neither is found.</summary>
    private static int AssignmentEnd(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (--depth < 0)
                {
                    return i;
                }
            }
            else if ((c == ';' || c == ',') && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The unconditional statements of every <c>AddAuthorization(o =&gt; …)</c> lambda in <paramref name="unit"/>:
    /// the opening run of a block body, or the expression body as one statement.</summary>
    private static List<(int Start, int End)> AddAuthorizationRuns(SourceUnit unit)
    {
        var code = unit.Code;
        var runs = new List<(int Start, int End)>();
        foreach (Match a in AddAuthorizationCall.Matches(code))
        {
            var open = a.Index + a.Length - 1;
            var close = MatchClose(code, open);
            var args = close < 0 ? new List<(int Start, int End)>() : SplitTopLevel(code, open + 1, close);
            if (args.Count == 0)
            {
                continue;
            }

            var arrow = code.IndexOf("=>", args[0].Start, StringComparison.Ordinal);
            if (arrow < 0 || arrow >= args[0].End)
            {
                continue;
            }

            var body = SkipWs(code, arrow + 2);
            if (body < code.Length && code[body] == '{')
            {
                runs.AddRange(PlainStatementRun(code, body));
            }
            else
            {
                runs.Add((body, args[0].End));
            }
        }

        return runs;
    }

    private static bool AppliedUnconditionally(IReadOnlyList<SourceUnit> units, SourceUnit unit, int slotIndex)
    {
        var code = unit.Code;

        // The FallbackPolicy token is the HEAD of the statement: `options.FallbackPolicy = …` (or the expression body).
        bool IsHeadOf((int Start, int End) statement)
        {
            var m = Regex.Match(code[statement.Start..statement.End], @"^(?:[A-Za-z_]\w*\s*\.\s*)?(?=FallbackPolicy\s*=(?![=>]))");
            return m.Success && statement.Start + m.Length == slotIndex;
        }

        if (AddAuthorizationRuns(unit).Any(IsHeadOf))
        {
            return true;
        }

        var method = unit.MethodAt(slotIndex);
        if (method is null)
        {
            return false;
        }

        var inMethodRun = code[method.BodyStart] == '{'
            ? PlainStatementRun(code, method.BodyStart).Any(IsHeadOf)
            : IsHeadOf((SkipWs(code, method.BodyStart + 2), method.BodyEnd));
        if (!inMethodRun)
        {
            return false;
        }

        // ...and the method's CALL is itself an unconditional statement of an AddAuthorization lambda.
        return units.Any(u => AddAuthorizationRuns(u).Any(statement =>
        {
            var call = Regex.Match(u.Code[statement.Start..statement.End],
                $@"^(?:[A-Za-z_]\w*\s*\.\s*)*{Regex.Escape(method.Name)}\s*\(");
            if (!call.Success)
            {
                return false;
            }

            var callClose = MatchClose(u.Code, statement.Start + call.Length - 1);
            return callClose >= 0 && SkipWs(u.Code, callClose + 1) >= statement.End;
        }));
    }

    [Fact(DisplayName = "Task 167 f1: the authorization FallbackPolicy requires an authenticated user and is applied (owner round 14 item 2)")]
    public void TheAuthorizationFallbackPolicyRequiresAnAuthenticatedUser()
    {
        var units = BffAndSharedUnits().ToList();
        var violations = FallbackPolicyViolations(units);
        Assert.True(
            violations.Count == 0,
            "The runtime half of 'no route is anonymous by omission' is the authorization FallbackPolicy (owner round 14 item "
            + "2): an endpoint that declares no authorization metadata must answer 401, not serve anyone. Set it once, in "
            + "AuthorizationModule.ApplyFallbackPolicy, to an authenticated-user policy, and apply it inside "
            + "AddAuthorization(...).\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: it is THE one in AuthorizationModule.
        var assignment = Assert.Single(RealPolicyCatalog.Value.FallbackAssignments);
        Assert.Equal("Infrastructure/DI/AuthorizationModule.cs", assignment.File);
        Assert.True(assignment.RequiresAuthenticatedUser);
    }

    [Fact(DisplayName = "Task 167 f1 controls: a missing, permissive, doubled or unapplied FallbackPolicy fails; the applied authenticated one passes")]
    public void FallbackPolicy_NegativeControl_EachUnsafeShapeFails()
    {
        static IReadOnlyList<SourceUnit> Module(string body, string extra = "")
            => new[] { new SourceUnit("Infrastructure/DI/AuthorizationModule.cs",
                "public static class AuthorizationModule\n{\n    public static IServiceCollection Add(IServiceCollection services)\n    {\n"
                + "        services.AddAuthorization(options =>\n        {\n" + body + "\n            options.AddPolicy(\"SystemAdmin\", p => p.RequireAuthenticatedUser());\n"
                + "        });\n        return services;\n    }\n" + extra + "}") };

        const string helper = "    public static void ApplyFallbackPolicy(AuthorizationOptions options)\n    {\n"
                              + "        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();\n    }\n";

        // NEGATIVE: none at all (the state before owner round 14).
        Assert.Contains(FallbackPolicyViolations(Module(string.Empty)), v => v.StartsWith("no authorization FallbackPolicy", StringComparison.Ordinal));

        // NEGATIVE: permissive, null, and the default policy passed through by reference (not provably authenticated).
        foreach (var rhs in new[]
                 {
                     "new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build()",
                     "null",
                     "options.DefaultPolicy",
                 })
        {
            Assert.Contains(FallbackPolicyViolations(Module($"            options.FallbackPolicy = {rhs};")),
                v => v.Contains("it must be exactly", StringComparison.Ordinal));
        }

        // NEGATIVE: written in a helper nothing applies; and set twice.
        Assert.Contains(FallbackPolicyViolations(Module(string.Empty, helper)), v => v.Contains("nothing applies it", StringComparison.Ordinal));
        Assert.Contains(FallbackPolicyViolations(Module(
                "            ApplyFallbackPolicy(options);\n            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();",
                helper)),
            v => v.Contains("is set 2 times", StringComparison.Ordinal));

        // NEGATIVE: the AuthorizationBuilder setter form is read too.
        Assert.Contains(FallbackPolicyViolations(new[] { new SourceUnit("Infrastructure/DI/B.cs",
                "public static class B { public static void A(IServiceCollection s) { s.AddAuthorizationBuilder()"
                + ".SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build()); } }") }),
            v => v.Contains("it must be exactly", StringComparison.Ordinal));

        // NEGATIVE (task 167 f2, the f1 verifier's item 9): applied CONDITIONALLY — the call or the assignment sits under
        // an if, after an early return, or in a lambda that never runs; or the helper assigns it conditionally.
        foreach (var body in new[]
                 {
                     "            if (DateTime.UtcNow.Year < 0) ApplyFallbackPolicy(options);",
                     "            if (flag) { ApplyFallbackPolicy(options); }",
                     "            options.AddPolicy(\"X\", p => p.RequireAuthenticatedUser());\n            if (flag) return;\n            ApplyFallbackPolicy(options);",
                     "            Action later = () => ApplyFallbackPolicy(options);",
                     "            flag.Then(() => ApplyFallbackPolicy(options));",
                 })
        {
            Assert.Contains(FallbackPolicyViolations(Module(body, helper)), v => v.Contains("nothing applies it UNCONDITIONALLY", StringComparison.Ordinal));
        }

        Assert.Contains(FallbackPolicyViolations(Module(
                "            if (flag) { options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); }")),
            v => v.Contains("nothing applies it UNCONDITIONALLY", StringComparison.Ordinal));
        const string conditionalHelper = "    public static void ApplyFallbackPolicy(AuthorizationOptions options)\n    {\n"
            + "        if (Enabled) options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();\n    }\n";
        Assert.Contains(FallbackPolicyViolations(Module("            ApplyFallbackPolicy(options);", conditionalHelper)),
            v => v.Contains("nothing applies it UNCONDITIONALLY", StringComparison.Ordinal));

        // POSITIVE: inline in AddAuthorization, through a helper called from inside it (the real shape, with its guard
        // clause), after other plain statements, and as an expression-bodied AddAuthorization lambda.
        Assert.Empty(FallbackPolicyViolations(Module(
            "            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();")));
        Assert.Empty(FallbackPolicyViolations(Module("            ApplyFallbackPolicy(options);", helper)));
        Assert.Empty(FallbackPolicyViolations(Module("            ApplyFallbackPolicy(options);",
            helper.Replace("    {\n        options.", "    {\n        ArgumentNullException.ThrowIfNull(options);\n        options.", StringComparison.Ordinal))));
        Assert.Empty(FallbackPolicyViolations(Module(
            "            options.AddPolicy(\"X\", p => p.RequireAuthenticatedUser());\n            AuthorizationModule.ApplyFallbackPolicy(options);", helper)));
        Assert.Empty(FallbackPolicyViolations(new[] { new SourceUnit("Infrastructure/DI/E.cs",
            "public static class E { public static void A(IServiceCollection s) { s.AddAuthorization(o => "
            + "o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); } }") }));
    }

    // =============================================================================================
    // NO TEST PROVES A ROUTE EXISTS WITH AN ANONYMOUS REQUEST (task 167 f2 — the f1 verifier's item 8)
    // ---------------------------------------------------------------------------------------------
    // The FallbackPolicy also challenges a request that matches NO route, so an ANONYMOUS request answers 401 whether
    // or not the route exists (and 401, not 405, for a wrong verb). A test that proves presence with an anonymous
    // "not 404" / "not 405", or that names a route "registered / routed / exists" on the strength of an anonymous 401,
    // can no longer fail: f1 fixed 21 by grep, the verifier then found 3 more that stayed green with their route
    // renamed, f2 found 2 more, and sibling branches already carry 2 of the 401 shape. Evidence of presence is a
    // SIGNED-IN request (a bearer, or a client the file builds signed in) or the endpoint table (EndpointTable,
    // EndpointDataSource). This rule makes that a build failure instead of a reviewer's grep.
    // =============================================================================================

    private static readonly Regex AnonymousPresenceAssertion = new(
        @"\.\s*NotBe\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*(?:NotFound|MethodNotAllowed)\b"
        + @"|\.\s*NotBe\s*\(\s*(?:404|405)\s*[,)]"
        + @"|\.\s*NotBe\s*\(\s*StatusCodes\s*\.\s*Status40[45]\w*"
        + @"|Assert\s*\.\s*NotEqual\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*(?:NotFound|MethodNotAllowed)\b"
        + @"|!=\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*NotFound\b",
        RegexOptions.Compiled);

    private static readonly Regex PresenceClaimingName = new(
        @"(?:Still|Is|Are)(?:Routed|Registered|Mapped)|EndpointExists|RouteExists|EndpointsExist", RegexOptions.Compiled);

    private static readonly Regex UnauthorizedAssertion = new(
        @"\.\s*Be\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*Unauthorized\b|\.\s*BeOneOf\s*\([^;]*HttpStatusCode\s*\.\s*Unauthorized\b"
        + @"|Assert\s*\.\s*Equal\s*\(\s*(?:System\.Net\.)?HttpStatusCode\s*\.\s*Unauthorized\b",
        RegexOptions.Compiled);

    private static readonly Regex SignedInOrTableEvidence = new(
        @"Authorization\s*=\s*new\s+(?:[\w.]+\.)?AuthenticationHeaderValue\b"
        + @"|\bCreateAuthenticated\w*\s*\(|\bCreateClientWithRights\s*\(|\bCreateReportingClient\s*\(|\bCreateTestSession\s*\("
        + @"|\bEndpointTable\s*\.|\bEndpointDataSource\b|\bRouteEndpoint\b"
        + @"|\bauthenticated\s*:\s*true\b|\bTestHttpContexts\s*\.\s*Authenticated\b",
        RegexOptions.Compiled);

    private static readonly Regex ClientCall = new(
        @"(?<![\w.])(?<client>[A-Za-z_]\w*)\s*!?\s*\.\s*(?:Get|Post|Put|Patch|Delete|Send)\w*Async\s*\(", RegexOptions.Compiled);

    private static readonly Regex SignedInClientFactory = new(
        @"\b(?:CreateAuthenticated\w*|CreateHttpClient|CreateClientWithRights|CreateReportingClient|CreateTestSession)\s*\(",
        RegexOptions.Compiled);

    /// <summary>The unit's code with every parsed method BODY blanked: what remains is class-level setup — field
    /// initializers and constructors — which applies to every test of the class. Another test method's body never
    /// does (xUnit builds a new instance per test), so a header one test sets lends nothing to the next.</summary>
    private static string ClassSetupCodeOf(SourceUnit unit)
    {
        var chars = unit.Code.ToCharArray();
        foreach (var method in unit.Methods)
        {
            for (var i = method.BodyStart; i < method.BodyEnd; i++)
            {
                if (chars[i] != '\n')
                {
                    chars[i] = ' ';
                }
            }
        }

        return new string(chars);
    }

    /// <summary>"file:line Method — why" for every test method in <paramref name="units"/> that asserts a route's
    /// PRESENCE with only an anonymous request as evidence.</summary>
    private static List<string> AnonymousPresenceProofViolations(IEnumerable<SourceUnit> units)
    {
        var violations = new List<string>();
        foreach (var unit in units)
        {
            string? setup = null;
            foreach (var method in unit.Methods)
            {
                var body = method.Body;
                var notFoundShape = AnonymousPresenceAssertion.IsMatch(body);
                var claimShape = PresenceClaimingName.IsMatch(method.Name) && UnauthorizedAssertion.IsMatch(body);
                if (!notFoundShape && !claimShape)
                {
                    continue;
                }

                if (SignedInOrTableEvidence.IsMatch(body))
                {
                    continue;
                }

                // A client the method uses that is built signed in, or signed in, by the method itself or by class-level
                // setup (e.g. `_httpClient = _fixture.CreateHttpClient();` in the constructor) — never by ANOTHER test.
                setup ??= ClassSetupCodeOf(unit);
                var scopes = new[] { body, setup };
                var signedInClient = ClientCall.Matches(body).Select(m => m.Groups["client"].Value).Distinct(StringComparer.Ordinal)
                    .Any(client => scopes.Any(scope =>
                        Regex.IsMatch(scope, $@"(?<![\w.]){Regex.Escape(client)}\s*=\s*[^;]*?{SignedInClientFactory}")
                        || Regex.IsMatch(scope, $@"(?<![\w.]){Regex.Escape(client)}\s*\.\s*DefaultRequestHeaders\s*\.\s*Authorization\s*=")));
                if (signedInClient)
                {
                    continue;
                }

                violations.Add($"{unit.Path}:{unit.LineOf(method.NameIndex)} {method.Name} — "
                               + (notFoundShape
                                   ? "asserts \"not 404 / not 405\" with no signed-in request and no endpoint-table read"
                                   : "claims the route is registered/routed on the strength of an anonymous 401"));
            }
        }

        return violations;
    }

    [Fact(DisplayName = "Task 167 f2: no test proves a route exists with an ANONYMOUS request (the FallbackPolicy answers 401 for a missing route)")]
    public void NoTestProvesRoutePresenceWithAnAnonymousRequest()
    {
        var units = SourceScan.TestSourceFiles()
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new SourceUnit(SourceScan.Relative(f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f)))
            .ToList();
        var violations = AnonymousPresenceProofViolations(units);
        Assert.True(
            violations.Count == 0,
            "These tests prove a route is PRESENT with an anonymous request. Since unified-access-control-r2 task 167 the "
            + "BFF's authorization FallbackPolicy answers an anonymous request with 401 whether or not a route exists (and "
            + "401, not 405, for a wrong verb), so the assertion cannot fail — it stays green with the route deleted. Prove "
            + "presence with a SIGNED-IN request (anything but 404) or with EndpointTable.AssertMapped(factory, verb, path) "
            + "(tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/EndpointTable.cs).\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the scan reads the test tree, and the shapes it looks for are in use with their evidence.
        Assert.True(units.Count > 500, $"only {units.Count} test files were read");
        Assert.Contains(units, u => u.Methods.Any(m => AnonymousPresenceAssertion.IsMatch(m.Body) && SignedInOrTableEvidence.IsMatch(m.Body)));
    }

    [Fact(DisplayName = "Task 167 f2 controls: an anonymous presence proof fails; a signed-in or endpoint-table proof passes")]
    public void AnonymousPresenceProof_NegativeControl_OnlySignedInOrTableEvidenceCounts()
    {
        static List<string> Scan(string body, string members = "    private readonly HttpClient _client = factory.CreateClient();\n",
            string name = "Route_EndpointExists_AcceptsGet")
            => AnonymousPresenceProofViolations(new[] { new SourceUnit("tests/fake/PresenceTests.cs",
                "public class PresenceTests\n{\n" + members + $"    [Fact]\n    public async Task {name}()\n    {{\n" + body + "\n    }\n}") });

        // NEGATIVE — the shapes the verifier and f2 found: anonymous not-404, not-405, != NotFound, and a test NAMED as a
        // presence proof that only asserts an anonymous 401 (the sibling branches' SurvivingSiblings_AreStillRouted).
        Assert.Single(Scan("        var r = await _client.GetAsync(\"/api/x\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.NotFound);"));
        Assert.Single(Scan("        var r = await _client.GetAsync(\"/api/x/1\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);"));
        Assert.Single(Scan("        var r = await _client.GetAsync(\"/api/x\");\n        var exists = r.StatusCode != HttpStatusCode.NotFound;\n        Assert.True(exists);"));
        Assert.Single(Scan("        var r = await _factory.CreateClient().GetAsync(\"/api/x\");\n        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized, \"routed\");",
            name: "SurvivingSiblings_AreStillRouted_401WithoutABearer"));

        // POSITIVE — a bearer on the request, a client the file builds signed in, the endpoint table, and a plain
        // "requires authentication" test (asserts 401 but claims nothing about presence).
        Assert.Empty(Scan("        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", \"t\");\n"
                          + "        var r = await _client.GetAsync(\"/api/x\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.NotFound);"));
        Assert.Empty(Scan("        var r = await _httpClient!.GetAsync(\"/api/x\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.NotFound);",
            members: "    private readonly HttpClient? _httpClient = _fixture.CreateHttpClient();\n"));
        Assert.Empty(Scan("        var r = await _client.GetAsync(\"/api/x\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.NotFound);\n"
                          + "        EndpointTable.AssertMapped(_factory, \"GET\", \"/api/x\");"));
        Assert.Empty(Scan("        var r = await _client.GetAsync(\"/api/x\");\n        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);",
            name: "Route_WithoutAuth_RequiresAuthentication"));

        // A file-level client built ANONYMOUSLY lends nothing.
        Assert.Single(Scan("        var r = await _httpClient!.GetAsync(\"/api/x\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.NotFound);",
            members: "    private readonly HttpClient? _httpClient = _fixture.CreateUnauthenticatedClient();\n"));

        // A header ANOTHER test sets on the shared field lends nothing (xUnit builds a new instance per test) — the
        // false negative the f2-5 seed exposed; a header the CONSTRUCTOR sets applies to every test.
        const string otherTestSignsIn =
            "    [Fact]\n    public async Task Other_WithAuth()\n    {\n"
            + "        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", \"t\");\n    }\n";
        Assert.Single(Scan("        var r = await _client.GetAsync(\"/api/x/1\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);",
            members: "    private readonly HttpClient _client = factory.CreateClient();\n" + otherTestSignsIn));
        Assert.Empty(Scan("        var r = await _client.GetAsync(\"/api/x/1\");\n        r.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);",
            members: "    private readonly HttpClient _client;\n    public PresenceTests(CustomWebAppFactory factory)\n    {\n"
                     + "        _client = factory.CreateClient();\n        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", \"t\");\n    }\n"));
    }

    // =============================================================================================
    // THE EXPLICITLY ANONYMOUS SURFACE AND THE PROBE POLICY — pinned sets (task 167 f1, owner round 14)
    // =============================================================================================

    /// <summary>"added" / "removed" lines between a computed set and its pinned set.</summary>
    private static List<string> PinnedSetDifferences(IEnumerable<string> computed, IEnumerable<string> pinned)
    {
        var actual = computed.ToHashSet(StringComparer.Ordinal);
        var expected = pinned.ToHashSet(StringComparer.Ordinal);
        return actual.Except(expected).Select(k => $"added: {k}")
            .Concat(expected.Except(actual).Select(k => $"removed: {k}"))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> AnonymousKeys(IEnumerable<RouteRegistration> routes)
        => routes.Where(r => r.Anonymous).Select(r => r.Key);

    private static bool CarriesHealthProbePolicy(RouteRegistration route)
        => route.Chain.Any(c => c.Name == "RequireRateLimiting" && c.Args.Trim() == "\"health-probe\"");

    /// <summary>A plain, non-interpolated, non-concatenated string literal — the only rate-limit policy NAME the guard can
    /// read. Anything else (<c>"health-" + "probe"</c>, a constant, an interpolation, a policy object) could name the
    /// LOOSER probe policy without the pin seeing it (task 167 f2, the f1 verifier's item 7).</summary>
    private static readonly Regex PlainStringLiteral = new("^\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"$", RegexOptions.Compiled);

    private static List<string> HealthProbePolicyViolations(IEnumerable<RouteRegistration> routes)
    {
        var list = routes.ToList();
        var violations = PinnedSetDifferences(list.Where(CarriesHealthProbePolicy).Select(r => r.Key), HealthProbeRoutes);
        violations.AddRange(list.Where(r => CarriesHealthProbePolicy(r) && !r.Anonymous)
            .Select(r => $"not anonymous: {r.Key} carries the probe policy but is not a public liveness probe"));
        violations.AddRange(list
            .SelectMany(r => r.Chain.Where(c => c.Name == "RequireRateLimiting" && !PlainStringLiteral.IsMatch(c.Args.Trim()))
                .Select(c => $"unreadable rate-limit policy: {r.Key} at {c.File}:{c.Line} — RequireRateLimiting({Squash(c.Args)}) is not a "
                             + "plain string literal, so the guard cannot tell whether it names the looser \"health-probe\" policy. "
                             + "Name the policy with a literal (e.g. RequireRateLimiting(\"anonymous\"))"))
            .Distinct(StringComparer.Ordinal));
        return violations;
    }

    [Fact(DisplayName = "Task 167 f1: the explicitly anonymous surface is pinned — every public route says .AllowAnonymous() and is listed")]
    public void TheExplicitlyAnonymousSurfaceIsPinned()
    {
        Assert.Equal(ExplicitlyAnonymousRoutes.Count, ExplicitlyAnonymousRoutes.Select(a => a.Route).Distinct(StringComparer.Ordinal).Count());
        var differences = PinnedSetDifferences(AnonymousKeys(Real.Live), ExplicitlyAnonymousRoutes.Select(a => a.Route));
        Assert.True(
            differences.Count == 0,
            "The set of routes reachable WITHOUT SIGNING IN changed. Since owner round 14 a route is public only by an "
            + "explicit .AllowAnonymous(); the set is pinned so it changes only in a reviewed diff. An ADDED route needs a "
            + "line in ExplicitlyAnonymousRoutes AND an AnonymousByDesign waiver naming its mandatory control (or a Pending "
            + "waiver); a REMOVED one is deleted from the list in the diff that gates it.\n\n  " + string.Join("\n  ", differences));

        // Each listed route is declared on a chain the scanner reads (AnonymityIsDeclaredOnlyOnAScannedChain refuses
        // every other form), and Rule A sends each to an AnonymousByDesign or Pending waiver.
        foreach (var route in ExplicitlyAnonymousRoutes)
        {
            var waiver = Assert.Single(Waivers, w => w.Route == route.Route);
            Assert.True(waiver.Kind == WaiverKind.Pending || waiver.Basis == PermanentBasis.AnonymousByDesign, route.Route);
        }
    }

    [Fact(DisplayName = "Task 167 f1: the health-probe rate-limit policy covers exactly the three liveness probes (owner round 14 item 1)")]
    public void TheHealthProbeRateLimitPolicyCoversExactlyTheLivenessProbes()
    {
        var violations = HealthProbePolicyViolations(Real.Live);
        Assert.True(
            violations.Count == 0,
            "\"health-probe\" (120/min per client IP) is LOOSER than \"anonymous\" (10/min). It exists so the App Service "
            + "health check, the slot-swap warm-up ping and the 5-second deploy pollers never see a 429 on GET /healthz, "
            + "/healthz/catalog and /ping (owner round 14 item 1). Any other route keeps its own policy; a probe that "
            + "loses it goes back to the 10/min budget a deploy poller can exhaust.\n\n  " + string.Join("\n  ", violations));

        // Round 12 item 1 still holds: every probe carries exactly ONE rate limit, and it is this one.
        foreach (var key in HealthProbeRoutes)
        {
            var route = Real.Live.Single(r => r.Key == key);
            Assert.Single(route.Chain, c => c.Name == "RequireRateLimiting");
            Assert.True(route.Anonymous, key);
        }
    }

    [Fact(DisplayName = "Task 167 f1 controls: the anonymous-surface and probe-policy pins fail on added, removed and misplaced routes")]
    public void AnonymousSurfaceAndProbePolicy_NegativeControl_PinsBite()
    {
        static List<RouteRegistration> Probes(params string[] lines) => ScanText("Infrastructure/DI/EndpointMappingExtensions.cs", lines);

        var real = new[]
        {
            "        app.MapHealthChecks(\"/healthz\", new HealthCheckOptions()).AllowAnonymous().RequireRateLimiting(\"health-probe\");",
            "        app.MapHealthChecks(\"/healthz/catalog\", new HealthCheckOptions()).AllowAnonymous().RequireRateLimiting(\"health-probe\");",
            "        app.MapGet(\"/ping\", () => Results.Text(\"pong\")).AllowAnonymous().RequireRateLimiting(\"health-probe\");",
            "        app.MapGet(\"/status\", () => Results.Ok()).AllowAnonymous().RequireRateLimiting(\"anonymous\");",
        };

        // POSITIVE: the sanctioned shape.
        Assert.Empty(HealthProbePolicyViolations(Probes(real)));

        // NEGATIVE: the looser policy spreads to /status (added), a probe goes back to "anonymous" (removed), and a
        // signed-in route borrows it (added + not anonymous).
        Assert.Contains("added: GET /status", HealthProbePolicyViolations(Probes(real[0], real[1], real[2],
            "        app.MapGet(\"/status\", () => Results.Ok()).AllowAnonymous().RequireRateLimiting(\"health-probe\");")));
        Assert.Contains("removed: GET /ping", HealthProbePolicyViolations(Probes(real[0], real[1],
            "        app.MapGet(\"/ping\", () => Results.Text(\"pong\")).AllowAnonymous().RequireRateLimiting(\"anonymous\");", real[3])));
        var borrowed = HealthProbePolicyViolations(Probes(real.Append(
            "        app.MapGet(\"/api/me\", Get).RequireAuthorization().RequireRateLimiting(\"health-probe\");").ToArray()));
        Assert.Contains("added: GET /api/me", borrowed);
        Assert.Contains(borrowed, v => v.StartsWith("not anonymous: GET /api/me", StringComparison.Ordinal));

        // NEGATIVE (task 167 f2, the f1 verifier's item 7 seed): a policy NAME the pin cannot read. Before f2 the
        // concatenation spread the 12x looser budget to the anonymous Dataverse probe with the guard green.
        foreach (var argument in new[] { "\"health-\" + \"probe\"", "ProbePolicyName", "$\"health-probe\"", "@\"health-probe\"", "Policies.Probe" })
        {
            var unread = HealthProbePolicyViolations(Probes(real.Append(
                $"        app.MapGet(\"/healthz/dataverse\", Probe).AllowAnonymous().RequireRateLimiting({argument});").ToArray()));
            Assert.Contains(unread, v => v.StartsWith("unreadable rate-limit policy: GET /healthz/dataverse", StringComparison.Ordinal));
        }

        // POSITIVE: the same route on a literal "anonymous" policy is readable and not the probe policy.
        Assert.Empty(HealthProbePolicyViolations(Probes(real.Append(
            "        app.MapGet(\"/healthz/dataverse\", Probe).AllowAnonymous().RequireRateLimiting(\"anonymous\");").ToArray())));

        // The anonymous surface: a new public route is "added", a gated one "removed"; the declared set is "equal".
        var pinned = new[] { "GET /healthz", "GET /healthz/catalog", "GET /ping", "GET /status" };
        Assert.Empty(PinnedSetDifferences(AnonymousKeys(Probes(real)), pinned));
        Assert.Equal(new[] { "added: POST /api/zz/public" }, PinnedSetDifferences(AnonymousKeys(Probes(real.Append(
            "        app.MapPost(\"/api/zz/public\", Post).AllowAnonymous();").ToArray())), pinned));
        Assert.Equal(new[] { "removed: GET /status" }, PinnedSetDifferences(AnonymousKeys(Probes(real[0], real[1], real[2],
            "        app.MapGet(\"/status\", () => Results.Ok()).RequireAuthorization();")), pinned));
    }

    // =============================================================================================
    // GROUP CONTINUATIONS — only an UNCONDITIONAL one is credited (task 167 f1, the r2 verifier's item 5)
    // =============================================================================================

    [Fact(DisplayName = "Task 167 f1 controls: a conditional or skippable group continuation is a problem and earns nothing; an unbroken run is credited")]
    public void GroupContinuation_NegativeControl_OnlyAnUnconditionalRunIsCredited()
    {
        static (List<RouteRegistration> Routes, List<string> Problems) Scan(params string[] lines)
        {
            var all = ScanText("Api/Fake/Continuation.cs", lines);
            return (all.Where(r => !r.Unparseable).ToList(), all.Where(r => r.Unparseable).Select(r => r.Problem!).ToList());
        }

        // NEGATIVE — the r2 verifier's own seed, as a fixture: the continuation sits in a braced if-block. Before f1 it
        // was credited (AtStatementStart took '{' as a statement start) and the guard stayed green.
        var seed = Scan(
            "        var docs = app.MapGroup(\"/api/documents\");",
            "        if (DateTime.UtcNow.Year < 0) { docs.RequireAuthorization(); }",
            "        docs.MapGet(\"/{documentId}/versions\", ListVersions);",
            "        docs.MapGet(\"/{documentId}/versions/{versionId}/content\", GetVersionContent);");
        Assert.Contains(seed.Problems, p => p.Contains("'docs.RequireAuthorization()' adds to group 'docs' outside the unbroken run",
            StringComparison.Ordinal));
        Assert.Equal(2, seed.Routes.Count);
        Assert.All(seed.Routes, r => Assert.DoesNotContain(r.Chain, c => c.Name == "RequireAuthorization"));
        Assert.Equal(
            new[] { "GET /api/documents/{documentId}/versions", "GET /api/documents/{documentId}/versions/{versionId}/content" },
            AnonymousByOmissionViolations(seed.Routes).Select(v => v[..v.IndexOf('\n')]).ToArray());

        // NEGATIVE — every other way a continuation can be conditional or skipped. Each is a problem, and the chain the
        // routes inherit gains nothing from it.
        foreach (var lines in new[]
                 {
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        if (flag) docs.RequireAuthorization();", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        if (flag) { } else { docs.RequireAuthorization(); }", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        docs.MapGet(\"/x\", H);", "        if (flag) return;", "        docs.RequireAuthorization();" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        Log(\"x\");", "        docs.RequireAuthorization();", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        foreach (var x in xs) { docs.AddDocumentAuthorizationFilter(\"read\"); }", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        Action secure = () => docs.RequireAuthorization();", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        var secured = docs.RequireAuthorization();", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        try { docs.RequireAuthorization(); } catch { }", "        docs.MapGet(\"/x\", H);" },
                 })
        {
            var (routes, problems) = Scan(lines);
            Assert.Single(problems);
            var route = Assert.Single(routes);
            Assert.DoesNotContain(route.Chain, c => c.Name is "RequireAuthorization" or "AddDocumentAuthorizationFilter");
            Assert.Equal(Credit.None, CreditOf(route));
        }

        // NEGATIVE — a group RECEIVED as a parameter, continued inside an if-block of the receiving method.
        const string received =
            "public static class E\n{\n    public static void MapAll(this IEndpointRouteBuilder app)\n    {\n"
            + "        var g = app.MapGroup(\"/api/e\");\n        MapChildren(g, true);\n    }\n\n"
            + "    private static void MapChildren(RouteGroupBuilder group, bool flag)\n    {\n"
            + "        if (flag) { group.RequireAuthorization(); }\n        group.MapGet(\"/x\", H);\n    }\n}";
        var parameterCase = ScanFixtures(new[] { ("Api/Fake/E.cs", received), ("Program.cs", "var app = builder.Build();\napp.MapAll();") }, "Api/Fake/E.cs");
        Assert.Contains(parameterCase, r => r.Unparseable && r.Problem!.Contains("adds to group 'group'", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterCase.Single(r => !r.Unparseable).Chain, c => c.Name == "RequireAuthorization");

        // POSITIVE — the unbroken run: directly after the declaration (comments between are fine), after other
        // statements on the SAME group, several in a row, inside the block that declares the group, and as the first
        // statements of a method that receives the group.
        foreach (var lines in new[]
                 {
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        // sign-in for every route below", "        docs.RequireAuthorization();", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        docs.MapGet(\"/x\", H);", "        docs.RequireAuthorization();" },
                     new[] { "        var docs = app.MapGroup(\"/api/d\");", "        docs.RequireAuthorization();", "        docs.AddDocumentAuthorizationFilter(\"read\");", "        docs.MapGet(\"/x\", H);" },
                     new[] { "        if (enabled)", "        {", "            var docs = app.MapGroup(\"/api/d\");", "            docs.RequireAuthorization();", "            docs.MapGet(\"/x\", H);", "        }" },
                 })
        {
            var (routes, problems) = Scan(lines);
            Assert.Empty(problems);
            var route = Assert.Single(routes);
            Assert.Contains(route.Chain, c => c.Name == "RequireAuthorization");
            Assert.Empty(AnonymousByOmissionViolations(routes));
        }

        var receivedOk = received.Replace("        if (flag) { group.RequireAuthorization(); }\n", "        group.RequireAuthorization();\n", StringComparison.Ordinal);
        var parameterOk = ScanFixtures(new[] { ("Api/Fake/E.cs", receivedOk), ("Program.cs", "var app = builder.Build();\napp.MapAll();") }, "Api/Fake/E.cs");
        Assert.DoesNotContain(parameterOk, r => r.Unparseable);
        Assert.Contains(Assert.Single(parameterOk).Chain, c => c.Name == "RequireAuthorization");
    }

    [Fact(DisplayName = "Task 167 f2 controls: a helper that receives the group is credited only when EVERY call passing the group in is unconditional")]
    public void GroupContinuation_NegativeControl_AReceivedGroupIsCreditedOnlyThroughUnconditionalCalls()
    {
        // MapV declares the group and calls the helper(s); {0} is the statements between the declaration and the
        // registration; {1} is the declaration's own chain; {2} extra members.
        static (List<RouteRegistration> Routes, List<string> Problems) Scan(string between, string declarationChain = ".RequireAuthorization()",
            string helpers = "    private static void SecureRead(RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n")
        {
            var source = "public static class V\n{\n    public static void MapV(this IEndpointRouteBuilder app)\n    {\n"
                         + $"        var docs = app.MapGroup(\"/api/documents\"){declarationChain};\n"
                         + between
                         + "        docs.MapGet(\"/{documentId}/versions\", ListVersions);\n    }\n\n" + helpers + "}";
            var all = ScanFixtures(new[] { ("Api/DocumentVersionEndpoints.cs", source), ("Program.cs", "var app = builder.Build();\napp.MapV();") },
                "Api/DocumentVersionEndpoints.cs");
            return (all.Where(r => !r.Unparseable).ToList(), all.Where(r => r.Unparseable).Select(r => r.Problem!).ToList());
        }

        static void NotCredited((List<RouteRegistration> Routes, List<string> Problems) scan, string because)
        {
            Assert.True(scan.Problems.Any(p => p.Contains("which RECEIVES the group", StringComparison.Ordinal)),
                because + ": " + string.Join(" | ", scan.Problems));
            var route = Assert.Single(scan.Routes);
            Assert.DoesNotContain(route.Chain, c => c.Name == "AddDocumentAuthorizationFilter");
            Assert.Equal(Credit.None, CreditOf(route));
        }

        // NEGATIVE — the f1 verifier's item 4 seed, as a fixture: the helper's filter would be credited to the version
        // routes although the call that applies it never runs. (The no-call CONTROL — Rule A fires — is below.)
        NotCredited(Scan("        if (DateTime.UtcNow.Year < 0) SecureRead(docs);\n"), "brace-less if");
        NotCredited(Scan("        if (flag) { SecureRead(docs); }\n"), "braced if");
        NotCredited(Scan("        Log(\"x\");\n        SecureRead(docs);\n"), "after an unrelated statement");
        NotCredited(Scan("        Action later = () => SecureRead(docs);\n"), "in a lambda");
        NotCredited(Scan("        if (flag) docs.SecureReadExt();\n",
            helpers: "    private static void SecureReadExt(this RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n"),
            "extension form, conditional");

        // NEGATIVE — through TWO helpers: the outer call is unconditional but the inner one is not, and vice versa.
        NotCredited(Scan("        Outer(docs);\n", helpers:
            "    private static void Outer(RouteGroupBuilder g)\n    {\n        if (flag) SecureRead(g);\n    }\n\n"
            + "    private static void SecureRead(RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n"),
            "inner call conditional");
        NotCredited(Scan("        if (flag) Outer(docs);\n", helpers:
            "    private static void Outer(RouteGroupBuilder g)\n    {\n        SecureRead(g);\n    }\n\n"
            + "    private static void SecureRead(RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n"),
            "outer call conditional");

        // NEGATIVE — the sign-in variant: the group itself declares nothing; a conditional helper call must not make the
        // routes "signed in" (the no-call control fails NoRouteIsAnonymousByOmission the same way).
        var signIn = Scan("        if (DateTime.UtcNow.Year < 0) Secure(docs);\n", declarationChain: string.Empty,
            helpers: "    private static void Secure(RouteGroupBuilder group)\n    {\n        group.RequireAuthorization();\n    }\n");
        Assert.Contains(signIn.Problems, p => p.Contains("which RECEIVES the group", StringComparison.Ordinal));
        Assert.Equal(new[] { "GET /api/documents/{documentId}/versions" },
            AnonymousByOmissionViolations(signIn.Routes).Select(v => v[..v.IndexOf('\n')]).ToArray());

        // CONTROL — no call at all: Rule A fires on the uncredited route, exactly as for the conditional call above.
        var none = Scan(string.Empty);
        Assert.Empty(none.Problems);
        Assert.Single(RuleAViolations(none.Routes.Select(r => Assess(r, Array.Empty<HandlerDecision>())), Array.Empty<Waiver>()));

        // POSITIVE — every call passing the group runs whenever the group exists: directly after the declaration,
        // after other statements of the run, in extension form, and through an unconditional chain of two helpers.
        foreach (var credited in new[]
                 {
                     Scan("        SecureRead(docs);\n"),
                     Scan("        docs.WithTags(\"Docs\");\n        V.SecureRead(docs);\n"),
                     Scan("        SecureRead(g: docs);\n"),
                     Scan("        docs.SecureReadExt();\n",
                         helpers: "    private static void SecureReadExt(this RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n"),
                     Scan("        Outer(docs);\n", helpers:
                         "    private static void Outer(RouteGroupBuilder g)\n    {\n        SecureRead(g);\n    }\n\n"
                         + "    private static void SecureRead(RouteGroupBuilder g)\n    {\n        g.AddDocumentAuthorizationFilter(\"read\");\n    }\n"),
                 })
        {
            Assert.Empty(credited.Problems);
            var route = Assert.Single(credited.Routes);
            Assert.Contains(route.Chain, c => c.Name == "AddDocumentAuthorizationFilter");
            Assert.Equal(Credit.PerResource, CreditOf(route));
        }

        var signInOk = Scan("        Secure(docs);\n", declarationChain: string.Empty,
            helpers: "    private static void Secure(RouteGroupBuilder group)\n    {\n        group.RequireAuthorization();\n    }\n");
        Assert.Empty(signInOk.Problems);
        Assert.Empty(AnonymousByOmissionViolations(signInOk.Routes));
    }

    [Fact(DisplayName = "Task 167 r1 controls: attribute anonymity, wrapper anonymity and unread registration forms each fail")]
    public void UnreadableShapes_NegativeControl_AttributeAnonymityAndUnreadFormsFail()
    {
        List<string> Anonymity(string source)
        {
            var routes = ScanFixtures(new[] { ("Api/Fake/Anon.cs", source), ("Program.cs", "var app = builder.Build();\napp.MapAnon();") },
                "Api/Fake/Anon.cs");
            return AnonymityViolations(new[] { new SourceUnit("Api/Fake/Anon.cs", source) }, routes);
        }

        static string Wrap(string body) => "public static class AnonEndpoints\n{\n    public static void MapAnon(this IEndpointRouteBuilder app)\n    {\n"
                                           + body + "\n    }\n}";

        // NEGATIVE: [AllowAnonymous] on an inline lambda — the scanner's Anonymous flag would be false.
        var lambdaAttr = Wrap("        app.MapGet(\"/x\", [AllowAnonymous] () => Results.Ok()).RequireAuthorization();");
        Assert.False(ScanFixtures(new[] { ("Api/Fake/Anon.cs", lambdaAttr), ("Program.cs", "var app = builder.Build();\napp.MapAnon();") },
            "Api/Fake/Anon.cs").Single().Anonymous);
        Assert.Contains(Anonymity(lambdaAttr), v => v.StartsWith("Api/Fake/Anon.cs:5:", StringComparison.Ordinal) && v.Contains("ATTRIBUTE"));

        // NEGATIVE: [AllowAnonymous] on a named handler method, and WithMetadata(new AllowAnonymousAttribute()).
        Assert.Contains(Anonymity(Wrap("        app.MapGet(\"/x\", H);") + "\n[AllowAnonymous] static IResult H() => Results.Ok();"),
            v => v.Contains("ATTRIBUTE"));
        Assert.Contains(Anonymity(Wrap("        app.MapGet(\"/x\", H).WithMetadata(new AllowAnonymousAttribute());")),
            v => v.Contains("'AllowAnonymousAttribute'"));

        // NEGATIVE: an .AllowAnonymous() hidden in a wrapper extension the chain calls by another name.
        var wrapper = Wrap("        app.MapGet(\"/x\", H).Public();")
                      + "\npublic static class Ext { public static RouteHandlerBuilder Public(this RouteHandlerBuilder b) => b.AllowAnonymous(); }";
        Assert.Contains(Anonymity(wrapper), v => v.Contains("no route chain the scanner reads"));

        // POSITIVE: .AllowAnonymous() on the registration chain is seen, and a comment or log text naming it is not code.
        Assert.Empty(Anonymity(Wrap("        // [AllowAnonymous] was considered here\n        app.MapGet(\"/x\", H).AllowAnonymous().RequireRateLimiting(\"anonymous\");")));

        // NEGATIVE: the four registration families finding 4 named, plus an undeclared Map* call.
        var declared = new HashSet<string>(StringComparer.Ordinal) { "MapAnon", "MapX" };
        foreach (var call in new[] { "app.Map(\"/x\", H);", "app.MapFallback(H);", "app.MapFallbackToFile(\"index.html\");",
                                     "app.MapHub<ChatHub>(\"/hub\");", "app.MapControllers();", "app.MapSomethingNew(\"/y\");" })
        {
            var unit = new SourceUnit("Api/Fake/Unread.cs", Wrap("        " + call));
            Assert.Single(UnreadRegistrationViolations(new[] { unit }, declared));
        }

        // A framework name stays refused even if something declares a method of the same name.
        Assert.Single(UnreadRegistrationViolations(new[] { new SourceUnit("Api/Fake/U.cs", Wrap("        app.MapHub<H>(\"/h\");")) },
            new HashSet<string>(StringComparer.Ordinal) { "MapHub" }));

        // POSITIVE: the read vocabulary and a DECLARED Map* method (the BFF's own MapXEndpoints) pass; a comment is not code.
        var fine = new SourceUnit("Api/Fake/Fine.cs", Wrap(
            "        var g = app.MapGroup(\"/api/f\");\n        g.MapGet(\"/a\", H);\n        g.MapMethods(\"/b\", [\"PATCH\"], H);\n"
            + "        app.MapHealthChecks(\"/h\");\n        app.MapX();\n        // app.MapFallback(H);"));
        Assert.Empty(UnreadRegistrationViolations(new[] { fine }, declared));
    }

    [Fact(DisplayName = "Task 167 r2 controls: a route anonymous by omission fails even under a waiver; declared sign-in or anonymity passes")]
    public void AnonymousByOmission_NegativeControl_FiresEvenUnderAWaiver()
    {
        // NEGATIVE — the r2 verifier's seed, as a fixture: a bare registration, plus a Permanent ReferenceData waiver.
        var bare = ScanText("Api/UserEndpoints.cs", new[] { "        app.MapGet(\"/api/zzref\", () => Results.Ok());" });
        var route = Assert.Single(bare);
        Assert.Equal("GET /api/zzref", route.Key);
        Assert.False(route.Anonymous);   // it does not SCAN as anonymous — that is the hole
        var assessed = Assess(route, Array.Empty<HandlerDecision>());
        var waiver = Permanent("GET /api/zzref", PermanentBasis.ReferenceData, "167",
            "Seeded by AnonymousByOmission_NegativeControl_FiresEvenUnderAWaiver to prove the rule bites; UserEndpoints.cs:1.");

        // Before r2 this was the whole verdict: the waiver satisfied Rule A and broke no waiver rule, so the guard was
        // green for a route anyone can call without signing in.
        Assert.Empty(RuleAViolations(new[] { assessed }, new[] { waiver }));
        Assert.Empty(WaiverViolations(new[] { assessed }, new[] { waiver }));

        // r2: the omission rule names the route, and it consults no waiver.
        Assert.Contains(AnonymousByOmissionViolations(bare), v => v.StartsWith("GET /api/zzref\n      at Api/UserEndpoints.cs:1 ", StringComparison.Ordinal));

        // NEGATIVE: a credited per-resource filter is not sign-in (filters run after authorization); an [Authorize]
        // attribute and WithMetadata(new AuthorizeAttribute()) are not read (fail closed — declare it on the chain);
        // a rate limit is not sign-in.
        foreach (var lines in new[]
                 {
                     new[] { "        var g = app.MapGroup(\"/api/zz\");", "        g.MapGet(\"/{id}\", Get).AddEntityAccessFilter();" },
                     new[] { "        app.MapGet(\"/api/zz/{id}\", [Authorize] (Guid id) => Results.Ok());" },
                     new[] { "        app.MapGet(\"/api/zz/{id}\", Get).WithMetadata(new AuthorizeAttribute());" },
                     new[] { "        app.MapGet(\"/api/zz/{id}\", Get).RequireRateLimiting(\"anonymous\");" },
                 })
        {
            var routes = ScanText("Api/Fake/Omit.cs", lines);
            Assert.Single(routes);
            Assert.False(routes[0].Unparseable, routes[0].Problem);   // a real, resolved route — not a parse failure
            Assert.Single(AnonymousByOmissionViolations(routes));
        }

        // POSITIVE: sign-in declared on the route, on the group, through a nested group, by a statement continuing the
        // group, or by a named policy; and an explicit .AllowAnonymous() (which Rule A then sends to AnonymousByDesign).
        foreach (var lines in new[]
                 {
                     new[] { "        app.MapGet(\"/api/zz/{id}\", Get).RequireAuthorization();" },
                     new[] { "        var g = app.MapGroup(\"/api/zz\").RequireAuthorization();", "        g.MapGet(\"/{id}\", Get);" },
                     new[] { "        var g = app.MapGroup(\"/api/zz\").RequireAuthorization();", "        var h = g.MapGroup(\"/inner\");", "        h.MapGet(\"/{id}\", Get);" },
                     new[] { "        var g = app.MapGroup(\"/api/zz\");", "        g.RequireAuthorization();", "        g.MapGet(\"/{id}\", Get);" },
                     new[] { "        app.MapPost(\"/api/zz/admin\", Post).RequireAuthorization(\"SystemAdmin\");" },
                     new[] { "        app.MapGet(\"/ping\", () => Results.Text(\"pong\")).AllowAnonymous().RequireRateLimiting(\"anonymous\");" },
                 })
        {
            var routes = ScanText("Api/Fake/Declared.cs", lines);
            Assert.Single(routes);
            Assert.False(routes[0].Unparseable, routes[0].Problem);
            Assert.Empty(AnonymousByOmissionViolations(routes));
        }
    }

    // =============================================================================================
    // CONTROLS — negative (the detector fires on a seeded violation) and positive (it does not fire on the
    // sanctioned shape), per tests/CLAUDE.md. Inline fixtures, so a control cannot go stale.
    // =============================================================================================

    [Fact(DisplayName = "Task 074 negative control: the detector fires on each historical miss, reintroduced as source")]
    public void Detector_NegativeControl_FiresOnEachHistoricalMiss()
    {
        // RETROACTIVE VALIDATION. Each case is the registration as it stood when the miss was live.

        // Miss #3 — POST /api/documents/{documentId}/share-link: no per-document filter on a signed-in-only group.
        var shareLink = ScanText("Api/FileAccessEndpoints.cs", new[]
        {
            "        var docs = app.MapGroup(\"/api/documents\").RequireAuthorization();",
            "        docs.MapPost(\"/{documentId}/share-link\", CreateShareLink)",
            "            .WithName(\"CreateDocumentShareLink\")",
            "            .Produces<ShareLinkResponse>(StatusCodes.Status200OK);",
        });
        Assert.Single(shareLink);
        Assert.Equal(Credit.None, CreditOf(shareLink[0]));
        Assert.Equal("POST /api/documents/{documentId}/share-link", shareLink[0].Key);

        // Miss #2 — an OBOEndpoints drive-keyed byte read behind rate limiting and RequireAuthorization() only.
        var obo = ScanText("Api/OBOEndpoints.cs", new[]
        {
            "        app.MapGet(\"/api/obo/drives/{driveId}/items/{itemId}/content\", async (",
            "            string driveId, string itemId, SpeFileStore store) =>",
            "        {",
            "            var stream = await store.DownloadContentAsUserAsync(driveId, itemId);",
            "            return TypedResults.Stream(stream);",
            "        }).RequireRateLimiting(\"graph-read\").RequireAuthorization();",
        });
        Assert.Single(obo);
        Assert.Equal(Credit.None, CreditOf(obo[0]));
        Assert.Equal("GET /api/obo/drives/{driveId}/items/{itemId}/content", obo[0].Key);

        // Miss #4's SHAPE — a container-keyed write whose only gate is a resource POLICY: classified
        // ResourcePolicy (real, pinned), not None and not PerResource. ("canuploadfiles" stands in for the deleted
        // "canwritefiles" — task 083 removed that policy; the shape is under test, not the literal.)
        var upload = ScanText("Api/UploadEndpoints.cs", new[]
        {
            "        app.MapPut(\"/api/containers/{containerId}/files/{*path}\", async (",
            "            string containerId, string path, HttpRequest req) =>",
            "        {",
            "            return TypedResults.Ok();",
            "        })",
            "        .RequireAuthorization(\"canuploadfiles\");",
        });
        Assert.Single(upload);
        Assert.Equal(Credit.ResourcePolicy, CreditOf(upload[0]));
        Assert.Equal("PUT /api/containers/{containerId}/files/{*path}", upload[0].Key);

        // The sixth miss — container-keyed document listing behind RequireAuthorization() alone.
        var containerDocs = ScanText("Api/DataverseDocumentsEndpoints.cs", new[]
        {
            "        app.MapGet(\"/api/v1/containers/{containerId}/documents\", async (",
            "            string containerId, IDocumentDataverseService svc) =>",
            "        {",
            "            return TypedResults.Ok(await svc.ListAsync(containerId));",
            "        })",
            "        .RequireAuthorization();",
        });
        Assert.Single(containerDocs);
        Assert.Equal(Credit.None, CreditOf(containerDocs[0]));

        // Miss #1 — /api/ai/search DID carry a filter. Rule A credits the form (it is on the allow-list today
        // because task 070 made it decide); Rule B is what catches a decorative one — see the next control.
        var search = ScanText("Api/Ai/SemanticSearchEndpoints.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/ai/search\").RequireAuthorization();",
            "        group.MapPost(\"/\", Search)",
            "            .AddSemanticSearchAuthorizationFilter()",
            "            .Produces<SemanticSearchResponse>(StatusCodes.Status200OK);",
        });
        Assert.Single(search);
        Assert.Equal(Credit.PerResource, CreditOf(search[0]));
    }

    [Fact(DisplayName = "Task 074 negative control: Rule B fires on the decorative filter that made /api/ai/search a hole")]
    public void RuleB_NegativeControl_FiresOnADecorativeFilter()
    {
        const string decorative = """
            public class SemanticSearchAuthorizationFilter : IEndpointFilter
            {
                private readonly ILogger<SemanticSearchAuthorizationFilter>? _logger;
                public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
                {
                    var userTenantId = context.HttpContext.User.FindFirst("tid")?.Value;
                    if (string.IsNullOrEmpty(userTenantId)) return Results.Problem(statusCode: 401);
                    return await next(context);
                }
                private AuthorizationResult ValidateScopeAuthorization(SemanticSearchRequest r, string t)
                {
                    switch (r.Scope)
                    {
                        case SearchScope.All: return new AuthorizationResult(true, null);
                        default: return new AuthorizationResult(true, null);
                    }
                }
            }
            """;
        Assert.False(ConsultsDecisionService(CodeOf(decorative)),
            "Rule B must flag a filter that consults no authorization decision service — the /api/ai/search filter verbatim in shape.");

        const string sanctioned = """
            public class DocumentAuthorizationFilter : IEndpointFilter
            {
                private readonly AuthorizationService _authorizationService;
                public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
                {
                    var result = await _authorizationService.AuthorizeAsync(authContext);
                    if (!result.IsAllowed) return Results.Problem(statusCode: 403);
                    return await next(c);
                }
            }
            """;
        Assert.True(ConsultsDecisionService(CodeOf(sanctioned)), "Rule B must NOT flag DocumentAuthorizationFilter.");

        // Prose is not evidence: a doc comment or a "Future:" note naming a service must not pass.
        const string prose = """
            /// <summary>Validates access via AuthorizationService and AccessRights.</summary>
            // Future: consult IAccessDataSource / RetrievePrincipalAccess for per-document rights.
            public class FutureAuthorizationFilter : IEndpointFilter
            {
                private readonly ILogger _logger;
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate n) => n(c);
            }
            """;
        Assert.False(ConsultsDecisionService(CodeOf(prose)), "Rule B must not accept a doc comment as evidence of a decision.");

        // Task 167: a log message naming a service is not evidence either — literals are blanked.
        Assert.False(ConsultsDecisionService(CodeOf("class F { void M() { _logger.LogInformation(\"AuthorizationService said yes\"); } }")));
    }

    [Fact(DisplayName = "Task 167: decision tokens match WHOLE identifiers, not substrings")]
    public void DecisionTokens_MatchWholeIdentifiersOnly()
    {
        // Negative: "IAiAuthorizationServiceFactory" and "MyAuthorizationServiceHelper" contain "AuthorizationService"
        // but are not it. Before task 167 the substring match passed AiAuthorizationFilter and the AnalysisAccess mode
        // of AnalysisAuthorizationFilter through Rule B on exactly this kind of accident.
        Assert.False(ContainsToken(CodeOf("class F { private readonly IAiAuthorizationServiceFactory _f; }"), "AuthorizationService"));
        Assert.False(ContainsToken(CodeOf("class F { private readonly IAiAuthorizationServiceFactory _f; }"), "IAiAuthorizationService"));
        Assert.False(ConsultsDecisionService(CodeOf("class F { private readonly MyAuthorizationServiceHelper _h; }")));
        Assert.True(ContainsToken(CodeOf("var x = Spaarke.Dataverse.AccessRights.Read;"), "AccessRights"));

        // Positive: AnalysisAuthorizationFilter.cs (its DocumentAccess path) satisfies "IAiAuthorizationService".
        var analysis = CodeOf(File.ReadAllText(Path.Combine(BffRoot, "Api", "Filters", "AnalysisAuthorizationFilter.cs")));
        Assert.True(ContainsToken(analysis, "IAiAuthorizationService"));
        Assert.Contains("IAiAuthorizationService", DecisionServices);
        Assert.Empty(KnownDecorativeFilters);
    }

    [Fact(DisplayName = "Task 074 positive control: the detector does not fire on the gated routes it protects")]
    public void Detector_PositiveControl_DoesNotFireOnGatedRoutes()
    {
        // ⚠️ UPDATED by task 167. This fixture used .AddEndpointFilter<BulkDownloadAuthorizationFilter>() and
        // .AddEndpointFilter<EntityAccessFilter>() — forms the real code has never used (it attaches
        // .AddBulkDownloadAuthorizationFilter() and .AddEntityAccessFilter()). Under the explicit allow-list a
        // fixture in an unused form proves nothing about the code, so it now uses the real forms.
        var gated = ScanText("Api/FileAccessEndpoints.cs", new[]
        {
            "        var docs = app.MapGroup(\"/api/documents\").RequireAuthorization();",
            "        docs.MapGet(\"/{documentId}/download\", GetDownload)",
            "            .AddDocumentAuthorizationFilter(\"read\")",
            "            .WithName(\"GetDocumentDownload\");",
            "        docs.MapPost(\"/bulk\", Bulk)",
            "            .AddBulkDownloadAuthorizationFilter()",
            "            .WithName(\"Bulk\");",
        });
        Assert.Equal(2, gated.Count);
        Assert.All(gated, r => Assert.Equal(Credit.PerResource, CreditOf(r)));

        // A group-level filter is inherited by the group's routes.
        var groupFiltered = ScanText("Api/Fake/GroupFiltered.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/thing\")",
            "            .RequireAuthorization()",
            "            .AddEntityAccessFilter();",
            "        group.MapGet(\"/{id}\", Get);",
        });
        Assert.Single(groupFiltered);
        Assert.Equal(Credit.PerResource, CreditOf(groupFiltered[0]));

        // An explicitly anonymous route is Anonymous, not None.
        var anon = ScanText("Api/Fake/Health.cs", new[] { "        app.MapGet(\"/ping\", () => Results.Text(\"pong\")).AllowAnonymous();" });
        Assert.Single(anon);
        Assert.Equal(Credit.Anonymous, CreditOf(anon[0]));

        // A comment that MENTIONS a filter is not the filter.
        var commentOnly = ScanText("Api/Fake/Commented.cs", new[]
        {
            "        // This route had NO per-document filter, so AddDocumentAuthorizationFilter(\"read\")",
            "        // was added by task 002. See the note above.",
            "        docs.MapPost(\"/{documentId}/share-link\", CreateShareLink)",
            "            .WithName(\"CreateDocumentShareLink\");",
        });
        Assert.Single(commentOnly);
        Assert.Equal(Credit.None, CreditOf(commentOnly[0]));
    }

    [Fact(DisplayName = "Task 120 negative control: baseline authentication, rate limiting and idempotency are NOT gates")]
    public void AuthFilterRecognition_NegativeControl_BaselineAuthnIsNotAGate()
    {
        var baselineOnly = ScanText("Api/Fake/OfficeBaseline.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/office\").RequireAuthorization();",
            "        group.MapGet(\"/search/entities\", SearchEntitiesAsync)",
            "            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Search)",
            "            .AddOfficeAuthFilter();",
            "        group.MapPost(\"/todo\", CreateTodoAsync)",
            "            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.QuickCreate)",
            "            .AddIdempotencyFilter()",
            "            .AddOfficeAuthFilter();",
        });
        Assert.Equal(2, baselineOnly.Count);
        Assert.All(baselineOnly, r => Assert.Equal(Credit.None, CreditOf(r)));
    }

    [Fact(DisplayName = "Task 120 positive control: the real Office gates ARE recognised, on the real file")]
    public void AuthFilterRecognition_PositiveControl_OfficeGatesAreRecognised()
    {
        var byKey = RealAssessments.Value.ToDictionary(a => a.Key, StringComparer.Ordinal);
        foreach (var key in new[]
                 {
                     "POST /api/office/save",                       // .AddEntityAccessFilter()
                     "GET /api/office/jobs/{jobId:guid}",           // .AddJobOwnershipFilter()
                     "GET /api/office/jobs/{jobId:guid}/stream",
                     "POST /api/office/todo",                       // .AddTodoSourceAccessFilter()
                     "POST /api/office/quickcreate/{entityType}",   // .AddQuickCreateSourceAccessFilter()
                 })
        {
            Assert.True(byKey.ContainsKey(key), $"Expected to find {key} — has the route been renamed?");
            Assert.Equal(Credit.PerResource, byKey[key].Credit);
        }
    }

    [Fact(DisplayName = "Task 120 control: a route on a NESTED group resolves to its full path, and inherits the outer chain")]
    public void NestedGroupPrefix_NegativeControl_ResolvesThroughTheParent()
    {
        var nested = ScanText("Api/Fake/Nested.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/office\").RequireAuthorization();",
            "        var jobs = group.MapGroup(\"/jobs\");",
            "        jobs.MapGet(\"/{jobId:guid}\", GetJobStatusAsync).AddJobOwnershipFilter();",
        });
        Assert.Single(nested);
        Assert.Equal("GET /api/office/jobs/{jobId:guid}", nested[0].Key);

        // ⚠️ UPDATED by task 167: the inherited filter is an ADMIN mechanism, so the credit is AdminOnly (pinned)
        // where task 074's taxonomy said "Filter". The fixture is unchanged; inheritance is what is under test.
        var inherited = ScanText("Api/Fake/NestedInherit.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/spe\").AddSpeAdminAuthorizationFilter();",
            "        var items = group.MapGroup(\"/containers\");",
            "        items.MapGet(\"/{id}/content\", GetContentAsync);",
        });
        Assert.Single(inherited);
        Assert.Equal("GET /api/spe/containers/{id}/content", inherited[0].Key);
        Assert.Contains("AddSpeAdminAuthorizationFilter", inherited[0].Forms);
        Assert.Equal(Credit.AdminOnly, CreditOf(inherited[0]));
    }

    [Fact(DisplayName = "Task 167 negative controls: Rule A fires on a removed filter and on a signed-in-only route")]
    public void RuleA_NegativeControl_FiresOnARemovedFilterAndOnSignedInOnly()
    {
        // A copy of the REAL FileAccessEndpoints.cs with one .AddDocumentAuthorizationFilter("read") removed,
        // bound to a root the way Program.cs binds the real one.
        var real = File.ReadAllText(Path.Combine(BffRoot, "Api", "FileAccessEndpoints.cs"));
        var marker = ".AddDocumentAuthorizationFilter(\"read\")";
        // Searched in the comment-blanked text (same offsets as the raw file): the file's prose names this very
        // call above a route, and removing a comment would prove nothing.
        var first = Lex(real).Text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(first > 0, "the fixture needs a real read-gated route to un-gate");
        var ungated = real.Remove(first, marker.Length);

        var routes = ScanFixtures(new[]
        {
            ("Api/FileAccessEndpoints.cs", ungated),
            ("Program.cs", "var app = builder.Build();\napp.MapFileAccessEndpoints();"),
        }, "Api/FileAccessEndpoints.cs");
        var violations = RuleAViolations(routes.Select(r => Assess(r, Array.Empty<HandlerDecision>())), Waivers);
        Assert.Single(violations);
        Assert.Contains("/api/documents/", violations[0]);

        var signedInOnly = ScanText("Api/Fake/SignedIn.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/fake\").RequireAuthorization().RequireRateLimiting(\"x\");",
            "        group.MapGet(\"/{id}\", Get);",
        });
        var v2 = RuleAViolations(signedInOnly.Select(r => Assess(r, Array.Empty<HandlerDecision>())), Waivers);
        Assert.Single(v2);
        Assert.StartsWith("GET /api/fake/{id}", v2[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Task 167 negative controls: removing any sweep waiver fails Rule A; changing an observed chain makes it stale")]
    public void SweepWaivers_NegativeControl_RemovalFailsAndChainChangeIsStale()
    {
        foreach (var entry in SweepFindings.Where(e => e.ResolvedBy is null && e.Gap == Gap.NoDecision))
        {
            var without = Waivers.Where(w => w.Route != entry.Route).ToList();
            var violations = RuleAViolations(RealAssessments.Value, without);
            Assert.True(violations.Any(v => v.StartsWith(entry.Route + "\n", StringComparison.Ordinal)),
                $"Removing the waiver of {entry.SweepId} {entry.Route} must make Rule A name it");
        }

        foreach (var entry in SweepFindings.Where(e => e.ResolvedBy is null && e.Gap == Gap.InsufficientDecision))
        {
            var changed = RealAssessments.Value
                .Select(a => a.Key == entry.Route ? a with { Fingerprint = a.Fingerprint + " + AddFooFilter" } : a)
                .ToList();
            Assert.Contains(WaiverViolations(changed, Waivers), v => v.StartsWith(entry.Route + " ", StringComparison.Ordinal) && v.Contains("STALE"));
        }

        // The same, from SOURCE: S-52's chain in a fixture, with an extra credited form, no longer matches its
        // recorded fingerprint.
        var s52 = ScanText("Api/Ai/AnalysisEndpoints.cs", new[]
        {
            "        var group = app.MapGroup(\"/api/ai/analysis\").RequireAuthorization();",
            "        group.MapPost(\"/execute\", Execute).AddAnalysisExecuteAuthorizationFilter().AddEntityAccessFilter();",
        }).Select(r => Assess(r, Array.Empty<HandlerDecision>())).ToList();
        var waiver = Waivers.Single(w => w.Route == "POST /api/ai/analysis/execute");
        Assert.Contains(WaiverViolations(s52, new[] { waiver }), v => v.Contains("fingerprint changed"));
    }

    [Fact(DisplayName = "Task 167 negative controls: each stale and malformed waiver shape fails")]
    public void WaiverRules_NegativeControl_EachShapeFails()
    {
        var real = RealAssessments.Value;
        string Some(Func<Assessment, bool> p) => real.First(p).Key;

        var credited = Some(a => a.Credit == Credit.PerResource);
        var uncredited = Some(a => a.Credit == Credit.None);
        var anonymous = Some(a => a.Credit == Credit.Anonymous);
        const string reason = "Seeded by WaiverRules_NegativeControl_EachShapeFails to prove the rule bites; see Foo.cs:1.";

        void Fires(Waiver w, string fragment) =>
            Assert.Contains(WaiverViolations(real, new[] { w }), v => v.Contains(fragment, StringComparison.Ordinal));

        // A NoDecision waiver on a credited route (one that gained credit, or always had it).
        Fires(Pending(credited, "UNOWNED-NEW", Gap.NoDecision, reason), "STALE — the route is credited");
        // A HandlerDecision is credit too.
        Fires(Pending(Some(a => a.Credit == Credit.HandlerDecision), "UNOWNED-NEW", Gap.NoDecision, reason), "STALE — the route is credited");
        // An InsufficientDecision waiver whose fingerprint does not match.
        Fires(Pending(credited, "UNOWNED-NEW", Gap.InsufficientDecision, reason, observed: "Something + Else"), "fingerprint changed");
        // A Permanent waiver on a credited route is redundant.
        Fires(Permanent(credited, PermanentBasis.ReferenceData, "167", reason), "REDUNDANT");
        // AnonymousByDesign on a route that is not anonymous.
        Fires(Permanent(uncredited, PermanentBasis.AnonymousByDesign, "167", reason), "AnonymousByDesign on a route that is not anonymous");
        // A non-anonymous basis on an anonymous route.
        Fires(Permanent(anonymous, PermanentBasis.CallerScopedOnly, "167", reason), "only AnonymousByDesign");
        // Any waiver on an absent route.
        Fires(Pending("GET /api/no/such/route", "UNOWNED-NEW", Gap.NoDecision, reason), "STALE — the route no longer exists");
        // Closed sets.
        Fires(Pending(uncredited, "999", Gap.NoDecision, reason), "a Pending owner must be one of");
        Fires(Permanent(uncredited, PermanentBasis.None, "167", reason), "PermanentBasis from the closed set");
        Fires(Permanent(uncredited, (PermanentBasis)99, "167", reason), "PermanentBasis from the closed set");
        Fires(Permanent(uncredited, PermanentBasis.ReferenceData, "167", "A reason long enough to pass the length rule but citing nothing at all."), "cite the handler file:line");

        // An anonymous route with neither waiver fails Rule A.
        Assert.Contains(RuleAViolations(real, Waivers.Where(w => w.Route != anonymous).ToList()), v => v.StartsWith(anonymous + "\n", StringComparison.Ordinal));

        // POSITIVE: adding .RequireRateLimiting to an InsufficientDecision route does NOT change its fingerprint.
        var before = ScanText("Api/Fake/F.cs", new[] { "app.MapPost(\"/api/fake\", H).RequireAuthorization().AddSessionOwnershipFilter();" });
        var after = ScanText("Api/Fake/F.cs", new[] { "app.MapPost(\"/api/fake\", H).RequireAuthorization().AddSessionOwnershipFilter().RequireRateLimiting(\"ai-stream\");" });
        Assert.Equal(Assess(before[0], Array.Empty<HandlerDecision>()).Fingerprint, Assess(after[0], Array.Empty<HandlerDecision>()).Fingerprint);
        Assert.Equal("AddSessionOwnershipFilter", Assess(after[0], Array.Empty<HandlerDecision>()).Fingerprint);
    }

    [Fact(DisplayName = "Task 167 negative controls: the sweep ledger refuses a Permanent waiver, a silent deletion, a re-owning and a bad proof")]
    public void SweepLedger_NegativeControl_EachShapeFails()
    {
        var real = RealAssessments.Value;
        const string reason = "Seeded by SweepLedger_NegativeControl_EachShapeFails to prove the rule bites; Foo.cs:1.";

        // A Permanent waiver on POST /api/v1/events.
        var permanent = Waivers.Append(Permanent("POST /api/v1/events", PermanentBasis.CreateWithNoPriorResource, "167", reason)).ToList();
        Assert.Contains(LedgerViolations(real, permanent, SweepFindings), v => v.Contains("NEVER carry a Permanent waiver"));

        // The old Permanent "COLLECTION READ" waiver on GET /api/v1/documents, restored.
        var restored = Waivers.Where(w => w.Route != "GET /api/v1/documents")
            .Append(Permanent("GET /api/v1/documents", PermanentBasis.CallerScopedOnly, "-", reason)).ToList();
        Assert.Contains(LedgerViolations(real, restored, SweepFindings), v => v.Contains("GET /api/v1/documents") && v.Contains("Permanent"));

        // Deleting the Pending waiver of S-10 without setting ResolvedBy.
        var deleted = Waivers.Where(w => w.Route != "GET /api/dataverse/record/{entityLogicalName}/{id:guid}").ToList();
        Assert.Contains(LedgerViolations(real, deleted, SweepFindings), v => v.StartsWith("S-10 ", StringComparison.Ordinal) && v.Contains("REMEDY"));

        // Re-owning POST /api/communications/send to 166.
        var reowned = Waivers.Select(w => w.Route == "POST /api/communications/send" ? w with { OwningTask = "166" } : w).ToList();
        Assert.Contains(LedgerViolations(real, reowned, SweepFindings), v => v.Contains("POST /api/communications/send ") && v.Contains("owner 166"));

        // ResolvedBy set with a ProofTest naming a method that does not exist (and the route not credited).
        var resolved = SweepFindings.Select(e => e.Route == "POST /api/v1/events"
            ? e with { ResolvedBy = "159", ProofTest = "tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs::NoSuchDenyTest" }
            : e).ToList();
        var v5 = LedgerViolations(real, Waivers.Where(w => w.Route != "POST /api/v1/events").ToList(), resolved);
        Assert.Contains(v5, v => v.Contains("NoSuchDenyTest"));
        Assert.Contains(v5, v => v.Contains("does not pass Rule A by credit"));

        // POSITIVE: a resolved entry whose route IS credited and whose proof exists passes — simulated by crediting
        // the route and pointing at a method that exists.
        var credited = real.Select(a => a.Key == "POST /api/v1/events" ? a with { Credit = Credit.HandlerDecision } : a).ToList();
        var goodProof = SweepFindings.Select(e => e.Route == "POST /api/v1/events"
            ? e with { ResolvedBy = "159", ProofTest = "tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs::SweepLedger_NegativeControl_EachShapeFails" }
            : e).ToList();
        Assert.DoesNotContain(LedgerViolations(credited, Waivers.Where(w => w.Route != "POST /api/v1/events").ToList(), goodProof),
            v => v.Contains("POST /api/v1/events"));
    }

    // The three absence-proof shapes the deleting tasks wrote (tasks 159, 160, 164 on integ/uac-r2-batch4), as fixtures —
    // so the retired-route rule is shown to accept what the integration will record, and to refuse look-alikes.
    private const string RetiredProofFixture = """
        public class RetiredRouteProofs
        {
            private static readonly (string Verb, string Pattern)[] RetiredRoutes =
            {
                ("GET", "/api/ai/prompts/"),
                ("PUT", "/api/ai/prompts/{id}"),
            };

            [Fact]
            public void RetiredRoutes_AreAbsentFromTheEndpointTable()
            {
                var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
                var survivors = new List<string>();
                foreach (var (verb, pattern) in RetiredRoutes)
                {
                    survivors.AddRange(endpoints.Where(e => Same(e, verb, pattern)).Select(e => e.DisplayName!));
                }

                survivors.Should().BeEmpty("retired");
            }

            [Theory]
            [InlineData("PUT", "/api/v1/events/{id}")]
            [InlineData("DELETE", "/api/v1/events/{id}")]
            public async Task DeletedRoutes_AreNotMapped_AndReachNothing(string verb, string path)
            {
                var response = await host.SendAsync(new HttpRequestMessage(new HttpMethod(verb), path.Replace("{id}", Guid.NewGuid().ToString())));
                response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            }

            [Fact]
            public async Task PostFetch_IsNotMapped_ForAnAuthenticatedCaller()
            {
                using var client = _fixture.CreateAuthenticatedClient();
                var response = await client.PostAsJsonAsync("/api/dataverse/fetch", new { entityName = "sprk_matter" });
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, "deleted");
            }

            [Fact]
            public async Task GetRecord_IsNotMapped_ForAnAuthenticatedCaller()
            {
                using var client = _fixture.CreateAuthenticatedClient();
                var response = await client.GetAsync($"/api/dataverse/record/sprk_matter/{Guid.NewGuid()}?$select=sprk_name");
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, "deleted");
            }

            [Fact]
            public async Task PutEvent_IsStillRouted()
            {
                var response = await client.PutAsync("/api/v1/events/16700000-0000-0000-0000-000000000001", content);
                response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "present");
            }

            [Fact]
            public async Task CompleteRoute_IsNotMapped()
            {
                var response = await client.PutAsync("/api/v1/events/16700000-0000-0000-0000-000000000001/complete", content);
                response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            }

            [Fact]
            public async Task PutEvent_ReturnsOk()
            {
                var response = await client.PutAsync("/api/v1/events/16700000-0000-0000-0000-000000000001", content);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
        """;

    [Fact(DisplayName = "Task 167 f2 controls: a retired sweep route resolves only with ResolvedBy and a ProofTest that pins its absence (round 34 item 4)")]
    public void SweepLedger_RetiredRoutes_NegativeControl_OnlyAPinnedAbsenceResolves()
    {
        const string proofFile = "tests/fake/RetiredRouteProofs.cs";
        string? Read(string path) => path == proofFile ? RetiredProofFixture : ReadRepoFile(path);

        // Simulate the integration state: the route is deleted (absent from the scan) and its waiver is gone. Nothing
        // here depends on whether the route is still on THIS branch, so the control survives the deletions landing.
        const string seededReason = "Seeded by the round-34 item 4 control: a waiver left behind on a deleted sweep route.";
        List<string> Ledger(string route, string? resolvedBy, string? method, IReadOnlyList<Assessment>? routes = null, bool keepWaiver = false)
        {
            var assessments = routes ?? RealAssessments.Value.Where(a => a.Key != route).ToList();
            var waivers = Waivers.Where(w => w.Route != route).ToList();
            if (keepWaiver)
            {
                waivers.Add(Pending(route, "159", Gap.NoDecision, seededReason));
            }

            var ledger = SweepFindings.Select(e => e.Route == route
                ? e with { ResolvedBy = resolvedBy, ProofTest = method is null ? null : $"{proofFile}::{method}" }
                : e).ToList();
            var label = SweepFindings.Single(e => e.Route == route).SweepId + " " + route;
            return LedgerViolations(assessments, waivers, ledger, Read)
                .Where(v => v.StartsWith(label + " ", StringComparison.Ordinal) || v.StartsWith(label + ":", StringComparison.Ordinal))
                .ToList();
        }

        // POSITIVE — each real shape: an InlineData theory (159), a verb-named client call with a literal and with an
        // interpolated path (160), and an endpoint-table read over a RetiredRoutes array (164).
        Assert.Empty(Ledger("PUT /api/v1/events/{id:guid}", "159", "DeletedRoutes_AreNotMapped_AndReachNothing"));
        Assert.Empty(Ledger("DELETE /api/v1/events/{id:guid}", "159", "DeletedRoutes_AreNotMapped_AndReachNothing"));
        Assert.Empty(Ledger("POST /api/dataverse/fetch", "160", "PostFetch_IsNotMapped_ForAnAuthenticatedCaller"));
        Assert.Empty(Ledger("GET /api/dataverse/record/{entityLogicalName}/{id:guid}", "160", "GetRecord_IsNotMapped_ForAnAuthenticatedCaller"));
        Assert.Empty(Ledger("GET /api/ai/prompts", "164", "RetiredRoutes_AreAbsentFromTheEndpointTable"));
        Assert.Empty(Ledger("PUT /api/ai/prompts/{id}", "164", "RetiredRoutes_AreAbsentFromTheEndpointTable"));

        // NEGATIVE — absent with no ResolvedBy (the pre-f2 rule, kept for everything that is not a pinned retirement).
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", null, null), v => v.Contains("Do not drop or re-key", StringComparison.Ordinal));

        // NEGATIVE — ResolvedBy set but: no ProofTest; a method that does not exist; a test that asserts PRESENCE; a test
        // of a DIFFERENT verb or path; a test that names the route but asserts no absence.
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", null), v => v.Contains("ProofTest must be", StringComparison.Ordinal));
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "NoSuchTest"), v => v.Contains("is not in", StringComparison.Ordinal));
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "PutEvent_IsStillRouted"), v => v.Contains("never asserts absence", StringComparison.Ordinal));
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "PutEvent_ReturnsOk"), v => v.Contains("never asserts absence", StringComparison.Ordinal));
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "CompleteRoute_IsNotMapped"), v => v.Contains("names no request", StringComparison.Ordinal));
        Assert.Contains(Ledger("POST /api/v1/events/{id:guid}/cancel", "159", "DeletedRoutes_AreNotMapped_AndReachNothing"),
            v => v.Contains("names no request", StringComparison.Ordinal));
        Assert.Contains(Ledger("GET /api/v1/events/{id:guid}/logs", "159", "PostFetch_IsNotMapped_ForAnAuthenticatedCaller"),
            v => v.Contains("names no request", StringComparison.Ordinal));

        // NEGATIVE — the waiver is still there.
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "DeletedRoutes_AreNotMapped_AndReachNothing", keepWaiver: true),
            v => v.Contains("carries no waiver", StringComparison.Ordinal));

        // NEGATIVE — RE-KEYED, not retired: the same verb and path survive under another parameter name.
        var rekeyed = ScanText("Api/Events/EventEndpoints.cs", new[] { "app.MapPut(\"/api/v1/events/{eventId:guid}\", H).RequireAuthorization();" })
            .Select(r => Assess(r, Array.Empty<HandlerDecision>()));
        var withTwin = RealAssessments.Value.Where(a => a.Key != "PUT /api/v1/events/{id:guid}").Concat(rekeyed).ToList();
        Assert.Contains(Ledger("PUT /api/v1/events/{id:guid}", "159", "DeletedRoutes_AreNotMapped_AndReachNothing", withTwin),
            v => v.Contains("RE-KEYED", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 167 f2 controls: admin credit resolves a sweep entry only in a SystemAdmin or SPE-admin AdminOnlyRoutes group (round 34 item 5)")]
    public void SweepLedger_AdminPolicy_NegativeControl_OnlyAnAdminPolicyGroupResolves()
    {
        const string route = "POST /api/admin/record-matching/sync";   // S-47, owned by 165 — not under /api/spe/
        const string proof = "tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs::SweepLedger_AdminPolicy_NegativeControl_OnlyAnAdminPolicyGroupResolves";
        const string reason = "Seeded by the round-34 item 5 control: a record-matching admin route gated as an operator surface.";

        Assessment Gated(string chain) => ScanText("Api/Admin/RecordMatchingAdminEndpoints.cs",
                new[] { $"app.MapPost(\"/api/admin/record-matching/sync\", H).RequireAuthorization(){chain};" })
            .Select(r => Assess(r, Array.Empty<HandlerDecision>())).Single();

        List<string> Ledger(Assessment gated, IReadOnlyList<AdminOnlyGroup> groups)
        {
            var routes = RealAssessments.Value.Where(a => a.Key != route).Append(gated).ToList();
            var ledger = SweepFindings.Select(e => e.Route == route ? e with { ResolvedBy = "165", ProofTest = proof } : e).ToList();
            return LedgerViolations(routes, Waivers.Where(w => w.Route != route).ToList(), ledger, adminOnly: groups)
                .Where(v => v.StartsWith("S-47 ", StringComparison.Ordinal)).ToList();
        }

        // The real set without this route (task 165 may pin it for real at integration), plus a seeded group for it.
        var withoutRoute = AdminOnlyRoutes.Select(g => g with { Routes = g.Routes.Where(r => r != route).ToArray() }).ToList();
        IReadOnlyList<AdminOnlyGroup> With(string mechanism)
            => withoutRoute.Append(new AdminOnlyGroup("Api/Admin/RecordMatchingAdminEndpoints.cs", mechanism, reason, new[] { route })).ToList();

        var systemAdmin = Gated(".RequireAuthorization(\"SystemAdmin\")");
        Assert.Equal(Credit.AdminOnly, systemAdmin.Credit);

        // POSITIVE — a NON-SPE route gated by SystemAdmin and pinned in a SystemAdmin group resolves (before f2 only
        // "/api/spe/" keys could); the SPE admin filter does too.
        Assert.Empty(Ledger(systemAdmin, With(SystemAdminPolicy)));
        Assert.Empty(Ledger(Gated(".AddSpeAdminAuthorizationFilter()"), With(SpeAdminPolicy)));
        Assert.Empty(AdminOnlyMechanismViolations(new[] { systemAdmin }, With(SystemAdminPolicy).TakeLast(1).ToList()));

        // NEGATIVE — not in the pinned set; in a group whose mechanism is not an admin POLICY (the RAG machine credential,
        // the registration approver role); in a SystemAdmin group while the route carries only the RAG key.
        Assert.Contains(Ledger(systemAdmin, withoutRoute), v => v.Contains("not in AdminOnlyRoutes", StringComparison.Ordinal));
        var ragKey = Gated(".RequireAuthorization(AuthPolicies.RagApiKey)");
        Assert.Contains(Ledger(ragKey, With(RagApiKeyCredential)), v => v.Contains("not an admin policy for a sweep entry", StringComparison.Ordinal));
        Assert.Contains(Ledger(Gated(".AddRegistrationAuthorizationFilter()"), With(RegistrationApproverRole)),
            v => v.Contains("not an admin policy for a sweep entry", StringComparison.Ordinal));
        Assert.Contains(Ledger(ragKey, With(SystemAdminPolicy)), v => v.Contains("does not pass Rule A by credit", StringComparison.Ordinal));

        // NEGATIVE — the pin itself: a route added to a SystemAdmin group without SystemAdmin on its chain.
        Assert.Contains(AdminOnlyMechanismViolations(new[] { ragKey }, With(SystemAdminPolicy).TakeLast(1).ToList()),
            v => v.StartsWith(route + " is in the Api/Admin/RecordMatchingAdminEndpoints.cs group gated by", StringComparison.Ordinal));
        Assert.Contains(AdminOnlyMechanismViolations(Array.Empty<Assessment>(), new[] { new AdminOnlyGroup("Api/X.cs", "AddFooFilter", reason, Array.Empty<string>()) }),
            v => v.Contains("is not one of the four admin mechanisms", StringComparison.Ordinal));
        Assert.Contains(AdminOnlyMechanismViolations(Array.Empty<Assessment>(), With(SystemAdminPolicy).Append(
                new AdminOnlyGroup("Api/Y.cs", SpeAdminPolicy, reason, new[] { route })).ToList()),
            v => v.Contains("is listed in 2 AdminOnlyRoutes groups", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 167 controls: the pass-through forms earn no credit; the deciding forms do; an unknown form fails")]
    public void Credit_NegativeControl_DecorativeFormsAreNotCredited()
    {
        string[] decorative =
        {
            ".AddAnalysisRecordAuthorizationFilter()",
            ".AddEndpointFilter<CommunicationAuthorizationFilter>()",
            ".AddAiAuthorizationFilter()",
            ".AddEndpointFilter<AiAuthorizationFilter>()",
            ".AddDataverseAuthorizationFilter(EntitySource.FromRouteValueWithRecord)",
            ".AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody)",
            ".AddTenantAuthorizationFilter()",
            ".AddEndpointFilter<WorkspaceAuthorizationFilter>()",
            ".AddAgentAuthorizationFilter()",
            ".AddCallerPrincipalAuthorizationFilter()",
            ".AddReportingAuthorizationFilter()",
            ".AddSpeAdminTenantScopeFilter()",
            ".RequireAuthorization(AuthPolicies.ExternalCollaboration)",
        };
        foreach (var form in decorative)
        {
            var r = ScanText("Api/Fake/D.cs", new[] { $"app.MapGet(\"/api/fake\", H).RequireAuthorization(){form};" }).Single();
            Assert.True(CreditOf(r) == Credit.None, $"{form} must earn NO credit — it passes through");
        }

        string[] deciding =
        {
            ".AddAnalysisExecuteAuthorizationFilter()",
            ".AddDataverseAuthorizationFilter(EntitySource.FromRouteValue)",
            ".AddDataverseAuthorizationFilter(EntitySource.FromRouteValue, routeKey: \"entityLogicalName\")",
            ".AddBulkDownloadAuthorizationFilter()",
            ".AddSessionOwnershipFilter()",
            ".AddDelegationRuleFilter()",
        };
        foreach (var form in deciding)
        {
            var r = ScanText("Api/Fake/C.cs", new[] { $"app.MapGet(\"/api/fake\", H).RequireAuthorization(){form};" }).Single();
            Assert.True(CreditOf(r) == Credit.PerResource, $"{form} must be credited per resource");
        }

        // Fail closed: an unknown authorization-shaped form earns no credit AND fails the census naming file:line.
        var unknown = ScanText("Api/Fake/U.cs", new[] { "app.MapGet(\"/api/fake\", H)", "    .AddFooAuthorizationFilter();" }).Single();
        var assessed = Assess(unknown, Array.Empty<HandlerDecision>());
        Assert.Equal(Credit.None, assessed.Credit);
        var call = Assert.Single(assessed.UnclassifiedCalls);
        Assert.Equal("Api/Fake/U.cs", call.File);
        Assert.Equal(2, call.Line);

        // An allow-list entry no route attaches fails; a form in two lists fails.
        var used = RealAssessments.Value.SelectMany(a => a.Route.Forms).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(UnusedEntryViolations(used, CreditedForms.Select(c => c.Form).Append("AddEndpointFilter<EntityAccessFilter>")),
            v => v.StartsWith("AddEndpointFilter<EntityAccessFilter>", StringComparison.Ordinal));
        Assert.Empty(UnusedEntryViolations(used, CreditedForms.Select(c => c.Form)));
    }

    [Fact(DisplayName = "Task 167 controls: admin credit is exactly four mechanisms, and the admin pin fires both ways")]
    public void Admin_NegativeControl_PinFiresAndOnlyFourMechanismsCount()
    {
        Assert.Equal(new[]
        {
            "AddRegistrationAuthorizationFilter", "AddSpeAdminAuthorizationFilter",
            "RequireAuthorization(\"SystemAdmin\")", "RequireAuthorization(AuthPolicies.RagApiKey)",
        }, AdminMechanisms.Select(a => a.Form).OrderBy(f => f, StringComparer.Ordinal));

        // "added": a SystemAdmin route that is not in the pin.
        var sysAdmin = ScanText("Api/Fake/A.cs", new[] { "app.MapPost(\"/api/fake/admin\", H).RequireAuthorization(\"SystemAdmin\");" }).Single();
        var withNew = RealAssessments.Value.Append(Assess(sysAdmin, Array.Empty<HandlerDecision>())).ToList();
        var (added, _) = PinDiff(withNew.Where(a => a.Credit == Credit.AdminOnly).Select(a => a.Key), AdminOnlyRoutes.SelectMany(g => g.Routes));
        Assert.Equal(new[] { "POST /api/fake/admin" }, added);

        // "removed": a pinned SPE config route that gains a per-resource filter, and its InsufficientDecision waiver
        // goes stale with it.
        var config = ScanText("Api/Fake/S.cs", new[]
        {
            "var group = app.MapGroup(\"/api/spe\").RequireAuthorization().AddSpeAdminAuthorizationFilter().AddSpeAdminTenantScopeFilter();",
            "var configs = group.MapGroup(\"/configs\");",
            "configs.MapGet(\"/{id:guid}\", Get).AddEntityAccessFilter();",
        }).Select(r => Assess(r, Array.Empty<HandlerDecision>())).Single();
        Assert.Equal("GET /api/spe/configs/{id:guid}", config.Key);
        var swapped = RealAssessments.Value.Select(a => a.Key == config.Key ? config : a).ToList();
        var (_, removed) = PinDiff(swapped.Where(a => a.Credit == Credit.AdminOnly).Select(a => a.Key), AdminOnlyRoutes.SelectMany(g => g.Routes));
        Assert.Equal(new[] { "GET /api/spe/configs/{id:guid}" }, removed);
        Assert.Contains(WaiverViolations(swapped, Waivers), v => v.StartsWith("GET /api/spe/configs/{id:guid} ", StringComparison.Ordinal) && v.Contains("fingerprint changed"));

        // NOT admin: the Reporting role filter and the ExternalCollaboration scheme policy are flagged, not admin.
        foreach (var form in new[] { ".AddReportingAuthorizationFilter()", ".RequireAuthorization(AuthPolicies.ExternalCollaboration)" })
        {
            var r = ScanText("Api/Fake/R.cs", new[] { $"app.MapGet(\"/api/fake/r\", H){form};" }).Single();
            Assert.Equal(Credit.None, CreditOf(r));
            Assert.Single(RuleAViolations(new[] { Assess(r, Array.Empty<HandlerDecision>()) }, Array.Empty<Waiver>()));
        }
    }

    [Fact(DisplayName = "Task 167 controls: a HandlerDecision is credited only when the seam is really reached")]
    public void HandlerDecision_NegativeControl_EachBrokenDeclarationFails()
    {
        // POSITIVE, on the real code: GET /api/documents/{documentId}/permissions (AuthorizationService in the handler).
        var permissions = RealAssessments.Value.Single(a => a.Key == "GET /api/documents/{documentId}/permissions");
        Assert.Equal(Credit.HandlerDecision, permissions.Credit);
        Assert.Null(permissions.HandlerProblem);

        // POSITIVE: a hop into src/server/shared resolves, it is not refused. The fixture handler calls the REAL
        // Spaarke.Core AuthorizationService.GetCallerRecordAccessAsync, whose body reaches IAccessDataSource through its
        // _accessDataSource field (Spaarke.Core/Auth/AuthorizationService.cs:14, :281).
        const string coreHop = """
            public static class CoreHopEndpoints
            {
                public static void MapCoreHop(this IEndpointRouteBuilder app)
                {
                    app.MapGet("/api/fake/core/{id}", Handle).RequireAuthorization();
                }

                private static Task<IResult> Handle(Guid id, AuthorizationService auth) => auth.GetCallerRecordAccessAsync(id);
            }
            """;
        var coreRoute = ScanFixtures(new[] { ("Api/Fake/CoreHop.cs", coreHop), ("Program.cs", "var app = builder.Build();\napp.MapCoreHop();") },
            "Api/Fake/CoreHop.cs").Single();
        Assert.Null(VerifyHandlerDecision(
            new HandlerDecision(coreRoute.Key, "Handle", "IAccessDataSource", new[] { "AuthorizationService.GetCallerRecordAccessAsync" }, "x"),
            coreRoute).Problem);
        Assert.Contains(ServerTypes.Value.Named("AuthorizationService"), t => t.Unit.Path.StartsWith("src/server/shared/Spaarke.Core/", StringComparison.Ordinal));

        const string endpoint = """
            public static class FakeEndpoints
            {
                public static void MapFake(this IEndpointRouteBuilder app)
                {
                    app.MapGet("/api/fake/{id}", Handle).RequireAuthorization();
                }

                private static Task<IResult> Handle(Guid id, FakeService svc)
                {
                    // AuthorizationService is mentioned only in this comment.
                    return svc.LoadAsync(id);
                }
            }

            public class FakeService
            {
                private readonly AuthorizationService _auth;
                public Task<IResult> LoadAsync(Guid id) => _auth.CheckAsync(id);
                public Task<IResult> LoadAsync(string id) => Task.FromResult(Results.Ok());
            }

            public interface IFakeService { Task<IResult> LoadAsync(Guid id); }
            """;
        var units = new[] { ("Api/Fake/FakeEndpoints.cs", endpoint), ("Program.cs", "var app = builder.Build();\napp.MapFake();") };
        var route = ScanFixtures(units, "Api/Fake/FakeEndpoints.cs").Single();
        var fixtureUnits = new[] { new SourceUnit("Api/Fake/FakeEndpoints.cs", endpoint) };

        string? Problem(HandlerDecision d) => VerifyHandlerDecision(d, route, fixtureUnits).Problem;

        // The handler body lacks the seam (it is only in a comment).
        Assert.Contains("is not reached", Problem(new HandlerDecision(route.Key, "Handle", "AuthorizationService", Array.Empty<string>(), "x")));
        // A handler that does not exist.
        Assert.Contains("not 'NoSuchHandler'", Problem(new HandlerDecision(route.Key, "NoSuchHandler", "AuthorizationService", Array.Empty<string>(), "x")));
        // A seam outside both vocabularies.
        Assert.Contains("neither DecisionServices nor CallerContextSeams", Problem(new HandlerDecision(route.Key, "Handle", "FakeService", Array.Empty<string>(), "x")));
        // A hop naming an interface.
        Assert.Contains("INTERFACE", Problem(new HandlerDecision(route.Key, "Handle", "AuthorizationService", new[] { "IFakeService.LoadAsync" }, "x")));
        // A hop method with two overloads, only one of which reaches the seam.
        Assert.Contains("1 of 2", Problem(new HandlerDecision(route.Key, "Handle", "AuthorizationService", new[] { "FakeService.LoadAsync" }, "x")));
        // Too many hops.
        Assert.Contains("at most", Problem(new HandlerDecision(route.Key, "Handle", "AuthorizationService", new[] { "A.B", "C.D", "E.F" }, "x")));
    }

    [Fact(DisplayName = "Task 167 control: the same-type expansion finds a helper the body calls, and only that")]
    public void HandlerDecision_SameTypeExpansion_IsOneLevelAndCalledOnly()
    {
        const string source = """
            public class Svc
            {
                private readonly IImpersonatedCommunicationQuery _query;
                public Task Run() => Helper();
                public Task Other() => Task.CompletedTask;
                private Task Helper() => _query.QueryAsync();
                private Task Unused() => _query.QueryAsync();
            }
            """;
        var unit = new SourceUnit("Svc.cs", source);
        Body BodyOf(string name) => Expand(unit, "Svc", PartOf(unit.Methods.Single(m => m.Name == name)), name);

        Assert.True(BodyHasSeam(BodyOf("Run"), "IImpersonatedCommunicationQuery"));     // Run → Helper → _query
        Assert.False(BodyHasSeam(BodyOf("Other"), "IImpersonatedCommunicationQuery"));  // Unused is never called
    }

    [Fact(DisplayName = "Task 167 r1 controls: a seam only in the signature, or borrowed from a sibling's parameter, earns nothing")]
    public void HandlerDecision_NegativeControl_SignatureOnlyAndBorrowedSeamsFail()
    {
        // Each fixture is one endpoint file bound to the root, verified against a declaration that names its handler.
        static string? ProblemOf(string source, string handler, string seam, params string[] hops)
        {
            var units = new[] { ("Api/Fake/SigEndpoints.cs", source), ("Program.cs", "var app = builder.Build();\napp.MapSig();") };
            var route = ScanFixtures(units, "Api/Fake/SigEndpoints.cs").Single();
            return VerifyHandlerDecision(new HandlerDecision(route.Key, handler, seam, hops, "x"), route,
                new[] { new SourceUnit("Api/Fake/SigEndpoints.cs", source) }).Problem;
        }

        static string Endpoint(string registration, string members) => $$"""
            public static class SigEndpoints
            {
                public static void MapSig(this IEndpointRouteBuilder app)
                {
                    {{registration}}
                }

                {{members}}
            }
            """;

        // NEGATIVE (the verifier's fixture): an UNUSED DI parameter of the seam type, the work done by an app-only client.
        Assert.Contains("is not reached", ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", Handle).RequireAuthorization();",
            "private static Task<IResult> Handle(Guid id, AuthorizationService unused, IAppOnlyClient client) => client.ReadAnyRecordAsync(id);"),
            "Handle", "AuthorizationService"));

        // NEGATIVE: the same with a QUALIFIED type name and a block body (the SendToIndex spelling).
        Assert.Contains("is not reached", ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", Handle).RequireAuthorization();",
            "private static async Task<IResult> Handle(Guid id, Spaarke.Core.Auth.AuthorizationService unused, IAppOnlyClient client)\n"
            + "{\n    return await client.ReadAnyRecordAsync(id);\n}"),
            "Handle", "AuthorizationService"));

        // NEGATIVE: an inline lambda whose parameter list carries the unused seam.
        Assert.Contains("is not reached", ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", async (Guid id, AuthorizationService unused, IAppOnlyClient client) => await client.ReadAnyRecordAsync(id)).RequireAuthorization();",
            string.Empty),
            "inline", "AuthorizationService"));

        // NEGATIVE (finding 2): a SIBLING method's parameter `AccessRights rights` is not a member of the type; the
        // handler's own `string rights` is not the seam type.
        Assert.Contains("is not reached", ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{rights}\", Handle).RequireAuthorization();",
            "private static bool Other(AccessRights rights) => rights.HasFlag(AccessRights.Read);\n"
            + "private static Task<IResult> Handle(string rights, IAppOnlyClient client) => client.ReadAnyRecordAsync(rights);"),
            "Handle", "AccessRights"));

        // NEGATIVE: a hop named only in the SIGNATURE (the handler shares the hop method's name and never calls it).
        const string hopService = """
            public class SigLoadService
            {
                private readonly AuthorizationService _auth;
                public Task<IResult> LoadAsync(Guid id) => _auth.CheckAsync(id);
            }
            """;
        var withHop = Endpoint(
            "app.MapGet(\"/api/fake/{id}\", LoadAsync).RequireAuthorization();",
            "private static Task<IResult> LoadAsync(Guid id, IAppOnlyClient client) => client.ReadAnyRecordAsync(id);") + "\n" + hopService;
        Assert.Contains("does not call LoadAsync", ProblemOf(withHop, "LoadAsync", "AuthorizationService", "SigLoadService.LoadAsync"));

        // NEGATIVE: a named-argument LABEL that merely spells the parameter's name is not a use.
        Assert.Contains("is not reached", ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", Handle).RequireAuthorization();",
            "private static Task<IResult> Handle(Guid id, AuthorizationService auth, IAppOnlyClient client) => client.ReadAnyRecordAsync(id, auth: null);"),
            "Handle", "AuthorizationService"));

        // POSITIVE: the seam parameter USED in the body passes — simple and qualified, method and lambda.
        Assert.Null(ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", Handle).RequireAuthorization();",
            "private static Task<IResult> Handle(Guid id, Spaarke.Core.Auth.AuthorizationService auth) => auth.CheckAsync(id);"),
            "Handle", "AuthorizationService"));
        Assert.Null(ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", async ([FromServices] AuthorizationService auth, Guid id) => await auth.CheckAsync(id)).RequireAuthorization();",
            string.Empty),
            "inline", "AuthorizationService"));

        // POSITIVE: a top-level FIELD of the seam type used by a same-type helper the handler calls (one level).
        Assert.Null(ProblemOf(Endpoint(
            "app.MapGet(\"/api/fake/{id}\", Handle).RequireAuthorization();",
            "private static readonly IImpersonatedCommunicationQuery? _query = null;\n"
            + "private static Task<IResult> Handle(Guid id) => Read(id);\n"
            + "private static Task<IResult> Read(Guid id) => _query!.QueryAsync(id);"),
            "Handle", "IImpersonatedCommunicationQuery"));

        // POSITIVE, on the real code: the permissions handler USES its AuthorizationService parameter.
        Assert.Equal(Credit.HandlerDecision, RealAssessments.Value.Single(a => a.Key == "GET /api/documents/{documentId}/permissions").Credit);
        Assert.Equal(Credit.HandlerDecision, RealAssessments.Value.Single(a => a.Key == "POST /api/ai/rag/send-to-index").Credit);
    }

    [Fact(DisplayName = "Task 167 controls: unreadable registrations fail as unparseable, never skipped")]
    public void Scanner_NegativeControl_UnreadableShapesAreUnparseable()
    {
        // A path from a const that does not resolve in the file.
        var constPath = ScanText("Api/Fake/C.cs", new[] { "app.MapGet(Routes.Missing, H).RequireAuthorization();" });
        Assert.True(Assert.Single(constPath).Unparseable);

        // A MapMethods call whose verbs are not in a readable form.
        var methods = ScanText("Api/Fake/M.cs", new[] { "app.MapMethods(\"/x\", HttpVerbs.All, H);" });
        Assert.True(Assert.Single(methods).Unparseable);

        // A route group bound across files by an aggregator that is not pinned.
        var child = """
            public static class ChildEndpoints
            {
                public static RouteGroupBuilder MapChildEndpoints(this RouteGroupBuilder group)
                {
                    group.MapGet("/thing", H);
                    return group;
                }
            }
            """;
        var aggregator = """
            public static class UnknownAggregator
            {
                public static void MapAll(this IEndpointRouteBuilder app)
                {
                    var group = app.MapGroup("/api/unknown").RequireAuthorization();
                    group.MapChildEndpoints();
                }
            }
            """;
        var program = "var app = builder.Build();\napp.MapAll();";
        var bound = ScanFixtures(new[] { ("Api/Child.cs", child), ("Api/UnknownAggregator.cs", aggregator), ("Program.cs", program) }, "Api/Child.cs");
        Assert.Contains(bound, r => r.Unparseable && r.Problem!.Contains("not a pinned aggregator"));

        // POSITIVE: the same shape through a PINNED aggregator resolves to the full key.
        var pinned = ScanFixtures(new[] { ("Api/Child.cs", child), ("Api/UnknownAggregator.cs", aggregator), ("Program.cs", program) },
            "Api/Child.cs", aggregators: new[] { "Api/UnknownAggregator.cs" });
        var resolved = Assert.Single(pinned);
        Assert.Equal("GET /api/unknown/thing", resolved.Key);
        Assert.Equal("Api/UnknownAggregator.cs", resolved.Aggregator);

        // A route builder that is assigned can be extended elsewhere — unparseable.
        var assigned = ScanText("Api/Fake/A.cs", new[] { "var b = app.MapGet(\"/x\", H);", "b.RequireAuthorization();" });
        Assert.True(Assert.Single(assigned).Unparseable);
    }

    [Fact(DisplayName = "Task 167 controls: a duplicate route key fails naming both files; Rule E spares the root builder only")]
    public void DuplicateKeysAndRuleE_NegativeControl()
    {
        // ExternalModuleDataEndpoints scanned WITHOUT its aggregator binding produces "POST /api/dataverse/fetch" —
        // exactly the key of sweep S-09 in Api/Dataverse/FetchEndpoints.cs.
        var unbound = ScanText("Api/ExternalAccess/ExternalModuleDataEndpoints.cs", new[]
        {
            "var data = externalGroup.MapGroup(\"/api/dataverse\");",
            "data.MapPost(\"/fetch\", ExecuteScopedFetchAsync);",
        });
        var duplicates = DuplicateKeys(Real.Live.Concat(unbound));
        var dup = Assert.Single(duplicates);
        Assert.Contains("Api/Dataverse/FetchEndpoints.cs", dup);
        Assert.Contains("Api/ExternalAccess/ExternalModuleDataEndpoints.cs", dup);

        // Rule E: an absolute "/api/..." path on the aggregator-bound group fails ...
        var child = """
            public static class ChildEndpoints
            {
                public static void MapChildEndpoints(this RouteGroupBuilder group, IEndpointRouteBuilder routes)
                {
                    group.MapGet("/api/spe/absolute", H);
                    routes.MapPost("/api/root/webhook", W).AllowAnonymous();
                }
            }
            """;
        var aggregator = """
            public static class Agg
            {
                public static void MapAll(this IEndpointRouteBuilder routes)
                {
                    var group = routes.MapGroup("/api/x").RequireAuthorization();
                    group.MapChildEndpoints(routes);
                }
            }
            """;
        var scanned = ScanFixtures(new[] { ("Api/Child.cs", child), ("Api/Agg.cs", aggregator), ("Program.cs", "var app = builder.Build();\napp.MapAll();") },
            "Api/Child.cs", aggregators: new[] { "Api/Agg.cs" });
        var offenders = RuleEViolations(scanned);
        Assert.Single(offenders);
        Assert.Contains("/api/spe/absolute", offenders[0]);
        // ... while the webhook on the ROOT builder parameter is not flagged, and keeps its own key.
        Assert.Contains(scanned, r => r.Key == "POST /api/root/webhook" && r.Aggregator is null);
    }

    [Fact(DisplayName = "Task 167 controls: each pass-through pin fails when its pass-through is replaced by a decision")]
    public void NonDecidingPins_NegativeControl_FailWhenTheFilterDecides()
    {
        foreach (var entry in NonDecidingAttachments)
        {
            var raw = File.ReadAllText(Path.Combine(BffRoot, entry.FilterFile.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(PassThroughStillPresent(entry.Form, raw), $"{entry.Form}: the real source must still pass through");

            var fixed_ = WithDecision(entry.Form, raw);
            Assert.False(PassThroughStillPresent(entry.Form, fixed_),
                $"{entry.Form}: a fixture of {entry.FilterFile} whose pass-through is replaced by a decision must fail the pin");
        }
    }

    /// <summary>The filter source with its pass-through replaced by a decision — what a fix task's change looks
    /// like to the pin.</summary>
    private static string WithDecision(string form, string raw) => form switch
    {
        "AddAnalysisRecordAuthorizationFilter" => Regex.Replace(raw,
            @"(AuthorizeAnalysisAccessAsync\([^)]*\)\s*\{)",
            "$1 var decided = await _authorizationService.AuthorizeAsync(user, new[] { Guid.Empty }, context.HttpContext, default);"),
        "AddAiAuthorizationFilter" or "AddEndpointFilter<AiAuthorizationFilter>" => Regex.Replace(raw,
            @"if\s*\(\s*documentIds\.Count\s*==\s*0\s*\)\s*\{\s*return\s+await\s+next\s*\(\s*context\s*\)\s*;",
            "if (documentIds.Count == 0) { return Results.Problem(statusCode: 403);"),
        "AddTenantAuthorizationFilter" => Regex.Replace(raw,
            @"(if\s*\(\s*string\.IsNullOrEmpty\(\s*requestedTenantId\s*\)\s*\)\s*\{[^{}]*?)return\s+await\s+next\s*\(\s*context\s*\)\s*;",
            "$1return Results.Problem(statusCode: 403);"),
        "AddSpeAdminTenantScopeFilter" => Regex.Replace(raw,
            @"(if\s*\(\s*!\s*TryReadConfigId\s*\([^)]*\)\s*\)\s*\{\s*)return\s+await\s+next\s*\(\s*context\s*\)\s*;",
            "$1return Results.NotFound();"),
        _ => raw + "\ninternal sealed class SeededDecision { private readonly CallerRecordAccessProbe _probe = null!; "
                 + "private readonly AuthorizationService _authorizationService = null!; void M() { _ = RetrievePrincipalAccess; } }",
    };

    // =============================================================================================
    // THE TASK-167 ACCEPTANCE PINS — the specific routes the task names, asserted so a later edit cannot quietly
    // move one of them
    // =============================================================================================

    [Fact(DisplayName = "Task 167: the named routes carry exactly the classification the task recorded")]
    public void NamedRoutes_CarryTheirRecordedClassification()
    {
        var byKey = RealAssessments.Value.ToDictionary(a => a.Key, StringComparer.Ordinal);
        Waiver? WaiverOf(string key) => Waivers.SingleOrDefault(w => w.Route == key);

        // Schema routes credited through EntitySource.FromRouteValue need no waiver.
        foreach (var key in new[]
                 {
                     "GET /api/dataverse/metadata/{entityLogicalName}",
                     "GET /api/dataverse/gridconfigurations/{entityLogicalName}",
                     "GET /api/dataverse/savedqueries/{entityLogicalName}",
                 })
        {
            Assert.Equal(Credit.PerResource, byKey[key].Credit);
            Assert.Null(WaiverOf(key));
        }

        // The sweep's refuted routes.
        foreach (var key in new[]
                 {
                     "POST /api/finance/matters/{matterId:guid}/recalculate",
                     "POST /api/finance/projects/{projectId:guid}/recalculate",
                     "POST /api/v1/external-access/revoke",
                 })
        {
            Assert.Equal(Credit.PerResource, byKey[key].Credit);
        }

        Assert.Null(WaiverOf("POST /api/finance/matters/{matterId:guid}/recalculate"));
        Assert.Null(WaiverOf("POST /api/finance/projects/{projectId:guid}/recalculate"));

        // Owner round 12 item 9 (overrides the AC's "revoke: no waiver"): CREDITED routes task 166 must fix carry a
        // Pending InsufficientDecision waiver owned by 166, so the guard tracks them until 166 resolves them.
        foreach (var key in new[] { "POST /api/v1/external-access/revoke", "POST /api/office/todo", "POST /api/v1/external-access/close-project" })
        {
            var w = WaiverOf(key)!;
            Assert.Equal((WaiverKind.Pending, "166", Gap.InsufficientDecision), (w.Kind, w.OwningTask, w.Gap));
            Assert.Equal(Credit.PerResource, byKey[key].Credit);
        }

        // Owner round 12 item 1: the three probes that relied on "a response fixed in source" now carry the
        // mandatory rate limit their AnonymousByDesign waiver names — and owner round 14 item 1 makes it the dedicated
        // per-IP "health-probe" policy, not the shared 10/min "anonymous" one.
        foreach (var key in new[] { "GET /healthz", "GET /healthz/catalog", "GET /ping" })
        {
            Assert.Equal(PermanentBasis.AnonymousByDesign, WaiverOf(key)!.Basis);
            Assert.Contains(byKey[key].Route.Chain, c => c.Name == "RequireRateLimiting" && c.Args.Trim() == "\"health-probe\"");
            Assert.DoesNotContain(byKey[key].Route.Chain, c => c.Name == "RequireRateLimiting" && c.Args.Trim() == "\"anonymous\"");
            Assert.Contains("RequireRateLimiting(\"health-probe\")", WaiverOf(key)!.Reason);
        }

        // Owner round 12 item 4: OWNER-COMPARISON — heartbeat meets it (one uniform 404); DELETE pin does not yet
        // (404 vs 403), so it is Pending, owned by 166.
        Assert.Equal(PermanentBasis.OwnerComparison, WaiverOf("POST /api/compose/document/{documentId:guid}/heartbeat")!.Basis);
        var pin = WaiverOf("DELETE /api/memory/pins/{pinId}")!;
        Assert.Equal((WaiverKind.Pending, "166", Gap.NoDecision), (pin.Kind, pin.OwningTask, pin.Gap));

        // Owner round 12 item 6: the five playbook lists are CallerScopedOnly.
        foreach (var key in new[] { "GET /api/ai/playbooks", "GET /api/ai/playbooks/public", "GET /api/ai/playbooks/templates",
                                    "GET /api/ai/chat/playbooks", "GET /api/agent/playbooks" })
        {
            Assert.Equal(PermanentBasis.CallerScopedOnly, WaiverOf(key)!.Basis);
        }

        Assert.Equal("UNOWNED-NEW", WaiverOf("GET /api/ai/chat/context-mappings/analysis/{analysisId}")!.OwningTask);
        Assert.Equal(WaiverKind.Pending, WaiverOf("GET /api/reporting/reports")!.Kind);
        foreach (var run in new[] { "", "/stream", "/detail" })
        {
            Assert.Equal(PermanentBasis.CallerScopedOnly, WaiverOf($"GET /api/ai/playbooks/runs/{{runId:guid}}{run}")!.Basis);
        }

        // The anonymous document read the sweep did not cover.
        var healthDoc = WaiverOf("GET /healthz/dataverse/doc/{id}")!;
        Assert.Equal(WaiverKind.Pending, healthDoc.Kind);
        Assert.Equal(Gap.NoDecision, healthDoc.Gap);
        // Owned by 166, not UNOWNED-NEW: task 166 amendment (a) names this route, and owner round 12 item 8 keeps such
        // findings with 161/166 (it supersedes the AC text "Pending UNOWNED-NEW"; recorded in task 167 round r2).
        Assert.Equal("166", healthDoc.OwningTask);
        Assert.Contains("bff-deploy", healthDoc.Reason);
        Assert.Equal(Credit.Anonymous, byKey["GET /healthz/dataverse/doc/{id}"].Credit);

        // Existing waivers, re-verified.
        Assert.Equal(PermanentBasis.CreateWithNoPriorResource, WaiverOf("PUT /api/obo/me/files/{*path}")!.Basis);
        Assert.Equal(PermanentBasis.ReferenceData, WaiverOf("GET /api/office/search/matter-types")!.Basis);
        Assert.Equal("UNOWNED-NEW", WaiverOf("POST /api/v1/documents")!.OwningTask);
        foreach (var key in new[]
                 {
                     "GET /api/office/communications/by-message-id/{internetMessageId}",
                     "GET /api/office/communications/by-message-id/{internetMessageId}/suggestions",
                     "GET /api/office/communications/{commId:guid}/linked-todos",
                     "GET /api/office/search/entities",
                     "POST /api/ai/rag/send-to-index",
                 })
        {
            Assert.Equal(Credit.HandlerDecision, byKey[key].Credit);
            Assert.Null(WaiverOf(key));
        }

        // The UNOWNED-NEW list is the one in the task note — a change here must change the note.
        Assert.Equal(ExpectedUnownedNewCount, Waivers.Count(w => w.OwningTask == "UNOWNED-NEW"));
    }
}
