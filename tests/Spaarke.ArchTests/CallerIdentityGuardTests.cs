using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The executable form of the 2026-08-26 caller-identity defect: no type under <c>src/server/**</c>
/// may read an identity claim directly, and no ownership predicate may be gated on a
/// <c>Guid.TryParse</c> whose failure path drops it.
///
/// <para><b>Why this exists.</b> On 2026-08-26 every document request in dev returned 403 while 11,932
/// tests stayed green. Under inbound claim-type mapping — which this app leaves ON — .NET routes the
/// token's <c>sub</c> to <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> and its
/// <c>oid</c> to a long schema URI. Entra's <c>sub</c> is <i>pairwise</i>: stable per (user,
/// application) and resolvable nowhere else. Authorization code that read it was asking Dataverse
/// about a principal that cannot exist. 38 sites across four spellings were affected, and the two that
/// looked MOST correct were among the broken ones:</para>
/// <code>
///   FindFirst(NameIdentifier)                        // -> sub
///   FindFirst(NameIdentifier) ?? FindFirst("oid")    // -> sub; the ?? tail is DEAD
///   FindFirst("oid") ?? FindFirst(NameIdentifier)    // -> sub; short "oid" DOESN'T EXIST under mapping
///   FindFirst("oid")                                 // -> null
/// </code>
///
/// <para><b>Why a guard and not a convention.</b> The sweep that removed those reads also removed 23
/// <i>dead</i> <c>?? NameIdentifier</c> tails — sites that were correct only because Entra always
/// issues <c>oid</c>. Every one of them was written by someone copying a neighbouring file. A prose
/// rule does not survive that; a red build does. Same instrument, and same reasoning, as
/// <see cref="CredentialGuardTests"/>.</para>
///
/// <para><b>Two rules, because there are two defect classes.</b> RULE 1 covers reading the wrong claim.
/// RULE 2 covers misusing a correctly-read one, which is what actually produced the two DISCLOSURES
/// (<c>PortfolioService</c>, <c>WorkspaceLayoutService</c>) — neither of which contains a claim read at
/// all, and neither of which RULE 1 or any claim-read census could ever have found.</para>
/// </summary>
public class CallerIdentityGuardTests
{
    // =============================================================================================
    // THE ALLOWLIST
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. First ask whether the site needs to read a claim at all. Almost never. The BFF has exactly
    //      one identity primitive — CallerResolution — with three named entry points:
    //        ResolveObjectId       the caller's Entra oid, or null => answer 401. Use for AUTHORIZATION.
    //        ResolveObjectIdGuid   the same, parsed.
    //        ResolveOpaqueCallerKey  accepts `sub`. Use ONLY for partition / idempotency / cache keys.
    //      If you are authorizing, you want ResolveObjectId and you need no entry here.
    //
    //   2. An entry is justified only when the file IS an identity primitive, or belongs to another
    //      project that owns its own resolution (see the unified-access-control-r2 rows).
    //
    //   3. Write a reason a reviewer two years from now can evaluate. "Legacy" is not a reason.
    //
    //   4. Adding a fourth resolver to the BFF is a coordination event, not a local decision —
    //      unified-access-control-r2 maintains a census of caller-identity primitives across the
    //      solution. Tell them before adding one.
    // =============================================================================================
    public static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
    {
        ["Infrastructure/Authentication/CallerResolution.cs"] =
            "THE identity primitive. It is the one place these claims are read, by construction.",

        ["Infrastructure/ExternalAccess/CallerPrincipalResolver.cs"] =
            "Owned by unified-access-control-r2: resolves the external-access principal (plane, contact, "
            + "accessible sets), a different question from 'what is the caller's oid'. Their census row 3.",

        ["Infrastructure/Logging/AuditEnrichmentMiddleware.cs"] =
            "Owned by unified-access-control-r2 (their task 081). Their coordination doc of 2026-08-27 "
            + "explicitly cleared this file as conflict-free; rewriting it here would manufacture the "
            + "collision their file register ruled out.",

        ["services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs"] =
            "Separate service, separate assembly — cannot reference Sprk.Bff.Api's CallerResolution. "
            + "Reads the schema URI FIRST, which is the form that exists under inbound mapping, so it is "
            + "correct as written. Revisit if the primitive is ever hoisted into a shared library.",

        ["services/Sprk.Provisioning.ControlPlane.Api/Middleware/AuditLogMiddleware.cs"] =
            "Same as RunsEndpoints.cs above: separate assembly, schema-URI-first, correct as written.",
    };

    // `oid` short form, the long schema URI, NameIdentifier, and bare `sub` — via FindFirst or
    // FindFirstValue. This is the population the 2026-08-27 sweep drove to zero outside the allowlist.
    private static readonly Regex IdentityClaimRead = new(
        """FindFirst(Value)?\s*\(\s*("oid"|"sub"|"http://schemas\.microsoft\.com/identity/claims/objectidentifier"|ClaimTypes\.NameIdentifier|\w*OidClaimType|ObjectIdClaimType)\s*\)""",
        RegexOptions.Compiled);

    [Fact]
    public void Rule1_NoDirectIdentityClaimReadOutsideTheAllowlist()
    {
        var offenders = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var rel = Relative(file);
            if (Allowlist.Keys.Any(a => rel.EndsWith(a, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripCommentRespectingStrings(lines[i]);
                if (IdentityClaimRead.IsMatch(code))
                {
                    offenders.Add($"{rel}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Direct identity-claim read(s) found outside the allowlist. Use CallerResolution:\n"
            + "  ResolveObjectId(user)        -> the caller's Entra oid; null means 401, NOT 403.\n"
            + "  ResolveOpaqueCallerKey(user) -> accepts `sub`; ONLY for partition/idempotency/cache keys.\n"
            + "Reading NameIdentifier for identity yields `sub`, which matches no Dataverse systemuser.\n\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Rule2_NoOwnershipPredicateGatedOnAGuidTryParse()
    {
        // The shape that produced BOTH disclosures. The guard wrapped the FILTER rather than the query,
        // so a caller whose id would not parse had the ownership condition REMOVED instead of denied:
        //
        //     if (Guid.TryParse(userId, out var g))                   // always false for `sub`
        //         query.Criteria.AddCondition("ownerid", Equal, g);   // therefore never added
        //
        // The query then ran unscoped on the app identity, which Dataverse row-level security does not
        // trim — so every caller received every row. Note this defect is invisible to Rule 1: neither
        // offending file read a claim at all.
        var offenders = new List<string>();
        var ownerCondition = new Regex(
            """AddCondition\s*\(\s*"(ownerid|owninguser|createdby)"|\bownerid\b\s*==|==\s*\bownerid\b""",
            RegexOptions.Compiled);

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var rel = Relative(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("Guid.TryParse", StringComparison.Ordinal))
                {
                    continue;
                }

                // Only an `if`-guard is dangerous: `var ok = Guid.TryParse(...)` followed by an explicit
                // deny is fine, and so is a TryParse used for a route argument.
                if (!lines[i].Contains("if ", StringComparison.Ordinal))
                {
                    continue;
                }

                // Look at the guarded block: the next few lines, up to its closing brace.
                var window = string.Join("\n", lines.Skip(i + 1).Take(6).Select(StripCommentRespectingStrings));
                if (ownerCondition.IsMatch(window))
                {
                    offenders.Add($"{rel}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "An ownership predicate is gated on a Guid.TryParse. If the parse fails the predicate is\n"
            + "DROPPED, not denied — the query runs unscoped and discloses every row. Resolve the caller\n"
            + "first and fail closed:\n\n"
            + "    var systemUserId = await resolver.ResolveSystemUserIdAsync(oid, ct);\n"
            + "    if (systemUserId is not { } owner) return Array.Empty<T>();   // deny, do not widen\n"
            + "    query.Criteria.AddCondition(\"ownerid\", ConditionOperator.Equal, owner);\n\n"
            + "Note `ownerid` holds a Dataverse systemuserid, NOT an Entra oid — comparing the two\n"
            + "matches nothing, which is an outage rather than a disclosure but still wrong.\n\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryAllowlistEntryCarriesAReason()
    {
        var blank = Allowlist.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();

        Assert.True(blank.Count == 0,
            "Allowlist entries must carry a written justification — an entry without one is how the next "
            + "audit concludes the exemption is permanent:\n" + string.Join("\n", blank));
    }

    [Fact]
    public void EveryAllowlistEntryStillExists()
    {
        // A stale allowlist silently widens: a path that no longer exists exempts nothing today, but
        // will exempt a NEW file that happens to land there.
        var missing = Allowlist.Keys
            .Where(k => !SourceScan.ServerSourceFiles().Any(f => Relative(f).EndsWith(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(missing.Count == 0,
            "Allowlisted file(s) no longer exist. Remove the entries:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void NegativeControl_TheRuleActuallyMatchesTheDefect()
    {
        // Without this, a regex that matches nothing would make Rule 1 pass vacuously — which is the
        // same failure mode as the fixtures that let the original defect through a green suite.
        Assert.Matches(IdentityClaimRead, """user.FindFirst(ClaimTypes.NameIdentifier)?.Value""");
        Assert.Matches(IdentityClaimRead, """user.FindFirst("oid")?.Value""");
        Assert.Matches(IdentityClaimRead, """user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")""");
        Assert.DoesNotMatch(IdentityClaimRead, """CallerResolution.ResolveObjectId(user)""");
        Assert.DoesNotMatch(IdentityClaimRead, """user.FindFirst("tid")?.Value""");
    }

    [Fact]
    public void NegativeControl_CommentStrippingRespectsStringLiterals()
    {
        // This bit the 2026-08-27 analysis itself. SourceScan.StripLineComment cuts at the first "//"
        // anywhere in the line, so the "//" inside the schema URI truncates it to `"http:` — and sites
        // that DID check the correct claim were reported as broken. Confident false positives.
        const string line = """var x = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;""";

        Assert.Equal(line, StripCommentRespectingStrings(line));
        Assert.Equal("var y = 1; ", StripCommentRespectingStrings("var y = 1; // FindFirst(\"oid\") in a comment"));
    }

    /// <summary>
    /// Removes a <c>//</c> line comment WITHOUT cutting inside a string literal.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>SourceScan.StripLineComment</c>, which is string-literal-blind. That helper is
    /// left alone rather than fixed here: other guards depend on its current behaviour and changing
    /// shared scan infrastructure is not this file's job. Flagged for a follow-up.
    /// </remarks>
    internal static string StripCommentRespectingStrings(string line)
    {
        var inString = false;
        var inChar = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\\' && (inString || inChar))
            {
                i++;                       // skip the escaped character
                continue;
            }

            if (c == '"' && !inChar)
            {
                inString = !inString;
            }
            else if (c == '\'' && !inString)
            {
                inChar = !inChar;
            }
            else if (c == '/' && !inString && !inChar && i + 1 < line.Length && line[i + 1] == '/')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');
}
