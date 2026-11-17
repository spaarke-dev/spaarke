using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

// A partial of the adjustment service's own test class (its private World), kept in that class's namespace (as
// ProcedureAssignmentServiceTests.FirmDescription.cs does from this folder).
namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095f-2b (EB-D-241; design 095f-part2 sections 1, 3, 8 to 11): an adjustment's add and its removal each file ONE Decision Record
/// (<c>add-adjustment</c>, <c>remove-adjustment</c>) after the line and the totals, idempotent on a decision id derived from the invoice and the client's adjustment id (an add) and on an id
/// derived from the invoice and the line (a removal), never the raw client id. The optional note is in the record's Values and, through the totals' own conditional update, in
/// the invoice's <c>sprk_decisionnote</c>; an add without a note writes its reason there. Over the REAL adjustment service, totals service,
/// recorder and writer harness: each test proves the stored state (the records, the invoice's columns, the line rows).
/// </summary>
/// <remarks>
/// <para><b>Golden hashes.</b> Before 095f-2b no adjustment had a Decision Record, so there is no pre-change hash to capture. The two no-note hashes
/// below were pinned from the first build of 095f-2b on the fixed world (<see cref="GoldenInvoice"/>, adjustment id <see cref="GoldenAdjustment"/>,
/// line <see cref="GoldenLine"/>, decider <see cref="Reviewer"/>); they guard the no-note content from here on (a null note key, or any change to
/// the declared Values, would move them). Null, empty and whitespace-only notes are proved to hash the same as no note.</para>
/// </remarks>
public sealed partial class InvoiceAdjustmentServiceTests
{
    private static readonly Guid GoldenInvoice = Guid.Parse("095f2b00-0000-4000-8000-0000000000a1");
    private static readonly Guid GoldenAdjustment = Guid.Parse("095f2b00-0000-4000-8000-0000000000b1");
    private static readonly Guid GoldenLine = Guid.Parse("095f2b00-0000-4000-8000-0000000000c1");

    /// <summary>Pinned from the first 095f-2b build (see the remarks): a 10 percent add with the default reason and no note.</summary>
    private const string AddNoNoteHash = "B05596B1ADB707716B4DD648D008421DA5F9A38ABD7038E97AAD65F7AAF79531";

    /// <summary>Pinned from the first 095f-2b build (see the remarks): the removal of <see cref="GoldenLine"/> with no note.</summary>
    private const string RemoveNoNoteHash = "4B6F19E4DD9510B5DA0C2C67ECFC908CA0E40A0688F2C05CF7EE16D8D7B5AEE6";

    private const string Sentinel = "NOTE-095F2B-SENTINEL";
    private const string Earlier = "an earlier decision's note";

    private static AddAdjustmentCommand AddWithNote(
        World w, string? note, Guid? id = null, string method = "percent", decimal? value = 10m, string? reason = Why, Guid? reviewer = null, Guid? rule = null) =>
        new(w.InvoiceId, id ?? Guid.NewGuid(), method, value, reason, rule, reviewer ?? Reviewer, note);

    private static RemoveAdjustmentCommand RemoveOf(World w, Guid lineId, string? note, Guid? reviewer = null) => new(w.InvoiceId, lineId, reviewer ?? Reviewer, note);

    /// <summary>An active fixed Adjustment line of the invoice that no Decision Record names yet (as one added before 095f-2b).</summary>
    private static Entity ActiveAdjustment(World w, Guid? id = null, decimal amount = 50m)
    {
        var line = new Entity("sprk_billingevent", id ?? Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
            ["sprk_linesequence"] = 8,
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
            ["sprk_amount"] = new Money(amount),
            ["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Fixed),
            ["sprk_adjustmentvalue"] = amount,
            ["sprk_adjustmentreason"] = "Agreed credit",
            ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted),
            ["statecode"] = new OptionSetValue(0),
            ["versionnumber"] = 40L,
        };
        w.Lines.Add(line);
        return line;
    }

    private static JsonNode StepsOf(Entity record) => JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!;

    private static string HashOf(Entity record) => StepsOf(record)["reviewHash"]!.GetValue<string>();

    private static JsonObject ValuesOf(Entity record) => StepsOf(record)["steps"]!.AsArray().Single()!["values"]!.AsObject();

    private static List<Entity> RecordsOf(World w, string code) =>
        w.DecisionRecords.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == code).ToList();

    private static string? DecisionNote(World w) => w.Invoice.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn);

    /// <summary>The record step fails (the writer's create times out) until <see cref="HealTheRecordStep"/>.</summary>
    private static void BreakTheRecordStep(World w) =>
        w.H.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("record"));

    private static void HealTheRecordStep(World w) =>
        w.H.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => w.H.Created.Add(e))
            .ReturnsAsync(w.H.NewId);

    private static void AssertTheCorrectionNoteIsNeverWritten(World w)
    {
        w.TotalsWrites.Should().NotContain(f => f.ContainsKey(InvoiceCorrectionNote.Column), "the totals path writes sprk_decisionnote only");
        w.Invoice.Contains(InvoiceCorrectionNote.Column).Should().BeFalse("no adjustment writes sprk_correctionnote");
    }

    private static void AssertRefusedBeforeAnyLockOrRead(World w)
    {
        w.Locks.Acquired.Should().Be(0, "the note is checked before the lock");
        w.Entities.Invocations.Should().BeEmpty("and before any read");
        w.Records.Invocations.Should().BeEmpty();
        w.H.Lookups.Should().BeEmpty("and before the record lookup");
        w.H.Created.Should().BeEmpty();
    }

    // =========================================================================================
    // One record per act, with the declared Values; the note in the record and in the column
    // =========================================================================================

    [Fact(DisplayName = "095f-2b: an add files ONE add-adjustment record under the invoice (reason, method, value, note), and the note reaches sprk_decisionnote")]
    public async Task Add_FilesOneRecord_WithTheDeclaredValues_AndTheNoteInTheColumn()
    {
        var w = new World();
        var id = Guid.NewGuid();

        var result = await w.Service().AddAsync(AddWithNote(w, "  Agreed on the call\r\n\twith the firm  ", id), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var record = w.DecisionRecords.Should().ContainSingle().Subject;
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(InvoiceAdjustmentService.AddActionCode);
        record.GetAttributeValue<string>("sprk_reason").Should().Be(Why);
        StepsOf(record)["reviewId"]!.GetValue<string>().Should().Be(BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, InvoiceAdjustmentService.AddDecisionIdFor(w.InvoiceId, id)).ToString("D"),
            "the record's key is the derived decision id on the invoice");
        var values = ValuesOf(record);
        values.Select(v => v.Key).Should().BeEquivalentTo(["method", "value", "note"], "the declared codes; no rule when none is cited");
        values["method"]!.GetValue<string>().Should().Be("percent");
        values["value"]!.GetValue<decimal>().Should().Be(10m);
        values["note"]!.GetValue<string>().Should().Be("Agreed on the call\r\n\twith the firm", "trimmed, with the line break and tab kept");
        DecisionNote(w).Should().Be("Agreed on the call\r\n\twith the firm");
        w.TotalsWrites.Should().ContainSingle().Which[InvoiceCorrectionNote.DecisionColumn].Should().Be("Agreed on the call\r\n\twith the firm",
            "the note rides in the totals' own conditional update");
        AssertTheCorrectionNoteIsNeverWritten(w);
    }

    [Fact(DisplayName = "095f-2b: a fixed add citing a rule carries method 'fixed' and the rule in its Values")]
    public async Task Add_AFixedAdjustmentCitingARule_CarriesTheRule()
    {
        var w = new World();
        var rule = Rule();
        w.Candidates.Add(rule);

        var result = await w.Service().AddAsync(AddWithNote(w, note: null, method: "fixed", value: 75m, rule: rule.PolicyVersionId), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var values = ValuesOf(w.DecisionRecords.Should().ContainSingle().Subject);
        values.Select(v => v.Key).Should().BeEquivalentTo(["method", "value", "rule"]);
        values["method"]!.GetValue<string>().Should().Be("fixed");
        values["rule"]!.GetValue<Guid>().Should().Be(rule.PolicyVersionId);
    }

    [Theory(DisplayName = "095f-2b: an add with no note (null, empty, whitespace) has no note key, hashes as the pinned no-note golden, and writes its reason to sprk_decisionnote")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public async Task Add_WithoutANote_HasNoNoteKey_HashesAsTheGolden_AndTheReasonIsTheColumn(string? note)
    {
        var w = new World(invoiceId: GoldenInvoice);
        w.Invoice[InvoiceCorrectionNote.DecisionColumn] = Earlier;

        var result = await w.Service().AddAsync(AddWithNote(w, note, GoldenAdjustment), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var record = w.DecisionRecords.Should().ContainSingle().Subject;
        ValuesOf(record).ContainsKey("note").Should().BeFalse("no note means no key (a null key would change the hash)");
        HashOf(record).Should().Be(AddNoNoteHash);
        DecisionNote(w).Should().Be(Why, "with no note an add writes its reason to the column");
        AssertTheCorrectionNoteIsNeverWritten(w);
    }

    [Fact(DisplayName = "095f-2b: a removal files ONE remove-adjustment record (no reason; the adjustment and the note), and the note reaches sprk_decisionnote")]
    public async Task Remove_FilesOneRecord_WithTheDeclaredValues_AndTheNoteInTheColumn()
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        var result = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Firm agreed to drop it"), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var record = w.DecisionRecords.Should().ContainSingle().Subject;
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(InvoiceAdjustmentService.RemoveActionCode);
        record.GetAttributeValue<string>("sprk_reason").Should().BeNull("a removal takes no reason");
        StepsOf(record)["reviewId"]!.GetValue<string>().Should().Be(
            BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, InvoiceAdjustmentService.RemoveDecisionIdFor(w.InvoiceId, line.Id)).ToString("D"));
        var values = ValuesOf(record);
        values.Select(v => v.Key).Should().BeEquivalentTo(["adjustment", "note"]);
        values["adjustment"]!.GetValue<Guid>().Should().Be(line.Id);
        values["note"]!.GetValue<string>().Should().Be("Firm agreed to drop it");
        DecisionNote(w).Should().Be("Firm agreed to drop it");
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the line is kept, inactive");
        AssertTheCorrectionNoteIsNeverWritten(w);
    }

    [Theory(DisplayName = "095f-2b: a removal with no note (null, empty, whitespace) has no note key, hashes as the pinned no-note golden, and leaves sprk_decisionnote as it was")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_WithoutANote_HasNoNoteKey_HashesAsTheGolden_AndLeavesTheColumn(string? note)
    {
        var w = new World(invoiceId: GoldenInvoice);
        w.Invoice[InvoiceCorrectionNote.DecisionColumn] = Earlier;
        ActiveAdjustment(w, GoldenLine);

        var result = await w.Service().RemoveAsync(RemoveOf(w, GoldenLine, note), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.Applied);
        var record = w.DecisionRecords.Should().ContainSingle().Subject;
        ValuesOf(record).ContainsKey("note").Should().BeFalse();
        HashOf(record).Should().Be(RemoveNoNoteHash);
        DecisionNote(w).Should().Be(Earlier, "a removal has no reason, so with no note the column is left as it is");
        w.TotalsWrites.Should().ContainSingle().Which.ContainsKey(InvoiceCorrectionNote.DecisionColumn).Should().BeFalse();
    }

    // =========================================================================================
    // N1: the shared note rule, before any lock or read
    // =========================================================================================

    public static TheoryData<string, string> RefusedNotes() => new()
    {
        { new string('n', InvoiceCorrectionNote.MaxLength + 1), InvoiceCorrectionNote.NoteTooLongCode },
        { Sentinel + " bell \u0007", InvoiceCorrectionNote.NoteInvalidCharactersCode },
        { Sentinel + " escape \u001b[0m", InvoiceCorrectionNote.NoteInvalidCharactersCode },
    };

    [Theory(DisplayName = "095f-2b N1 must-fire: an add whose note is too long or carries a control character is 400 with the shared code, before the lock and any read")]
    [MemberData(nameof(RefusedNotes))]
    public async Task Add_ARefusedNote_Is400_BeforeAnyLockOrRead(string note, string code)
    {
        var w = new World();

        var result = await w.Service().AddAsync(AddWithNote(w, note), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be(code);
        result.Detail.Should().NotContain(Sentinel, "the refusal never echoes the note");
        AssertRefusedBeforeAnyLockOrRead(w);
    }

    [Theory(DisplayName = "095f-2b N1 must-fire: a removal whose note is too long or carries a control character is 400 with the shared code, before the lock and any read")]
    [MemberData(nameof(RefusedNotes))]
    public async Task Remove_ARefusedNote_Is400_BeforeAnyLockOrRead(string note, string code)
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        var result = await w.Service().RemoveAsync(RemoveOf(w, line.Id, note), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.InvalidRequest);
        result.Code.Should().Be(code);
        AssertRefusedBeforeAnyLockOrRead(w);
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
    }

    public static TheoryData<string> AcceptedNotes() => new()
    {
        new string('n', InvoiceCorrectionNote.MaxLength),
        "line one\nline two",
        "a\ttab",
    };

    [Theory(DisplayName = "095f-2b N1 must-not-fire: a note at the limit, or with a line break or a tab, is accepted on both acts")]
    [MemberData(nameof(AcceptedNotes))]
    public async Task ANoteAtTheLimit_OrWithALineBreakOrTab_IsAccepted(string note)
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        (await w.Service().AddAsync(AddWithNote(w, note), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, note), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);

        w.DecisionRecords.Should().HaveCount(2).And.OnlyContain(r => ValuesOf(r)["note"]!.GetValue<string>() == note);
    }

    // =========================================================================================
    // Repeats: one record per act; another note is 409 id_reused, never a second record or a 503
    // =========================================================================================

    [Fact(DisplayName = "095f-2b AC: a repeated adjustment id with the same content files ONE record (the repeat is already recorded)")]
    public async Task Add_ARepeatWithTheSameContent_FilesOneRecord()
    {
        var w = new World();
        var command = AddWithNote(w, "Same note");

        var first = await w.Service().AddAsync(command, CancellationToken.None);
        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        w.DecisionRecords.Should().ContainSingle();
        w.H.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once, "the repeat creates nothing");
        w.CreateCount.Should().Be(1);
        DecisionNote(w).Should().Be("Same note");
    }

    [Fact(DisplayName = "095f-2b AC: a repeated removal with the same content files ONE record")]
    public async Task Remove_ARepeatWithTheSameContent_FilesOneRecord()
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Dropped"), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Dropped"), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        w.DeactivationCount.Should().Be(1);
    }

    [Theory(DisplayName = "095f-2b AC: a repeated adjustment id that changes only the note (or drops or adds one) is 409 id_reused; no second record; the column keeps the first note")]
    [InlineData("First note", "Edited note")]
    [InlineData("First note", null)]
    [InlineData(null, "A note added later")]
    public async Task Add_ARepeatThatChangesOnlyTheNote_Is409IdReused_NoSecondRecord(string? firstNote, string? repeatNote)
    {
        var w = new World();
        var id = Guid.NewGuid();
        (await w.Service().AddAsync(AddWithNote(w, firstNote, id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var columnAfterFirst = DecisionNote(w);

        var repeat = await w.Service().AddAsync(AddWithNote(w, repeatNote, id), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        repeat.Code.Should().Be(AdjustmentCodes.IdReused);
        w.DecisionRecords.Should().ContainSingle();
        DecisionNote(w).Should().Be(columnAfterFirst, "a refused repeat never overwrites the recorded decision's note");
        w.CreateCount.Should().Be(1);
    }

    [Theory(DisplayName = "095f-2b AC: a repeated removal with an edited note is 409 id_reused; no second record; the column keeps the first note")]
    [InlineData("First note", "Edited note")]
    [InlineData("First note", null)]
    [InlineData(null, "A note added later")]
    public async Task Remove_ARepeatWithAnEditedNote_Is409IdReused_NoSecondRecord(string? firstNote, string? repeatNote)
    {
        var w = new World();
        w.Invoice[InvoiceCorrectionNote.DecisionColumn] = Earlier;
        var line = ActiveAdjustment(w);
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, firstNote), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var columnAfterFirst = DecisionNote(w);

        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, repeatNote), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        repeat.Code.Should().Be(AdjustmentCodes.IdReused);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        DecisionNote(w).Should().Be(columnAfterFirst);
    }

    [Fact(DisplayName = "095f-2b AC: a removal followed by the add of a new adjustment files TWO records")]
    public async Task ARemovalThenANewAdd_FilesTwoRecords()
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, null), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        (await w.Service().AddAsync(AddWithNote(w, null), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);

        w.DecisionRecords.Select(r => r.GetAttributeValue<string>("sprk_actioncode")).Should()
            .Equal(InvoiceAdjustmentService.RemoveActionCode, InvoiceAdjustmentService.AddActionCode);
    }

    public static TheoryData<string, decimal, string> EditedContent() => new()
    {
        { "fixed", 10m, Why },
        { "percent", 11m, Why },
        { "percent", 10m, "Another reason" },
    };

    [Theory(DisplayName = "095f-2b (ISS-029): a repeat that edits the method, value or reason is 409 id_reused before the totals and the writer, never a 503")]
    [MemberData(nameof(EditedContent))]
    public async Task Add_ARepeatThatEditsOtherContent_Is409_BeforeTheTotalsAndTheWriter(string method, decimal value, string reason)
    {
        var w = new World();
        var id = Guid.NewGuid();
        (await w.Service().AddAsync(AddWithNote(w, "n", id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var totalsWrites = w.TotalsWriteCount;
        var lookups = w.H.Lookups.Count;

        var repeat = await w.Service().AddAsync(AddWithNote(w, "n", id, method, value, reason), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        w.TotalsWriteCount.Should().Be(totalsWrites, "refused before the totals");
        w.H.Lookups.Should().HaveCount(lookups, "and before the record lookup");
        w.DecisionRecords.Should().ContainSingle();
    }

    [Theory(DisplayName = "095f-2b (ISS-029): after a strand at the record step, a repeat that edits the method, value or reason is 409, never a 503 that no repeat can complete")]
    [MemberData(nameof(EditedContent))]
    public async Task Add_AfterAStrand_ARepeatThatEditsOtherContent_Is409(string method, decimal value, string reason)
    {
        var w = new World();
        var id = Guid.NewGuid();
        BreakTheRecordStep(w);
        (await w.Service().AddAsync(AddWithNote(w, "n", id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);
        HealTheRecordStep(w);

        var repeat = await w.Service().AddAsync(AddWithNote(w, "n", id, method, value, reason), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        w.DecisionRecords.Should().BeEmpty("the edited repeat files nothing; the original repeat still can");
        (await w.Service().AddAsync(AddWithNote(w, "n", id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied, "control: the unedited repeat completes");
        w.DecisionRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "095f-2b: a repeat of an add by another reviewer is 409 id_reused (the record names its decider); nothing more is written")]
    public async Task Add_ARepeatByAnotherReviewer_Is409()
    {
        var w = new World();
        var id = Guid.NewGuid();
        (await w.Service().AddAsync(AddWithNote(w, null, id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var totalsWrites = w.TotalsWriteCount;

        var repeat = await w.Service().AddAsync(AddWithNote(w, null, id, reviewer: Remover), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        w.TotalsWriteCount.Should().Be(totalsWrites);
        w.DecisionRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "095f-2b: a repeat of a removal by another reviewer is 409 id_reused; nothing more is written")]
    public async Task Remove_ARepeatByAnotherReviewer_Is409()
    {
        var w = new World();
        var line = ActiveAdjustment(w);
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, null), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        var totalsWrites = w.TotalsWriteCount;

        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, null, Remover), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.IdReused);
        w.TotalsWriteCount.Should().Be(totalsWrites);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Should().NotBeNull();
    }

    [Fact(DisplayName = "095f-2b: a first add whose adjustment id is already recorded for another decision is 409 id_reused; nothing is written")]
    public async Task Add_AFirstAttemptWhoseIdIsAlreadyRecorded_Is409_NothingWritten()
    {
        var w = new World();
        var id = Guid.NewGuid();
        await w.Recorder().RecordAsync(new BillingDecision(
            InvoiceAdjustmentService.AddDecisionIdFor(w.InvoiceId, id), InvoiceAdjustmentService.AddActionCode, w.InvoiceId, BillingDecider.Human(Reviewer, InvoiceAdjustmentService.ReviewerRole),
            new Dictionary<string, object?> { ["invoiceId"] = w.InvoiceId }, "Added", Reason: "Another adjustment",
            Values: new Dictionary<string, object?> { ["method"] = "fixed", ["value"] = 5m }));

        var result = await w.Service().AddAsync(AddWithNote(w, null, id), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.IdReused);
        w.CreateCount.Should().Be(0);
        w.TotalsWriteCount.Should().Be(0);
        w.DecisionRecords.Should().ContainSingle("only the earlier decision");
    }

    // =========================================================================================
    // The 503-then-repeat pattern: a strand finishes on the repeat and files exactly one record
    // =========================================================================================

    [Fact(DisplayName = "095f-2b: an add stranded at the record step is 503 incomplete (line and totals written); the same repeat finishes it with exactly ONE record")]
    public async Task Add_StrandedAtTheRecordStep_TheRepeatFilesExactlyOneRecord()
    {
        var w = new World();
        var command = AddWithNote(w, "Kept note");
        BreakTheRecordStep(w);

        var first = await w.Service().AddAsync(command, CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Incomplete);
        first.Code.Should().Be(AdjustmentCodes.Incomplete);
        first.Detail.Should().Contain("not recorded");
        w.CreateCount.Should().Be(1);
        DecisionNote(w).Should().Be("Kept note");
        w.DecisionRecords.Should().BeEmpty();

        HealTheRecordStep(w);
        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        ValuesOf(w.DecisionRecords.Should().ContainSingle().Subject)["note"]!.GetValue<string>().Should().Be("Kept note");
        w.CreateCount.Should().Be(1, "the line is not added twice");
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        w.DecisionRecords.Should().ContainSingle("a further repeat is already recorded");
    }

    [Fact(DisplayName = "095f-2b: a removal stranded at the record step is 503 incomplete (removed, totals written); the same repeat finishes it with exactly ONE record")]
    public async Task Remove_StrandedAtTheRecordStep_TheRepeatFilesExactlyOneRecord()
    {
        var w = new World();
        var line = ActiveAdjustment(w);
        BreakTheRecordStep(w);

        var first = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Incomplete);
        first.Detail.Should().Contain("removal was not recorded").And.NotContain("saved");
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1);

        HealTheRecordStep(w);
        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        w.DeactivationCount.Should().Be(1);
    }

    [Fact(DisplayName = "095f-2b: an add stranded at the totals step finishes on a repeat with another note: one record, and the record and the column carry the repeat's note")]
    public async Task Add_StrandedAtTheTotals_ARepeatWithAnotherNote_FilesOneRecordWithThatNote()
    {
        var w = new World();
        var id = Guid.NewGuid();
        w.TotalsFault = new TimeoutException("totals");

        var first = await w.Service().AddAsync(AddWithNote(w, "First", id), CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Incomplete);
        w.DecisionRecords.Should().BeEmpty();
        DecisionNote(w).Should().BeNull();

        w.TotalsFault = null;
        var repeat = await w.Service().AddAsync(AddWithNote(w, "Second", id), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        ValuesOf(w.DecisionRecords.Should().ContainSingle().Subject)["note"]!.GetValue<string>().Should().Be("Second");
        DecisionNote(w).Should().Be("Second");
    }

    // =========================================================================================
    // Review round 1: the decision ids are namespaced hashes, never the client's raw id
    // =========================================================================================

    /// <summary>A decision of another billing act on the invoice, filed under <paramref name="decisionId"/> (what approve, reject and return derive).</summary>
    private static BillingDecision OtherDecision(World w, string code, Guid decisionId) => new(
        decisionId, code, w.InvoiceId, BillingDecider.Human(Reviewer, InvoiceAdjustmentService.ReviewerRole),
        new Dictionary<string, object?> { ["invoiceId"] = w.InvoiceId, ["approvedTotal"] = 900m }, "Done",
        Reason: code == InvoiceApprovalService.ApproveActionCode ? null : "A reason", Values: new Dictionary<string, object?>());

    /// <summary>The ids another billing decision on an invoice derives (approve, reject, return), for the same input id.</summary>
    public static TheoryData<string, Guid> OtherDecisionIds() => new()
    {
        { InvoiceApprovalService.ApproveActionCode, InvoiceApprovalService.DecisionIdFor(Guid.Parse("095f2b01-0000-4000-8000-000000000001")) },
        { InvoiceRejectReturnService.RejectActionCode, InvoiceRejectReturnService.RejectDecisionIdFor(Guid.Parse("095f2b01-0000-4000-8000-000000000002")) },
        { InvoiceRejectReturnService.ReturnActionCode, InvoiceRejectReturnService.ReturnDecisionIdFor(Guid.Parse("095f2b01-0000-4000-8000-000000000003")) },
    };

    [Fact(DisplayName = "095f-2b R1 (F1): the decision ids are namespaced hashes: never the raw adjustment id, different per invoice, and pinned")]
    public void TheDecisionIds_AreNamespacedHashes_NeverTheRawId_AndDifferPerInvoice()
    {
        var invoiceA = Guid.Parse("095f2b02-0000-4000-8000-00000000000a");
        var invoiceB = Guid.Parse("095f2b02-0000-4000-8000-00000000000b");
        var id = Guid.Parse("095f2b02-0000-4000-8000-0000000000c1");

        InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id).Should().NotBe(id, "the client's id is never the decision id");
        InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id).Should().NotBe(InvoiceAdjustmentService.AddDecisionIdFor(invoiceB, id), "the same id on two invoices");
        InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id).Should().Be(InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id), "deterministic");
        InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id).Should().Be(InvoiceLineDecisionService.IdOf($"ebilling-add-adjustment-{invoiceA:D}-{id:D}"));

        InvoiceAdjustmentService.RemoveDecisionIdFor(invoiceA, id).Should().NotBe(id);
        InvoiceAdjustmentService.RemoveDecisionIdFor(invoiceA, id).Should().NotBe(InvoiceAdjustmentService.RemoveDecisionIdFor(invoiceB, id), "the same line id on two invoices");
        InvoiceAdjustmentService.RemoveDecisionIdFor(invoiceA, id).Should().Be(InvoiceLineDecisionService.IdOf($"ebilling-remove-adjustment-{invoiceA:D}-{id:D}"));
        InvoiceAdjustmentService.RemoveDecisionIdFor(invoiceA, id).Should().NotBe(InvoiceAdjustmentService.AddDecisionIdFor(invoiceA, id), "an add and a removal never share an id");
    }

    [Fact(DisplayName = "095f-2b R1 (F1): an add's record is keyed by the derived decision id, not the client's raw adjustment id")]
    public async Task Add_TheRecordIsKeyedByTheDerivedDecisionId_NotTheRawAdjustmentId()
    {
        var w = new World();
        var id = Guid.NewGuid();

        (await w.Service().AddAsync(AddWithNote(w, null, id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);

        var key = StepsOf(w.DecisionRecords.Should().ContainSingle().Subject)["reviewId"]!.GetValue<string>();
        key.Should().Be(BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, InvoiceAdjustmentService.AddDecisionIdFor(w.InvoiceId, id)).ToString("D"));
        key.Should().NotBe(BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, id).ToString("D"));
    }

    [Theory(DisplayName = "095f-2b R1 (F1): an add whose adjustment id equals another decision's id no longer blocks that decision (the add first, then the decision)")]
    [MemberData(nameof(OtherDecisionIds))]
    public async Task Add_WhoseIdEqualsAnotherDecisionsId_DoesNotBlockThatDecision(string code, Guid otherDecisionId)
    {
        var w = new World();

        var add = await w.Service().AddAsync(AddWithNote(w, null, otherDecisionId), CancellationToken.None);
        var other = await Record.ExceptionAsync(() => w.Recorder().RecordAsync(OtherDecision(w, code, otherDecisionId)));

        add.Status.Should().Be(AdjustmentStatus.Applied);
        other.Should().BeNull("the later decision records normally under its own id");
        w.DecisionRecords.Select(r => r.GetAttributeValue<string>("sprk_actioncode")).Should().BeEquivalentTo([InvoiceAdjustmentService.AddActionCode, code]);
    }

    [Theory(DisplayName = "095f-2b R1 (F1): an add whose adjustment id equals an already recorded decision's id is not refused (the decision first, then the add)")]
    [MemberData(nameof(OtherDecisionIds))]
    public async Task Add_WhoseIdEqualsAnAlreadyRecordedDecisionsId_IsNotRefused(string code, Guid otherDecisionId)
    {
        var w = new World();
        await w.Recorder().RecordAsync(OtherDecision(w, code, otherDecisionId));

        var add = await w.Service().AddAsync(AddWithNote(w, null, otherDecisionId), CancellationToken.None);

        add.Status.Should().Be(AdjustmentStatus.Applied);
        w.CreateCount.Should().Be(1);
        w.DecisionRecords.Select(r => r.GetAttributeValue<string>("sprk_actioncode")).Should().BeEquivalentTo([code, InvoiceAdjustmentService.AddActionCode]);
    }

    [Theory(DisplayName = "095f-2b R1 (F1): a removal's decision id is not the earlier lineId-only hash, so a decision under that id does not block the removal")]
    [MemberData(nameof(OtherDecisionIds))]
    public async Task Remove_ADecisionUnderTheEarlierLineOnlyId_DoesNotBlockTheRemoval(string code, Guid _)
    {
        var w = new World();
        var line = ActiveAdjustment(w);
        await w.Recorder().RecordAsync(OtherDecision(w, code, InvoiceLineDecisionService.IdOf($"remove-{line.Id:D}")));

        var removal = await w.Service().RemoveAsync(RemoveOf(w, line.Id, null), CancellationToken.None);

        removal.Status.Should().Be(AdjustmentStatus.Applied);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1);
    }

    [Fact(DisplayName = "095f-2b R1 (F1): the same adjustment id on two invoices files two records with different decision ids")]
    public async Task Add_TheSameAdjustmentIdOnTwoInvoices_FilesTwoRecordsUnderDifferentDecisionIds()
    {
        var id = Guid.NewGuid();
        var a = new World();
        var b = new World();

        (await a.Service().AddAsync(AddWithNote(a, null, id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        (await b.Service().AddAsync(AddWithNote(b, null, id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);

        InvoiceAdjustmentService.AddDecisionIdFor(a.InvoiceId, id).Should().NotBe(InvoiceAdjustmentService.AddDecisionIdFor(b.InvoiceId, id));
        a.DecisionRecords.Should().ContainSingle();
        b.DecisionRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "095f-2b R1 (F1) must-not-fire: a repeat of an add under the derived id still files ONE record, and a different note is still 409")]
    public async Task Add_RepeatsUnderTheDerivedId_StillFileOneRecord_AndAnotherNoteIsStill409()
    {
        var w = new World();
        var id = Guid.NewGuid();

        (await w.Service().AddAsync(AddWithNote(w, "Same", id), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        (await w.Service().AddAsync(AddWithNote(w, "Same", id), CancellationToken.None)).AlreadyApplied.Should().BeTrue();
        var edited = await w.Service().AddAsync(AddWithNote(w, "Other", id), CancellationToken.None);

        edited.Status.Should().Be(AdjustmentStatus.IdReused);
        w.DecisionRecords.Should().ContainSingle();
    }

    // =========================================================================================
    // Review round 1 (F1 low): a faulted stored-record lookup on a repeat does not claim the totals were updated
    // =========================================================================================

    [Fact(DisplayName = "095f-2b R1: an add repeat whose stored-record lookup faults is 503 with neutral wording (it never says the totals were updated), and the next repeat finishes it")]
    public async Task Add_ALookupFaultOnARepeat_Is503WithNeutralWording()
    {
        var w = new World();
        var command = AddWithNote(w, "Kept");
        w.TotalsFault = new TimeoutException("totals");
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);

        w.H.LookupFault = new TimeoutException("lookup");
        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Incomplete);
        repeat.Code.Should().Be(AdjustmentCodes.Incomplete);
        repeat.Detail.Should().Contain("not finished").And.Contain("Repeat the same request");
        repeat.Detail.Should().NotContain("totals were updated").And.NotContain("not recorded").And.NotContain("saved");
        w.TotalsWriteCount.Should().Be(0, "the totals were never confirmed, and the faulted repeat did not run them");

        w.H.LookupFault = null;
        w.TotalsFault = null;
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        w.DecisionRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "095f-2b R1: a removal repeat whose stored-record lookup faults is 503 with neutral wording (it never says the totals were updated), and the next repeat finishes it")]
    public async Task Remove_ALookupFaultOnARepeat_Is503WithNeutralWording()
    {
        var w = new World();
        var line = ActiveAdjustment(w);
        w.TotalsFault = new TimeoutException("totals");
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);

        w.H.LookupFault = new TimeoutException("lookup");
        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Incomplete);
        repeat.Code.Should().Be(AdjustmentCodes.Incomplete);
        repeat.Detail.Should().Contain("not finished").And.Contain("Repeat the same request");
        repeat.Detail.Should().NotContain("totals were updated").And.NotContain("not recorded");

        w.H.LookupFault = null;
        w.TotalsFault = null;
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
    }

    // =========================================================================================
    // The catalog entries, as the writer applies them
    // =========================================================================================

    private static BillingDecision Decision(string code, string? reason, IReadOnlyDictionary<string, object?> values) => new(
        Guid.NewGuid(), code, Guid.NewGuid(), BillingDecider.Human(Reviewer, InvoiceAdjustmentService.ReviewerRole),
        new Dictionary<string, object?> { ["approvedTotal"] = 900m }, "Done", Reason: reason, Values: values);

    [Fact(DisplayName = "095f-2b AC: the writer refuses add-adjustment without a reason (ReviewInvalid) and accepts it with one")]
    public void TheWriter_RefusesAnAddWithoutAReason()
    {
        var recorder = new World().Recorder();
        var values = new Dictionary<string, object?> { ["method"] = "percent", ["value"] = 10m };

        recorder.Invoking(r => r.Preflight(Decision(InvoiceAdjustmentService.AddActionCode, null, values))).Should().Throw<DecisionRecordRefusedException>()
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        recorder.Invoking(r => r.Preflight(Decision(InvoiceAdjustmentService.AddActionCode, "  ", values))).Should().Throw<DecisionRecordRefusedException>();
        recorder.Invoking(r => r.Preflight(Decision(InvoiceAdjustmentService.AddActionCode, Why, values))).Should().NotThrow("control: with a reason it is accepted");
    }

    [Fact(DisplayName = "095f-2b AC: the writer accepts remove-adjustment with a null reason, and refuses it without the adjustment")]
    public void TheWriter_AcceptsARemovalWithANullReason()
    {
        var recorder = new World().Recorder();

        recorder.Invoking(r => r.Preflight(Decision(InvoiceAdjustmentService.RemoveActionCode, null, new Dictionary<string, object?> { ["adjustment"] = Guid.NewGuid() })))
            .Should().NotThrow();
        recorder.Invoking(r => r.Preflight(Decision(InvoiceAdjustmentService.RemoveActionCode, null, new Dictionary<string, object?>())))
            .Should().Throw<DecisionRecordRefusedException>("control: its required parameter is checked");
    }

    // =========================================================================================
    // The note is never logged, and never written anywhere but the record and sprk_decisionnote
    // =========================================================================================

    [Fact(DisplayName = "095f-2b: no log line (service, totals, recorder, writer) carries the note, through success, refusal, strand and repeat")]
    public async Task NoLogLine_CarriesTheNote()
    {
        var w = new World();
        var id = Guid.NewGuid();
        var line = ActiveAdjustment(w);

        await w.Service().AddAsync(AddWithNote(w, Sentinel + " \u0007"), CancellationToken.None);       // refused note
        BreakTheRecordStep(w);
        await w.Service().AddAsync(AddWithNote(w, Sentinel + " add", id), CancellationToken.None);      // strand
        HealTheRecordStep(w);
        await w.Service().AddAsync(AddWithNote(w, Sentinel + " add", id), CancellationToken.None);      // finished
        await w.Service().AddAsync(AddWithNote(w, Sentinel + " edited", id), CancellationToken.None);   // 409
        await w.Service().RemoveAsync(RemoveOf(w, line.Id, Sentinel + " remove"), CancellationToken.None);
        await w.Service().RemoveAsync(RemoveOf(w, line.Id, Sentinel + " edited"), CancellationToken.None); // 409

        w.DecisionRecords.Should().HaveCount(2, "control: the notes reached their records");
        w.Log.Entries.Should().NotBeEmpty("control: the strand and the refusals were logged");
        w.Log.Entries.Select(e => e.Message)
            .Concat(w.TotalsLog.Entries.Select(e => e.Message))
            .Concat(w.RecorderLog.Entries.Select(e => e.Message))
            .Concat(w.H.Log.Entries.Select(e => e.Message))
            .Should().NotContain(m => m.Contains(Sentinel));
    }

    [Fact(DisplayName = "095f-2b must-not-fire: neither act writes sprk_correctionnote, or the note on any line row (control: it is on the records and the column)")]
    public async Task TheNote_IsOnlyOnTheRecordAndTheDecisionColumn()
    {
        var w = new World();
        var line = ActiveAdjustment(w);

        await w.Service().AddAsync(AddWithNote(w, Sentinel + " add"), CancellationToken.None);
        await w.Service().RemoveAsync(RemoveOf(w, line.Id, Sentinel + " remove"), CancellationToken.None);

        w.DecisionRecords.Should().HaveCount(2).And.OnlyContain(r => r.GetAttributeValue<string>("sprk_steps").Contains(Sentinel), "control");
        DecisionNote(w).Should().Be(Sentinel + " remove", "control: the latest decision's note");
        AssertTheCorrectionNoteIsNeverWritten(w);
        w.Lines.SelectMany(l => l.Attributes.Values.OfType<string>()).Should().NotContain(v => v.Contains(Sentinel), "no line row carries the note");
    }
}
