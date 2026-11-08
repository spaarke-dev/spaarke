using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (ebilling-r1 reconcile): the REAL invoice-family pre-write rule
/// (<see cref="RecordTableRules.InvoiceFamilyRule"/>, registered on <c>sprk_invoice</c>) driven through the REAL service's
/// step 5½ and rendered through <see cref="RecordCreationProblemMap"/> — the body a route will return once it is re-pointed.
/// A PARTIAL of the pipeline seam fixture: the same service, doubles and journal; no fixture rule except where the guard has
/// no reachable path (an unknown table).
/// </summary>
/// <remarks>
/// The route contract reproduced (<c>ChildRecordEndpoints.InvoiceFamilyRefused</c>): what the row names wrong is 400 with the
/// guard's code; <c>invoice_family_read_failed</c> / <c>invoice_family_unknown_entity</c> are 500 with that code (F2); a
/// record the row names that the caller cannot see is the uniform 404, and only <see cref="PreWriteRefusal.NotFound"/> gets
/// there. Every refusal writes nothing.
/// </remarks>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    private static readonly Guid FamilyProject = Guid.Parse("a0170000-0000-4000-8000-000000000001");
    private static readonly Guid FamilySupersededInvoice = Guid.Parse("a0170000-0000-4000-8000-000000000002");

    private RecordCreationRequest InvoiceRequest(object body) => WebApiRequest("sprk_invoice", body);

    /// <summary>The superseded invoice as the caller reads it: its core lookup (the scripted row answers the rest as null).</summary>
    private void SupersededInvoiceRegards(Guid matter) =>
        _scripted.RowValues[FamilySupersededInvoice] = new Dictionary<string, object?> { ["_sprk_matter_value"] = matter.ToString("D") };

    private void AssertNothingWritten()
    {
        _createAttempts.Should().BeEmpty("a refused invoice-family row is never written");
        _creates.Should().BeEmpty();
        _scripted.Posts.Should().BeEmpty();
        _scripted.Patches.Should().BeEmpty();
        Journal().Should().NotContain(["owner", "create", "restamp", "mirror", "membership"]);
        _publisher.Published.Should().BeEmpty();
    }

    // =====================================================================================
    // Deliverable 7: the guard refuses → nothing is written
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnInvoiceNamingTwoCoreRecords_IsRefusedByTheRegisteredRule_400WithTheGuardsCode_AndWritesNothing()
    {
        var outcome = await Service().CreateAsync(InvoiceRequest(new
        {
            sprk_name = "Two core records",
            sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})",
            sprk_Project_bind = $"/sprk_projects({FamilyProject:D})",
        }), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull("FR-05: refused, never picked");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        outcome.Refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        outcome.Refusal.PreWrite.Should().NotBeNull("the registry's rule refused it, at step 5½");
        var problem = Problem(outcome, "sprk_invoice");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Extensions["errorCode"].Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        problem.Extensions["reasonCode"].Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        AssertNothingWritten();
    }

    // =====================================================================================
    // Must-not-fire: a row the guard admits is created
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnInvoiceUnderOneMatter_PassesTheRule_AndIsCreatedOnce()
    {
        var outcome = await Service().CreateAsync(InvoiceRequest(new
        {
            sprk_name = "One core record",
            sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})",
        }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Steps.Should().ContainSingle(s => s.Step == RecordCreationStep.PreWriteRules)
            .Which.State.Should().Be(RecordCreationStepState.Ran, "the registered rule ran and admitted the row");
        _creates.Should().ContainSingle().Which.Table.Should().Be("sprk_invoice");
    }

    // =====================================================================================
    // F2: read_failed and unknown_entity are 500 Undecided with the guard's code
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheSupersededInvoiceComesBackWithoutItsLookups_IsUndecided500ReadFailed_AndWritesNothing()
    {
        SupersededInvoiceRegards(OrdinaryMatter);
        _scripted.GetOverride = path => path.StartsWith($"sprk_invoices({FamilySupersededInvoice:D})?$select=", StringComparison.Ordinal)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { }))
            : null;

        var outcome = await Service().CreateAsync(InvoiceRequest(new
        {
            sprk_name = "Supersedes an unreadable answer",
            sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})",
            sprk_SupersedesInvoice_bind = $"/sprk_invoices({FamilySupersededInvoice:D})",
        }), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Undecided, "a read the guard needed failed: never 'not set'");
        outcome.Refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Refusal.PreWrite!.IsUndecided.Should().BeTrue();
        var problem = Problem(outcome, "sprk_invoice");
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Extensions["errorCode"].Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        AssertNothingWritten();
    }

    [Fact]
    public async Task CreateAsync_TheGuardsUnknownEntityAnswer_IsUndecided500_WithItsCode()
    {
        // The guard answers unknown_entity only for a table outside its family, which the registered rule never asks about;
        // the guard's answer is fed through the rule's REAL mapping to pin the runner's half of the contract.
        var decided = new OwnedChildWrite.InvoiceFamilyStep(
            InvoiceFamilyCoreRecord.Refused("sprk_event", InvoiceFamilyCoreRecordGuard.UnknownEntityCode, "sprk_event is not an invoice-family table."),
            null, false);
        WithAsyncTestRule((_, _) => Task.FromResult(RecordTableRules.InvoiceFamilyPreWrite(decided)));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Undecided);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Extensions["errorCode"].Should().Be(InvoiceFamilyCoreRecordGuard.UnknownEntityCode);
        problem.Detail.Should().Be("sprk_event is not an invoice-family table.");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // The uniform not-found: only NotFound() reaches it
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnInvoiceSupersedingOneTheCallerCannotRead_IsTheUniformNotFound_AndWritesNothing()
    {
        _scripted.Missing.Add(MissingMatter);
        var missing = await Service().CreateAsync(EventRequest(MissingMatter), AsCaller(), CancellationToken.None);
        _journal.Clear();
        SupersededInvoiceRegards(OrdinaryMatter);
        _scripted.UnreadableRows.Add(FamilySupersededInvoice);

        var outcome = await Service().CreateAsync(InvoiceRequest(new
        {
            sprk_name = "Supersedes an invisible invoice",
            sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})",
            sprk_SupersedesInvoice_bind = $"/sprk_invoices({FamilySupersededInvoice:D})",
        }), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.PreWrite!.ParentUnavailable.Should().BeTrue("the rule's not-found, not step 5's");
        outcome.Refusal.Detail.Should().Be(RecordCreationService.NotAvailableToFileUnder);
        var problem = Problem(outcome, "sprk_invoice");
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.NotFoundCode);
        JsonSerializer.Serialize(problem).Should().NotContain(FamilySupersededInvoice.ToString("D"));
        JsonSerializer.Serialize(problem).Replace("invoice", "event").Should().Be(
            JsonSerializer.Serialize(Problem(missing, "sprk_event")), "the uniform body: only the noun differs (NFR-01)");
        AssertNothingWritten();
    }

    [Fact]
    public async Task CreateAsync_TheGuardsRecordNotFoundCode_IsTheUniformNotFound_Not400()
    {
        var decided = new OwnedChildWrite.InvoiceFamilyStep(
            InvoiceFamilyCoreRecord.Refused("sprk_invoice", InvoiceFamilyCoreRecordGuard.RecordNotFoundCode, "sprk_invoice(…) does not exist."),
            null, false);
        WithAsyncTestRule((_, _) => Task.FromResult(RecordTableRules.InvoiceFamilyPreWrite(decided)));

        var outcome = await Service().CreateAsync(EventRequest(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.NotFoundCode);
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // F4: a supersedes refusal keeps the guard's Code and Reason through the service and the map
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnInvoiceSupersedingOneOfAnotherMatter_KeepsTheGuardsCodeAndReason_ThroughTheProblemMap()
    {
        SupersededInvoiceRegards(SecureMatter);

        var outcome = await Service().CreateAsync(InvoiceRequest(new
        {
            sprk_name = "Supersedes another matter's invoice",
            sprk_Matter_bind = $"/sprk_matters({OrdinaryMatter:D})",
            sprk_SupersedesInvoice_bind = $"/sprk_invoices({FamilySupersededInvoice:D})",
        }), AsCaller(), CancellationToken.None);

        // The oracle: the guard itself, asked about the same row and the same superseded invoice.
        var guardSaid = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync(
            "sprk_invoice",
            column => column switch
            {
                "sprk_matter" => new EntityReference("sprk_matter", OrdinaryMatter),
                InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn => new EntityReference("sprk_invoice", FamilySupersededInvoice),
                _ => null,
            },
            (_, _) => Task.FromResult(InvoiceFamilyCoreRecordGuard.Check("sprk_invoice",
                column => column == "sprk_matter" ? new EntityReference("sprk_matter", SecureMatter) : null)));
        guardSaid.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        outcome.Refusal.PreWrite!.Code.Should().Be(guardSaid.RefusalCode);
        outcome.Refusal.PreWrite.Detail.Should().Be(guardSaid.Reason);
        outcome.Refusal.Code.Should().Be(guardSaid.RefusalCode);
        outcome.Refusal.Detail.Should().Be(guardSaid.Reason);
        var problem = Problem(outcome, "sprk_invoice");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Extensions["errorCode"].Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
        problem.Detail.Should().Be(guardSaid.Reason);
        AssertNothingWritten();
    }
}
