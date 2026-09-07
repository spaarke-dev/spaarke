using System.Globalization;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The NFR-04 negative-canary invariant, isolated as a pure function so that its FAILURE direction can
/// itself be proven (see <c>ImpersonationNegativeCanaryTests</c> § "perturbation").
/// </summary>
/// <remarks>
/// <para><b>Why this is a separate type rather than inline assertions.</b> The whole value of this
/// canary is that it goes RED when Dataverse impersonation is inert. A canary whose assertions are
/// written inline in a test that can only be run against a live tenant is a canary nobody has ever
/// seen fail — and an assertion nobody has seen fail is an assertion nobody has verified. Hoisting the
/// comparison into a pure verdict function lets the suite feed it the exact fail-OPEN state
/// (impersonated set == app-only set) on every CI run, with no tenant, and assert that it reports
/// failure. That perturbation check is what makes the gate real today.</para>
///
/// <para><b>The failure it exists to catch</b> (investigation 08 §3): <c>DataverseImpersonation.Apply</c>
/// silently no-ops on a null caller BY DESIGN, and <c>impersonateSystemUserId</c> is an OPTIONAL
/// parameter throughout <c>DataverseWebApiService</c> — so app-only is the silent default of the whole
/// client. A call path that forgets the header compiles, runs, and returns the System-Administrator row
/// set with HTTP 200 and no error. There is no exception to catch and no log line to grep. The ONLY
/// signal is that the impersonated answer stopped being smaller than the app-only answer.</para>
///
/// <para><b>Equality is a failure, never a skip and never a warning</b> (spec NFR-04). If a
/// low-privilege caller's impersonated read returns exactly what the app identity sees, impersonation
/// did nothing. Reporting that as "pass" is worse than having no test, because it converts an unknown
/// into a false assurance that gates a merge.</para>
/// </remarks>
public static class ImpersonationNegativeCanary
{
    /// <summary>How many offending ids to name in a failure message before truncating.</summary>
    private const int MaxIdsInMessage = 5;

    /// <summary>
    /// Compares an app-only row set against the same query issued under impersonation.
    /// </summary>
    /// <param name="appOnlyIds">Ids returned WITHOUT <c>MSCRMCallerID</c> — the control.</param>
    /// <param name="impersonatedIds">Ids returned WITH <c>MSCRMCallerID</c> = the canary user.</param>
    /// <remarks>
    /// Both inputs are normalized to sets first: the primitive returns rows, and a duplicated row would
    /// otherwise let a COUNT comparison pass while the underlying sets are identical.
    /// </remarks>
    public static CanaryOutcome Evaluate(
        IReadOnlyCollection<Guid> appOnlyIds,
        IReadOnlyCollection<Guid> impersonatedIds)
    {
        ArgumentNullException.ThrowIfNull(appOnlyIds);
        ArgumentNullException.ThrowIfNull(impersonatedIds);

        var appOnly = appOnlyIds.ToHashSet();
        var impersonated = impersonatedIds.ToHashSet();

        // ── Guard 1: a vacuous baseline proves nothing. ──────────────────────────────────────────
        // An empty app-only read makes "strictly fewer" unsatisfiable and "subset" trivially true.
        // Left unguarded, a typo'd entity set (which Dataverse answers with an error, but a swallowed
        // one, or an empty collection for a filter that matches nothing) would present as a canary
        // that cannot fail. Refuse to render a verdict instead.
        if (appOnly.Count == 0)
        {
            return CanaryOutcome.Fail(
                CanaryVerdict.VacuousBaseline,
                "The app-only baseline query returned ZERO rows, so the canary comparison proves nothing "
                + "(subset is trivially true and 'strictly fewer' is unsatisfiable). Check the entity set "
                + "name, the $select field, and that the environment actually contains seeded records for "
                + "this entity. Do NOT treat this as a pass.");
        }

        // ── Guard 2: an empty impersonated set is not evidence that impersonation worked. ─────────
        // It satisfies subset AND strictly-fewer, but it is equally consistent with a broken query, a
        // disabled canary user, or a caller with no Read privilege at all — none of which exercise the
        // header. The canary contract is that the user owns exactly K > 0 records, so zero is a
        // misconfiguration. (Strengthening over investigation 08 §3d, which specified only subset +
        // strictly-fewer; it strengthens the gate and weakens nothing.)
        if (impersonated.Count == 0)
        {
            return CanaryOutcome.Fail(
                CanaryVerdict.EmptyImpersonatedSet,
                "The impersonated query returned ZERO rows. That satisfies 'subset' and 'strictly fewer' "
                + "arithmetically but is NOT evidence the impersonation header was applied — it looks "
                + "identical to a disabled canary user, a revoked Read privilege, or a malformed query. "
                + "The canary user is contracted to own K > 0 seeded records; verify its security role "
                + "and its seeded rows before reading anything into this run.");
        }

        // ── Invariant A: subset. ─────────────────────────────────────────────────────────────────
        // The impersonated caller can never see a row the app identity cannot. If it does, the header
        // is targeting an identity BROADER than the app user — or, more likely, the app user has been
        // narrowed (see investigation 08 §3c: a narrowly-scoped app user silently NARROWS impersonated
        // results, which is the wrong-answer mode this direction detects).
        var extra = impersonated.Except(appOnly).ToArray();
        if (extra.Length > 0)
        {
            return CanaryOutcome.Fail(
                CanaryVerdict.NotASubset,
                $"The impersonated result is NOT a subset of the app-only result: {extra.Length} id(s) "
                + $"appeared under impersonation but not app-only ({Format(extra)}). Either the app "
                + "identity has been narrowed below the canary user (it must stay broadly scoped — "
                + "investigation 08 §3c), or the app-only baseline query is not the SAME query.");
        }

        // ── Invariant B: STRICTLY fewer. This is the header-not-applied catcher. ──────────────────
        if (impersonated.Count == appOnly.Count)
        {
            return CanaryOutcome.Fail(
                CanaryVerdict.Inert,
                $"IMPERSONATION IS INERT: the impersonated read returned the SAME {impersonated.Count} "
                + "row(s) as the app-only read. The MSCRMCallerID header is not being applied, and every "
                + "caller on this path is receiving the app identity's org-wide row set with HTTP 200 and "
                + "no error. Causes to check, in order: (1) the caller systemuserid was null/empty and the "
                + "helper no-opped, (2) an intermediary proxy stripped the header, (3) the BFF app user "
                + "lost prvActOnBehalfOfAnotherUser, (4) the canary user was granted a wider role and is "
                + "no longer low-privilege. This is a MERGE GATE (spec NFR-04) — it fails the build.");
        }

        return CanaryOutcome.Pass(
            $"Impersonation is live: {impersonated.Count} impersonated row(s) is a strict subset of "
            + $"{appOnly.Count} app-only row(s).");
    }

    /// <summary>
    /// Test 2's exactness check: the impersonated set must be EXACTLY the seeded ids the canary user
    /// owns. Subset + strictly-fewer proves the header did something; only exactness proves it did the
    /// RIGHT thing (a header applied to the wrong user also yields a strict subset).
    /// </summary>
    public static CanaryOutcome EvaluateExactness(
        IReadOnlyCollection<Guid> impersonatedIds,
        IReadOnlyCollection<Guid> expectedSeedIds)
    {
        ArgumentNullException.ThrowIfNull(impersonatedIds);
        ArgumentNullException.ThrowIfNull(expectedSeedIds);

        var impersonated = impersonatedIds.ToHashSet();
        var expected = expectedSeedIds.ToHashSet();

        if (expected.Count == 0)
        {
            return CanaryOutcome.Fail(
                CanaryVerdict.VacuousBaseline,
                "No expected seed ids were supplied, so exactness cannot be evaluated. The canary "
                + "contract requires K > 0 seeded records; see tests/integration/auth/README.md.");
        }

        var missing = expected.Except(impersonated).ToArray();
        var unexpected = impersonated.Except(expected).ToArray();

        if (missing.Length == 0 && unexpected.Length == 0)
        {
            return CanaryOutcome.Pass(
                $"The impersonated set is exactly the {expected.Count} seeded id(s).");
        }

        return CanaryOutcome.Fail(
            CanaryVerdict.NotExactlyTheSeededSet,
            "The impersonated set is not exactly the seeded set. "
            + $"Missing (seeded but not returned — the canary user lost access or the rows were "
            + $"reassigned): {Format(missing)}. "
            + $"Unexpected (returned but not seeded — the canary user is NOT low-privilege any more, or "
            + $"the header targeted a different user): {Format(unexpected)}.");
    }

    private static string Format(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return "(none)";
        }

        var shown = string.Join(", ", ids.Take(MaxIdsInMessage));
        return ids.Count <= MaxIdsInMessage
            ? shown
            : string.Create(CultureInfo.InvariantCulture, $"{shown}, … (+{ids.Count - MaxIdsInMessage} more)");
    }
}

/// <summary>Why the canary reached the verdict it did. Every non-<see cref="Scoped"/> value fails the build.</summary>
public enum CanaryVerdict
{
    /// <summary>Impersonation demonstrably narrowed the result — the only passing verdict.</summary>
    Scoped,

    /// <summary>Impersonated == app-only. The header did nothing. The fail-OPEN state NFR-04 exists for.</summary>
    Inert,

    /// <summary>Impersonation returned rows the app identity cannot see.</summary>
    NotASubset,

    /// <summary>The app-only baseline was empty, so no comparison is meaningful.</summary>
    VacuousBaseline,

    /// <summary>The impersonated read returned nothing — arithmetically "narrower", but not evidence.</summary>
    EmptyImpersonatedSet,

    /// <summary>The header narrowed the result, but not to the seeded set (wrong user, or drifted seed).</summary>
    NotExactlyTheSeededSet
}

/// <summary>The canary's verdict plus the operator-actionable message that belongs in the build log.</summary>
/// <param name="Verdict">The classification.</param>
/// <param name="Message">What an operator should check, named specifically enough to act on.</param>
public sealed record CanaryOutcome(CanaryVerdict Verdict, string Message)
{
    /// <summary>True only for <see cref="CanaryVerdict.Scoped"/>. Every other verdict fails the gate.</summary>
    public bool Passed => Verdict == CanaryVerdict.Scoped;

    internal static CanaryOutcome Pass(string message) => new(CanaryVerdict.Scoped, message);

    internal static CanaryOutcome Fail(CanaryVerdict verdict, string message) => new(verdict, message);
}
