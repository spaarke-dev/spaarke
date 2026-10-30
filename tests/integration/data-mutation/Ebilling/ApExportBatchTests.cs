using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// What the AP export batch WRITES, in what order, and what it leaves behind when a write fails or races (ebilling-r1 task 150; FR-41, FR-06,
/// D-56, D-85, EB-D-148, EB-D-178). The REAL service, authorization seam, Stored File service and mapping run over an in-memory Dataverse and blob
/// store (<see cref="ApExportWorld"/>), so every assertion is on stored state. Interleavings are driven by explicit hooks, never by sleeps; the
/// one true-parallel test runs 25 rounds.
/// </summary>
public sealed class ApExportBatchTests
{
    private const string Csv = "text/csv";
    private const string Json = "application/json";

    // =========================================================================================
    // The happy path
    // =========================================================================================

    [Fact(DisplayName = "150 FR-41: a batch of Approved invoices writes a CSV and a JSON as internal-only Stored Files, snapshots the mapping, sets count and total, and sends every invoice to AP")]
    public async Task Export_WritesBothFiles_Snapshots_AndSendsTheInvoices()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A", approved: 900m);
        var b = world.AddInvoice("INV-B", approved: 450.5m, lines: 1);
        world.GrantEverything();

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        var batch = world.Batches().Should().ContainSingle().Subject;
        batch.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusCompleted);
        batch.GetAttributeValue<int>("sprk_invoicecount").Should().Be(2);
        batch.GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(1350.5m);
        batch.GetAttributeValue<string>("sprk_mappingsnapshot").Should().Be(ApExportDefaultMapping.Json);
        batch.GetAttributeValue<OptionSetValueCollection>("sprk_formats").Select(o => o.Value)
            .Should().BeEquivalentTo([ApExportService.FormatCsv, ApExportService.FormatJson]);

        foreach (var id in new[] { a.Id, b.Id })
        {
            world.StatusOfInvoice(id).Should().Be(InvoiceStatusCodes.SentToAp);
            world.BatchOfInvoice(id).Should().Be(batch.Id);
        }

        world.Lifecycle.Sent.Should().BeEquivalentTo([a.Id, b.Id]);

        // Two Stored Files: purpose Export file, parent the batch, never firm-visible, Servable, written once each.
        var files = world.StoredFiles();
        files.Should().HaveCount(2);
        foreach (var file in files)
        {
            file.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose).Value.Should().Be((int)StoredFilePurpose.ExportFile);
            file.GetAttributeValue<EntityReference>("sprk_billingexport").Id.Should().Be(batch.Id);
            file.GetAttributeValue<bool>(StoredFileSchema.FirmVisible).Should().BeFalse();
            file.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value.Should().Be((int)StoredFileStatus.Servable);
            file.Contains(StoredFileSchema.SourceDocument).Should().BeFalse("an export file has no source document, so the firm route never sees it");
            file.Contains("sprk_invoice").Should().BeFalse("an export file hangs under the batch only");
        }

        // No sprk_document is created or written, and the batch's document lookups stay unset.
        world.Dataverse.WritesTo("sprk_document").Should().Be(0);
        batch.Contains("sprk_csvdocument").Should().BeFalse();
        batch.Contains("sprk_jsondocument").Should().BeFalse();

        // The file rows' owner is asked through the acting user (194b): the batch files nothing, so requestedBy is what lets the write succeed.
        world.OwnerQuestions.Should().Contain(q => q.RequestedBy != null && q.RequestedBy.SystemUserId == ApExportWorld.CallerSystemUser);
        world.OwnerQuestions.Should().NotContain(q => q.RequestedBy == null || q.RequestedBy.SystemUserId == null,
            "every ownership question names the exporting user; without one the Stored File write fails closed");

        // The CSV: D-56 header, one row per invoice, in invoice-number order, with the values read once.
        var csv = world.TextOf(world.FileOf(batch.Id, Csv)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        csv[0].Should().StartWith("BatchId,SpaarkeInvoiceId,VendorName,VendorApId,VendorTaxId,InvoiceNumber,TrackingNumber,InvoiceDate,DueDate");
        csv.Should().HaveCount(3);
        csv[1].Should().Contain($"{batch.GetAttributeValue<string>("sprk_name")},{a.Id:D},Acme Law LLP,,,INV-A,INV-A.1,2026-09-30,,2026-09-01,2026-09-30,USD,1000.00,60.00,40.00,900.00,Matter,M-1001,\"Smith v. Jones, et al.\",M-1001,,,Pat Approver,2026-10-20,");
        csv[2].Should().Contain(",INV-B,");

        // The JSON: the same columns plus lines[] (date, type, timekeeper, hours, rate, billed, approved, codes).
        using var json = System.Text.Json.JsonDocument.Parse(world.TextOf(world.FileOf(batch.Id, Json)));
        var invoices = json.RootElement.GetProperty("invoices");
        invoices.GetArrayLength().Should().Be(2);
        invoices[0].GetProperty("InvoiceNumber").GetString().Should().Be("INV-A");
        invoices[0].GetProperty("VendorApId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        invoices[0].GetProperty("lines").GetArrayLength().Should().Be(2);
        var line = invoices[0].GetProperty("lines")[1];
        line.GetProperty("date").GetString().Should().Be("2026-09-12");
        line.GetProperty("type").GetString().Should().Be("Fee");
        line.GetProperty("hours").GetDecimal().Should().Be(3.0m);
        line.GetProperty("billed").GetDecimal().Should().Be(900m);
        line.GetProperty("taskCode").GetString().Should().Be("L110");
        invoices[1].GetProperty("lines").GetArrayLength().Should().Be(1);

        // The answer carries the batch, its files and the invoice ids.
        result.Batch!.Files.Select(f => f.Format).Should().BeEquivalentTo(["csv", "json"]);
        result.Batch.InvoiceIds.Should().BeEquivalentTo([a.Id, b.Id]);
        result.Batch.Status.Should().Be("Completed");

        // The read model agrees.
        var read = await world.Service.GetBatchAsync(batch.Id, CancellationToken.None);
        read!.Files.Should().HaveCount(2);
        read.InvoiceIds.Should().BeEquivalentTo([a.Id, b.Id]);
        read.Status.Should().Be("Completed");
    }

    [Fact(DisplayName = "150: a requested format list limits the files (csv only writes one file)")]
    public async Task Export_WithOneFormat_WritesOneFile()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();

        var result = await world.Service.CreateBatchAsync(new ApExportRequest([a.Id], ["CSV"]), world.Caller, ApExportWorld.CallerSystemUser, CancellationToken.None);

        result.Outcome.Should().Be(ApExportOutcome.Created);
        world.StoredFiles().Should().ContainSingle().Which.GetAttributeValue<string>(StoredFileSchema.ContentType).Should().Be(Csv);
    }

    [Theory(DisplayName = "150: a malformed request is refused with nothing read or written")]
    [InlineData("none")]
    [InlineData("duplicate")]
    [InlineData("empty-guid")]
    [InlineData("bad-format")]
    [InlineData("too-many")]
    public async Task InvalidRequest_Is400_AndWritesNothing(string kind)
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        var request = kind switch
        {
            "none" => new ApExportRequest([], null),
            "duplicate" => new ApExportRequest([a.Id, a.Id], null),
            "empty-guid" => new ApExportRequest([Guid.Empty], null),
            "bad-format" => new ApExportRequest([a.Id], ["xml"]),
            _ => new ApExportRequest(Enumerable.Range(0, ApExportService.MaxInvoicesPerBatch + 1).Select(_ => Guid.NewGuid()).ToList(), null),
        };

        var result = await world.Service.CreateBatchAsync(request, world.Caller, ApExportWorld.CallerSystemUser, CancellationToken.None);

        result.Outcome.Should().Be(ApExportOutcome.InvalidRequest);
        result.Code.Should().Be(ApExportCodes.InvalidRequest);
        world.Dataverse.Writes.Should().Be(0);
        world.Access.Calls.Should().BeEmpty();
    }

    // =========================================================================================
    // Selection: only Approved, not-yet-exported invoices (FR-41)
    // =========================================================================================

    [Theory(DisplayName = "150 FR-41: an In Review, Rejected, Returned to Firm, Paid, Sent to AP or already-exported invoice refuses the WHOLE request with a reason per invoice; nothing is claimed")]
    [InlineData(InvoiceStatusCodes.InReview, false, ApExportCodes.ReasonNotApproved)]
    [InlineData(InvoiceStatusCodes.Rejected, false, ApExportCodes.ReasonNotApproved)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, false, ApExportCodes.ReasonNotApproved)]
    [InlineData(InvoiceStatusCodes.Paid, false, ApExportCodes.ReasonNotApproved)]
    [InlineData(InvoiceStatusCodes.SentToAp, false, ApExportCodes.ReasonNotApproved)]
    [InlineData(InvoiceStatusCodes.Approved, true, ApExportCodes.ReasonAlreadyExported)]
    public async Task NotExportable_RefusesTheWholeRequest_NothingClaimed(int status, bool alreadyInABatch, string reason)
    {
        var world = new ApExportWorld();
        var good = world.AddInvoice("INV-GOOD");
        var bad = world.AddInvoice("INV-BAD", status: status, batch: alreadyInABatch ? Guid.NewGuid() : null);
        world.GrantEverything();

        var result = await world.ExportAsync(good, bad);

        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        result.Code.Should().Be(ApExportCodes.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(bad.Id, reason));
        world.Dataverse.Writes.Should().Be(0, "nothing is created, claimed or sent");
        world.BatchOfInvoice(good.Id).Should().BeNull();
        world.StatusOfInvoice(good.Id).Should().Be(InvoiceStatusCodes.Approved);
        world.StoredFiles().Should().BeEmpty();
    }

    [Fact(DisplayName = "150: an invoice the caller may decide but that does not exist is refused as invoice_not_found")]
    public async Task UnknownInvoice_IsRefused()
    {
        var world = new ApExportWorld();
        var good = world.AddInvoice("INV-GOOD");
        var ghost = Guid.NewGuid();
        world.GrantEverything();
        world.Access.Grant("sprk_invoices", ghost, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write);

        var result = await world.Service.CreateBatchAsync(new ApExportRequest([good.Id, ghost], null), world.Caller, ApExportWorld.CallerSystemUser, CancellationToken.None);

        result.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        result.Invoices.Should().ContainSingle().Which.Should().Be(new ApExportInvoiceRefusal(ghost, ApExportCodes.ReasonNotFound));
        world.Dataverse.Writes.Should().Be(0);
    }

    // =========================================================================================
    // Concurrency: an invoice is never in two batches (FR-41)
    // =========================================================================================

    [Fact(DisplayName = "150 FR-41: a rival that changes an invoice between the read and the claim is a 409; the batch is Failed, no invoice stays claimed, nothing is sent, no file is written")]
    public async Task RivalClaim_BetweenReadAndClaim_Is409_AndLeavesNoClaim()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var rivalBatch = Guid.NewGuid();
        world.Dataverse.Put(new Entity("sprk_billingexport", rivalBatch) { ["sprk_exportstatus"] = new OptionSetValue(ApExportService.StatusCompleted) });
        var first = a.Id.CompareTo(b.Id) < 0 ? a : b; // claims run in id order
        var second = ReferenceEquals(first, a) ? b : a;
        world.Dataverse.BeforeConditionalUpdate = (table, id) =>
        {
            if (table == "sprk_invoice" && id == second.Id && world.BatchOfInvoice(first.Id) is not null)
            {
                // The rival claims the SECOND invoice after this batch has already claimed the first.
                world.Dataverse.BeforeConditionalUpdate = null;
                world.Dataverse.Touch("sprk_invoice", second.Id, new Dictionary<string, object> { ["sprk_billingexport"] = new EntityReference("sprk_billingexport", rivalBatch) });
            }
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Conflict);
        result.Code.Should().Be(ApExportCodes.InvoiceConflict);
        world.BatchOfInvoice(first.Id).Should().BeNull("the claim this batch made on the first invoice is released");
        world.BatchOfInvoice(second.Id).Should().Be(rivalBatch, "the rival's claim is not touched");
        world.Batches().Where(b => b.Id != rivalBatch).Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the only claim left is the rival's, and its batch is Completed");
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.StatusOfInvoice(first.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Theory(DisplayName = "150 FR-41 acceptance: two concurrent exports over the same (or overlapping) invoices - exactly one wins, the other is 409 and leaves no claim; no invoice is in two batches")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoConcurrentExports_ExactlyOneWins(bool overlappingOnly)
    {
        for (var round = 0; round < 15; round++)
        {
            var world = new ApExportWorld();
            var invoices = Enumerable.Range(1, 4).Select(i => world.AddInvoice($"INV-{i}", lines: 1)).ToArray();
            world.GrantEverything();
            var first = overlappingOnly ? invoices[..3] : invoices;
            var second = overlappingOnly ? invoices[1..] : invoices;

            // Both exports read their selection (all Approved, none claimed) BEFORE either claims: each waits at its first invoice claim for the other.
            using var barrier = new Barrier(2);
            var arrivals = 0;
            world.Dataverse.BeforeConditionalUpdate = (table, _) =>
            {
                if (table == "sprk_invoice" && Interlocked.Increment(ref arrivals) <= 2)
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                }
            };

            var results = await Task.WhenAll(
                Task.Run(() => world.ExportAsync(first)),
                Task.Run(() => world.ExportAsync(second)));

            results.Count(r => r.Outcome == ApExportOutcome.Created).Should().Be(1, $"round {round}: exactly one export wins");
            results.Count(r => r.Outcome == ApExportOutcome.Conflict).Should().Be(1, $"round {round}: the other is a 409");
            var winner = world.Batches().Single(b => b.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value == ApExportService.StatusCompleted);
            var winning = results.Single(r => r.Outcome == ApExportOutcome.Created).Batch!;
            winning.BatchId.Should().Be(winner.Id);
            world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty($"round {round}: the loser left no claim");
            foreach (var invoice in invoices)
            {
                var claimedBy = world.BatchOfInvoice(invoice.Id);
                if (winning.InvoiceIds.Contains(invoice.Id))
                {
                    claimedBy.Should().Be(winner.Id);
                    world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
                }
                else
                {
                    claimedBy.Should().BeNull();
                    world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
                }
            }

            world.StoredFiles().Select(f => f.GetAttributeValue<EntityReference>("sprk_billingexport").Id).Distinct()
                .Should().BeEquivalentTo([winner.Id], $"round {round}: the loser wrote no file");
        }
    }

    [Fact(DisplayName = "150: an export that starts after another has finished is refused (400) - nothing is claimed or written")]
    public async Task SecondExportAfterTheFirst_IsRefused()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        (await world.ExportAsync(a)).Outcome.Should().Be(ApExportOutcome.Created);
        var writes = world.Dataverse.Writes;

        var again = await world.ExportAsync(a);

        again.Outcome.Should().Be(ApExportOutcome.SelectionRefused);
        world.Dataverse.Writes.Should().Be(writes);
        world.Batches().Should().ContainSingle();
    }

    [Fact(DisplayName = "150: an invoice that is cancelled after it was claimed but before it is sent stops the batch with 409; claims are released and no file is written")]
    public async Task StatusChangedAfterTheClaim_Stops_AndReleases()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var last = a.Id.CompareTo(b.Id) < 0 ? b : a;
        world.Dataverse.BeforeConditionalUpdate = (table, id) =>
        {
            // Just before the LAST claim, a rival moves the FIRST invoice out of Approved (a status write that does not touch the batch lookup).
            if (table == "sprk_invoice" && id == last.Id)
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                var firstInvoice = ReferenceEquals(last, a) ? b : a;
                world.Dataverse.Touch("sprk_invoice", firstInvoice.Id, new Dictionary<string, object> { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Cancelled) });
            }
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Conflict);
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
        world.Lifecycle.Sent.Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
    }

    // =========================================================================================
    // Failures after the claim: a Failed batch blocks nothing, nothing is half-exported
    // =========================================================================================

    [Fact(DisplayName = "150 acceptance: a file-store write failure leaves no invoice Sent to AP and no invoice claimed by a non-Completed batch")]
    public async Task UploadFailure_ReleasesEverything()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        world.Gateway.FailOp["create"] = new InvalidOperationException("storage down");

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        result.Code.Should().Be(ApExportCodes.FileFailed);
        result.BatchId.Should().NotBeNull();
        world.Lifecycle.Sent.Should().BeEmpty();
        foreach (var id in new[] { a.Id, b.Id })
        {
            world.StatusOfInvoice(id).Should().Be(InvoiceStatusCodes.Approved);
            world.BatchOfInvoice(id).Should().BeNull();
        }

        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
        world.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);

        // A Failed batch blocks nothing: the same invoices export in a new batch once the store is back.
        world.Gateway.FailOp.Clear();
        var again = await world.ExportAsync(a, b);
        again.Outcome.Should().Be(ApExportOutcome.Created);
    }

    [Fact(DisplayName = "150: when the SECOND file fails, the first file is withdrawn through the file store (row Removed, blob deleted); nothing is sent")]
    public async Task SecondFileFails_FirstIsWithdrawn()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        var creates = 0;
        world.Gateway.BeforeOp = (op, _) =>
        {
            if (op == "create" && ++creates == 2)
            {
                throw new InvalidOperationException("storage down");
            }
        };

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        var written = world.StoredFiles().Single();
        written.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value.Should().Be((int)StoredFileStatus.Removed, "a file of a failed batch is never served");
        world.Gateway.Get("billing-exports", written.GetAttributeValue<string>(StoredFileSchema.BlobPath)).Should().BeNull("its bytes are deleted");
        world.Lifecycle.Sent.Should().BeEmpty();
        world.BatchOfInvoice(a.Id).Should().BeNull();
    }

    [Fact(DisplayName = "150 acceptance: a transition failure on the first invoice leaves no invoice Sent to AP, no claim, and no servable file")]
    public async Task TransitionFailure_OnTheFirstInvoice_ReleasesEverything()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        world.Lifecycle.Fails.Add(a.Id);
        world.Lifecycle.Fails.Add(b.Id);

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        result.Code.Should().Be(ApExportCodes.TransitionFailed);
        world.Lifecycle.Sent.Should().BeEmpty();
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty();
        world.StoredFiles().Should().OnlyContain(f => f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Removed);
        foreach (var id in new[] { a.Id, b.Id })
        {
            world.StatusOfInvoice(id).Should().Be(InvoiceStatusCodes.Approved);
        }
    }

    [Fact(DisplayName = "150 known limit: a transition failure AFTER the first invoice moved cannot un-send it - 500 transition_incomplete, files withdrawn, the unsent invoice released, the sent one keeps its claim on the Failed batch")]
    public async Task TransitionFailure_AfterOneInvoiceMoved_IsReportedAsIncomplete_AndFailsSafe()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var ordered = new[] { a, b }.OrderBy(i => i.Id).ToArray();
        world.Lifecycle.Fails.Add(ordered[1].Id);

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.TransitionIncomplete);
        result.BatchId.Should().NotBeNull();
        world.StatusOfInvoice(ordered[0].Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.BatchOfInvoice(ordered[0].Id).Should().Be(result.BatchId, "the sent invoice stays attributed to the batch so it can never be exported again");
        world.StatusOfInvoice(ordered[1].Id).Should().Be(InvoiceStatusCodes.Approved);
        world.BatchOfInvoice(ordered[1].Id).Should().BeNull();
        world.StoredFiles().Should().OnlyContain(f => f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Removed,
            "no payment file exists for the Failed batch");
        world.Batches().Single().GetAttributeValue<string>("sprk_failurereason").Should().Contain(ApExportCodes.TransitionIncomplete);
    }

    [Fact(DisplayName = "150 review: a claim that COMMITS and then faults (a lost response) is still released - a Failed batch blocks nothing")]
    public async Task ClaimCommittedThenFaulted_IsStillReleased()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var faulted = false;
        world.Dataverse.FaultAfterCommit = (op, table, _) =>
        {
            if (op == "update-if" && table == "sprk_invoice" && !faulted)
            {
                faulted = true;
                return new InvalidOperationException("response lost");
            }

            return null;
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Failed);
        world.InvoicesClaimedByANonCompletedBatch().Should().BeEmpty("the claim that committed was released");
        world.BatchOfInvoice(a.Id).Should().BeNull();
        world.BatchOfInvoice(b.Id).Should().BeNull();
        world.Lifecycle.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 review: a move to Sent to AP that COMMITS and then faults is counted as sent - the claim is kept and the answer is transition_incomplete, not 'nothing was exported'")]
    public async Task TransitionCommittedThenFaulted_IsCountedAsSent()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var first = new[] { a, b }.OrderBy(i => i.Id).First();
        var other = ReferenceEquals(first, a) ? b : a;
        world.Lifecycle.FailsAfterMove.Add(first.Id);

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.TransitionIncomplete);
        world.StatusOfInvoice(first.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.BatchOfInvoice(first.Id).Should().Be(result.BatchId, "an invoice that is Sent to AP keeps its claim");
        world.BatchOfInvoice(other.Id).Should().BeNull();
        world.StatusOfInvoice(other.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "150 review: a client that disconnects while the invoices are being sent does not leave a part-sent batch - the irreversible step ignores the request's cancellation")]
    public async Task CancellationDuringTheSendStep_DoesNotSplitTheBatch()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        var c = world.AddInvoice("INV-C");
        world.GrantEverything();
        using var cts = new CancellationTokenSource();
        world.Lifecycle.AfterMove = _ => cts.Cancel(); // the client goes away after the first invoice moved

        var result = await world.Service.CreateBatchAsync(ApExportWorld.Select(a, b, c), world.Caller, ApExportWorld.CallerSystemUser, cts.Token);

        result.Outcome.Should().Be(ApExportOutcome.Created);
        world.Lifecycle.Sent.Should().HaveCount(3);
        world.Batches().Single().GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusCompleted);
    }

    [Fact(DisplayName = "150: when the batch cannot be marked Completed the invoices stay sent and the files stay, nothing is withdrawn or released, the answer is completion_incomplete, and the batch read advertises no file")]
    public async Task CompletionFault_IsReportedAsIncomplete_AndWithdrawsNothing()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Dataverse.FaultOn = (op, table, _) => op == "update" && table == "sprk_billingexport" ? new InvalidOperationException("down") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.CompletionIncomplete);
        world.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        world.BatchOfInvoice(a.Id).Should().Be(result.BatchId);
        world.StoredFiles().Should().OnlyContain(f => f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Servable);
        world.Batches().Single().GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusGenerating);

        world.Dataverse.FaultOn = null;
        var read = await world.Service.GetBatchAsync(result.BatchId!.Value, CancellationToken.None);
        read!.Status.Should().Be("Generating");
        read.Files.Should().BeEmpty("a file of a batch that is not Completed is never advertised");
    }

    [Fact(DisplayName = "150: a compensation that cannot release a claim is reported as compensation_incomplete (the batch is still marked Failed; the invoice stays blocked for an operator)")]
    public async Task CompensationThatCannotRelease_IsReportedAsIncomplete()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Gateway.BeforeOp = (op, _) =>
        {
            if (op == "create")
            {
                // The store fails, and from now on a claim cannot be released.
                world.Dataverse.FaultOn = (o, table, _) => o == "update-if" && table == "sprk_invoice" ? new InvalidOperationException("down") : null;
                throw new InvalidOperationException("storage down");
            }
        };

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Incomplete);
        result.Code.Should().Be(ApExportCodes.CompensationIncomplete);
        world.Batches().Single().GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        world.BatchOfInvoice(a.Id).Should().Be(result.BatchId, "the claim could not be released and is reported, not hidden");
    }

    [Fact(DisplayName = "150: a json-only request writes one JSON file and records json as the batch's only format")]
    public async Task JsonOnly_WritesOneFile()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();

        var result = await world.Service.CreateBatchAsync(new ApExportRequest([a.Id], ["json"]), world.Caller, ApExportWorld.CallerSystemUser, CancellationToken.None);

        result.Outcome.Should().Be(ApExportOutcome.Created);
        world.StoredFiles().Should().ContainSingle().Which.GetAttributeValue<string>(StoredFileSchema.ContentType).Should().Be(Json);
        world.Batches().Single().GetAttributeValue<OptionSetValueCollection>("sprk_formats").Select(o => o.Value).Should().Equal(ApExportService.FormatJson);
    }

    [Fact(DisplayName = "150: an owner resolver that throws is a clean 'unavailable' with nothing written, not an unhandled error")]
    public async Task OwnerResolverThrowing_IsUnavailable()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.OwnerThrows = true;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        world.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "150: the batch row cannot be created - 503, nothing claimed or sent")]
    public async Task BatchCreateFault_WritesNothing()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Dataverse.FaultOn = (op, table, _) => op == "create" && table == "sprk_billingexport" ? new InvalidOperationException("down") : null;

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        world.BatchOfInvoice(a.Id).Should().BeNull();
        world.Lifecycle.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "150: a file store that is not configured fails with a stable code BEFORE any claim")]
    public async Task FileStoreNotConfigured_FailsBeforeAnyClaim()
    {
        var world = new ApExportWorld(fileStoreConfigured: false);
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        result.Code.Should().Be(ApExportCodes.Unavailable);
        world.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "150: no owner for the batch - refused before the batch or any claim is written")]
    public async Task OwnerRefused_WritesNothing()
    {
        var world = new ApExportWorld { OwnerRefused = true };
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Unavailable);
        world.Dataverse.Writes.Should().Be(0);
    }

    // =========================================================================================
    // The mapping is data (FR-41, D-56)
    // =========================================================================================

    [Fact(DisplayName = "150 FR-41: changing the settings mapping changes the NEXT batch with no code change; the earlier batch's files and snapshot are unchanged, also after the core record is renamed")]
    public async Task MappingChange_AffectsOnlyTheNextBatch()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();

        var first = await world.ExportAsync(a);
        var firstBatch = first.Batch!.BatchId;
        var firstCsv = world.TextOf(world.FileOf(firstBatch, Csv));
        var firstJson = world.TextOf(world.FileOf(firstBatch, Json));
        var firstSnapshot = world.Dataverse.Get("sprk_billingexport", firstBatch).GetAttributeValue<string>("sprk_mappingsnapshot");

        // The settings change (renamed column, different order, a subset, a GL column added later is a data change too), and the core record is renamed.
        world.MappingJson = """
            {"version":1,
             "columns":[{"name":"Invoice","source":"invoice.firmInvoiceNumber"},{"name":"Amount","source":"invoice.approvedTotal","format":"decimal"},{"name":"Matter","source":"record.name"}],
             "lineColumns":[{"name":"hrs","source":"line.hours","format":"decimal"}]}
            """;
        world.Dataverse.Touch("sprk_invoice", a.Id, new Dictionary<string, object> { ["sprk_regardingrecordname"] = "Renamed Matter" });
        world.Dataverse.Touch("sprk_organization", ApExportWorld.FirmId, new Dictionary<string, object> { ["sprk_organizationname"] = "Renamed Firm" });

        var second = await world.ExportAsync(b);

        second.Outcome.Should().Be(ApExportOutcome.Created);
        var secondCsv = world.TextOf(world.FileOf(second.Batch!.BatchId, Csv));
        secondCsv.Should().StartWith("Invoice,Amount,Matter\r\nINV-B,900.00,\"Smith v. Jones, et al.\"");
        world.Dataverse.Get("sprk_billingexport", second.Batch.BatchId).GetAttributeValue<string>("sprk_mappingsnapshot").Should().Be(world.MappingJson, "the snapshot is the settings text verbatim");

        world.TextOf(world.FileOf(firstBatch, Csv)).Should().Be(firstCsv, "a past batch file is never regenerated");
        world.TextOf(world.FileOf(firstBatch, Json)).Should().Be(firstJson);
        world.Dataverse.Get("sprk_billingexport", firstBatch).GetAttributeValue<string>("sprk_mappingsnapshot").Should().Be(firstSnapshot);
        firstCsv.Should().Contain("Acme Law LLP").And.Contain("Smith v. Jones").And.NotContain("Renamed");
    }

    [Fact(DisplayName = "150: a mapping with an unknown source is refused (422) before the batch is created - a typo never writes an empty AP column")]
    public async Task InvalidMapping_IsRefused_BeforeAnyWrite()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.MappingJson = """{"version":1,"columns":[{"name":"X","source":"invoice.nope"}]}""";

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.MappingInvalid);
        result.Code.Should().Be(ApExportCodes.MappingInvalid);
        world.Dataverse.Writes.Should().Be(0);
    }

    // =========================================================================================
    // Candidates and the read model
    // =========================================================================================

    [Fact(DisplayName = "150 D-85: the candidates list shows only Approved, un-batched invoices the caller may export; a secure invoice the caller cannot decide is excluded")]
    public async Task Candidates_AreApprovedUnbatchedAndExportable()
    {
        var world = new ApExportWorld();
        var ok = world.AddInvoice("INV-OK");
        world.AddInvoice("INV-REVIEW", status: InvoiceStatusCodes.InReview);
        world.AddInvoice("INV-BATCHED", batch: Guid.NewGuid());
        world.GrantEverything();
        var secure = world.AddInvoice("INV-SECURE", grant: false); // added after the grant: the caller holds no decide right on it

        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        candidates!.Candidates.Select(c => c.InvoiceId).Should().BeEquivalentTo([ok.Id]);
        candidates.Candidates.Should().NotContain(c => c.InvoiceId == secure.Id);
        candidates.Candidates.Single().Should().Match<ApExportCandidateDto>(c => c.FirmInvoiceNumber == "INV-OK" && c.ApprovedTotal == 900m && c.Currency == "USD");
    }

    [Fact(DisplayName = "150: a rights fault while listing candidates shows nothing (null), never the unfiltered list")]
    public async Task Candidates_OnARightsFault_ShowNothing()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Access.Grant("sprk_invoices", a.Id, Spaarke.Dataverse.AccessRights.None);
        world.Access.Fault("sprk_invoices", a.Id); // Dataverse gave no answer (rights None, Faulted): an unknown, not a denial

        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        candidates.Should().BeNull();
    }

    [Fact(DisplayName = "150: an unknown batch reads as null")]
    public async Task UnknownBatch_ReadsAsNull()
    {
        var world = new ApExportWorld();

        (await world.Service.GetBatchAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }
}
