using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 045a, guard 1 (G1; owner decisions 2026-11-07): the ADR-024 regarding pair is never
/// written HALF by server code. A member that writes <c>sprk_regardingrecordtype</c> also writes <c>sprk_regardingrecordid</c>.
/// The general create's own completion of a caller's type-only pair (<c>RecordCreationService</c> step 4½) rebuilds the row
/// through the item mapper, so it is pinned by its behaviour tests, not here.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped; <see cref="SourceScan"/>),
/// a sibling of <see cref="CorePairTypeImpliesIdGuardTests"/> scoped to the MEMBER rather than to a line distance. It looks at
/// WRITES only, never at reads or at lists that merely NAME the columns (a refuse list, a <c>ColumnSet</c>, a select). A key is
/// the literal <c>"sprk_regardingrecordtype"</c> (any case, with or without <c>@odata.bind</c>), a <c>const string</c> declared
/// with that value in the same file, a shared constant (<c>PairTypeColumn</c>, <c>RegardingRecordTypeColumn</c>,
/// <c>FieldRegardingRecordType</c>, <c>EventRecordTypeNavigationProperty</c>), or that shared constant interpolated with
/// <c>@odata.bind</c>; the id's keys alike (<c>PairIdColumn</c>, <c>RegardingRecordIdColumn</c>, <c>FieldRegardingRecordId</c>).</para>
/// <list type="number">
/// <item><b>Rule 1, keyed writes.</b> An indexer assignment (<c>e[KEY] = …</c>, not <c>==</c>; an object initializer's
/// <c>[KEY] = …</c> too), a <c>.Add(KEY, …)</c> / <c>.TryAdd(KEY, …)</c>, an <c>Entity.SetAttributeValue(KEY, …)</c> or a JSON
/// writer call with the type's key must have a write of the id's key in the same member (from one access-modified declaration
/// to the next).</item>
/// <item><b>Rule 2, column maps.</b> No map entry has the type's column as its VALUE (<c>[x] = "sprk_regardingrecordtype"</c>):
/// such a map routes a write to the type through a run-time key (<c>entity[map[x]] = …</c>) that rule 1 cannot pair. Fix round 1
/// (K4): <c>TaskActionCore.RegardingFieldByEntity</c> had one (<c>sprk_recordtype_ref</c> → the type), writing a task's pair
/// type with no id; the entry is gone and this rule keeps it gone.</item>
/// </list>
/// <para>Not seen: a key built at run time in any other way — a loop over a column list (<c>InvoiceCorrectionService</c>'s copy
/// of an invoice's resolver fields, <c>foreach (var column in new[] { …RegardingRecordTypeColumn, … }) row[column] = …</c>,
/// which copies the type and the id together but only as far as the source row holds both), a computed name, a map value
/// held in a variable; a dictionary collection initializer (<c>{ { KEY, v } }</c>); a column map built in the other shapes rule 2
/// does not read — <c>map.Add("x", "sprk_regardingrecordtype")</c> / <c>TryAdd</c> calls, a <c>{ { "x", "sprk_regardingrecordtype" } }</c>
/// initializer, and a tuple list (<c>("x", "sprk_regardingrecordtype")</c>) routed through later; an anonymous object; a hand-written JSON
/// string; a DTO property mapped by <c>[JsonPropertyName]</c>; a conditional write (the guard sees that both writes are in the
/// member, not that both always run); a type and id written by two members of one call chain. Member spans are approximate:
/// a member declared without an access modifier (an explicit interface implementation, a local function is fine — it stays
/// in its member) and a field initializer are attributed to the member before them. Only <c>//</c> comments are stripped: a
/// write inside a <c>/* … */</c> block comment is still seen (a false positive, never a false negative). Enforced by: this guard
/// (ArchTests in CI and the Stop hook). No baseline: since fix round 1 (K4) no current site violates either rule.</para>
/// <para><b>Controls.</b> <see cref="Control_ATypeWrittenWithoutItsIdInItsMember_IsFound"/> (must fire) and
/// <see cref="Control_APairWriteInOneMember_AReadOrARefuseList_IsNotFound"/> (must not fire) run the same detector over synthetic
/// source.</para>
/// </remarks>
public class RegardingPairTypeImpliesIdGuardTests
{
    private const string TypeColumn = "sprk_regardingrecordtype";
    private const string IdColumn = "sprk_regardingrecordid";

    private static readonly string[] TypeConstants =
        ["PairTypeColumn", "RegardingRecordTypeColumn", "FieldRegardingRecordType", "EventRecordTypeNavigationProperty"];

    private static readonly string[] IdConstants = ["PairIdColumn", "RegardingRecordIdColumn", "FieldRegardingRecordId"];

    /// <summary>A type member: an access modifier, so a local function or a lambda stays inside the member that declares it.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:(?:static|async|override|virtual|sealed|new|readonly|partial|extern|unsafe)\s+)*(?:public|private|internal|protected)\s+[^\n;=]*?\b(\w+)\s*(?:<[^\n>]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>The keys that name <paramref name="column"/> in <paramref name="code"/>: its literal (and bind), the shared
    /// constants (bare or interpolated with the bind), and any same-file <c>const string</c> with its value.</summary>
    private static string KeyPattern(string code, string column, IEnumerable<string> sharedConstants)
    {
        var shared = "(?:" + string.Join("|", sharedConstants.Select(Regex.Escape)) + ")";
        var keys = new List<string>
        {
            "\"" + Regex.Escape(column) + @"(?:@odata\.bind)?""",
            @"\$""\{\s*(?:\w+\.)*" + shared + @"\s*\}(?:@odata\.bind)?""",
            @"\b(?:\w+\.)*" + shared + @"\b",
        };
        foreach (Match m in Regex.Matches(code, @"\bconst\s+string\s+(\w+)\s*=\s*""" + Regex.Escape(column) + @"(?:@odata\.bind)?""\s*;",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            keys.Add(@"\b" + Regex.Escape(m.Groups[1].Value) + @"\b");
        return "(?:" + string.Join("|", keys.Distinct()) + ")";
    }

    private static Regex Writes(string key) => new(
        @"\[\s*" + key + @"\s*\]\s*=(?!=)"
        + @"|\.\s*(?:Add|TryAdd|SetAttributeValue)\s*\(\s*" + key + @"\s*,"
        + @"|\bWrite(?:String|PropertyName|Null|Boolean|Number|StartObject|StartArray)\s*\(\s*" + key + @"\s*[,)]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Guard 1 false positive (fixed): the id written through the copy helper <c>CopyStringIfPresent(source, target, KEY)</c>
    /// (<c>CommunicationService</c>) counts as an id write, recognised only for that helper with the ID key as its THIRD argument. A
    /// call that copies another column is no id write; the helper's arguments are plain names or member accesses.</summary>
    private static Regex CopyHelperIdWrite(string idKey) => new(
        @"\bCopyStringIfPresent\s*\(\s*[\w.]+\s*,\s*[\w.]+\s*,\s*" + idKey + @"\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The problems in one file's source (empty when every type write has its id in its member); and how many type
    /// writes it saw.</summary>
    internal static (IReadOnlyList<string> Problems, int TypeWrites) Scan(string source)
    {
        var code = SourceScan.CodeText(source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));
        var typeWrites = Writes(KeyPattern(code, TypeColumn, TypeConstants)).Matches(code);
        var idKey = KeyPattern(code, IdColumn, IdConstants);
        var idWrites = Writes(idKey).Matches(code).Select(m => m.Index)
            .Concat(CopyHelperIdWrite(idKey).Matches(code).Select(m => m.Index)).ToList();
        var memberStarts = MemberDeclaration.Matches(code).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
        var problems = new List<string>();

        foreach (Match write in typeWrites)
        {
            var at = memberStarts.LastOrDefault(m => m.Index <= write.Index);
            var start = memberStarts.Count == 0 || at == default ? 0 : at.Index;
            var next = memberStarts.FirstOrDefault(m => m.Index > write.Index);
            var end = next == default ? code.Length : next.Index;
            if (idWrites.Any(i => i >= start && i < end))
                continue;

            var member = at == default ? "(file scope)" : at.Name;
            problems.Add($"line {SourceScan.LineOf(code, write.Index)}: {member} writes the regarding record type "
                         + $"('{write.Value.Trim()}') with no write of {IdColumn} in the same member");
        }

        // Rule 2: no map entry routes a write to the type (the type's column as an entry's VALUE).
        foreach (Match entry in TypeAsMapValue.Matches(code))
        {
            problems.Add($"line {SourceScan.LineOf(code, entry.Index)}: a map entry names the regarding record type as a column to "
                         + $"write ('{entry.Value.Trim()}'); a write through it has a run-time key no rule can pair with {IdColumn}");
        }

        return (problems, typeWrites.Count);
    }

    /// <summary>An entry <c>[…] = "sprk_regardingrecordtype"</c> (any case, with or without the bind): a column map's value.</summary>
    private static readonly Regex TypeAsMapValue = new(
        @"\]\s*=\s*""" + Regex.Escape(TypeColumn) + @"(?:@odata\.bind)?""\s*[,;}\r\n]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact(DisplayName = "045a guard 1: server code that writes sprk_regardingrecordtype also writes sprk_regardingrecordid in the same member")]
    public void EveryRegardingTypeWrite_AlsoWritesTheIdInItsMember()
    {
        var problems = new List<string>();
        var writers = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var (fileProblems, typeWrites) = Scan(File.ReadAllText(file));
            problems.AddRange(fileProblems.Select(p => $"{relative}: {p}"));
            if (typeWrites > 0)
                writers.Add(relative);
        }

        // Non-vacuity: today's writers of the type are seen, in each key shape (shared constant, literal bind, interpolated bind).
        foreach (var expected in new[]
                 {
                     "src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Signals/Actions/BudgetInquiryExecutor.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionExecutors.cs",
                     "src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs",
                     "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs",
                 })
        {
            if (!writers.Contains(expected))
                problems.Add($"{expected}: expected to be seen writing the regarding record type; the scan lost its signal.");
        }

        Assert.True(problems.Count == 0,
            "The ADR-024 regarding pair is never written half (task 045a, G1): a member that writes "
            + $"{TypeColumn} also writes {IdColumn}:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "045a guard 1 control (must fire): a type written without its id in its member is found")]
    public void Control_ATypeWrittenWithoutItsIdInItsMember_IsFound()
    {
        Assert.Single(Scan("""entity["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);""").Problems);
        Assert.Single(Scan("""payload["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({t})";""").Problems);
        Assert.Single(Scan("""fields.Add("sprk_regardingrecordtype", t);""").Problems);
        Assert.Single(Scan("""fields.TryAdd(CoreAncestorResolver.RegardingRecordTypeColumn, t);""").Problems);
        Assert.Single(Scan("""entity.SetAttributeValue("sprk_regardingrecordtype", r);""").Problems);
        Assert.Single(Scan("""writer.WriteString("sprk_regardingrecordtype@odata.bind", path);""").Problems);
        Assert.Single(Scan("""extra[SecureRootInheritance.PairTypeColumn] = r;""").Problems);
        Assert.Single(Scan("""payload[$"{RegardingRecordType.EventRecordTypeNavigationProperty}@odata.bind"] = path;""").Problems);
        Assert.Single(Scan("""
            private const string RegardingTypeField = "sprk_regardingrecordtype";
            void M() { entity[RegardingTypeField] = r; }
            """).Problems);
        Assert.Single(Scan("""var body = new Dictionary<string, object?> { ["sprk_RegardingRecordType@odata.bind"] = path };""").Problems);

        // The id in ANOTHER member is not the pair, however near.
        Assert.Single(Scan("""
            private void WriteType(Entity entity) { entity["sprk_regardingrecordtype"] = r; }
            private void WriteId(Entity entity) { entity["sprk_regardingrecordid"] = id; }
            """).Problems);

        // Rule 2 (fix round 1, K4): a column map routing a write to the type — TaskActionCore's former entry, verbatim.
        Assert.Single(Scan("""
            private static readonly IReadOnlyDictionary<string, string> RegardingFieldByEntity =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sprk_matter"] = "sprk_regardingmatter",
                    ["sprk_recordtype_ref"] = "sprk_regardingrecordtype",
                    ["account"] = "sprk_regardingaccount",
                };
            """).Problems);
        Assert.Single(Scan("""map["recordtype"] = "SPRK_RegardingRecordType";""").Problems);

        // Guard 1 false positive (fixed): the copy helper counts as an id write only for the ID column. A member that writes the
        // type and copies a DIFFERENT column through the helper has no id; so does one that passes the id as the SOURCE argument.
        Assert.Single(Scan("""
            private void M(Entity source, Entity row)
            {
                CopyStringIfPresent(source, row, "sprk_regardingrecordname");
                row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);
            }
            """).Problems);
        Assert.Single(Scan("""
            private void M(Entity source, Entity row)
            {
                CopyStringIfPresent(source, row, "sprk_regardingrecordnumber");
                CopyStringIfPresent(source, row, "sprk_regardingrecordurl");
                row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);
            }
            private void N(Entity source, Entity row) { CopyStringIfPresent(source, row, "sprk_regardingrecordid"); }
            """).Problems);

        // An id only in a comment is no id; an id that is only READ is no write.
        Assert.Single(Scan("""
            private void M(Entity entity)
            {
                entity["sprk_regardingrecordtype"] = r;
                // entity["sprk_regardingrecordid"] = id;
                var id = entity["sprk_regardingrecordid"];
            }
            """).Problems);
    }

    [Fact(DisplayName = "045a guard 1 control (must not fire): a pair write in one member, a read, or a list that only NAMES the columns is not flagged")]
    public void Control_APairWriteInOneMember_AReadOrARefuseList_IsNotFound()
    {
        // The pair in one member, in each write shape, however far apart.
        Assert.Empty(Scan("""
            private void M(Entity entity)
            {
                entity["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);
                var a = 1;
                var b = 2;
                var c = 3;
                var d = 4;
                var e = 5;
                var f = 6;
                var g = 7;
                var h = 8;
                var i = 9;
                var j = 10;
                var k = 11;
                entity["sprk_regardingrecordid"] = id.ToString("D");
            }
            """).Problems);
        Assert.Empty(Scan("""
            private void M(Dictionary<string, object?> payload)
            {
                if (refId is { } r) payload[$"{RegardingRecordType.EventRecordTypeNavigationProperty}@odata.bind"] = r;
                payload["sprk_regardingrecordid"] = targetId.ToString("D");
            }
            """).Problems);
        Assert.Empty(Scan("""
            private const string RegardingTypeField = "sprk_regardingrecordtype";
            private const string RegardingIdField = "sprk_regardingrecordid";
            internal static void M() { fields.Add(RegardingTypeField, t); fields.Add(RegardingIdField, i); }
            """).Problems);
        Assert.Empty(Scan("""
            private static Dictionary<string, object?> Body() => new()
            {
                ["sprk_RegardingRecordType@odata.bind"] = path,
                [SecureRootInheritance.PairIdColumn] = id,
            };
            """).Problems);
        Assert.Empty(Scan("""
            private void M(Entity e) { e.SetAttributeValue(TodoRegardingBuilder.FieldRegardingRecordType, t); e.SetAttributeValue(TodoRegardingBuilder.FieldRegardingRecordId, i); }
            """).Problems);

        // Guard 1 false positive (fixed): CommunicationService.CopyRegardingFromSourceAsync's shape — the id copied through the
        // helper, the type written later in the same member, after the helper copies of the display values.
        Assert.Empty(Scan("""
            private async Task<bool> CopyRegardingFromSourceAsync(DataverseEntity communication, DataverseEntity source)
            {
                CopyStringIfPresent(source, communication, "sprk_regardingrecordid");
                if (SameRecord(communication.GetAttributeValue<string>("sprk_regardingrecordid"), source.GetAttributeValue<string>("sprk_regardingrecordid")))
                {
                    CopyStringIfPresent(source, communication, "sprk_regardingrecordname");
                    communication["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", typeId);
                }
                return true;
            }
            """).Problems);
        Assert.Empty(Scan("""
            private void M(Entity s, Entity t) { CopyStringIfPresent(s, t, RegardingRecordIdColumn); t[RegardingRecordTypeColumn] = r; }
            """).Problems);

        // Reads: an attribute read, a condition, a filter, an equality test, a column set, a select.
        Assert.Empty(Scan("""
            var t = row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype");
            query.Criteria.AddCondition("sprk_regardingrecordtype", ConditionOperator.NotNull);
            var filter = $"_sprk_regardingrecordtype_value eq {id}";
            if (row["sprk_regardingrecordtype"] == null) return;
            var cols = new ColumnSet("sprk_regardingrecordtype", "sprk_regardingrecordid");
            columns.Add(LookupValue(SecureRootInheritance.PairTypeColumn));
            """).Problems);

        // Lists that only NAME the columns: a refuse list, a collection expression, a set initializer, a key-only lookup.
        Assert.Empty(Scan("""
            RefusedColumns = Columns("sprk_regardingrecordtype", "sprk_regardingrecordid"),
            private static readonly string[] Display = ["sprk_regardingrecordtype", "sprk_regardingrecordname"];
            var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_regardingrecordtype" };
            leftOut.Add(SecureRootInheritance.PairTypeColumn);
            """).Problems);

        // Rule 2 must not fire: the type as a switch arm's pattern, a comparison, or a map's KEY read; another column as a value.
        Assert.Empty(Scan("""
            var nav = column switch { "sprk_regardingrecordtype" => EventRecordTypeNavigationProperty, _ => null };
            if (column == "sprk_regardingrecordtype") return;
            var target = map["sprk_regardingrecordtype"];
            fields["sprk_regardingrecordtypename"] = "x";
            map["sprk_matter"] = "sprk_regardingrecordtypes_note";
            """).Problems);

        // A different column whose name merely contains the type's (no word boundary) is not the type.
        Assert.Empty(Scan("""entity[RegardingPairTypeColumnName] = r;""").Problems);
    }
}
