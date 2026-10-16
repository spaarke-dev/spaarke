using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #1576: every lookup that maps an Entra <c>oid</c> to a Dataverse <c>systemuser</c> — a filter on
/// <c>systemuser.azureactivedirectoryobjectid</c> — also filters <c>isdisabled</c>, or carries an explicit
/// <c>includes disabled: &lt;reason&gt;</c> comment where resolving a disabled user is the point.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A disabled systemuser keeps its oid. A lookup without the filter resolves a disabled account
/// exactly like an active one, so its rights are computed, it is stamped as a sender or creator, or it scopes a business
/// unit. No behavioural test notices: every fixture's users are active. The rule is stated on
/// <c>ISystemUserIdentityResolver</c> (the shared resolver new code should use); this guard enforces it on the ad-hoc
/// copies. Enforced by: this guard (ArchTests, CI and the Stop hook), per CLAUDE.md §16.</para>
/// <para><b>What counts as a lookup.</b> Crude by design (<see cref="SourceScan"/>, line comments stripped): an OData
/// <c>azureactivedirectoryobjectid eq</c> clause, a <c>"azureactivedirectoryobjectid", ConditionOperator</c> condition
/// (also through a <c>const string</c> alias of the column name), or a FetchXML <c>condition attribute=</c> on the
/// column. Selecting the column (<c>ColumnSet</c>, <c>$select</c>) or reading it from a row is not a lookup. Only
/// <c>systemuser</c> carries this column in Spaarke's schema use (contacts bind through <c>sprk_externalobjectid</c>,
/// guarded by <c>ContactAadObjectIdColumnGuardTests</c>), so the scan does not need to identify the entity.</para>
/// <para><b>What satisfies it.</b> <c>isdisabled</c> in the code within <see cref="Window"/> lines of the lookup, or an
/// <c>includes disabled: &lt;reason&gt;</c> comment on the lookup line or up to <see cref="MarkerLookback"/> lines above
/// it.</para>
/// <para><b>Ratchet.</b> <see cref="Baseline"/> lists the lookups left unfixed because another open PR edits the file
/// (fixing them here would conflict). A new violation fails; a baseline entry that no longer violates also fails, so the
/// list only shrinks.</para>
/// </remarks>
public class SystemUserOidLookupDisabledGuardTests
{
    /// <summary>Lines either side of a lookup searched for its <c>isdisabled</c> condition.</summary>
    internal const int Window = 12;

    /// <summary>Lines above a lookup searched for an <c>includes disabled:</c> marker.</summary>
    internal const int MarkerLookback = 6;

    /// <summary>
    /// Known unfiltered lookups, by file, each with the reason it is not fixed yet. Issue #1576 fixed every other site;
    /// these wait only for the named PR to merge.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Baseline = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/ExternalAccess/AssignedAccessMaterializer.cs"] =
            "open PR #2016 (fix/uac-1620-unshare-owner) edits this file",
        ["src/server/api/Sprk.Bff.Api/Services/NotificationService.cs"] =
            "open PRs #1507 (task/ontology-131b-delete-notification-code) and #1494 (work/132-notification-delivery-correctness) edit this file",
    };

    private const string Column = "azureactivedirectoryobjectid";

    private static readonly Regex[] LookupPatterns =
    {
        new(Column + @"\s+eq\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("\"" + Column + "\"" + @"\s*,\s*ConditionOperator\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"attribute\s*=\s*(?:""""|\\""|')" + Column, RegexOptions.Compiled | RegexOptions.CultureInvariant),
    };

    private static readonly Regex ColumnAlias = new(
        @"\bconst\s+string\s+(?<name>\w+)\s*=\s*""" + Column + @"""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsDisabled = new(@"\bisdisabled\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Marker = new(@"includes disabled:\s*\S", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The 1-based lines of <paramref name="rawLines"/> holding an oid → systemuser lookup that neither filters
    /// <c>isdisabled</c> nor carries the marker. Empty when the rule holds.</summary>
    internal static IReadOnlyList<int> Violations(IReadOnlyList<string> rawLines)
    {
        var code = SourceScan.CodeText(rawLines);
        var codeLines = code.Split('\n');

        var patterns = LookupPatterns.ToList();
        foreach (Match alias in ColumnAlias.Matches(code))
        {
            var name = Regex.Escape(alias.Groups["name"].Value);
            patterns.Add(new Regex(@"\b" + name + @"\s*,\s*ConditionOperator\b", RegexOptions.CultureInvariant));
            patterns.Add(new Regex(@"\{" + name + @"\}\s+eq\b", RegexOptions.CultureInvariant));
        }

        var lookupLines = patterns
            .SelectMany(p => p.Matches(code).Select(m => SourceScan.LineOf(code, m.Index)))
            .Distinct()
            .OrderBy(l => l);

        var violations = new List<int>();
        foreach (var line in lookupLines)
        {
            var index = line - 1;
            var filtered = Enumerable.Range(Math.Max(0, index - Window), Math.Min(codeLines.Length, index + Window + 1) - Math.Max(0, index - Window))
                .Any(i => IsDisabled.IsMatch(codeLines[i]));
            var marked = Enumerable.Range(Math.Max(0, index - MarkerLookback), index - Math.Max(0, index - MarkerLookback) + 1)
                .Any(i => i < rawLines.Count && Marker.IsMatch(rawLines[i]));

            if (!filtered && !marked)
            {
                violations.Add(line);
            }
        }

        return violations;
    }

    [Fact(DisplayName = "Issue #1576: every oid → systemuser lookup filters isdisabled or says why it includes disabled users")]
    public void EverySystemUserOidLookup_FiltersIsDisabled_OrCarriesTheMarker()
    {
        var found = SourceScan.ServerSourceFiles()
            .Select(f => (File: SourceScan.Relative(f).Replace('\\', '/'), Lines: Violations(File.ReadAllLines(f))))
            .Where(r => r.Lines.Count > 0)
            .ToList();

        var unexpected = found.Where(r => !Baseline.ContainsKey(r.File))
            .Select(r => $"{r.File}:{string.Join(",", r.Lines)}")
            .ToList();
        var stale = Baseline.Keys.Where(k => found.All(r => r.File != k)).ToList();

        Assert.True(unexpected.Count == 0,
            "These lookups map an Entra oid to a Dataverse systemuser without filtering isdisabled (issue #1576): "
            + string.Join("; ", unexpected) + ". A disabled systemuser keeps its oid, so the lookup resolves it like an "
            + "active user. Add `isdisabled eq false` (OData) or an `isdisabled = false` condition, prefer "
            + "ISystemUserIdentityResolver.ResolveSystemUserIdAsync, or — where a disabled user must resolve — add a "
            + "`// includes disabled: <reason>` comment on or just above the lookup.");
        Assert.True(stale.Count == 0,
            "These baseline entries no longer violate the rule; remove them from SystemUserOidLookupDisabledGuardTests.Baseline "
            + "so the ratchet only shrinks: " + string.Join("; ", stale));
    }

    [Fact(DisplayName = "Issue #1576: every baseline entry names the open PR that keeps it unfixed")]
    public void EveryBaselineEntry_NamesTheOverlappingPullRequest()
    {
        Assert.All(Baseline, entry => Assert.Matches(@"#\d+", entry.Value));
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> MustFire => new()
    {
        // OData through a typed client, no isdisabled clause.
        """
        var rows = await client.QueryAsync<Row>("systemusers",
            filter: $"azureactivedirectoryobjectid eq {oid}", select: "systemuserid", top: 1, cancellationToken: ct);
        """,
        // OData URL.
        """
        var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'&$select=systemuserid";
        """,
        // QueryExpression condition split across lines.
        """
        var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid") };
        query.Criteria.Conditions.Add(new ConditionExpression(
            "azureactivedirectoryobjectid",
            ConditionOperator.Equal,
            oid));
        """,
        // FetchXML in a verbatim string.
        """
        var fetch = $@"<fetch top='1'><entity name='systemuser'><filter>
                          <condition attribute=""azureactivedirectoryobjectid"" operator=""eq"" value=""{oid}""/>
                        </filter></entity></fetch>";
        """,
        // A const alias of the column name.
        """
        private const string OidColumn = "azureactivedirectoryobjectid";
        public void Build(QueryExpression query, Guid oid) => query.Criteria.AddCondition(OidColumn, ConditionOperator.Equal, oid);
        """,
        // The marker without a reason does not count; nor does isdisabled in a COMMENT.
        """
        // includes disabled:
        // (isdisabled is not filtered here)
        var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'";
        """,
    };

    public static TheoryData<string> MustNotFire => new()
    {
        // OData with the clause.
        """
        var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{oid}' and isdisabled eq false&$select=systemuserid";
        """,
        // QueryExpression with the condition on the next statement.
        """
        query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, oid);
        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
        """,
        // The eligibility read: isdisabled selected and checked after the read.
        """
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("businessunitid", "isdisabled", "accessmode"),
            Criteria = { Conditions = { new ConditionExpression(AzureAdObjectIdColumn, ConditionOperator.Equal, oid) } }
        };
        """,
        // A deliberate exception, with its reason.
        """
        // includes disabled: a No Access entry is enforced against disabled users too.
        var filter = string.Join(" or ", oids.Select(o => $"azureactivedirectoryobjectid eq {o}"));
        """,
        // Selecting or reading the column is not a lookup; nor is a comment that quotes a filter.
        """
        var columns = new ColumnSet("domainname", "azureactivedirectoryobjectid");
        var oid = row.GetAttributeValue<Guid?>("azureactivedirectoryobjectid");
        // the lookup is "azureactivedirectoryobjectid eq {oid}"
        """,
    };

    [Theory(DisplayName = "Issue #1576 guard control: an unfiltered oid → systemuser lookup is a violation")]
    [MemberData(nameof(MustFire))]
    public void Control_UnfilteredLookup_IsAViolation(string source)
        => Assert.NotEmpty(Violations(source.Split('\n')));

    [Theory(DisplayName = "Issue #1576 guard control: a filtered, marked or non-lookup use is not a violation")]
    [MemberData(nameof(MustNotFire))]
    public void Control_FilteredMarkedOrNonLookup_IsNotAViolation(string source)
        => Assert.Empty(Violations(source.Split('\n')));
}
