using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

// A partial of the adjustment service's own test class (its private World), kept in that class's namespace (as the DecisionRecords partial does).
namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 ISS-030 (#2881): when a removal's totals recalculation is refused after THIS request deactivated the row, the row is reactivated (as the
/// add path deactivates its own new row on a refused calculation) and the refusal is answered: the invoice is left as it was. No Decision Record and no
/// note are written. A repeat that finds the row inactive with no removal record is a removal the refused request never finished, so it undoes too; a
/// repeat of a removal whose record exists is removed for good and is never reactivated. Over the REAL adjustment service, totals service, recorder and
/// writer harness: each test proves the stored state (the row, the records, the invoice's columns).
/// </summary>
public sealed partial class InvoiceAdjustmentServiceTests
{
    private static readonly DateTime PriorReviewedOn = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>The reviewer who added the adjustment in the fixtures (the row's prior stamp); <see cref="Reviewer"/> is the one removing it.</summary>
    private static readonly Guid PriorAdder = Guid.Parse("02000000-0000-4000-8000-0000000000a2");

    /// <summary>An active adjustment as the add path leaves it: stamped with the adding reviewer and the time.</summary>
    private static Entity StampedAdjustment(World w, Guid adder)
    {
        var line = ActiveAdjustment(w);
        line["sprk_reviewedby"] = new EntityReference("systemuser", adder);
        line["sprk_reviewedon"] = PriorReviewedOn;
        return line;
    }

    /// <summary>Another writer lowers the billed total below the line adjustments: the totals calculation is then refused (negative approved total).</summary>
    private static void RefuseTheTotals(World w) => w.Invoice["sprk_totalamount"] = new Money(10m);

    private static void HealTheTotals(World w, decimal billed = 1100m) => w.Invoice["sprk_totalamount"] = new Money(billed);

    /// <summary>The reactivation (the service's plain update of the line) fails until <see cref="HealTheUndoStep"/>.</summary>
    private static void BreakTheUndoStep(World w) =>
        w.Entities.Setup(e => e.UpdateAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("undo"));

    private static void HealTheUndoStep(World w) =>
        w.Entities.Setup(e => e.UpdateAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                var line = w.Lines.Single(l => l.Id == id);
                foreach (var (name, value) in fields)
                {
                    line[name] = value;
                }

                w.DeactivationCount++;
                return Task.CompletedTask;
            });

    private static void AssertTheRowIsActiveAgain(World w, Guid lineId)
    {
        var row = w.Row(lineId);
        row.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0, "the refused removal is undone: the row is active");
        row.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(1, "statecode and statuscode travel together");
    }

    [Fact(DisplayName = "ISS-030 AC1: a removal whose totals are refused reactivates the row with its prior stamp, answers the refusal, and writes no record, note or totals")]
    public async Task Remove_TheTotalsRefused_ReactivatesTheRow_RestoresTheStamp_WritesNoRecord()
    {
        var w = new World();
        var line = StampedAdjustment(w, PriorAdder);
        w.AfterDeactivate = () => RefuseTheTotals(w);

        var result = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Firm agreed to drop it"), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NegativeTotal, "the refusal is answered");
        AssertTheRowIsActiveAgain(w, line.Id);
        w.Row(line.Id).GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(PriorAdder, "the remover's stamp is replaced by the prior reviewer (the adder)");
        w.Row(line.Id).GetAttributeValue<DateTime>("sprk_reviewedon").Should().Be(PriorReviewedOn);
        w.DecisionRecords.Should().BeEmpty("a refused removal files no Decision Record");
        w.H.Created.Should().BeEmpty();
        DecisionNote(w).Should().BeNull("no note is written for a refused removal");
        w.TotalsWriteCount.Should().Be(0);
        w.DeactivationCount.Should().Be(2, "one deactivation, one reactivation");
        w.Queue.Sent.Should().BeEmpty("a refused removal queues no re-index");
        w.Log.Entries.Should().NotContain(e => e.Message.Contains("reverted on a repeat"), "the request that deactivated the row undoes it silently");
    }

    [Fact(DisplayName = "ISS-030: a refused removal of a row that held no stamp clears the remover's stamp")]
    public async Task Remove_TheTotalsRefused_ARowWithNoPriorStamp_HasTheRemoversStampCleared()
    {
        var w = new World();
        var line = ActiveAdjustment(w);
        w.AfterDeactivate = () => RefuseTheTotals(w);

        var result = await w.Service().RemoveAsync(RemoveOf(w, line.Id, null), CancellationToken.None);

        result.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        AssertTheRowIsActiveAgain(w, line.Id);
        w.Row(line.Id)["sprk_reviewedby"].Should().Be(DBNull.Value, "a cleared lookup, never left as the remover");
        w.Row(line.Id)["sprk_reviewedon"].Should().Be(DBNull.Value);
    }

    [Fact(DisplayName = "ISS-030 AC3: a failed reactivation is 503 incomplete at the undo step; the same request repeated reactivates the row and answers the refusal")]
    public async Task Remove_TheUndoFails_Is503Incomplete_TheRepeatReactivatesAndAnswersTheRefusal()
    {
        var w = new World();
        var line = StampedAdjustment(w, PriorAdder);
        w.AfterDeactivate = () => RefuseTheTotals(w);
        BreakTheUndoStep(w);
        var command = RemoveOf(w, line.Id, "Gone");

        var first = await w.Service().RemoveAsync(command, CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Incomplete);
        first.Code.Should().Be(AdjustmentCodes.Incomplete);
        first.Detail.Should().Contain("could not be put back", "the undo step has its own wording, never 'totals were not updated'");
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the undo failed, so the row is still inactive");
        w.DecisionRecords.Should().BeEmpty();

        HealTheUndoStep(w);
        var repeat = await w.Service().RemoveAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.NegativeTotal, "the repeat finds the row inactive with no record, recalculates, is refused again, and undoes");
        AssertTheRowIsActiveAgain(w, line.Id);
        w.DecisionRecords.Should().BeEmpty();
        DecisionNote(w).Should().BeNull();
        w.TotalsWriteCount.Should().Be(0);
        w.Queue.Sent.Should().BeEmpty();
        // Known limit 1: the first request overwrote the prior stamp and it is not recoverable, so the repeat's undo leaves the remover's stamp.
        w.Row(line.Id).GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(Reviewer, "the repeat's undo leaves the stamp as the removal wrote it");
        w.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("reverted on a repeat") && e.Message.Contains("the stamp is left as it is"),
            "an undo on a repeat is logged (ids only)");
        w.Log.Entries.Should().NotContain(e => e.Message.Contains("Gone"), "no request text in the log");

        // The row is active again, so a further identical request is an ordinary first removal attempt: the dry run refuses it before any write.
        var third = await w.Service().RemoveAsync(command, CancellationToken.None);
        third.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        AssertTheRowIsActiveAgain(w, line.Id);
    }

    [Fact(DisplayName = "ISS-030: a repeat after a failed undo, once the totals can be calculated again, finishes the removal with exactly ONE record")]
    public async Task Remove_TheUndoFailed_TheRepeatWithTotalsRestored_FinishesTheRemoval()
    {
        var w = new World();
        var line = StampedAdjustment(w, PriorAdder);
        w.AfterDeactivate = () => RefuseTheTotals(w);
        BreakTheUndoStep(w);
        var command = RemoveOf(w, line.Id, "Gone");

        (await w.Service().RemoveAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);
        HealTheTotals(w);
        var repeat = await w.Service().RemoveAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the removal is finished, not undone");
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        DecisionNote(w).Should().Be("Gone");
    }

    [Fact(DisplayName = "ISS-030 AC2 must-not-fire: a repeat of an earlier SUCCESSFUL removal whose recalculation is now refused does not reactivate the row")]
    public async Task Remove_ARepeatOfASuccessfulRemoval_WhoseTotalsAreNowRefused_IsNotReactivated()
    {
        var w = new World();
        var line = StampedAdjustment(w, PriorAdder);
        var command = RemoveOf(w, line.Id, "Dropped");
        (await w.Service().RemoveAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
        var deactivations = w.DeactivationCount;
        RefuseTheTotals(w);

        var repeat = await w.Service().RemoveAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.NegativeTotal, "the refusal is reported");
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the earlier removal finished: it is removed for good");
        w.Row(line.Id).GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(Reviewer, "the removal's stamp is untouched");
        w.DeactivationCount.Should().Be(deactivations, "no write on the row");
        RecordsOf(w, InvoiceAdjustmentService.RemoveActionCode).Should().ContainSingle();
    }

    [Fact(DisplayName = "ISS-030: a repeat by another reviewer after a failed undo is 409 id_reused and does not reactivate the row")]
    public async Task Remove_TheUndoFailed_ARepeatByAnotherReviewer_IsIdReused_AndNotReactivated()
    {
        var w = new World();
        var line = StampedAdjustment(w, PriorAdder);
        w.AfterDeactivate = () => RefuseTheTotals(w);
        BreakTheUndoStep(w);
        (await w.Service().RemoveAsync(RemoveOf(w, line.Id, null), CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);
        HealTheUndoStep(w);

        var other = await w.Service().RemoveAsync(RemoveOf(w, line.Id, null, reviewer: Remover), CancellationToken.None);

        other.Status.Should().Be(AdjustmentStatus.IdReused);
        w.Row(line.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1);
    }

    [Fact(DisplayName = "ISS-030: a repeat that finds the row inactive with no removal record and the totals still refused reactivates it (stamp left as is)")]
    public async Task Remove_ARepeatFindingTheRowInactiveWithNoRecord_TheTotalsRefused_Reactivates()
    {
        // The state a removal leaves when its undo failed, built directly (no setup through the failing step).
        var w = new World();
        var line = ActiveAdjustment(w);
        line["statecode"] = new OptionSetValue(1);
        line["statuscode"] = new OptionSetValue(2);
        line["sprk_reviewedby"] = new EntityReference("systemuser", Reviewer);
        line["sprk_reviewedon"] = PriorReviewedOn;
        RefuseTheTotals(w);

        var repeat = await w.Service().RemoveAsync(RemoveOf(w, line.Id, "Gone"), CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.NegativeTotal);
        AssertTheRowIsActiveAgain(w, line.Id);
        w.Row(line.Id).GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(Reviewer, "the stamp is left as it is");
        w.DecisionRecords.Should().BeEmpty();
        DecisionNote(w).Should().BeNull();
        w.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("reverted on a repeat"));
    }

    // =========================================================================================
    // F1 (review round 1): the add path, same rule - a refused calculation on a repeat undoes the row unless the add's record exists
    // =========================================================================================

    private static void RefuseTheTotalsAfterTheAddWrites(World w) => w.AfterCreate = () => w.Invoice["sprk_totalamount"] = new Money(100m);

    [Fact(DisplayName = "ISS-030 F1: a failed add-undo is 503 incomplete at the undo step, worded 'taken back', never 'saved ... totals were not updated'")]
    public async Task Add_TheUndoFails_Is503Incomplete_AtTheUndoStep_WithItsOwnWording()
    {
        var w = new World();
        RefuseTheTotalsAfterTheAddWrites(w);
        BreakTheUndoStep(w);

        var first = await w.Service().AddAsync(Add(w, "fixed", 150m), CancellationToken.None);

        first.Status.Should().Be(AdjustmentStatus.Incomplete);
        first.Code.Should().Be(AdjustmentCodes.Incomplete);
        first.Detail.Should().Contain("could not be taken back").And.NotContain("saved").And.NotContain("totals were not updated");
        w.Adjustments.Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0, "the undo failed: the row is still active");
        w.DecisionRecords.Should().BeEmpty();
    }

    [Fact(DisplayName = "ISS-030 F1 AC3: after a failed add-undo, the same add repeated while the refusal still holds deactivates the row and answers the refusal")]
    public async Task Add_TheUndoFailed_TheRepeatWhileRefused_DeactivatesTheRow()
    {
        var w = new World();
        RefuseTheTotalsAfterTheAddWrites(w);
        BreakTheUndoStep(w);
        var command = Add(w, "fixed", 150m);
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);
        HealTheUndoStep(w);

        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.NegativeTotal, "the refusal is answered");
        w.Adjustments.Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1, "the repeat undoes the add");
        w.DecisionRecords.Should().BeEmpty("a refused add files no record");
        w.TotalsWriteCount.Should().Be(0);
        w.CreateCount.Should().Be(1, "the repeat adds nothing");
        w.Queue.Sent.Should().BeEmpty();
        w.Log.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("undone on a repeat") && e.Message.Contains("the stamp is left as it is"));
    }

    [Fact(DisplayName = "ISS-030 F1: after a failed add-undo, once the totals calculate again the repeat finishes the add with exactly ONE record")]
    public async Task Add_TheUndoFailed_TheRepeatWithTotalsRestored_FinishesTheAdd()
    {
        var w = new World();
        RefuseTheTotalsAfterTheAddWrites(w);
        BreakTheUndoStep(w);
        var command = Add(w, "fixed", 150m);
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Incomplete);
        HealTheTotals(w);

        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        w.Adjustments.Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
        RecordsOf(w, InvoiceAdjustmentService.AddActionCode).Should().ContainSingle();
    }

    [Fact(DisplayName = "ISS-030 F1 must-not-fire: a repeat of an add whose Decision Record exists, with the totals now refused, is never undone")]
    public async Task Add_ARepeatOfAFinishedAdd_WhoseTotalsAreNowRefused_IsNotUndone()
    {
        var w = new World();
        var command = Add(w, "fixed", 25m);
        (await w.Service().AddAsync(command, CancellationToken.None)).Status.Should().Be(AdjustmentStatus.Applied);
        RecordsOf(w, InvoiceAdjustmentService.AddActionCode).Should().ContainSingle();
        var writes = w.DeactivationCount;
        w.Invoice["sprk_totalamount"] = new Money(10m);

        var repeat = await w.Service().AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AdjustmentStatus.NegativeTotal, "the refusal is reported");
        w.Adjustments.Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0, "the finished add stays");
        w.DeactivationCount.Should().Be(writes, "no write on the row");
        RecordsOf(w, InvoiceAdjustmentService.AddActionCode).Should().ContainSingle();
    }
}
