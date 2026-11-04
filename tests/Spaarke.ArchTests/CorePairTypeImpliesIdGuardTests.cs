using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 042c fix round 2 (B', #2561; owner 2026-11-04): a Signal's or Decision Record's core
/// pair is never written HALF. Any server code that writes <c>sprk_corerecordtype</c> also writes <c>sprk_corerecordid</c>.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped; <see cref="SourceScan"/>).
/// It looks at WRITES only, never at reads or at lists that merely NAME the columns (a refuse list, a <c>ColumnSet</c>, a select).
/// A key is the literal <c>"sprk_corerecordtype"</c> (any case, with or without <c>@odata.bind</c>), a <c>const string</c> declared
/// with that value in the same file, or the shared <c>SignalCoreRecordAccess.CoreRecordTypeColumn</c>; the id's keys alike.</para>
/// <list type="number">
/// <item><b>Rule 1, keyed writes.</b> An Entity or dictionary indexer assignment (<c>e[KEY] = …</c>, not <c>==</c>), a dictionary
/// <c>.Add(KEY, …)</c> / <c>.TryAdd(KEY, …)</c>, or a JSON writer call (<c>WriteString</c>, <c>WritePropertyName</c>,
/// <c>WriteNull</c>, …) with the type's key must have a write of the id's key within <see cref="Proximity"/> lines of it, in the
/// same file.</item>
/// <item><b>Rule 2, body rewrites.</b> A member that names the type's key and rewrites a mapped item's body (<c>JsonBody =</c> or
/// <c>Lookups =</c>; the key it writes there is dynamic, so rule 1 cannot see it) must be the ONE named pair writer
/// <c>WithCorePair</c> (<c>RecordCreationService</c>), and that member must write the id's key.</item>
/// </list>
/// <para>Not seen: a key built at run time outside a body rewrite, a dictionary collection initializer (<c>{ { KEY, v } }</c>, which
/// reads like a set initializer), an anonymous object, a hand-written JSON string, a conditional write in one method (a type write
/// with an id write nearby that does not always run with it: rule 1 checks that the two are near, not that both always run), a DTO
/// property mapped to the column by <c>[JsonPropertyName]</c> and written through the serializer, and
/// <c>Entity.SetAttributeValue(KEY, …)</c>. The behaviour tests pin the paths:
/// <c>SecureChildOwnershipAiToolTests.HalfCorePair</c> (chat and service) and <c>WriterBuilderOwnershipParityTests</c> (the system
/// principal). Enforced by: this guard (ArchTests in CI and the Stop hook). No baseline: there are no violations today.</para>
/// <para><b>Controls.</b> <see cref="Control_ATypeWrittenWithoutItsId_IsFound"/> (must fire) and
/// <see cref="Control_APairWrite_AReadOrARefuseList_IsNotFound"/> (must not fire) run the same detector over synthetic source.</para>
/// </remarks>
public class CorePairTypeImpliesIdGuardTests
{
    /// <summary>How far (in lines, either way) the id's write may be from the type's.</summary>
    internal const int Proximity = 10;

    /// <summary>The one member allowed to rewrite a body that carries the type (rule 2).</summary>
    internal const string PairWriter = "WithCorePair";

    private const string TypeColumn = "sprk_corerecordtype";
    private const string IdColumn = "sprk_corerecordid";

    /// <summary>A type member: an access modifier, so a local function (none) stays inside the member that declares it.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:(?:static|async|override|virtual|sealed|new|readonly|partial|extern|unsafe)\s+)*(?:public|private|internal|protected)\s+[^\n;=]*?\b(\w+)\s*(?:<[^\n>]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex RewritesABody = new(@"\b(?:JsonBody|Lookups)\s*=(?!=)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The keys that name <paramref name="column"/> in <paramref name="code"/>: its literal (and bind), the shared constant,
    /// and any same-file <c>const string</c> with its value.</summary>
    private static string KeyPattern(string code, string column, string sharedConstant)
    {
        var keys = new List<string>
        {
            "\"" + Regex.Escape(column) + @"(?:@odata\.bind)?""",
            @"(?:\w+\.)*" + Regex.Escape(sharedConstant) + @"\b",
        };
        foreach (Match m in Regex.Matches(code, @"\bconst\s+string\s+(\w+)\s*=\s*""" + Regex.Escape(column) + @"""\s*;", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            keys.Add(@"\b" + Regex.Escape(m.Groups[1].Value) + @"\b");
        return "(?:" + string.Join("|", keys.Distinct()) + ")";
    }

    private static Regex Writes(string key) => new(
        @"\[\s*" + key + @"\s*\]\s*=(?!=)"
        + @"|\.\s*(?:Add|TryAdd)\s*\(\s*" + key + @"\s*,"
        + @"|\bWrite(?:String|PropertyName|Null|Boolean|Number|StartObject|StartArray)\s*\(\s*" + key + @"\s*[,)]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex Names(string key) => new(key, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The problems in one file's source (empty when every type write has its id); and the sites each rule saw.</summary>
    internal static (IReadOnlyList<string> Problems, int KeyedTypeWrites, IReadOnlyList<string> BodyRewriters) Scan(string source)
    {
        var code = SourceScan.CodeText(source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));
        var typeKey = KeyPattern(code, TypeColumn, "CoreRecordTypeColumn");
        var idKey = KeyPattern(code, IdColumn, "CoreRecordIdColumn");
        var problems = new List<string>();

        // Rule 1: a keyed write of the type has a keyed write of the id nearby.
        var idWriteLines = Writes(idKey).Matches(code).Select(m => SourceScan.LineOf(code, m.Index)).ToList();
        var typeWrites = Writes(typeKey).Matches(code);
        foreach (Match write in typeWrites)
        {
            var line = SourceScan.LineOf(code, write.Index);
            if (!idWriteLines.Any(l => Math.Abs(l - line) <= Proximity))
                problems.Add($"line {line}: writes the core record type ('{write.Value.Trim()}') with no write of {IdColumn} within {Proximity} lines");
        }

        // Rule 2: a member that names the type and rewrites a body is the one pair writer, and it writes the id.
        var rewriters = new List<string>();
        var members = MemberDeclaration.Matches(code).Cast<Match>().ToList();
        for (var i = 0; i < members.Count; i++)
        {
            var start = members[i].Index;
            var end = i + 1 < members.Count ? members[i + 1].Index : code.Length;
            var span = code[start..end];
            if (!Names(typeKey).IsMatch(span) || !RewritesABody.IsMatch(span))
                continue;

            var name = members[i].Groups[1].Value;
            rewriters.Add(name);
            var line = SourceScan.LineOf(code, start);
            if (!string.Equals(name, PairWriter, StringComparison.Ordinal))
                problems.Add($"line {line}: {name} names the core record type and rewrites a body; only {PairWriter} may (it writes the pair whole)");
            else if (!Writes(idKey).IsMatch(span))
                problems.Add($"line {line}: {PairWriter} rewrites a body with the core record type but never writes {IdColumn}");
        }

        return (problems, typeWrites.Count, rewriters);
    }

    [Fact(DisplayName = "042c fix round 2 (B'): server code that writes sprk_corerecordtype also writes sprk_corerecordid; a body rewrite of the pair goes through WithCorePair")]
    public void EveryCoreTypeWrite_AlsoWritesTheId()
    {
        var problems = new List<string>();
        var keyedWriters = new List<string>();
        var rewriters = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var (fileProblems, keyed, bodyRewriters) = Scan(File.ReadAllText(file));
            problems.AddRange(fileProblems.Select(p => $"{relative}: {p}"));
            if (keyed > 0)
                keyedWriters.Add(relative);
            rewriters.AddRange(bodyRewriters.Select(r => $"{relative}:{r}"));
        }

        // Non-vacuity: today's writers of the pair are seen.
        foreach (var expected in new[]
                 {
                     "src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordFamilyReader.cs",
                 })
        {
            if (!keyedWriters.Contains(expected))
                problems.Add($"{expected}: expected to be seen writing the core record type; the scan lost its signal.");
        }

        if (!rewriters.Contains($"src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.cs:{PairWriter}"))
            problems.Add($"RecordCreationService.{PairWriter}: expected to be seen rewriting the pair; the scan lost its signal.");

        Assert.True(problems.Count == 0,
            "A Signal's or Decision Record's core pair is never written half (owner 2026-11-04, task 042c B'): a write of "
            + $"{TypeColumn} needs a write of {IdColumn} beside it, and a body rewrite that carries the type goes through "
            + $"RecordCreationService.{PairWriter}:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "042c fix round 2 (B') control (must fire): a type written without its id, or a body rewrite outside WithCorePair, is found")]
    public void Control_ATypeWrittenWithoutItsId_IsFound()
    {
        Assert.Single(Scan("""entity["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", t);""").Problems);
        Assert.Single(Scan("""e["sprk_CoreRecordType@odata.bind"] = $"/sprk_recordtype_refs({t})";""").Problems);
        Assert.Single(Scan("""fields.Add("sprk_corerecordtype", t);""").Problems);
        Assert.Single(Scan("""writer.WriteString("sprk_corerecordtype@odata.bind", path);""").Problems);
        Assert.Single(Scan("""
            private const string TypeCol = "sprk_corerecordtype";
            void M() { entity[TypeCol] = r; }
            """).Problems);
        Assert.Single(Scan("""entity[SignalCoreRecordAccess.CoreRecordTypeColumn] = r;""").Problems);

        // The id too far away is not the pair.
        Assert.Single(Scan("entity[\"sprk_corerecordtype\"] = r;" + string.Concat(Enumerable.Repeat("\nvar x = 1;", Proximity + 1))
                           + "\nentity[\"sprk_corerecordid\"] = id;").Problems);

        // An id only in a comment is no id.
        Assert.Single(Scan("""
            entity["sprk_corerecordtype"] = r;
            // entity["sprk_corerecordid"] = id;
            """).Problems);

        // A body rewrite that carries the type outside the pair writer, and a pair writer that drops the id.
        Assert.Single(Scan("""
            private const string CoreRecordTypeColumn = "sprk_corerecordtype";
            private static MappedItem Retype(MappedItem item, MappedLookup type)
            {
                return item with { Lookups = [type with { Column = CoreRecordTypeColumn }] };
            }
            """).Problems);
        Assert.Single(Scan("""
            private const string CoreRecordTypeColumn = "sprk_corerecordtype";
            private static MappedItem WithCorePair(MappedItem item, MappedLookup type, string id)
            {
                return item with { JsonBody = body, Lookups = [type with { Column = CoreRecordTypeColumn }] };
            }
            """).Problems);
    }

    [Fact(DisplayName = "042c fix round 2 (B') control (must not fire): a pair write, a read, or a list that only NAMES the columns (a refuse list) is not flagged")]
    public void Control_APairWrite_AReadOrARefuseList_IsNotFound()
    {
        // The pair, in each write shape.
        Assert.Empty(Scan("""
            entity["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", t);
            entity["sprk_corerecordid"] = id.ToString("D");
            """).Problems);
        Assert.Empty(Scan("""
            private const string CoreRecordTypeColumn = "sprk_corerecordtype";
            private const string CoreRecordIdColumn = "sprk_corerecordid";
            void M() { if (t is { } x) entity[CoreRecordTypeColumn] = x; if (i is { } y) entity[CoreRecordIdColumn] = y; }
            """).Problems);

        // Reads: an attribute read, a condition, a select, an equality test, a column set.
        Assert.Empty(Scan("""
            var t = row.GetAttributeValue<EntityReference>("sprk_corerecordtype");
            query.Criteria.AddCondition("sprk_corerecordtype", ConditionOperator.NotNull);
            var filter = $"_sprk_corerecordtype_value eq {id}";
            if (row["sprk_corerecordtype"] == null) return;
            var cols = new ColumnSet("sprk_corerecordtype", "sprk_corerecordid");
            columns.Add(LookupValue(CoreRecordTypeColumn));
            """).Problems);

        // Lists that only NAME the columns: a refuse list, a collection expression, a set initializer.
        Assert.Empty(Scan("""
            RefusedColumns = Columns("sprk_corerecordtype", "sprk_corerecordid"),
            private static readonly string[] Fixed = ["sprk_regardingrecordid", "sprk_corerecordtype", "sprk_corerecordid"];
            private static readonly string[] TypeOnly = ["sprk_corerecordtype"];
            var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_corerecordtype" };
            return RecordTableRules.RefuseColumns(table, fields, ["sprk_corerecordtype"]);
            """).Problems);

        // The one pair writer rewriting a body, with the id written.
        var (problems, _, rewriters) = Scan("""
            private const string CoreRecordTypeColumn = "sprk_corerecordtype";
            private const string CoreRecordIdColumn = "sprk_corerecordid";
            private static MappedItem WithCorePair(MappedItem item, MappedLookup? type, string? id)
            {
                writer.WriteString(CoreRecordIdColumn, id);
                static bool IsPairColumn(string c) => c == CoreRecordTypeColumn;
                return item with { JsonBody = body, Lookups = [type with { Column = CoreRecordTypeColumn }] };
            }
            private static MappedItem Other(MappedItem item) => item with { JsonBody = "{}" };
            """);
        Assert.Empty(problems);
        Assert.Equal([PairWriter], rewriters);
    }
}
