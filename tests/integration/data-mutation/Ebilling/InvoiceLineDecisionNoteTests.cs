using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095f-2d1 (EB-D-236; design 1 and 5.1): the optional note of an individual line decision (accept, reduce, disallow), over the
/// in-memory Dataverse (<see cref="ApprovalWorld"/>) with the REAL line decision service, totals service, recorder and writer harness. Each test
/// proves the stored state: the line's own Decision Record (<c>Values</c> inside <c>sprk_steps</c>, and its content hash) and the rows the
/// decision must NOT touch (the invoice's note columns, the correction rows).
/// </summary>
/// <remarks>
/// <para><b>Golden hashes (N2).</b> Captured from the code BEFORE 095f-2d1 (base 545ba823dc, two captures agreed) on the same fixed world: submission
/// <see cref="FixedSubmission"/>, one Flagged 400 line, decision id <see cref="FixedDecision"/>, decider Alice, reason "Rate above the card", and for
/// a Reduce an approved amount of 300. A line decision without a note, or with a whitespace-only one, must hash byte-identically, so a retry of a
/// pre-change decision is never refused by the writer.</para>
/// <para><b>Repeat with another note.</b> The note is inside the content hash, so the same decision id with another note (or with the note dropped)
/// is other content by the writer's own comparison (as a changed reason or amount is). Since ISS-029 (task iss029) that comparison runs before any
/// write: the repeat is 409 <c>ebilling.decision.id_reused</c>, the line is not rewritten (same row version), no second record is written and the
/// first note stays on the record. Full coverage: <see cref="InvoiceLineDecisionIdReusedTests"/>.</para>
/// </remarks>
public sealed class InvoiceLineDecisionNoteTests
{
    private static readonly Guid FixedSubmission = Guid.Parse("09500000-0000-4000-8000-00000000f2a1");
    private static readonly Guid FixedDecision = Guid.Parse("09500000-0000-4000-8000-00000000d201");

    /// <summary>Golden billing hashes of a line decision with no note, captured on base 545ba823dc (before the change).</summary>
    private const string AcceptNoNoteHash = "7AC93232492FFDCB1B61DBA27964018A498F81496AFD28B70070789639FE3343";
    private const string ReduceNoNoteHash = "4E5038DC295FCA9EAC63DD5AF7725BDA866E39124DC93E169616275EBCCC36CD";
    private const string DisallowNoNoteHash = "3BC0C09CC3810B18F1741BEADE63FD64DB03BE44C4875843E8A816BBECFA3240";

    private const string Sentinel = "NOTE-095F2D1-SENTINEL";
    private const string DefaultReason = "Rate above the card";

    /// <summary>An extraction JSON as 049 stores it, so Accept in bulk writes its seven "Accepted in bulk" correction rows.</summary>
    private const string Extraction =
        "{\"fields\":{\"Date\":{\"confidence\":0.95,\"combined\":0.9,\"page\":1},\"Hours\":{\"confidence\":0.9,\"page\":1},\"Rate\":{\"confidence\":0.92,\"page\":1},"
        + "\"Quantity\":{\"confidence\":null,\"page\":null},\"Amount\":{\"confidence\":0.99,\"page\":1},\"Narrative\":{\"confidence\":0.8,\"page\":1}},"
        + "\"coding\":{\"status\":\"Coded\",\"confidence\":0.82,\"task\":{\"code\":\"L110\",\"mismatch\":false},\"activity\":{\"code\":\"A101\",\"mismatch\":false},\"expense\":{\"code\":null}}}";

    public static TheoryData<LineDecisionAction> Actions => new() { LineDecisionAction.Accept, LineDecisionAction.Reduce, LineDecisionAction.Disallow };

    private static string CodeOf(LineDecisionAction action) => action switch
    {
        LineDecisionAction.Accept => "accept-line",
        LineDecisionAction.Reduce => "reduce-line",
        _ => "disallow-line",
    };

    private static string GoldenOf(LineDecisionAction action) => action switch
    {
        LineDecisionAction.Accept => AcceptNoNoteHash,
        LineDecisionAction.Reduce => ReduceNoNoteHash,
        _ => DisallowNoNoteHash,
    };

    private static ApprovalWorld FixedWorld(out Entity line)
    {
        var w = new ApprovalWorld(ownSubmissionId: FixedSubmission);
        line = w.AddLine(InvoiceLineStatus.Flagged, 400m);
        return w;
    }

    private static LineDecisionCommand CommandOf(
        ApprovalWorld w, LineDecisionAction action, IReadOnlyList<Guid> lineIds, string? note, Guid? decisionId = null, string? reason = DefaultReason, bool bulk = false) =>
        new(
            w.InvoiceId, decisionId ?? FixedDecision, action, action == LineDecisionAction.Accept ? null : reason,
            lineIds.Select(id => new LineDecisionLine(id, action == LineDecisionAction.Reduce ? 300m : null)).ToList(),
            AliceUser, bulk, CallerToken, note);

    private static Task<LineDecisionResult> Decide(
        ApprovalWorld w, LineDecisionAction action, Guid lineId, string? note, Guid? decisionId = null, string? reason = DefaultReason) =>
        w.LineService().DecideAsync(CommandOf(w, action, [lineId], note, decisionId, reason), CancellationToken.None);

    private static JsonNode Steps(Entity record) => JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!;

    private static string HashOf(Entity record) => Steps(record)["reviewHash"]!.GetValue<string>();

    /// <summary>The record's <c>values</c> node: null JSON when the decision carries none.</summary>
    private static JsonNode? ValuesOf(Entity record) => Steps(record)["steps"]!.AsArray().Single()!["values"];

    private static bool HasNoteKey(Entity record) => ValuesOf(record) is JsonObject values && values.ContainsKey("note");

    private static void AssertRefusedBeforeAnyLockOrRead(ApprovalWorld w)
    {
        w.Locks.Acquired.Should().BeEmpty("the note is checked before any lock");
        w.Entities.Invocations.Should().BeEmpty("and before any read");
        w.Writes.Should().Be(0);
        w.H.Created.Should().BeEmpty();
    }

    // ── One note, on the line's own record ──

    [Theory(DisplayName = "095f-2d1: a single-line note is in the line's own record Values, trimmed, with \\r\\n and \\t kept; the reason and approved amount are unchanged")]
    [MemberData(nameof(Actions))]
    public async Task Note_IsInTheLinesOwnRecord(LineDecisionAction action)
    {
        var w = FixedWorld(out var line);

        var result = await Decide(w, action, line.Id, "  Spoke to the partner.\r\nAgreed\tto this.  ");

        result.IsApplied.Should().BeTrue(result.Code + " " + result.Detail);
        var record = w.RecordsOf(CodeOf(action)).Should().ContainSingle().Subject;
        var values = ValuesOf(record)!.AsObject();
        values["note"]!.GetValue<string>().Should().Be("Spoke to the partner.\r\nAgreed\tto this.");
        if (action == LineDecisionAction.Reduce)
        {
            values["approvedAmount"]!.GetValue<decimal>().Should().Be(300m, "the amount stays beside the note");
        }
        else
        {
            values.ContainsKey("approvedAmount").Should().BeFalse();
        }

        record.GetAttributeValue<string>("sprk_reason").Should().Be(action == LineDecisionAction.Accept ? null : DefaultReason, "the reason stays the record's own");
        result.Lines!.Single().Reason.Should().Be(action == LineDecisionAction.Accept ? null : DefaultReason);
    }

    [Theory(DisplayName = "095f-2d1: ONE note on a multi-line call goes onto EVERY line's own record (N lines, N records, the same note), none onto the bulk correction rows")]
    [MemberData(nameof(Actions))]
    public async Task MultiLine_OneNote_GoesOnEveryRecord(LineDecisionAction action)
    {
        var w = new ApprovalWorld();
        var lines = new[] { w.AddLine(InvoiceLineStatus.Flagged, 330m, extraction: Extraction), w.AddLine(InvoiceLineStatus.Flagged, 330m, extraction: Extraction),
            w.AddLine(InvoiceLineStatus.Flagged, 330m, extraction: Extraction) };

        var result = await w.LineService().DecideAsync(
            CommandOf(w, action, lines.Select(l => l.Id).ToList(), "One note for all three", bulk: true), CancellationToken.None);

        result.IsApplied.Should().BeTrue(result.Code + " " + result.Detail);
        var records = w.RecordsOf(CodeOf(action));
        records.Should().HaveCount(3, "one record per line (D-75)");
        records.Should().OnlyContain(r => ValuesOf(r)!["note"]!.GetValue<string>() == "One note for all three");
        if (action == LineDecisionAction.Accept)
        {
            w.Corrections.Should().HaveCount(21, "control: the multi-line Accept does write its 7 'Accepted in bulk' rows per line");
        }

        w.Corrections.Should().NotContain(r => r.Contains(InvoiceCorrectionNote.CorrectionRowColumn), "a line note is never on a correction row");
    }

    [Theory(DisplayName = "095f-2d1 N2: with no note (null, empty or whitespace-only) the record has no note key and hashes byte-identically to before (golden)")]
    [InlineData(LineDecisionAction.Accept, null)]
    [InlineData(LineDecisionAction.Accept, "")]
    [InlineData(LineDecisionAction.Accept, "   ")]
    [InlineData(LineDecisionAction.Reduce, null)]
    [InlineData(LineDecisionAction.Reduce, " \t\r\n ")]
    [InlineData(LineDecisionAction.Disallow, null)]
    [InlineData(LineDecisionAction.Disallow, "")]
    [InlineData(LineDecisionAction.Disallow, "\n  \n")]
    public async Task NoNote_HashesAsBefore(LineDecisionAction action, string? note)
    {
        var w = FixedWorld(out var line);

        (await Decide(w, action, line.Id, note)).IsApplied.Should().BeTrue();

        var record = w.RecordsOf(CodeOf(action)).Single();
        HasNoteKey(record).Should().BeFalse("never a note key, and never note: null (design 1.2)");
        HashOf(record).Should().Be(GoldenOf(action), "a line decision without a note hashes exactly as before 095f-2d1");
        if (action != LineDecisionAction.Reduce)
        {
            ValuesOf(record).Should().BeNull("an Accept or a Disallow without a note carries no values at all, as before");
        }
    }

    [Theory(DisplayName = "095f-2d1: a note changes the hash (it is part of what was decided)")]
    [MemberData(nameof(Actions))]
    public async Task Note_IsInsideTheHash(LineDecisionAction action)
    {
        var w = FixedWorld(out var line);

        (await Decide(w, action, line.Id, "A note")).IsApplied.Should().BeTrue();

        HashOf(w.RecordsOf(CodeOf(action)).Single()).Should().NotBe(GoldenOf(action));
    }

    // ── Must-not-fire ──

    [Fact(DisplayName = "095f-2d1 must-not-fire: Accept all Clean writes accept-line records with no note key and no note on any correction row")]
    public async Task AcceptClean_Records_CarryNoNote()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);
        w.AddLine(InvoiceLineStatus.Clean, 200m, extraction: Extraction);

        var result = await w.LineService().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), AliceUser, CancellationToken.None, CallerToken);

        result.IsApplied.Should().BeTrue(result.Code + " " + result.Detail);
        var records = w.RecordsOf("accept-line");
        records.Should().HaveCount(2, "control: the bulk accept did record");
        records.Should().OnlyContain(r => !HasNoteKey(r));
        w.Corrections.Should().HaveCount(14, "control: the 'Accepted in bulk' rows were written").And.OnlyContain(r => !r.Contains(InvoiceCorrectionNote.CorrectionRowColumn));
    }

    [Fact(DisplayName = "095f-2d1 must-not-fire: the approval's bulk accept writes accept-line records with no note, even when the approval itself carries one")]
    public async Task ApprovalBulkAccept_CarriesNoNote_EvenWhenTheApprovalHasOne()
    {
        var w = new ApprovalWorld();
        var clean = w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);

        var approved = await w.ApproveAs(AliceUser, Sentinel + "-approval");

        approved.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_steps")
            .Should().Contain(Sentinel + "-approval", "control: the approval's own record has its note");
        w.Invoice[InvoiceCorrectionNote.DecisionColumn].Should().Be(Sentinel + "-approval", "control: the approval's column has it too");
        var records = w.RecordsOf("accept-line").Should().ContainSingle("the Clean line was accepted in bulk").Subject;
        HasNoteKey(records).Should().BeFalse();
        records.GetAttributeValue<string>("sprk_steps").Should().NotContain(Sentinel);
        w.Row("sprk_billingevent", clean.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        w.Corrections.Should().NotBeEmpty().And.OnlyContain(r => !r.Contains(InvoiceCorrectionNote.CorrectionRowColumn));
    }

    [Theory(DisplayName = "095f-2d1 must-not-fire: no line decision writes sprk_decisionnote or sprk_correctionnote (or the note anywhere but its record)")]
    [MemberData(nameof(Actions))]
    public async Task NoLineDecision_WritesAnInvoiceNoteColumn(LineDecisionAction action)
    {
        var w = FixedWorld(out var line);
        w.Invoice[InvoiceCorrectionNote.DecisionColumn] = "the invoice's earlier decision note";
        w.Invoice[InvoiceCorrectionNote.Column] = "the firm's own correction note";

        (await Decide(w, action, line.Id, Sentinel)).IsApplied.Should().BeTrue();

        w.Invoice[InvoiceCorrectionNote.DecisionColumn].Should().Be("the invoice's earlier decision note");
        w.Invoice[InvoiceCorrectionNote.Column].Should().Be("the firm's own correction note");
        w.RecordsOf(CodeOf(action)).Should().ContainSingle().Which.GetAttributeValue<string>("sprk_steps").Should().Contain(Sentinel, "control: the note is on the record");
        foreach (var row in w.Rows.Values)
        {
            row.Attributes.Values.OfType<string>().Should().NotContain(v => v.Contains(Sentinel), $"the note is on no {row.LogicalName} row");
        }
    }

    [Fact(DisplayName = "095f-2d1 must-not-fire: a line decision with no invoice note columns on the invoice leaves the invoice without them")]
    public async Task NoLineDecision_AddsTheColumnsToAnInvoiceWithout()
    {
        var w = FixedWorld(out var line);

        (await Decide(w, LineDecisionAction.Disallow, line.Id, Sentinel)).IsApplied.Should().BeTrue();

        w.Invoice.Contains(InvoiceCorrectionNote.DecisionColumn).Should().BeFalse();
        w.Invoice.Contains(InvoiceCorrectionNote.Column).Should().BeFalse();
    }

    // ── The validation set of 2a ──

    [Theory(DisplayName = "095f-2d1 N1: a 2001-character note is 400 ebilling.note_too_long; \\u0007 and \\u001b are 400 ebilling.note_invalid_characters; each before any lock or read, the note never echoed")]
    [InlineData(LineDecisionAction.Accept, "too-long", "ebilling.note_too_long")]
    [InlineData(LineDecisionAction.Reduce, "too-long", "ebilling.note_too_long")]
    [InlineData(LineDecisionAction.Disallow, "too-long", "ebilling.note_too_long")]
    [InlineData(LineDecisionAction.Accept, "bell", "ebilling.note_invalid_characters")]
    [InlineData(LineDecisionAction.Reduce, "bell", "ebilling.note_invalid_characters")]
    [InlineData(LineDecisionAction.Disallow, "escape", "ebilling.note_invalid_characters")]
    public async Task RefusedNote_Is400_BeforeAnyLockOrRead(LineDecisionAction action, string shape, string code)
    {
        var w = FixedWorld(out var line);
        var untouched = w.Entities.Invocations.Count;
        var note = shape switch
        {
            "too-long" => Sentinel + new string('x', InvoiceCorrectionNote.MaxLength),
            "bell" => Sentinel + "\u0007",
            _ => Sentinel + "\u001b[31m",
        };

        var single = await Decide(w, action, line.Id, note);
        var multi = await w.LineService().DecideAsync(CommandOf(w, action, [line.Id], note, bulk: true), CancellationToken.None);

        foreach (var result in new[] { single, multi })
        {
            result.Status.Should().Be(LineDecisionStatus.InvalidRequest);
            result.Code.Should().Be(code);
            result.Detail.Should().NotContain(Sentinel).And.NotContain("xxxx", "the refusal never echoes the note");
        }

        w.Entities.Invocations.Count.Should().Be(untouched);
        AssertRefusedBeforeAnyLockOrRead(w);
    }

    [Theory(DisplayName = "095f-2d1 N1: a note of 2000 characters (with \\r\\n and \\t) is accepted and stored untruncated and unstripped; one character too")]
    [InlineData(LineDecisionAction.Accept, 2000)]
    [InlineData(LineDecisionAction.Reduce, 2000)]
    [InlineData(LineDecisionAction.Disallow, 2000)]
    [InlineData(LineDecisionAction.Reduce, 1)]
    public async Task NoteUpToTheLimit_IsAccepted_Untruncated(LineDecisionAction action, int length)
    {
        var w = FixedWorld(out var line);
        var note = ("a\nb\tc\r\n" + new string('x', 2000))[..length];

        (await Decide(w, action, line.Id, note)).IsApplied.Should().BeTrue();

        ValuesOf(w.RecordsOf(CodeOf(action)).Single())!["note"]!.GetValue<string>().Should().Be(note, "never truncated or stripped");
    }

    [Theory(DisplayName = "095f-2d1: a missing reason is still refused first (ebilling.line.reason_required), whatever the note")]
    [InlineData(LineDecisionAction.Reduce)]
    [InlineData(LineDecisionAction.Disallow)]
    public async Task MissingReason_IsRefusedBeforeTheNote(LineDecisionAction action)
    {
        var w = FixedWorld(out var line);

        var result = await Decide(w, action, line.Id, new string('x', 3000), reason: "  ");

        result.Code.Should().Be("ebilling.line.reason_required");
        AssertRefusedBeforeAnyLockOrRead(w);
    }

    // ── Repeats ──

    [Theory(DisplayName = "095f-2d1: a repeat with the same decision id and the same note records nothing twice; the record keeps its note")]
    [MemberData(nameof(Actions))]
    public async Task Repeat_WithTheSameNote_RecordsNothingTwice(LineDecisionAction action)
    {
        var w = FixedWorld(out var line);
        (await Decide(w, action, line.Id, "The first note")).IsApplied.Should().BeTrue();

        var repeat = await Decide(w, action, line.Id, "The first note");

        repeat.IsApplied.Should().BeTrue(repeat.Code + " " + repeat.Detail);
        repeat.Lines!.Single().AlreadyRecorded.Should().BeTrue();
        var record = w.RecordsOf(CodeOf(action)).Should().ContainSingle().Subject;
        ValuesOf(record)!["note"]!.GetValue<string>().Should().Be("The first note");
    }

    [Theory(DisplayName = "095f-2d1 / iss029: a repeat with the same decision id and an EDITED (or dropped) note is other content: 409 id_reused before any write, no second record, the first note stays")]
    [InlineData(LineDecisionAction.Accept, "Another note")]
    [InlineData(LineDecisionAction.Reduce, "Another note")]
    [InlineData(LineDecisionAction.Disallow, "Another note")]
    [InlineData(LineDecisionAction.Reduce, null)]
    public async Task Repeat_WithAnotherNote_IsRefusedAsOtherContent(LineDecisionAction action, string? secondNote)
    {
        var w = FixedWorld(out var line);
        (await Decide(w, action, line.Id, "The first note")).IsApplied.Should().BeTrue();
        var version = w.Row("sprk_billingevent", line.Id).GetAttributeValue<long>("versionnumber");
        var writes = w.Writes;

        var repeat = await Decide(w, action, line.Id, secondNote);

        repeat.Status.Should().Be(LineDecisionStatus.IdReused);
        repeat.Code.Should().Be(LineDecisionCodes.IdReused);
        var record = w.RecordsOf(CodeOf(action)).Should().ContainSingle("no second record").Subject;
        ValuesOf(record)!["note"]!.GetValue<string>().Should().Be("The first note");
        w.Row("sprk_billingevent", line.Id).GetAttributeValue<long>("versionnumber").Should().Be(version, "the line is not rewritten");
        w.Writes.Should().Be(writes, "nothing is written");
    }

    // ── Logs ──

    [Fact(DisplayName = "095f-2d1 N5: no log line of the line decision service or the recorder carries the note, on a success, a refusal, a repeat, an edited repeat and a failed step")]
    public async Task NoLogLine_CarriesTheNote()
    {
        var logs = new List<ApprovalListLogger<InvoiceLineDecisionService>>();
        var recorderLogs = new List<ApprovalListLogger<BillingDecisionRecorder>>();

        async Task<ApprovalWorld> Run(Func<ApprovalWorld, Entity, InvoiceLineDecisionService, Task> act, Action<ApprovalWorld>? arrange = null)
        {
            var w = FixedWorld(out var line);
            arrange?.Invoke(w);
            var log = new ApprovalListLogger<InvoiceLineDecisionService>();
            var recorderLog = new ApprovalListLogger<BillingDecisionRecorder>();
            var service = new InvoiceLineDecisionService(
                w.Entities.Object, w.Totals(), new BillingDecisionRecorder(w.H.Writer, w.Identity.Object, recorderLog), w.Locks, w.Ownership.Object, w.Probe.Object,
                (_, _, _, _, _, _) => Task.FromResult(SignalAutomatedCloseOutcome.Closed), log);
            logs.Add(log);
            recorderLogs.Add(recorderLog);
            await act(w, line, service);
            return w;
        }

        Task<LineDecisionResult> Go(ApprovalWorld w, Entity line, InvoiceLineDecisionService s, string? note, LineDecisionAction action = LineDecisionAction.Reduce) =>
            s.DecideAsync(CommandOf(w, action, [line.Id], note), CancellationToken.None);

        await Run((w, l, s) => Go(w, l, s, Sentinel + "-1"));
        await Run(async (w, l, s) => { await Go(w, l, s, Sentinel + "-2"); await Go(w, l, s, Sentinel + "-2"); });
        await Run(async (w, l, s) => { await Go(w, l, s, Sentinel + "-3"); await Go(w, l, s, Sentinel + "-3-edited"); });
        await Run((w, l, s) => Go(w, l, s, Sentinel + new string('x', 2001)));
        await Run((w, l, s) => Go(w, l, s, Sentinel + "-5"), w => w.FaultsOnce["totals"] = new TimeoutException("dataverse"));
        await Run((w, l, s) => Go(w, l, s, Sentinel + "-6", LineDecisionAction.Disallow));

        foreach (var entry in logs.SelectMany(l => l.Entries).Concat(recorderLogs.SelectMany(l => l.Entries)))
        {
            entry.Message.Should().NotContain(Sentinel, "a note is never logged");
        }

        logs.SelectMany(l => l.Entries).Should().NotBeEmpty("control: the service logs something");
    }
}
