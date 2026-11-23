using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// The AP export and the owner of its files (ebilling-r1, dev incident 2026-11-03, uac-r2): a caller whose business unit cannot own the export files
/// (a System Administrator in the ROOT unit) used to get a batch row created, invoices claimed, and only then a "Try again" 503 from the file's own owner
/// question, with a Failed batch left in the root unit. The owner question is now asked BEFORE the batch row or any claim
/// (<c>ApExportService</c> step 3b, through <c>StoredFileService.ResolveOwnerForAppAsync</c> — the file's own question), and a refusal that a retry cannot
/// change is a 409 <c>ebilling.export.owner_unit_not_allowed</c>, not a retryable 503. Every assertion is on stored state
/// (<see cref="ApExportWorld"/>): rows, writes, blobs, the questions the resolver was asked.
/// </summary>
public sealed class ApExportOwnerUnitTests
{
    private static readonly Regex AnyGuid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static void NothingWritten(ApExportWorld world, params Entity[] invoices)
    {
        world.Dataverse.Writes.Should().Be(0, "nothing is created, claimed or sent");
        world.Batches().Should().BeEmpty("no batch row is created, so no Failed batch is left behind");
        world.StoredFiles().Should().BeEmpty();
        world.Gateway.Names.Should().BeEmpty("no blob is staged");
        world.Lifecycle.Sent.Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
        foreach (var invoice in invoices)
        {
            world.BatchOfInvoice(invoice.Id).Should().BeNull("no invoice is claimed");
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
        }
    }

    // =========================================================================================
    // Defect 2: the question is asked before anything is written
    // =========================================================================================

    [Fact(DisplayName = "ebilling-r1 AP export: a caller in the ROOT business unit gets 409 owner_unit_not_allowed with no batch row created, no invoice claimed and no file written")]
    public async Task RootUnitCaller_Is409_AndNothingIsWritten()
    {
        var world = new ApExportWorld { CallerInRootBusinessUnit = true };
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.OwnerUnitNotAllowed);
        result.Code.Should().Be(ApExportCodes.OwnerUnitNotAllowed).And.Be("ebilling.export.owner_unit_not_allowed");
        result.BatchId.Should().BeNull("no batch exists");
        result.Batch.Should().BeNull();
        NothingWritten(world, a, b);

        // The text says what an administrator must do and names no internal id.
        result.Detail.Should().Contain("business unit").And.Contain("administrator").And.Contain("will not help");
        AnyGuid.IsMatch(result.Detail!).Should().BeFalse("the answer names no id");

        // A retry changes nothing and writes nothing either: the refusal is deterministic, not a leaking fault.
        var again = await world.ExportAsync(a, b);
        again.Outcome.Should().Be(ApExportOutcome.OwnerUnitNotAllowed);
        NothingWritten(world, a, b);
    }

    [Fact(DisplayName = "ebilling-r1 AP export: the owner question is the FIRST thing asked, before the batch row, and is the file's own question - the same table, user and parents every file write asks")]
    public async Task OwnerQuestion_IsAskedFirst_AndIsTheFilesOwnQuestion()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var writesWhenAsked = new List<int>();
        world.OwnerAnswer = (_, _) =>
        {
            writesWhenAsked.Add(world.Dataverse.Writes);
            return null;
        };

        var result = await world.ExportAsync(a, b);

        // BU1 caller (the default): the pre-check passes and the export completes.
        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        world.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusCompleted);
        world.StoredFiles().Should().HaveCount(2);
        world.Lifecycle.Sent.Should().BeEquivalentTo([a.Id, b.Id]);

        // Asked: the pre-check (the file's question), the batch's own owner, then the CSV's and the JSON's questions.
        world.OwnerQuestions.Select(q => q.ChildEntityLogicalName).Should().Equal(StoredFileSchema.Entity, null, StoredFileSchema.Entity, StoredFileSchema.Entity);
        writesWhenAsked[0].Should().Be(0, "the pre-check is asked before the batch row, any claim or any other write");
        writesWhenAsked[1].Should().Be(0, "the batch's own owner is asked before the batch is created");

        // The pre-check and each file's check are the SAME question: the same table, the same acting user, the same parents (none: a billing export files nothing).
        var fileQuestions = world.OwnerQuestions.Where(q => q.ChildEntityLogicalName == StoredFileSchema.Entity).ToList();
        fileQuestions.Should().HaveCount(3);
        fileQuestions.Select(q => (q.CallerSystemUserId, q.CallerObjectId, q.RequestedBy?.SystemUserId, q.TargetEntityLogicalName, q.TargetRecordId, q.Parents.Count, q.SecureOnlyParents.Count, q.WhenUnfiled))
            .Distinct().Should().ContainSingle("the pre-check asks the file's own question, not a copy of it")
            .Which.Should().Be(((Guid?)ApExportWorld.CallerSystemUser, (Guid?)null, (Guid?)ApExportWorld.CallerSystemUser, (string?)null, (Guid?)null, 0, 0, UnfiledOwnership.ActingUserTeam));
    }

    // =========================================================================================
    // Defect 1: a refusal that can never succeed is not retryable; a fault still is
    // =========================================================================================

    [Fact(DisplayName = "ebilling-r1 AP export: a fault in the owner pre-check is 503 unavailable with nothing written - never the 409 and never 'allowed'")]
    public async Task PreCheckFault_Is503_AndNothingIsWritten()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.OwnerAnswer = (_, asked) => asked == 1 ? throw new InvalidOperationException("Dataverse unavailable") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        result.Code.Should().Be(ApExportCodes.Unavailable);
        result.Detail.Should().Contain("Try again");
        NothingWritten(world, a);
        world.OwnerQuestions.Should().ContainSingle("the export stopped at the pre-check");
    }

    public static TheoryData<string> UnknownAnswers => new()
    {
        "refusal: another owner code",      // a refusal the resolver gave for another reason is not the deterministic one
        "refusal: no code",                 // a refusal with no code at all
        "unchanged",                        // an answer that is neither an owner nor a refusal
        "owned by no team",                 // an owner with an empty id
    };

    [Theory(DisplayName = "ebilling-r1 AP export: any unknown or unreadable pre-check answer is a retryable 503 with nothing written - only the restricted-content owner refusal is the 409")]
    [MemberData(nameof(UnknownAnswers))]
    public async Task UnknownPreCheckAnswer_Is503_NeverThe409_AndNothingIsWritten(string answer)
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.OwnerAnswer = (_, asked) => asked != 1 ? null : answer switch
        {
            "refusal: another owner code" => RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team"),
            "refusal: no code" => RecordOwnerResolution.Refused(null!, "no code"),
            "unchanged" => RecordOwnerResolution.Unchanged("keeps its creator"),
            _ => RecordOwnerResolution.Owned(Guid.Empty),
        };

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        result.Code.Should().Be(ApExportCodes.Unavailable).And.NotBe(ApExportCodes.OwnerUnitNotAllowed);
        NothingWritten(world, a);
    }

    // =========================================================================================
    // The file's own check still decides (the pre-check and it cannot disagree except when the world changes between them)
    // =========================================================================================

    [Fact(DisplayName = "ebilling-r1 AP export: a transient owner fault in the FILE's own check (after the pre-check passed) is still 503 file_failed, compensates, and the Failed batch blocks nothing")]
    public async Task TransientFileOwnerFault_Is503FileFailed_AndCompensates()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        // Questions: 1 pre-check, 2 the batch's owner, 3 the first file's. The third faults.
        world.OwnerAnswer = (_, asked) => asked == 3 ? throw new InvalidOperationException("Dataverse unavailable") : null;

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        result.Code.Should().Be(ApExportCodes.FileFailed);
        result.Detail.Should().Contain("Try again");
        result.BatchId.Should().NotBeNull();
        world.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("compensation released every claim");
        foreach (var invoice in new[] { a, b })
        {
            world.BatchOfInvoice(invoice.Id).Should().BeNull();
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
        }

        // A Failed batch blocks nothing: the same invoices export once the fault is gone.
        world.OwnerAnswer = null;
        var again = await world.ExportAsync(a, b);
        again.Outcome.Should().Be(ApExportOutcome.Created, again.Detail);
        world.Batches().Should().HaveCount(2).And.ContainSingle(r => r.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value == ApExportService.StatusCompleted);
    }

    [Fact(DisplayName = "ebilling-r1 AP export: when the file's own check refuses after the pre-check passed (the world changed between them), the file's refusal still decides - 409, compensated, nothing exported")]
    public async Task FileOwnerRefusalAfterPreCheck_StillRefuses_409_AndCompensates()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        // The user's unit changed between the pre-check (1) and the first file write (3).
        world.OwnerAnswer = (_, asked) => asked == 3
            ? RecordOwnerResolution.Refused(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved, "the restricted-content owner team cannot be used (AnswerIsRootBusinessUnit)")
            : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.OwnerUnitNotAllowed);
        result.Code.Should().Be(ApExportCodes.OwnerUnitNotAllowed);
        AnyGuid.IsMatch(result.Detail!).Should().BeFalse("the answer names no id, even when a batch had been created");
        world.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        world.StoredFiles().Should().BeEmpty();
        world.Lifecycle.Sent.Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
        world.BatchOfInvoice(a.Id).Should().BeNull();

        // A Failed batch blocks nothing.
        world.OwnerAnswer = null;
        (await world.ExportAsync(a)).Outcome.Should().Be(ApExportOutcome.Created);
    }

    [Fact(DisplayName = "ebilling-r1 AP export: an owner refusal in the FILE's check that is not the restricted-content one stays the retryable 503 file_failed")]
    public async Task OtherFileOwnerRefusal_StaysRetryable503()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.OwnerAnswer = (_, asked) => asked == 3 ? RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        result.Code.Should().Be(ApExportCodes.FileFailed);
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
    }
}
