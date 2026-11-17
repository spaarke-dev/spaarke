using System.Reflection;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 095f-2d2 (EB-D-242; design 5.2 option B and 1.1): a reviewer's correct or confirm carries a note, stored on the
/// <c>sprk_invoicecorrection</c> row in the SAME create as the row (<c>sprk_note</c>). Over the in-memory Dataverse of <see cref="CorrectionWorld"/> with the
/// REAL <see cref="InvoiceCorrectionService"/>, so each test proves the stored row, not the calls made.
/// </summary>
/// <remarks>
/// The note is checked by the shared <see cref="InvoiceCorrectionNote.Check"/> before any read; it is never truncated or stripped. It is not part of the
/// row's derived id (a repeat with an edited note finds the same row and does not rewrite the note), no Decision Record is written and the writer is not
/// touched. The labels (Corrected, Confirmed individually, Accepted in bulk) are unchanged and a bulk accept carries no note.
/// </remarks>
public sealed class InvoiceCorrectionNoteTests
{
    private const string Column = "sprk_note";
    private const string Sentinel = "NOTE-095F2D2-SENTINEL";

    /// <summary>An extraction JSON as 049 stores it (date, hours, rate, amount, narrative extracted; task and activity coded): the bulk accept writes a row per field.</summary>
    private const string Extraction =
        "{\"fields\":{\"Date\":{\"confidence\":0.95,\"combined\":0.9,\"page\":1},\"Hours\":{\"confidence\":0.9,\"page\":1},\"Rate\":{\"confidence\":0.92,\"page\":1},"
        + "\"Quantity\":{\"confidence\":null,\"page\":null},\"Amount\":{\"confidence\":0.99,\"page\":1},\"Narrative\":{\"confidence\":0.8,\"page\":1}},"
        + "\"coding\":{\"status\":\"Coded\",\"confidence\":0.82,\"task\":{\"code\":\"L110\",\"mismatch\":false},\"activity\":{\"code\":\"A101\",\"mismatch\":false},\"expense\":{\"code\":null}}}";

    private static CorrectionCommand Line(string field, string? value, string? note, bool confirm = false) =>
        new(W.InvoiceId, W.FeeLineId, field, value, confirm, W.Reviewer, note);

    private static CorrectionCommand Header(string field, string? value, string? note, bool confirm = false) =>
        new(W.InvoiceId, null, field, value, confirm, W.Reviewer, note);

    private static void NothingReadOrWritten(CorrectionWorld world)
    {
        world.Entities.Invocations.Should().BeEmpty("the note is checked before any read");
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();
        world.RowTransactions.Should().Be(0);
    }

    /// <summary>One request of each shape: a line value, a line confirm, a header value and a header confirm.</summary>
    public static TheoryData<string> Shapes => new() { "line-correct", "line-confirm", "header-correct", "header-confirm" };

    private static CorrectionCommand Of(string shape, string? note) => shape switch
    {
        "line-correct" => Line("Hours", "2.5", note),
        "line-confirm" => Line("Amount", null, note, confirm: true),
        "header-correct" => Header("TotalDue", "2400", note),
        "header-confirm" => Header("TotalDue", null, note, confirm: true),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    // ── The note is written in the row's own create ──

    [Theory(DisplayName = "095f-2d2: a note is written on the correction row created by the call, in the same transaction, trimmed, with newline and tab kept")]
    [MemberData(nameof(Shapes))]
    public async Task Note_IsOnTheRowTheCallCreated(string shape)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Of(shape, "  Rate looked right.\n\tChecked the card.  "), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.GetAttributeValue<string>(Column).Should().Be("Rate looked right.\n\tChecked the card.");
        world.RowTransactions.Should().Be(1, "the note rides in the row's own upsert, not in a second write");
        row.GetAttributeValue<bool>("sprk_islabel").Should().BeTrue("D-64: the labels are unchanged");
    }

    [Theory(DisplayName = "095f-2d2 must-not-fire: no note, an empty or a whitespace-only note writes no sprk_note column at all")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n\t \r\n ")]
    public async Task NoNote_WritesNoColumn(string? note)
    {
        var world = new CorrectionWorld();

        (await world.Service().CorrectAsync(Line("Hours", "2.5", note), CancellationToken.None)).IsApplied.Should().BeTrue();

        var row = world.Corrections.Should().ContainSingle().Subject;
        row.Contains(Column).Should().BeFalse("a blank note is no note, never an empty or null column");
    }

    [Fact(DisplayName = "095f-2d2: the labels stay Corrected and Confirmed individually with a note")]
    public async Task Labels_AreUnchangedByANote()
    {
        var corrected = new CorrectionWorld();
        var confirmed = new CorrectionWorld();

        var a = await corrected.Service().CorrectAsync(Line("Hours", "2.5", Sentinel), CancellationToken.None);
        var b = await confirmed.Service().CorrectAsync(Line("Amount", null, Sentinel, confirm: true), CancellationToken.None);

        a.Correction!.Outcome.Should().Be("Corrected");
        corrected.Corrections.Single().GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(100000000);
        b.Correction!.Outcome.Should().Be("Confirmed");
        confirmed.Corrections.Single().GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(100000001);
    }

    // ── The row id ──

    [Theory(DisplayName = "095f-2d2 must-not-fire: the note is not part of the row's derived id (ids equal the pre-change ids, with and without a note)")]
    [MemberData(nameof(Shapes))]
    public async Task Note_IsNotPartOfTheRowId(string shape)
    {
        var plain = new CorrectionWorld();
        var noted = new CorrectionWorld();

        await plain.Service().CorrectAsync(Of(shape, null), CancellationToken.None);
        await noted.Service().CorrectAsync(Of(shape, Sentinel), CancellationToken.None);

        noted.Corrections.Single().Id.Should().Be(plain.Corrections.Single().Id);
    }

    [Fact(DisplayName = "095f-2d2: the row id is exactly the pre-change derived id (pinned from the key, not from a second run)")]
    public async Task RowId_IsThePreChangeDerivedId()
    {
        var world = new CorrectionWorld();
        var version = world.Line().GetAttributeValue<long>("versionnumber");

        await world.Service().CorrectAsync(Line("Hours", "2.5", Sentinel), CancellationToken.None);

        world.Corrections.Single().Id.Should().Be(InvoiceLineDecisionService.IdOf($"ebilling-correction-edit-{W.FeeLineId:D}-Hours-{version}-2.5"));
    }

    [Theory(DisplayName = "095f-2d2: a repeat with an edited or removed note finds the same row, is already applied and never rewrites the note")]
    [InlineData("A second, edited note")]
    [InlineData(null)]
    public async Task ARepeat_WithAnotherNote_KeepsTheFirstNote(string? repeatNote)
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        await service.CorrectAsync(Line("Hours", "2.5", "The first note"), CancellationToken.None);
        var id = world.Corrections.Single().Id;

        var again = await service.CorrectAsync(Line("Hours", "2.5", repeatNote), CancellationToken.None);

        again.IsApplied.Should().BeTrue();
        again.AlreadyApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.Id.Should().Be(id);
        row.GetAttributeValue<string>(Column).Should().Be("The first note", "an edited note is not a new correction and is not rewritten");
        world.RowTransactions.Should().Be(1, "the repeat wrote nothing");
    }

    [Fact(DisplayName = "095f-2d2: a repeat that adds a note to a row written without one leaves the row without it")]
    public async Task ARepeat_WithANoteAfterNone_DoesNotAddOne()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        await service.CorrectAsync(Line("Hours", "2.5", null), CancellationToken.None);

        (await service.CorrectAsync(Line("Hours", "2.5", "Late note"), CancellationToken.None)).AlreadyApplied.Should().BeTrue();

        world.Corrections.Single().Contains(Column).Should().BeFalse();
    }

    // ── Validation (design 1.1), before any read ──

    [Theory(DisplayName = "095f-2d2: 2000 characters are accepted whole and never truncated (line and header, correct and confirm)")]
    [MemberData(nameof(Shapes))]
    public async Task NoteUpToTheLimit_IsAccepted_AndNeverTruncated(string shape)
    {
        var world = new CorrectionWorld();
        var max = new string('n', InvoiceCorrectionNote.MaxLength);

        (await world.Service().CorrectAsync(Of(shape, max), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.Corrections.Single().GetAttributeValue<string>(Column).Should().Be(max).And.HaveLength(2000);
    }

    [Theory(DisplayName = "095f-2d2: 2001 characters are 400 ebilling.note_too_long, nothing read or written (line and header)")]
    [MemberData(nameof(Shapes))]
    public async Task NoteTooLong_Is400_BeforeAnyRead(string shape)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Of(shape, new string('n', InvoiceCorrectionNote.MaxLength + 1)), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be(InvoiceCorrectionNote.NoteTooLongCode).And.Be("ebilling.note_too_long");
        NothingReadOrWritten(world);
    }

    [Theory(DisplayName = "095f-2d2: a control character (bell, escape, NUL, C1) is 400 ebilling.note_invalid_characters, nothing read or written")]
    [InlineData("Bad\u0007note")]
    [InlineData("Bad\u001bnote")]
    [InlineData("Bad\0note")]
    [InlineData("Bad\u0085note")]
    public async Task ControlCharacter_Is400_BeforeAnyRead(string note)
    {
        var line = new CorrectionWorld();
        var header = new CorrectionWorld();

        var a = await line.Service().CorrectAsync(Line("Hours", "2.5", note), CancellationToken.None);
        var b = await header.Service().CorrectAsync(Header("TotalDue", "2400", note), CancellationToken.None);

        a.Status.Should().Be(CorrectionStatus.InvalidRequest);
        a.Code.Should().Be("ebilling.note_invalid_characters");
        b.Status.Should().Be(CorrectionStatus.InvalidRequest);
        b.Code.Should().Be("ebilling.note_invalid_characters");
        NothingReadOrWritten(line);
        NothingReadOrWritten(header);
    }

    [Fact(DisplayName = "095f-2d2: a refusal's detail never echoes the note")]
    public async Task Refusal_NeverEchoesTheNote()
    {
        var world = new CorrectionWorld();

        var tooLong = await world.Service().CorrectAsync(Line("Hours", "2.5", Sentinel + new string('n', 2000)), CancellationToken.None);
        var control = await world.Service().CorrectAsync(Line("Hours", "2.5", Sentinel + "\u0007"), CancellationToken.None);

        tooLong.Status.Should().Be(CorrectionStatus.InvalidRequest);
        control.Status.Should().Be(CorrectionStatus.InvalidRequest);
        tooLong.Detail.Should().NotContain(Sentinel);
        control.Detail.Should().NotContain(Sentinel);
    }

    [Fact(DisplayName = "095f-2d2: the request's own rules run first; a bad field with a bad note is the field's refusal")]
    public async Task TheFieldRuleIsCheckedBeforeTheNote()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Bogus", "1", "Bad\u0007note"), CancellationToken.None);

        result.Code.Should().Be(CorrectionCodes.FieldUnknown);
        NothingReadOrWritten(world);
    }

    [Fact(DisplayName = "095f-2d2: a refused note leaves no state; the same request with a good note then writes the row")]
    public async Task ARefusedNote_LeavesNoState()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        (await service.CorrectAsync(Line("Hours", "2.5", "Bad\u0007note"), CancellationToken.None)).Status.Should().Be(CorrectionStatus.InvalidRequest);

        var ok = await service.CorrectAsync(Line("Hours", "2.5", "Good note"), CancellationToken.None);

        ok.IsApplied.Should().BeTrue();
        ok.AlreadyApplied.Should().BeFalse();
        world.Corrections.Single().GetAttributeValue<string>(Column).Should().Be("Good note");
    }

    // ── Logs and readers ──

    [Fact(DisplayName = "095f-2d2: no log line carries the note (success, refusal and a failed write)")]
    public async Task NoLogLine_CarriesTheNote()
    {
        var ok = new CorrectionWorld();
        var refused = new CorrectionWorld();
        var failed = new CorrectionWorld { RowFault = new TimeoutException("simulated") };

        await ok.Service().CorrectAsync(Line("Hours", "2.5", Sentinel), CancellationToken.None);
        await refused.Service().CorrectAsync(Line("Hours", "2.5", Sentinel + "\u0007"), CancellationToken.None);
        var unavailable = await failed.Service().CorrectAsync(Line("Hours", "2.5", Sentinel), CancellationToken.None);

        unavailable.Status.Should().Be(CorrectionStatus.Unavailable);
        ok.Log.Entries.Should().NotBeEmpty();
        failed.Log.Entries.Should().NotBeEmpty();
        foreach (var world in new[] { ok, refused, failed })
        {
            world.Log.Entries.Should().NotContain(e => e.Message.Contains(Sentinel, StringComparison.Ordinal));
        }

        unavailable.Detail.Should().NotContain(Sentinel);
    }

    [Fact(DisplayName = "095f-2d2: the correction row the list route returns has no note member, and the read column set does not name sprk_note")]
    public void TheListedRow_HasNoNote()
    {
        typeof(CorrectionDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Should().NotContain(n => n.Contains("Note", StringComparison.OrdinalIgnoreCase));

        var columns = (string[])typeof(InvoiceCorrectionService).GetField("CorrectionColumns", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        columns.Should().NotContain(Column, "the History pane and the calibration report read only the columns they show");
    }

    // ── Bulk accept (D-64: Accepted in bulk carries no note) ──

    [Fact(DisplayName = "095f-2d2 must-not-fire: the 'Accepted in bulk' rows an approve writes carry no note column, even when the approve carries a note")]
    public async Task BulkAcceptedRows_CarryNoNote()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);
        w.AddLine(InvoiceLineStatus.Reduced, 500m, extraction: Extraction, approved: 400m);

        (await w.ApproveAs(ApprovalWorld.AliceUser, Sentinel)).Outcome.Should().Be(ApprovalOutcome.Approved);

        var bulk = w.Corrections.Where(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value == 100000002).ToList();
        bulk.Should().HaveCount(7, "the Clean line was accepted in bulk: date, hours, rate, amount, narrative, task code, activity code");
        w.Corrections.Should().OnlyContain(r => !r.Contains(Column), "no correction row an approve writes (Accepted in bulk or Unreviewed) carries a note");
    }
}
