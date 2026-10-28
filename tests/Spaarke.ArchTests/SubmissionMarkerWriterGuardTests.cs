using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095 (model §9.1 own-Restricted; task 088's marker). <c>sprk_document.sprk_restrictedbysubmission</c> records
/// that a document's own Restricted narrowing was set by an invoice submission. The marker is true only on an <c>.eml</c> that the
/// Outlook add-in's from-email submission creates. It is written only by that create, in the same write as the narrowing it records,
/// and never in an update. An update that sets it would label a narrowing it did not make. An update that clears it would hide one
/// it did make. A create that sets it without the narrowing would leave a marker with no Restricted to mark.
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> This copies <see cref="DocumentAccessPermissionWriterGuardTests"/>: a source scan of <c>src/server/**</c>
/// for every write of the column. A write is an indexer assignment, an <c>Add</c>/<c>TryAdd</c>/<c>SetAttributeValue</c>, an
/// initializer entry, a JSON writer call or a member assignment, through the literal or a constant that holds it
/// (<see cref="KnownAliases"/>). A write also counts when it goes to the request property that carries the marker
/// (<c>CreateDocumentRequest.RestrictedBySubmission</c>). Each write is attributed to its enclosing member.</para>
/// <para><b>The rules.</b></para>
/// <list type="bullet">
/// <item>Every writer is in <see cref="ApprovedWriters"/>.</item>
/// <item>Every approved writer writes the narrowing in the same member: the Access Permission and its own-narrowing record.</item>
/// <item>No member whose name says Update writes the marker, even a listed one (the update-payload ban).</item>
/// <item>The approved writers are called only from the create members (<see cref="ApprovedCallers"/>).</item>
/// <item>Only <c>CreateDocumentRequest</c> carries the marker property; no update request type has one.</item>
/// </list>
/// <para><b>Limits</b> (the same as task 189's guard). A key held in a local variable, a caller-supplied payload and writes outside
/// <c>src/server</c> are not seen. For the app-only generic writers (the Update Record node and output mapping), the run-time
/// refusal <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> names this column (task 088).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same rules over an inline source. A writer in
/// another member and the marker in an update payload are reported, and so is a stager called from an update. The approved create,
/// a read, a comparison, a column list and a comment are not.</para>
/// </remarks>
public class SubmissionMarkerWriterGuardTests
{
    private const string Stager = "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs";
    private const string Persistence = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs";

    /// <summary>A member that writes the marker (or calls a writer): its file (repo-relative, forward slashes), name and reason.</summary>
    private sealed record Writer(string File, string Member, string Reason);

    private static readonly IReadOnlyList<Writer> ApprovedWriters = new[]
    {
        new Writer(Stager, "StageDocumentCommunicationFields",
            "Task 095: it stages the marker onto the ONE document create, only with the own Restricted Access Permission and its " +
            "sprk_accessinheritance record staged in the same write. It throws when either is missing."),
        new Writer(Persistence, "ApplyEmailCommunicationOutcome",
            "Task 095: the one save-time owner of an email document's own narrowing (I-19). Its submissionNarrowing branch sets " +
            "RestrictedBySubmission on the CreateDocumentRequest together with the communication link, AccessPermission = " +
            "Restricted and AccessInheritance.OwnNarrowing(Restricted), which is non-provisional; it throws without a communication."),
    };

    /// <summary>The only members that may call an approved writer: document creates, every one.</summary>
    private static readonly IReadOnlyList<Writer> ApprovedCallers = new[]
    {
        new Writer(Stager, "CreateDocumentAsync", "Task 130/095: the document create (DocumentCreateStagesCommunicationFieldsGuardTests)."),
        new Writer(Persistence, "CreateDocumentWithSpePointersAsync",
            "Task 095: the Office save's create; the ONLY caller that passes submissionNarrowing (SubmissionCaller)."),
        new Writer(Worker, "CreateDocumentRecordAsync", "Task 130: the worker's fallback create (no submission flag)."),
        new Writer(Worker, "ProcessSingleAttachmentAsync", "Task 130: the worker's attachment-child create (no submission flag)."),
    };

    private const string Worker = "src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs";

    /// <summary>The one member that may pass the submission flag (a third argument) to the outcome.</summary>
    private static readonly Writer SubmissionCaller = new(Persistence, "CreateDocumentWithSpePointersAsync", "Task 095");

    /// <summary>The constants that hold the column.</summary>
    private static readonly IReadOnlyList<(string File, string Name)> KnownAliases = new[]
    {
        ("src/server/api/Sprk.Bff.Api/Services/Dataverse/ParentLineage.cs", "SubmissionMarkerColumn"), // task 088
        (Stager, "DocumentSubmissionMarkerColumn"), // task 095
    };

    [Fact(DisplayName = "Task 095: the submission marker is written only by the approved create path, with its narrowing, never in an update")]
    public void TheSubmissionMarkerIsWrittenOnlyByTheSubmissionCreate()
    {
        var scan = new Scan();
        foreach (var f in SourceScan.ServerSourceFiles())
            ScanFile(Normalize(SourceScan.Relative(f)), File.ReadAllLines(f), scan);

        // Non-vacuity: both approved writers and both approved callers are seen.
        foreach (var w in ApprovedWriters)
            Assert.True(scan.Writes.Any(s => s.File == w.File && s.Member == w.Member),
                $"{w.File} {w.Member} should be seen writing the submission marker: the scan lost its signal.");
        foreach (var c in ApprovedCallers)
            Assert.True(scan.Calls.Any(s => s.File == c.File && s.Member == c.Member),
                $"{c.File} {c.Member} should be seen calling an approved marker writer: the scan lost its signal.");

        Assert.True(scan.Calls.Any(c => c.File == SubmissionCaller.File && c.Member == SubmissionCaller.Member && PassesSubmissionFlag(c)),
            $"{SubmissionCaller.File} {SubmissionCaller.Member} should be seen passing the submission flag: the scan lost its signal.");

        var problems = Judge(scan, ApprovedWriters, ApprovedCallers, SubmissionCaller);
        Assert.True(problems.Count == 0,
            "sprk_restrictedbysubmission is written only in the from-email submission's .eml create, in the same write as the own " +
            "Restricted narrowing it records, and never in an update (task 095):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 095: only CreateDocumentRequest carries the marker; no update request type has it")]
    public void OnlyTheCreateRequestCarriesTheMarker()
    {
        Assert.Null(typeof(Spaarke.Dataverse.UpdateDocumentRequest).GetProperty("RestrictedBySubmission"));

        var carriers = new[] { typeof(Spaarke.Dataverse.CreateDocumentRequest).Assembly, typeof(Program).Assembly }
            .SelectMany(LoadableTypes)
            .Where(t => t.GetProperty("RestrictedBySubmission", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null)
            .Select(t => t.FullName)
            .ToArray();

        Assert.Equal(new[] { typeof(Spaarke.Dataverse.CreateDocumentRequest).FullName }, carriers);
    }

    [Fact(DisplayName = "Task 095: every constant that holds sprk_restrictedbysubmission is one the writer scan matches")]
    public void EveryConstantHoldingTheColumnIsMatchedByTheScan()
    {
        var found = SourceScan.ServerSourceFiles()
            .SelectMany(f => AliasesIn(File.ReadAllLines(f)).Select(name => (File: Normalize(SourceScan.Relative(f)), Name: name)))
            .ToList();

        var problems = found.Except(KnownAliases).Select(a => $"NEW constant {a.Name} in {a.File}: list it")
            .Concat(KnownAliases.Except(found).Select(a => $"STALE alias {a.Name} in {a.File}: remove it"))
            .Concat(KnownAliases.Where(a => !KeyMatches(a.Name)).Select(a => $"UNSEEN alias {a.Name} in {a.File}: Key does not match it"))
            .ToList();
        Assert.True(problems.Count == 0, "A constant holding sprk_restrictedbysubmission must be matched by the scan:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 095 guard control: a writer in another member and the marker in an update payload fire; the approved create and a read do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var scan = new Scan();
        ScanFile("control.cs", ControlSource.Split('\n'), scan);

        Assert.Equal(
            new[] { "AnonymousUpdatePayload", "ApprovedCreate", "ApprovedRequest", "OtherWriter", "UpdateDocumentAsync" },
            scan.Writes.Select(s => s.Member).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(scan.Writes, s => s.Member is "Reads");

        // The update ban fires even when the update member is LISTED as a writer.
        var approved = new[]
        {
            new Writer("control.cs", "ApprovedCreate", "control"),
            new Writer("control.cs", "ApprovedRequest", "control"),
            new Writer("control.cs", "UpdateDocumentAsync", "listed, but an update"),
        };
        var callers = new[]
        {
            new Writer("control.cs", "CreateDocumentAsync", "control"),
            new Writer("control.cs", "WorkerCreateAsync", "a listed create that must not pass the flag"),
        };
        var submissionCaller = new Writer("control.cs", "CreateDocumentAsync", "control");
        var problems = Judge(scan, approved, callers, submissionCaller);

        Assert.Contains(problems, p => p.StartsWith("UPDATE PAYLOAD", StringComparison.Ordinal) && p.Contains("UpdateDocumentAsync"));
        Assert.Contains(problems, p => p.StartsWith("UPDATE PAYLOAD", StringComparison.Ordinal) && p.Contains("AnonymousUpdatePayload"));
        Assert.Contains(problems, p => p.StartsWith("NEW writer", StringComparison.Ordinal) && p.Contains("OtherWriter"));
        Assert.Contains(problems, p => p.StartsWith("CALLED from", StringComparison.Ordinal) && p.Contains("RefreshDocumentUpdate"));
        Assert.Contains(problems, p => p.StartsWith("SUBMISSION FLAG", StringComparison.Ordinal) && p.Contains("WorkerCreateAsync"));
        // Must not fire: the approved create, the approved request, its create caller and the reads add nothing more.
        Assert.Equal(5, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains(" in ApprovedCreate") || p.Contains(" in ApprovedRequest")
            || p.Contains("Reads") || p.Contains(" CreateDocumentAsync:") || p.StartsWith("NO NARROWING", StringComparison.Ordinal));

        // An approved writer that drops the narrowing is reported.
        var noNarrowing = Judge(scan, approved.Append(new Writer("control.cs", "OtherWriter", "listed without narrowing")).ToArray(),
            callers, submissionCaller);
        Assert.Contains(noNarrowing, p => p.StartsWith("NO NARROWING", StringComparison.Ordinal) && p.Contains("OtherWriter"));

        Assert.Equal(new[] { "MarkerKey" }, AliasesIn(ControlSource.Split('\n')).ToArray());
    }

    [Fact(DisplayName = "Task 095 guard control: an alias Key does not match is reported; the listed forms are matched")]
    public void KeyMatches_MustFireAndMustNotFire()
    {
        Assert.False(KeyMatches("MarkerKey"), "a constant Key misses must be reported UNSEEN (must fire)");
        Assert.False(KeyMatches("SubmissionMarkerColumnName"), "a longer name is not the column constant");
        Assert.True(KeyMatches("SubmissionMarkerColumn"));
        Assert.True(KeyMatches("DocumentSubmissionMarkerColumn"));
        Assert.True(KeyMatches("InheritedAccessPermission.SubmissionMarkerColumn"));
    }

    private static bool KeyMatches(string name) => KeyedWrites[0].IsMatch($"update[{name}] = value;");

    // ── the callers one level up (095 review F6 / uac K1) ──────────────────────────────────────────────────────────
    //
    // The rules above stop at CreateDocumentWithSpePointersAsync, the one member that passes the flag to the outcome. Two more rules
    // close the path to it: only the Office save (OfficeService.SaveCoreAsync) passes submissionNarrowing to that create, and only the
    // from-email submission (FromEmailSubmissionService.SubmitAsync) runs the submission save. A positional flag (16 top-level arguments)
    // counts as passing it. Limits: a call through a delegate or reflection is not seen.

    private const string OfficeServiceFile = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs";
    private const string FromEmailServiceFile = "src/server/api/Sprk.Bff.Api/Services/Finance/Submission/FromEmailSubmissionService.cs";

    /// <summary>The one member that may pass <c>submissionNarrowing</c> to <c>CreateDocumentWithSpePointersAsync</c>.</summary>
    private static readonly Writer NarrowingCaller = new(OfficeServiceFile, "SaveCoreAsync",
        "Task 095: the save pipeline, which passes submission is not null; SaveEmailForSubmissionAsync is its only submission entry.");

    /// <summary>The one member that may call <c>SaveEmailForSubmissionAsync</c>.</summary>
    private static readonly Writer SubmissionSaveCaller = new(FromEmailServiceFile, "SubmitAsync",
        "Task 095: the from-email submission, after the marks, the firm, the replay of K and the communication's filing.");

    /// <summary>The position of <c>submissionNarrowing</c> in the full <c>CreateDocumentWithSpePointersAsync</c> signature (1-based).</summary>
    private const int NarrowingPosition = 16;

    [Fact(DisplayName = "Task 095: only OfficeService.SaveCoreAsync passes submissionNarrowing, and only the from-email submission runs the submission save")]
    public void TheSubmissionNarrowingIsReachedOnlyThroughTheFromEmailSubmission()
    {
        var uses = new List<Use>();
        foreach (var f in SourceScan.ServerSourceFiles())
            uses.AddRange(ScanUses(Normalize(SourceScan.Relative(f)), File.ReadAllLines(f)));

        // Non-vacuity: the approved call of each is seen.
        Assert.True(uses.Any(u => u.Kind == UseKind.NarrowingPass && u.File == NarrowingCaller.File && u.Member == NarrowingCaller.Member),
            "OfficeService.SaveCoreAsync should be seen passing submissionNarrowing: the scan lost its signal.");
        Assert.True(uses.Any(u => u.Kind == UseKind.SubmissionSave && u.File == SubmissionSaveCaller.File && u.Member == SubmissionSaveCaller.Member),
            "FromEmailSubmissionService.SubmitAsync should be seen calling SaveEmailForSubmissionAsync: the scan lost its signal.");

        var problems = JudgeUses(uses, NarrowingCaller, SubmissionSaveCaller);
        Assert.True(problems.Count == 0,
            "The from-email submission's narrowing is reached only through FromEmailSubmissionService.SubmitAsync → " +
            "SaveEmailForSubmissionAsync → OfficeService.SaveCoreAsync (task 095):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 095 guard control: another narrowing pass or submission-save call fires; the approved ones, plain creates and declarations do not")]
    public void Callers_MustFireAndMustNotFire()
    {
        var uses = ScanUses("control.cs", CallerControlSource.Split('\n')).ToList();
        var problems = JudgeUses(uses,
            new Writer("control.cs", "SaveCoreAsync", "control"), new Writer("control.cs", "SubmitAsync", "control"));

        // Must fire: a named pass and a positional pass elsewhere, and a submission save from another member (call and method group).
        Assert.Contains(problems, p => p.StartsWith("NARROWING PASS", StringComparison.Ordinal) && p.Contains("OtherSaveAsync"));
        Assert.Contains(problems, p => p.StartsWith("NARROWING PASS", StringComparison.Ordinal) && p.Contains("PositionalAsync"));
        Assert.Contains(problems, p => p.StartsWith("SUBMISSION SAVE", StringComparison.Ordinal) && p.Contains("SneakyAsync"));
        Assert.Contains(problems, p => p.StartsWith("SUBMISSION SAVE", StringComparison.Ordinal) && p.Contains("MethodGroup"));
        // Must not fire: the approved members, a create without the flag (15 arguments), the declarations and a comment.
        Assert.Equal(4, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains(" SaveCoreAsync:") || p.Contains(" SubmitAsync:") || p.Contains(" ForwardingAsync:"));
    }

    private enum UseKind { NarrowingPass, SubmissionSave }

    private sealed record Use(UseKind Kind, string File, string Member, int Line);

    private static readonly Regex CreateCall = new(@"\bCreateDocumentWithSpePointersAsync\s*\(", RegexOptions.Compiled);

    private static readonly Regex SubmissionSaveUse = new(
        @"(?<!\bIOfficeSubmissionEmailSave\.)(?<!Task<SubmissionEmailSaveOutcome>\s+)\bSaveEmailForSubmissionAsync\b", RegexOptions.Compiled);

    private static IEnumerable<Use> ScanUses(string file, IReadOnlyList<string> lines)
    {
        var bare = lines.Select(SourceScan.StripLineComment).Select(l => StringLiteral.Replace(l, "\"\"")).ToList();
        var members = MembersByLine(bare);
        for (var i = 0; i < bare.Count; i++)
        {
            foreach (Match m in SubmissionSaveUse.Matches(bare[i]))
                yield return new Use(UseKind.SubmissionSave, file, members[i], i + 1);

            foreach (Match m in CreateCall.Matches(bare[i]))
            {
                // A call's arguments up to its closing parenthesis (at most 40 lines: the Office save's call carries comments).
                var text = bare[i][(m.Index + m.Length)..];
                for (var j = i + 1; j < bare.Count && j <= i + 40; j++)
                    text += "\n" + bare[j];
                var (args, isDeclaration) = TopLevelArguments(text);
                if (!isDeclaration && (args.Any(a => Regex.IsMatch(a, @"^\s*submissionNarrowing\s*:")) || args.Count >= NarrowingPosition))
                    yield return new Use(UseKind.NarrowingPass, file, members[i], i + 1);
            }
        }
    }

    /// <summary>The top-level arguments of a call whose text starts just after its "("; a parameter list (a declaration) is flagged.</summary>
    private static (List<string> Args, bool IsDeclaration) TopLevelArguments(string text)
    {
        var args = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (c is '(' or '[' or '{') depth++;
            if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    args.Add(current.ToString());
                    break;
                }

                depth--;
            }

            if (c == ',' && depth == 0)
            {
                args.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        // A declaration's parameters are "Type name" pairs; the overload's own signature is not a call.
        var isDeclaration = args.Count > 0 && Regex.IsMatch(args[0].Trim(), @"^[A-Za-z_][\w.<>?]*\s+[A-Za-z_]\w*$");
        return (args, isDeclaration);
    }

    private static List<string> JudgeUses(IEnumerable<Use> uses, Writer narrowingCaller, Writer submissionSaveCaller)
    {
        var problems = new List<string>();
        foreach (var u in uses.OrderBy(u => u.File, StringComparer.Ordinal).ThenBy(u => u.Line))
        {
            if (u.Kind == UseKind.NarrowingPass && !(u.File == narrowingCaller.File && u.Member == narrowingCaller.Member))
                problems.Add($"NARROWING PASS from {u.File}:{u.Line} {u.Member}: only {narrowingCaller.Member} passes submissionNarrowing.");
            else if (u.Kind == UseKind.SubmissionSave && !(u.File == submissionSaveCaller.File && u.Member == submissionSaveCaller.Member))
                problems.Add($"SUBMISSION SAVE from {u.File}:{u.Line} {u.Member}: only {submissionSaveCaller.Member} runs SaveEmailForSubmissionAsync.");
        }

        return problems;
    }

    // The callers' control source. It is never compiled; only the detector reads it.
    private const string CallerControlSource = """
        internal interface IOfficeSubmissionEmailSave
        {
            Task<SubmissionEmailSaveOutcome> SaveEmailForSubmissionAsync(
                SaveRequest request, IReadOnlyList<string> marked, string userId, HttpContext http, CancellationToken ct);
        }

        internal sealed class Control
        {
            async Task<SubmissionEmailSaveOutcome> IOfficeSubmissionEmailSave.SaveEmailForSubmissionAsync(
                SaveRequest request, IReadOnlyList<string> marked, string userId, HttpContext http, CancellationToken ct)
            {
                return await SaveCoreAsync(request, userId, http, new SubmissionSaveState(), ct);
            }

            private async Task<SaveResponse> SaveCoreAsync(SaveRequest request, string userId, HttpContext http, SubmissionSaveState? submission, CancellationToken ct)
            {
                var created = await _persistence.CreateDocumentWithSpePointersAsync(
                    request, drive, item, url, name, docName, size, userId, ct,
                    // a comment, with commas, inside the call
                    pre, team, person,
                    emailCommunicationId: communication,
                    submissionNarrowing: submission is not null);
                return created;
            }

            private async Task OtherSaveAsync(SaveRequest request)
            {
                await _persistence.CreateDocumentWithSpePointersAsync(request, d, i, u, n, n, s, user, ct, submissionNarrowing: true);
            }

            private async Task PositionalAsync(SaveRequest request)
            {
                await _persistence.CreateDocumentWithSpePointersAsync(request, d, i, u, n, n, s, user, ct, p, t, c, e, j, f, true);
            }

            public Task<(Guid, bool, Guid?)> ForwardingAsync(SaveRequest request, string driveId)
                => _persistence.CreateDocumentWithSpePointersAsync(
                    request, d, i, u, n, n, s, user, ct, p, t, c, e, j, f);

            public async Task<FromEmailSubmissionOutcome> SubmitAsync(FromEmailSubmissionInput input, CancellationToken ct)
            {
                var outcome = await _save.SaveEmailForSubmissionAsync(request, shas, user, http, ct);
                // _save.SaveEmailForSubmissionAsync(request, shas, user, http, ct); in a comment
                return outcome;
            }

            private async Task SneakyAsync(SaveRequest request)
            {
                await ((IOfficeSubmissionEmailSave)this).SaveEmailForSubmissionAsync(request, shas, user, http, ct);
            }

            private void MethodGroup()
            {
                Func<SaveRequest, IReadOnlyList<string>, string, HttpContext, CancellationToken, Task<SubmissionEmailSaveOutcome>> run = _save.SaveEmailForSubmissionAsync;
            }
        }
        """;

    // ── rules ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<string> Judge(Scan scan, IReadOnlyList<Writer> approved, IReadOnlyList<Writer> callers, Writer submissionCaller)
    {
        var problems = new List<string>();
        foreach (var site in scan.Writes.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line))
        {
            if (site.Member.Contains("Update", StringComparison.OrdinalIgnoreCase))
                problems.Add($"UPDATE PAYLOAD: {site.File}:{site.Line} in {site.Member}: {site.Statement}");
            else if (!approved.Any(w => w.File == site.File && w.Member == site.Member))
                problems.Add($"NEW writer: {site.File}:{site.Line} in {site.Member}: {site.Statement}");
        }

        foreach (var w in approved.Where(w => !w.Member.Contains("Update", StringComparison.OrdinalIgnoreCase)))
        {
            if (!scan.Writes.Any(s => s.File == w.File && s.Member == w.Member))
                continue; // non-vacuity is asserted by the caller
            var body = scan.Bodies.TryGetValue((w.File, w.Member), out var b) ? b : string.Empty;
            if (!PermissionWrite.IsMatch(body) || !InheritanceWrite.IsMatch(body))
                problems.Add($"NO NARROWING: {w.File} {w.Member} writes the marker without the Access Permission and its record in the same member.");
        }

        var writerNames = approved.Select(w => w.Member).ToHashSet(StringComparer.Ordinal);
        foreach (var call in scan.Calls.Where(c => writerNames.Contains(c.Callee) && c.Member != c.Callee))
        {
            if (call.Member.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || !callers.Any(c => c.File == call.File && c.Member == call.Member))
                problems.Add($"CALLED from {call.File}:{call.Line} {call.Member}: {call.Callee} is called only from the create members.");
            else if (PassesSubmissionFlag(call) && !(call.File == submissionCaller.File && call.Member == submissionCaller.Member))
                problems.Add($"SUBMISSION FLAG from {call.File}:{call.Line} {call.Member}: only {submissionCaller.Member} passes it.");
        }

        return problems;
    }

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Site(string File, string Member, int Line, string Statement);

    /// <summary>A call of a writer; <see cref="Args"/> is its argument text, read across lines up to the statement's end.</summary>
    private sealed record Call(string File, string Member, string Callee, int Line, string Args);

    private sealed class Scan
    {
        public List<Site> Writes { get; } = new();
        public List<Call> Calls { get; } = new();
        public Dictionary<(string File, string Member), string> Bodies { get; } = new();
    }

    private const string Key = @"(?:""sprk_restrictedbysubmission""|(?:\w+\.)*\w*SubmissionMarkerColumn\b)";

    private static readonly Regex[] KeyedWrites =
    {
        new(@"\[\s*" + Key + @"\s*\]\s*=(?![=>])", RegexOptions.Compiled),
        new(@"\.(?:Add|TryAdd|SetAttributeValue)\(\s*" + Key + @"\s*,", RegexOptions.Compiled),
        new(@"(?:^|[{,])\s*\{\s*" + Key + @"\s*,", RegexOptions.Compiled),
        new(@"\.Write\w*\(\s*" + Key, RegexOptions.Compiled),
    };

    /// <summary>A member assignment of the column (an anonymous payload) or of the request property, matched with strings blanked.</summary>
    private static readonly Regex MemberWrite = new(
        @"(?<![\w""])(?:sprk_restrictedbysubmission|RestrictedBySubmission)\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PermissionWrite = new(@"\bAccessPermission(?:Column)?\s*\]?\s*=(?![=>])", RegexOptions.Compiled);

    private static readonly Regex InheritanceWrite = new(@"\b(?:Document)?AccessInheritance(?:Record|Column)\s*\]?\s*=(?![=>])", RegexOptions.Compiled);

    private static readonly Regex WriterCall = new(
        @"\b(?<callee>StageDocumentCommunicationFields|ApplyEmailCommunicationOutcome|ApprovedCreate|ApprovedRequest)\s*\((?<args>[^;]*)", RegexOptions.Compiled);

    /// <summary>A third argument to the outcome: the submission flag.</summary>
    private static bool PassesSubmissionFlag(Call call) =>
        call.Callee is "ApplyEmailCommunicationOutcome" or "ApprovedRequest" && call.Args.Split(',').Length >= 3;

    private static readonly Regex Alias = new(
        @"\b(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*""sprk_restrictedbysubmission""\s*;", RegexOptions.Compiled);

    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|extern|unsafe|new|partial|readonly)\s+)*" +
        @"(?!(?:return|await|new|throw|yield|else|case|in|is|as|var|using|select|where|from|let|goto|when)\b)" +
        @"(?:[A-Za-z_][\w.]*(?:<[^;{}]*?>)?|\([^;{}]*?\))(?:\[\])?\??\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|\$?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'", RegexOptions.Compiled);

    private static string Normalize(string relative) => relative.Replace('\\', '/');

    private static void ScanFile(string file, IReadOnlyList<string> lines, Scan into)
    {
        var code = lines.Select(SourceScan.StripLineComment).ToList();
        var bare = code.Select(l => StringLiteral.Replace(l, "\"\"")).ToList();
        var members = MembersByLine(bare);

        for (var i = 0; i < code.Count; i++)
        {
            var key = (file, members[i]);
            into.Bodies[key] = (into.Bodies.TryGetValue(key, out var body) ? body + "\n" : string.Empty) + bare[i];

            if (KeyedWrites.Any(r => r.IsMatch(code[i])) || MemberWrite.IsMatch(bare[i]))
                into.Writes.Add(new Site(file, members[i], i + 1, code[i].Trim()));

            foreach (Match m in WriterCall.Matches(bare[i]))
            {
                // A call wrapped over several lines: read its arguments on to the statement's end (at most 8 lines).
                var args = m.Groups["args"].Value;
                for (var j = i + 1; j < bare.Count && j <= i + 8 && !bare[j - 1].Contains(';') && !bare[j - 1].Contains('{'); j++)
                    args += " " + bare[j];
                into.Calls.Add(new Call(file, members[i], m.Groups["callee"].Value, i + 1, args.Split(';')[0]));
            }
        }
    }

    private static IEnumerable<string> AliasesIn(IReadOnlyList<string> lines) =>
        lines.Select(SourceScan.StripLineComment).SelectMany(l => Alias.Matches(l).Select(m => m.Groups[1].Value));

    /// <summary>The member each line belongs to (the same detector as <see cref="DocumentAccessPermissionWriterGuardTests"/>).</summary>
    private static string[] MembersByLine(IReadOnlyList<string> bare)
    {
        var result = new string[bare.Count];
        var stack = new List<(string Name, int Depth, bool Opened)>();
        var depth = 0;
        for (var i = 0; i < bare.Count; i++)
        {
            var line = bare[i];
            if (Declaration.Match(line) is { Success: true } declared)
                stack.Add((declared.Groups["name"].Value, depth, false));
            result[i] = stack.Count > 0 ? stack[^1].Name : "(type level)";

            foreach (var c in line)
            {
                if (c == '{')
                {
                    depth++;
                    if (stack.Count > 0 && !stack[^1].Opened && depth == stack[^1].Depth + 1)
                        stack[^1] = stack[^1] with { Opened = true };
                }
                else if (c == '}')
                {
                    depth--;
                    while (stack.Count > 0 && stack[^1].Opened && depth <= stack[^1].Depth)
                        stack.RemoveAt(stack.Count - 1);
                }
                else if (c == ';')
                {
                    while (stack.Count > 0 && !stack[^1].Opened && depth == stack[^1].Depth)
                        stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        return result;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // The control source. It is never compiled; only the detector reads it.
    private const string ControlSource = """
        internal sealed class Control
        {
            private const string MarkerKey = "sprk_restrictedbysubmission";

            internal async Task CreateDocumentAsync(Entity document, CreateDocumentRequest request, Guid? id, bool submission)
            {
                ApprovedCreate(document, request);
                var restricted = IsEmailDerived(type) && ApprovedRequest(request, id,
                    submission);
                await Task.CompletedTask;
            }

            internal async Task WorkerCreateAsync(CreateDocumentRequest request, Guid? id)
            {
                var restricted = ApprovedRequest(request, id);
                var marked = ApprovedRequest(request, id, true);
                await Task.CompletedTask;
            }

            internal static void ApprovedCreate(Entity document, CreateDocumentRequest request)
            {
                document[AccessPermissionColumn] = new OptionSetValue(100000002);
                document[DocumentAccessInheritanceColumn] = request.AccessInheritanceRecord;
                document[DocumentSubmissionMarkerColumn] = true;
            }

            internal static bool ApprovedRequest(CreateDocumentRequest create, Guid? id, bool submission = false)
            {
                create.AccessPermission = 100000002;
                create.AccessInheritanceRecord = "x";
                create.RestrictedBySubmission = true;
            }

            internal static void OtherWriter(Dictionary<string, object> fields)
            {
                fields["sprk_restrictedbysubmission"] = true;
            }

            internal async Task UpdateDocumentAsync(Entity update, CreateDocumentRequest request)
            {
                update[InheritedAccessPermission.SubmissionMarkerColumn] = false;
                update[AccessPermissionColumn] = new OptionSetValue(1);
                update[DocumentAccessInheritanceColumn] = "x";
                await Task.CompletedTask;
            }

            internal object AnonymousUpdatePayload() => new { sprk_name = "x", sprk_restrictedbysubmission = false };

            internal void RefreshDocumentUpdate(Entity document, CreateDocumentRequest request)
            {
                ApprovedCreate(document, request);
            }

            internal void Reads(Entity row, CreateDocumentRequest request, IEnumerable<string> columns)
            {
                var stored = row["sprk_restrictedbysubmission"];
                if (request.RestrictedBySubmission) { }
                if (request.RestrictedBySubmission == true) { }
                var set = new ColumnSet("sprk_name", "sprk_restrictedbysubmission");
                var named = columns.FirstOrDefault(k => k is Column or SubmissionMarkerColumn);
                var marked = row.GetAttributeValue<bool>(InheritedAccessPermission.SubmissionMarkerColumn);
                // document["sprk_restrictedbysubmission"] = true;
                _logger.LogInformation("sprk_restrictedbysubmission = {Value} {", value);
            }
        }
        """;
}
