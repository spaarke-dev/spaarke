using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #1576: every lookup that maps an Entra <c>oid</c> to a Dataverse <c>systemuser</c> — a filter on
/// <c>systemuser.azureactivedirectoryobjectid</c> — also excludes disabled users, or carries an explicit
/// <c>includes disabled: &lt;reason&gt;</c> comment where resolving a disabled user is the point.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A disabled systemuser keeps its oid. A lookup that does not exclude it resolves a disabled account
/// exactly like an active one, so its rights are computed, it is stamped as a sender or creator, or it scopes a business
/// unit. No behavioural test notices: every fixture's users are active. The rule is stated on
/// <c>ISystemUserIdentityResolver</c> (the shared resolver new code should use); this guard enforces it on the ad-hoc
/// copies. Enforced by: this guard (ArchTests, CI and the Stop hook), per CLAUDE.md §16.</para>
/// <para><b>What counts as a lookup</b> (code text, line comments stripped, <see cref="SourceScan"/>):</para>
/// <list type="bullet">
///   <item>an OData <c>azureactivedirectoryobjectid eq</c> clause;</item>
///   <item>a <c>"azureactivedirectoryobjectid", ConditionOperator</c> condition, also through a <c>const string</c> alias
///   of the column name;</item>
///   <item>a FetchXML <c>condition attribute=</c> on the column;</item>
///   <item>the alternate-key read <c>systemusers(azureactivedirectoryobjectid=…)</c> in a URL (preceded by <c>/</c> or a
///   quote — a message string such as <c>"GET systemusers(…) failed"</c> is not a read);</item>
///   <item>the column literal passed as an argument to a HELPER method (<c>ResolveFromUserAsync("azureactivedirectoryobjectid",
///   oid)</c>). Not a lookup: the literal inside a collection (<c>[…]</c>, <c>{…}</c>, a column array or <c>$select</c>), or
///   as an argument to a column-reading call (<see cref="NonLookupCallees"/>: <c>ColumnSet</c>, <c>GetAttributeValue</c>,
///   <c>GuidOf</c>, <c>TryGetProperty</c>, …).</item>
/// </list>
/// <para><b>What satisfies it</b> — evidence in the METHOD that holds the lookup (from its declaration to the next member
/// declaration), or for the helper shape in the HELPER's method in the same file:</para>
/// <list type="bullet">
///   <item>a FILTER on <c>isdisabled</c>: <c>isdisabled eq false</c> / <c>eq 0</c>, a <c>"isdisabled", ConditionOperator</c>
///   condition, or a FetchXML <c>condition attribute="isdisabled"</c>; or</item>
///   <item>the value READ AND CHECKED: <c>GetAttributeValue&lt;bool?&gt;("isdisabled")</c>, a <c>GetProperty</c> /
///   <c>TryGetProperty("isdisabled")</c> read, a DTO's <c>.IsDisabled</c> / <c>.isdisabled</c> read, or <c>IsStandingEligible(</c> (which
///   rejects disabled users).</item>
/// </list>
/// <para>Selecting the column alone (<c>ColumnSet</c>, <c>$select</c>) is NOT evidence: a selected value nobody checks
/// excludes nobody. An <c>includes disabled: &lt;reason&gt;</c> comment on the lookup line or up to
/// <see cref="MarkerLookback"/> lines above it is the explicit exception.</para>
/// <para><b>Stated limits</b> (shapes this scan cannot see, kept here rather than silently):</para>
/// <list type="bullet">
///   <item>Evidence is per method, not per query: a method with two systemuser reads, only one of them filtered, passes.</item>
///   <item>A helper defined in ANOTHER file is not followed: the call is a violation unless marked (fails safe).</item>
///   <item>The column name built at run time (concatenation, <c>nameof</c>, a non-const field or a const in another file)
///   is not recognised; nor is a lookup through <c>ISystemUserIdentityResolver</c>, which is filtered by contract.</item>
///   <item>"Read and checked" is recognised by the read's shape, not by proving the value gates the outcome.</item>
///   <item>Member boundaries are found by an access-modifier declaration regex; a nested local function shares its
///   container's method.</item>
/// </list>
/// <para><b>Ratchet.</b> <see cref="Baseline"/> lists lookups left unfixed because another open PR edits the file. A new
/// violation fails; a baseline entry that no longer violates also fails, so the list only shrinks.</para>
/// </remarks>
public class SystemUserOidLookupDisabledGuardTests
{
    /// <summary>Lines above a lookup searched for an <c>includes disabled:</c> marker.</summary>
    internal const int MarkerLookback = 6;

    /// <summary>
    /// Known unfiltered lookups, by file, each with the open PR or active project branch that keeps it unfixed (editing
    /// the file here would conflict with that work).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Baseline = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/UserProvisioning/DataverseWebApiGuestUserWriter.cs"] =
            "active project customer-provisioning-orchestration-r1 (branch work/customer-provisioning-orchestration-r1) edits "
            + "this file. Expected disposition once it merges: a deliberate exception — the alternate-key read is how Dataverse "
            + "ADDS a security-group member as a user, and provisioning must find the row, disabled or not, to assign its "
            + "business unit and roles",
    };

    private const string Column = "azureactivedirectoryobjectid";

    /// <summary>Calls whose column-name argument READS or SELECTS the column — not a lookup by it.</summary>
    internal static readonly IReadOnlySet<string> NonLookupCallees = new HashSet<string>(StringComparer.Ordinal)
    {
        "ColumnSet", "GetAttributeValue", "GetGuidLike", "GuidOf", "TryGetProperty", "GetProperty", "Contains",
        "ContainsKey", "ExtractAliasedGuid", "JsonPropertyName", "AddColumn", "AddColumns",
        // Conditions are matched by their own pattern below.
        "ConditionExpression", "AddCondition",
    };

    private static readonly Regex[] LookupPatterns =
    {
        new(Column + @"\s+eq\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new("\"" + Column + "\"" + @"\s*,\s*ConditionOperator\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"attribute\s*=\s*(?:""""|\\""|')" + Column, RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"[/""']systemusers\(\s*" + Column + @"\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    };

    private static readonly Regex ColumnLiteral = new("\"" + Column + "\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ColumnAlias = new(
        @"\bconst\s+string\s+(?<name>\w+)\s*=\s*""" + Column + @"""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex[] DisabledEvidence =
    {
        // Filters.
        new(@"\bisdisabled\s+eq\s+(?:false|0)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"""isdisabled""\s*,\s*ConditionOperator\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"attribute\s*=\s*(?:""""|\\""|')isdisabled", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        // Read and checked.
        new(@"GetAttributeValue\s*<\s*bool\??\s*>\s*\(\s*""isdisabled""", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\b(?:Try)?GetProperty\s*\(\s*""isdisabled""", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"\.IsDisabled\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\bIsStandingEligible\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    };

    /// <summary>A member declaration with an access modifier — the boundary of the method a lookup belongs to.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:public|private|internal|protected)\s+(?:(?:static|async|virtual|override|sealed|new|extern|unsafe|partial|readonly)\s+)*"
        + @"(?:\([^)\n]*\)|[\w.]+(?:\s*<[^\n(]*?>)?(?:\[\])*)\??\s+(?<name>\w+)\s*(?:<[^>\n]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex Marker = new(@"includes disabled:\s*\S", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The 1-based lines of <paramref name="rawLines"/> holding an oid → systemuser lookup with no disabled-user
    /// evidence in its method (or its helper's) and no marker. Empty when the rule holds.</summary>
    internal static IReadOnlyList<int> Violations(IReadOnlyList<string> rawLines)
    {
        var code = SourceScan.CodeText(rawLines);
        var declarations = MemberDeclaration.Matches(code).Select(m => (m.Index, Name: m.Groups["name"].Value)).ToList();

        // (index of the lookup, the code span whose evidence decides it)
        var lookups = new List<(int Index, string Span)>();

        var patterns = LookupPatterns.ToList();
        foreach (Match alias in ColumnAlias.Matches(code))
        {
            var name = Regex.Escape(alias.Groups["name"].Value);
            patterns.Add(new Regex(@"\b" + name + @"\s*,\s*ConditionOperator\b", RegexOptions.CultureInvariant));
            patterns.Add(new Regex(@"\{" + name + @"\}\s+eq\b", RegexOptions.CultureInvariant));
        }

        foreach (var pattern in patterns)
        {
            foreach (Match m in pattern.Matches(code))
            {
                lookups.Add((m.Index, MethodSpan(code, declarations, m.Index)));
            }
        }

        foreach (Match literal in ColumnLiteral.Matches(code))
        {
            if (EnclosingCallee(code, literal.Index) is not { } callee || NonLookupCallees.Contains(callee))
            {
                continue;
            }

            // A helper: its own method in this file must carry the evidence. Not found here → empty span → violation.
            var helper = declarations.FindIndex(d => d.Name == callee);
            var span = helper < 0 ? string.Empty : Span(code, declarations, helper);
            lookups.Add((literal.Index, span));
        }

        var violations = new SortedSet<int>();
        foreach (var (index, span) in lookups)
        {
            var line = SourceScan.LineOf(code, index);
            var marked = Enumerable.Range(Math.Max(0, line - 1 - MarkerLookback), Math.Min(line, MarkerLookback + 1))
                .Any(i => i < rawLines.Count && Marker.IsMatch(rawLines[i]));
            if (!marked && !DisabledEvidence.Any(e => e.IsMatch(span)))
            {
                violations.Add(line);
            }
        }

        return violations.ToList();
    }

    /// <summary>The method containing <paramref name="index"/>: its declaration to the next declaration. A lookup before
    /// any declaration (a top-level snippet) is judged over the whole text.</summary>
    private static string MethodSpan(string code, IReadOnlyList<(int Index, string Name)> declarations, int index)
    {
        var owner = -1;
        for (var i = 0; i < declarations.Count && declarations[i].Index <= index; i++)
        {
            owner = i;
        }

        return owner < 0 ? code : Span(code, declarations, owner);
    }

    private static string Span(string code, IReadOnlyList<(int Index, string Name)> declarations, int i)
    {
        var start = declarations[i].Index;
        var end = i + 1 < declarations.Count ? declarations[i + 1].Index : code.Length;
        return code[start..end];
    }

    /// <summary>The name of the call whose argument list directly contains <paramref name="index"/>, or null when the
    /// innermost open bracket is a collection or indexer (<c>[</c>, <c>{</c>) or there is none.</summary>
    private static string? EnclosingCallee(string code, int index)
    {
        var depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            var c = code[i];
            if (c is ')' or ']' or '}')
            {
                depth++;
            }
            else if (c is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                if (c != '(')
                {
                    return null;
                }

                // Skip whitespace and a generic argument list (GetAttributeValue<Guid?>), then read the identifier.
                var j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
                if (j >= 0 && code[j] == '>')
                {
                    var angle = 0;
                    for (; j >= 0; j--)
                    {
                        if (code[j] == '>') angle++;
                        else if (code[j] == '<' && --angle == 0) { j--; break; }
                    }
                }

                var end = j;
                while (j >= 0 && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j--;
                return end > j ? code[(j + 1)..(end + 1)] : null;
            }
            else if (c == ';' && depth == 0)
            {
                return null;
            }
        }

        return null;
    }

    [Fact(DisplayName = "Issue #1576: every oid → systemuser lookup excludes disabled users or says why it includes them")]
    public void EverySystemUserOidLookup_ExcludesDisabledUsers_OrCarriesTheMarker()
    {
        var found = SourceScan.ServerSourceFiles()
            .Select(f => (File: SourceScan.Relative(f).Replace('\\', '/'), Path: f))
            .Where(f => !f.File.Contains(".Tests/", StringComparison.Ordinal))
            .Select(f => (f.File, Lines: Violations(File.ReadAllLines(f.Path))))
            .Where(r => r.Lines.Count > 0)
            .ToList();

        var unexpected = found.Where(r => !Baseline.ContainsKey(r.File))
            .Select(r => $"{r.File}:{string.Join(",", r.Lines)}")
            .ToList();
        var stale = Baseline.Keys.Where(k => found.All(r => r.File != k)).ToList();

        Assert.True(unexpected.Count == 0,
            "These lookups map an Entra oid to a Dataverse systemuser without excluding disabled users (issue #1576): "
            + string.Join("; ", unexpected) + ". A disabled systemuser keeps its oid, so the lookup resolves it like an "
            + "active user. Add `isdisabled eq false` (OData) or an `isdisabled = false` condition in the same method (or the "
            + "helper's), prefer ISystemUserIdentityResolver.ResolveSystemUserIdAsync, or — where a disabled user must "
            + "resolve — add a `// includes disabled: <reason>` comment on or just above the lookup.");
        Assert.True(stale.Count == 0,
            "These baseline entries no longer violate the rule; remove them from SystemUserOidLookupDisabledGuardTests.Baseline "
            + "so the ratchet only shrinks: " + string.Join("; ", stale));
    }

    [Fact(DisplayName = "Issue #1576: every baseline entry names the open PR or active project branch that keeps it unfixed")]
    public void EveryBaselineEntry_NamesTheOverlappingWork()
    {
        Assert.All(Baseline, entry => Assert.Matches(@"#\d+|work/[\w.-]+", entry.Value));
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
        // (isdisabled eq false is not filtered here)
        var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'";
        """,
        // (F2 a) The reviewer's mutation shape: the column passed to a helper whose OWN query is unfiltered — even
        // though a sibling method in the same file filters isdisabled.
        """
        private async Task<Guid?> ResolveRequesterAsync(Guid objectId, CancellationToken ct)
        {
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, objectId);
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            return null;
        }

        private async Task<RecordOwnerResolution> ResolveOwnerOnlyAsync(Guid objectId, CancellationToken ct)
        {
            return await ResolveFromUserAsync("azureactivedirectoryobjectid", objectId, ct).ConfigureAwait(false);
        }

        private async Task<RecordOwnerResolution> ResolveFromUserAsync(string userKeyColumn, Guid userKey, CancellationToken ct)
        {
            var userQuery = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 2 };
            userQuery.Criteria.AddCondition(userKeyColumn, ConditionOperator.Equal, userKey);
            return null;
        }
        """,
        // (F2 a) The column passed to a helper that is not defined in this file.
        """
        var users = await _directory.UsersWhereAsync("azureactivedirectoryobjectid", oid, ct);
        """,
        // (F2 b) The alternate-key read.
        """
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(envUri, $"/api/data/v9.2/systemusers(azureactivedirectoryobjectid={objectId:D})?$select=systemuserid"));
        """,
        // (F2 c) isdisabled only SELECTED, never filtered or checked.
        """
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("businessunitid", "isdisabled", "accessmode"),
            Criteria = { Conditions = { new ConditionExpression(AzureAdObjectIdColumn, ConditionOperator.Equal, oid) } }
        };
        private const string AzureAdObjectIdColumn = "azureactivedirectoryobjectid";
        """,
        // (F2 c) isdisabled only in $select.
        """
        var url = $"systemusers?$filter=azureactivedirectoryobjectid eq {oid}&$select=systemuserid,isdisabled";
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
        // The eligibility read: isdisabled selected AND checked after the read.
        """
        internal async Task<Guid?> ResolveAsync(Guid oid, CancellationToken ct)
        {
            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid", "isdisabled") };
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, oid);
            var row = (await _dataverse.RetrieveMultipleAsync(query, ct)).Entities[0];
            if (row.GetAttributeValue<bool?>("isdisabled") != false) return null;
            return row.Id;
        }
        """,
        // A deliberate exception, with its reason.
        """
        // includes disabled: a No Access entry is enforced against disabled users too.
        var filter = string.Join(" or ", oids.Select(o => $"azureactivedirectoryobjectid eq {o}"));
        """,
        // Selecting or reading the column is not a lookup; nor is a comment that quotes a filter, or an error message.
        """
        var columns = new ColumnSet("domainname", "azureactivedirectoryobjectid");
        var oid = row.GetAttributeValue<Guid?>("azureactivedirectoryobjectid");
        var raw = GuidOf(row, "azureactivedirectoryobjectid");
        string[] cols = ["systemuserid", "azureactivedirectoryobjectid"];
        // the lookup is "azureactivedirectoryobjectid eq {oid}"
        throw new InvalidOperationException($"GET systemusers(azureactivedirectoryobjectid={objectId:D}) failed");
        """,
        // A helper in the same file whose own query filters isdisabled.
        """
        private Task<IReadOnlyList<Guid>> EnabledSystemUsersWithEntraOidAsync(Guid oid, CancellationToken ct) =>
            EnabledUsersWhereAsync("azureactivedirectoryobjectid", oid, ct);

        private async Task<IReadOnlyList<Guid>> EnabledUsersWhereAsync(string column, Guid value, CancellationToken ct)
        {
            query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            return null;
        }
        """,
    };

    [Theory(DisplayName = "Issue #1576 guard control: an oid → systemuser lookup that keeps disabled users is a violation")]
    [MemberData(nameof(MustFire))]
    public void Control_UnfilteredLookup_IsAViolation(string source)
        => Assert.NotEmpty(Violations(source.Split('\n')));

    [Theory(DisplayName = "Issue #1576 guard control: a filtered, checked, marked or non-lookup use is not a violation")]
    [MemberData(nameof(MustNotFire))]
    public void Control_FilteredMarkedOrNonLookup_IsNotAViolation(string source)
        => Assert.Empty(Violations(source.Split('\n')));
}
