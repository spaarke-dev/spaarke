using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Every <c>EventId</c> number in the ontology / billing range (50300-50399) declared anywhere under <c>src/server/**</c> is unique,
/// and every <c>EventId</c> declaration site under <c>src/server/**</c> is a form the parser understands.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> The ontology lanes each carve their own EventId block (503xx), and two lanes once
/// both took 50316 in different folders; every suite passed, because an EventId is only a number and nothing compiles or asserts
/// it is unique. The cost is silent: two different log events share an id, so an alert or query keyed on the id mixes them.</para>
///
/// <para><b>Scope.</b> Duplicates are judged for ids 50300-50399 across the whole of <c>src/server/**</c> (a collision between lanes
/// is across folders). Ids outside that range are another subsystem's and are not flagged. <c>Services/Signals</c> must still
/// yield declarations, so a moved folder cannot empty the guard silently.</para>
///
/// <para><b>What it matches (the sites).</b> <c>new EventId(...)</c>; <c>EventId X = new(...)</c>, <c>EventId X { get; } = new(...)</c>
/// and <c>EventId X =&gt; new(...)</c>; and <c>[LoggerMessage(...)]</c> that names an EventId (<c>EventId = n</c>) or passes one
/// positionally (<c>[LoggerMessage(n, LogLevel.X, ...)]</c>). The id is read from a decimal literal, positional or named
/// (<c>id: n</c>). Line comments are stripped first (<see cref="SourceScan.CodeText"/>), so an id quoted in a comment does not count.</para>
///
/// <para><b>Completeness tripwire.</b> A site whose id is not a plain decimal literal (a const identifier, a hex literal, an
/// expression) would be invisible to the duplicate check, so it fails the guard as "unrecognised EventId form at file:line" instead
/// of passing silently. Fix it by declaring the id as a decimal literal.</para>
///
/// <para><b>Ratchet.</b> There are no duplicates in range today, so there is no baseline list. If a duplicate is ever baselined,
/// list it here and fail on any not listed; do not renumber a shipped id (it is already in logs and alerts).</para>
///
/// <para><b>Enforced by:</b> this class. MAINTAIN-class structural fitness function (<c>tests/CLAUDE.md</c>).</para>
/// </remarks>
public sealed class OntologyEventIdUniquenessGuardTests
{
    private const int RangeStart = 50300;
    private const int RangeEnd = 50399;

    private static readonly string SignalsDir = Path.Combine(
        SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals");

    private static readonly Regex NewEventIdSite = new(
        @"\bnew\s+EventId\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TargetTypedSite = new(
        @"\bEventId\??\s+\w+\s*(?:\{\s*get;\s*\}\s*)?=>?\s*new\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LoggerMessageSite = new(
        @"\[\s*LoggerMessage\s*\((?<args>.*?)\)\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>A decimal literal, optionally as the named argument <c>id:</c>, right after a constructor's open paren.</summary>
    private static readonly Regex IdAfterParen = new(
        @"\G\s*(?:id\s*:\s*)?(?<id>\d+)\s*[,)]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NamedLoggerMessageId = new(
        @"\bEventId\s*=\s*(?<id>\d+)\s*(?:,|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PositionalLoggerMessage = new(
        @"^\s*(?:eventId\s*:\s*)?(?<first>[^,\s=]+)\s*,\s*(?:level\s*:\s*)?(?:\w+\.)*LogLevel\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Pure logic ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every EventId declaration site in one file's text with its 1-based line; <c>Id</c> is null when the site is not a
    /// form the parser reads (the tripwire's input).
    /// </summary>
    internal static IReadOnlyList<(int? Id, int Line)> Sites(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var sites = new List<(int Index, int? Id)>();

        foreach (var m in new[] { NewEventIdSite, TargetTypedSite }.SelectMany(r => r.Matches(code).Cast<Match>()))
        {
            var id = IdAfterParen.Match(code, m.Index + m.Length);
            sites.Add((m.Index, id.Success ? ParseId(id.Groups["id"].Value) : null));
        }

        foreach (Match m in LoggerMessageSite.Matches(code))
        {
            var args = m.Groups["args"].Value;
            var named = Regex.IsMatch(args, @"\bEventId\s*=", RegexOptions.CultureInvariant);
            var positional = PositionalLoggerMessage.Match(args);
            if (!named && !positional.Success)
            {
                continue; // a LoggerMessage with no EventId has no id to collide
            }

            int? id = null;
            if (named)
            {
                var n = NamedLoggerMessageId.Match(args);
                id = n.Success ? ParseId(n.Groups["id"].Value) : null;
            }
            else if (positional.Groups["first"].Value.All(char.IsAsciiDigit))
            {
                id = ParseId(positional.Groups["first"].Value);
            }

            sites.Add((m.Index, id));
        }

        return sites.OrderBy(s => s.Index).Select(s => (s.Id, SourceScan.LineOf(code, s.Index))).ToList();
    }

    private static int? ParseId(string digits) =>
        int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The EventId numbers declared in one file's text, with the 1-based line of each (unparsed sites are left out).</summary>
    internal static IEnumerable<(int Id, int Line)> Declared(string source) =>
        Sites(source).Where(s => s.Id.HasValue).Select(s => (s.Id!.Value, s.Line));

    /// <summary><c>file:line</c> of every declaration site the parser could not turn into an id.</summary>
    internal static IReadOnlyList<string> Unrecognised(IEnumerable<(string File, string Source)> files) =>
        files.SelectMany(f => Sites(f.Source).Where(s => s.Id is null).Select(s => $"{f.File}:{s.Line}")).ToList();

    /// <summary>Ids in range declared more than once across the files, each with every <c>file:line</c> that declares it.</summary>
    internal static IReadOnlyList<(int Id, IReadOnlyList<string> Sites)> Duplicates(
        IEnumerable<(string File, string Source)> files, int rangeStart = RangeStart, int rangeEnd = RangeEnd)
    {
        return files
            .SelectMany(f => Declared(f.Source).Select(d => (d.Id, Site: $"{f.File}:{d.Line}")))
            .Where(d => d.Id >= rangeStart && d.Id <= rangeEnd)
            .GroupBy(d => d.Id)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(d => d.Site).ToList()))
            .ToList();
    }

    // ── Real tree ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Every EventId number in 50300-50399 declared under src/server is unique")]
    public void RealTree_EventIdsAreUnique()
    {
        var duplicates = Duplicates(ServerFiles());

        Assert.True(duplicates.Count == 0,
            "Two log events share an EventId, so alerts and queries keyed on it mix them. Move the newer declaration to a free id "
            + "(check the ranges in OntologyWriterEvents.cs and the *Events classes beside each service): "
            + string.Join("; ", duplicates.Select(d => $"{d.Id} at {string.Join(", ", d.Sites)}")));
    }

    [Fact(DisplayName = "Every EventId declaration site under src/server is a form the parser reads (tripwire)")]
    public void RealTree_NoUnrecognisedEventIdForm()
    {
        var unrecognised = Unrecognised(ServerFiles());

        Assert.True(unrecognised.Count == 0,
            "An EventId declared in a form this guard cannot read would escape the uniqueness check. Declare the id as a decimal "
            + "literal (new EventId(50316, ...), [LoggerMessage(EventId = 50316, ...)]), or teach the parser the new form: "
            + string.Join("; ", unrecognised.Select(u => "unrecognised EventId form at " + u)));
    }

    [Fact(DisplayName = "The real scan sees the Services/Signals declarations (the scan is not empty)")]
    public void RealTree_ScanSeesSignalsDeclarations()
    {
        var ids = SignalFiles().SelectMany(f => Declared(f.Source)).Select(d => d.Id).ToList();

        // 50300 (OntologyWriterEvents) and 50316 (DecisionRecordWriter) are the two ids that once collided.
        Assert.Contains(50300, ids);
        Assert.Contains(50316, ids);
        Assert.True(ids.Count >= 30, $"Found only {ids.Count} EventId declarations; the scan is probably broken.");

        var all = ServerFiles().SelectMany(f => Declared(f.Source)).Select(d => d.Id).ToList();
        Assert.True(all.Count > ids.Count, "The wide scan found no EventId outside Services/Signals; the src/server walk is probably broken.");
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Control (must fire): the same id declared in two files is reported with both sites")]
    public void Control_DuplicateId_Fires()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));"),
            ("B.cs", "public static readonly EventId Two = new EventId(50316, nameof(Two));"),
            ("C.cs", "[LoggerMessage(EventId = 50399, Level = LogLevel.Error)]"),
        ]);

        var only = Assert.Single(duplicates);
        Assert.Equal(50316, only.Id);
        Assert.Equal(["A.cs:1", "B.cs:1"], only.Sites);
    }

    [Fact(DisplayName = "Control (must fire): a LoggerMessage EventId that repeats a static EventId is reported")]
    public void Control_LoggerMessageDuplicate_Fires()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));"),
            ("B.cs", "[LoggerMessage(EventId = 50316, Level = LogLevel.Error)]"),
        ]);

        Assert.Equal(50316, Assert.Single(duplicates).Id);
    }

    [Fact(DisplayName = "Control (must fire): a named-argument id and a positional LoggerMessage id are read and reported")]
    public void Control_NamedAndPositionalForms_AreDetected()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(id: 50316, name: nameof(One));"),
            ("B.cs", "public static readonly EventId Two { get; } = new(50316, nameof(Two));"),
            ("C.cs", "public static EventId Three => new EventId(id: 50316);"),
            ("D.cs", "[LoggerMessage(50316, LogLevel.Error, \"x\")]"),
            ("E.cs", "[LoggerMessage(\n    EventId = 50316,\n    Level = LogLevel.Error)]"),
        ]);

        var only = Assert.Single(duplicates);
        Assert.Equal(50316, only.Id);
        Assert.Equal(5, only.Sites.Count);
    }

    [Fact(DisplayName = "Control (must fire): an id written as a const, a hex literal or an expression trips the tripwire")]
    public void Control_UnparseableForms_TripTheWire()
    {
        (string File, string Source)[] files =
        [
            ("Const.cs", "public static readonly EventId A = new EventId(SomeConst, \"a\");"),
            ("Hex.cs", "public static readonly EventId B = new(0xC4AC, nameof(B));"),
            ("Expr.cs", "public static readonly EventId C = new(Base + 16, nameof(C));"),
            ("Prop.cs", "public static EventId D => new(SomeConst);"),
            ("LmConst.cs", "[LoggerMessage(EventId = SomeConst, Level = LogLevel.Error)]"),
            ("LmPositional.cs", "[LoggerMessage(SomeConst, LogLevel.Error, \"x\")]"),
        ];

        var tripped = Unrecognised(files);
        Assert.Equal(files.Length, tripped.Count);
        foreach (var (file, _) in files)
        {
            Assert.Contains(tripped, t => t.StartsWith(file + ":", StringComparison.Ordinal));
        }

        // Each unparsed site is invisible to the duplicate check, which is exactly why the tripwire exists.
        Assert.Empty(Duplicates(files));
    }

    [Fact(DisplayName = "Control (must not fire): distinct ids, ids out of range, ids in comments and a LoggerMessage with no EventId pass")]
    public void Control_DistinctIds_DoNotFire()
    {
        (string File, string Source)[] files =
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));\n// was EventId Old = new(50317, nameof(Old));"),
            ("B.cs", "public static readonly EventId Two = new(50317, nameof(Two));"),
            ("C.cs", "[LoggerMessage(EventId = 50318, Level = LogLevel.Error)]"),
            ("D.cs", "[LoggerMessage(LogLevel.Error, \"no id\")]\n[LoggerMessage(Level = LogLevel.Warning, Message = \"no id\")]"),
            ("E.cs", "public static readonly EventId X = new(9701, \"a\");"),
            ("F.cs", "public static readonly EventId Y = new(9701, \"b\");"), // a duplicate, but outside 50300-50399: not this guard's
        ];

        Assert.Empty(Duplicates(files));
        Assert.Empty(Unrecognised(files));

        // A real unparsed site still trips; only the comment on the same line is ignored.
        Assert.Single(Unrecognised([.. files, ("G.cs", "var e = new EventId(SomeConst, \"x\"); // new EventId(0xFF)")]));
    }

    private static IEnumerable<(string File, string Source)> ServerFiles() =>
        SourceScan.ServerSourceFiles().Select(f => (SourceScan.Relative(f), File.ReadAllText(f)));

    private static IEnumerable<(string File, string Source)> SignalFiles()
    {
        Assert.True(Directory.Exists(SignalsDir), $"{SignalsDir} not found");
        return Directory
            .EnumerateFiles(SignalsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (SourceScan.Relative(f), File.ReadAllText(f)));
    }
}
