using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 130a (FR-38, D-66, EB-D-202): <see cref="FirmFindingsProjection"/>. The Dataverse seam is
/// <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>, answered from fixture rows by the query's entity
/// WITHOUT evaluating its filter and with only the requested columns, so a stray row or a forgotten column shows. The clause seam is
/// <see cref="FirmClauseReader"/>, a fake that counts its calls.
/// </summary>
public sealed class FirmFindingsProjectionTests
{
    private static readonly Guid SubmissionId = Guid.Parse("20000000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
    private static readonly Guid OtherInvoiceId = Guid.Parse("20000000-0000-4000-8000-0000000000b9");
    private static readonly Guid PolicyA = Guid.Parse("20000000-0000-4000-8000-0000000000d1");
    private static readonly Guid PolicyB = Guid.Parse("20000000-0000-4000-8000-0000000000d3");
    private static readonly Guid VersionA = Guid.Parse("20000000-0000-4000-8000-0000000000d2");
    private static readonly Guid VersionB = Guid.Parse("20000000-0000-4000-8000-0000000000d4");

    private const string ClauseQuote = "Travel time is billed at half rate.";
    private const string ClauseSection = "Section 4.2";
    private const string FirmLabel = "Acme Billing Guidelines";

    private static Guid LineId(int sequence) => InvoiceSubmissionPersister.LineIdFor(SubmissionId, sequence - 1);

    private sealed class World
    {
        public readonly List<Entity> Invoices = [];
        public readonly List<Entity> Lines = [];
        public readonly List<Entity> Records = [];
        public readonly List<Entity> Signals = [];
        public readonly List<Entity> Versions = [];
        public readonly List<Entity> Policies = [];
        public readonly List<Entity> Submissions = [];
        public readonly List<QueryExpression> Queries = [];
        public readonly List<Guid> ClauseCalls = [];
        public readonly HashSet<string> FaultingEntities = [];
        public Func<Guid, FirmClauseText> Clause = _ => FirmClauseText.None;
        public bool ClauseFault;
        public readonly Mock<IGenericEntityService> Entities = new(MockBehavior.Strict);

        public World(int status = InvoiceStatusCodes.InReview)
        {
            Invoices.Add(new Entity("sprk_invoice", InvoiceId)
            {
                ["sprk_trackingnumber"] = "TRK-1",
                ["sprk_totalamount"] = new Money(1000m),
                ["sprk_approvedtotal"] = new Money(900m),
                ["statuscode"] = new OptionSetValue(status),
            });
            Versions.Add(Version(VersionA, PolicyA, 3));
            Versions.Add(Version(VersionB, PolicyB, 1));
            Policies.Add(Policy(PolicyA, "Travel time"));
            Policies.Add(Policy(PolicyB, "Block billing"));

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    lock (Queries)
                    {
                        Queries.Add(query);
                    }

                    if (FaultingEntities.Contains(query.EntityName))
                    {
                        throw new InvalidOperationException($"{query.EntityName} is down");
                    }

                    var rows = query.EntityName switch
                    {
                        "sprk_invoice" => Invoices,
                        "sprk_billingevent" => Lines,
                        "sprk_decisionrecord" => Records.Where(r => SatisfiesWriterConditions(r, query.Criteria)).ToList(),
                        "sprk_signal" => Signals,
                        "sprk_policyversion" => Versions,
                        "sprk_policy" => Policies,
                        "sprk_invoicesubmission" => Submissions,
                        _ => throw new InvalidOperationException($"Unexpected read of {query.EntityName}"),
                    };
                    return new EntityCollection(rows.Select(r => Requested(r, query.ColumnSet)).ToList());
                });
        }

        public int Reads(string entity) => Queries.Count(q => q.EntityName == entity);

        /// <summary>EB-D-208: the BFF writer identity every Decision Record is created by (<c>createdby</c>); a record from anyone else is a user's.</summary>
        public Guid WriterId = Guid.Parse("20000000-0000-4000-8000-0000000000c1");

        /// <summary>Every time the projection asked for the writer id.</summary>
        public int WriterCalls;

        /// <summary>Non-null: asking for the writer id throws this (the writer connection is down).</summary>
        public Exception? WriterFault;

        /// <summary>When set, the writer id the projection is told (default <see cref="WriterId"/>); <see cref="Guid.Empty"/> is an unknown writer.</summary>
        public Guid? WriterIdAnswered;

        public FirmFindingsProjection Projection() => new(
            Entities.Object,
            (version, invoice, ct) =>
            {
                ClauseCalls.Add(version);
                invoice.Should().Be(InvoiceId);
                return ClauseFault ? Task.FromException<FirmClauseText>(new InvalidOperationException("clause down")) : Task.FromResult(Clause(version));
            },
            ct =>
            {
                WriterCalls++;
                return WriterFault is { } fault ? Task.FromException<Guid>(fault) : Task.FromResult(WriterIdAnswered ?? WriterId);
            },
            NullLogger<FirmFindingsProjection>.Instance);

        /// <summary>
        /// The store applies the query's writer rule (<see cref="WriterCreatedRecord"/>: <c>createdby</c> equal to the writer, <c>createdonbehalfby</c>
        /// null) to the stored row, as Dataverse does. Every OTHER condition is left alone (the double never evaluated them), so a projection that
        /// drops the writer rule from its query returns the user's record and shows.
        /// </summary>
        private static bool SatisfiesWriterConditions(Entity row, FilterExpression criteria)
        {
            foreach (var condition in criteria.Conditions)
            {
                if (condition.AttributeName == "createdby" && condition.Operator == ConditionOperator.Equal
                    && row.GetAttributeValue<EntityReference>("createdby")?.Id != (Guid)condition.Values[0])
                {
                    return false;
                }

                if (condition.AttributeName == "createdonbehalfby" && condition.Operator == ConditionOperator.Null
                    && row.GetAttributeValue<EntityReference>("createdonbehalfby") is not null)
                {
                    return false;
                }
            }

            return true;
        }

        private static Entity Requested(Entity row, ColumnSet columns)
        {
            var projected = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Columns.Where(row.Contains))
            {
                projected[column] = row[column];
            }

            return projected;
        }

        public Entity AddLine(
            int sequence, InvoiceLineStatus status, decimal amount = 100m, decimal? approved = null,
            InvoiceLineCostType type = InvoiceLineCostType.Fee, string? description = "Draft the response")
        {
            var line = new Entity("sprk_billingevent", LineId(sequence))
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_name"] = $"Line {sequence}",
                ["sprk_linesequence"] = sequence,
                ["sprk_costtype"] = new OptionSetValue((int)type),
                ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ["sprk_timekeeper"] = "J. Smith",
                ["sprk_amount"] = new Money(amount),
                ["sprk_description"] = description,
                ["sprk_linestatus"] = new OptionSetValue((int)status),
                ["sprk_reviewedby"] = new EntityReference("systemuser", Guid.NewGuid()) { Name = "Rita Reviewer" },
            };
            if (approved is not null)
            {
                line["sprk_approvedamount"] = new Money(approved.Value);
            }

            Lines.Add(line);
            return line;
        }

        /// <summary>A line a reviewer added: the id and marker Add line writes.</summary>
        public Entity AddReviewerLine(Guid decisionId, decimal amount = 75m)
        {
            var line = new Entity("sprk_billingevent", ExtractionProvenance.AddedLineIdFor(InvoiceId, decisionId))
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_linesequence"] = 99,
                ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
                ["sprk_amount"] = new Money(amount),
                ["sprk_description"] = "Added by the reviewer",
                ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted),
                ["sprk_extractionconfidence"] = ExtractionProvenance.ForReviewerAddedLine(decisionId),
            };
            Lines.Add(line);
            return line;
        }

        public Entity AddAdjustment(int sequence, string reason, Guid? rule, decimal amount = -50m, decimal value = 5m)
        {
            var line = new Entity("sprk_billingevent", LineId(sequence))
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_linesequence"] = sequence,
                ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
                ["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Percent),
                ["sprk_adjustmentvalue"] = value,
                ["sprk_amount"] = new Money(amount),
                ["sprk_adjustmentreason"] = reason,
            };
            if (rule is not null)
            {
                line["sprk_adjustmentrule"] = new EntityReference("sprk_policyversion", rule.Value);
            }

            Lines.Add(line);
            return line;
        }

        public Entity AddRecord(
            string code, Guid? line, string? reason, Guid? invoice = null, DateTime? decidedOn = null, Guid? id = null,
            Guid? createdBy = null, Guid? onBehalfOf = null)
        {
            var record = new Entity("sprk_decisionrecord", id ?? Guid.NewGuid())
            {
                ["sprk_actioncode"] = code,
                ["sprk_reason"] = reason,
                ["sprk_decidedon"] = decidedOn ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                ["createdby"] = new EntityReference("systemuser", createdBy ?? WriterId),
            };
            if (onBehalfOf is not null)
            {
                record["createdonbehalfby"] = new EntityReference("systemuser", onBehalfOf.Value);
            }

            if (line is not null)
            {
                record["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line.Value);
            }

            if (invoice is not null)
            {
                record["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice.Value);
            }

            Records.Add(record);
            return record;
        }

        public Entity AddSignal(Guid? record, Guid? version, string? firmReason, Guid? line = null, Guid? invoice = null)
        {
            var signal = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_firmreason"] = firmReason,
                ["createdon"] = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            };
            if (record is not null)
            {
                signal["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", record.Value);
            }

            if (version is not null)
            {
                signal["sprk_policyversion"] = new EntityReference("sprk_policyversion", version.Value);
            }

            if (line is not null)
            {
                signal["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line.Value);
            }

            if (invoice is not null)
            {
                signal["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice.Value);
            }

            Signals.Add(signal);
            return signal;
        }

        public Entity AddSubmission(int status, string tracking = "TRK-1")
        {
            var submission = new Entity("sprk_invoicesubmission", SubmissionId)
            {
                ["sprk_processingstatus"] = new OptionSetValue(status),
                ["sprk_trackingnumber"] = tracking,
            };
            Submissions.Add(submission);
            return submission;
        }

        /// <summary>A Reduced line decided with a reason, its record and one Signal that record resolved.</summary>
        public (Entity Line, Entity Record) AddReducedWithCitation(
            int sequence, string reason, Guid version, string? firmReason, decimal amount = 100m, decimal approved = 60m)
        {
            var line = AddLine(sequence, InvoiceLineStatus.Reduced, amount, approved);
            var record = AddRecord("reduce-line", line.Id, reason, InvoiceId);
            AddSignal(record.Id, version, firmReason, line.Id, InvoiceId);
            return (line, record);
        }

        private static Entity Version(Guid id, Guid policy, int number) => new("sprk_policyversion", id)
        {
            ["sprk_policy"] = new EntityReference("sprk_policy", policy),
            ["sprk_versionnumber"] = number,
        };

        private static Entity Policy(Guid id, string name) => new("sprk_policy", id)
        {
            ["sprk_name"] = name,
            ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        };
    }

    private static readonly FirmClauseText FlagOnText = new(ClauseQuote, ClauseSection, FirmLabel);

    // ── Contract: In Review (EB-D-202) ──

    [Fact]
    public async Task InReview_ProjectsDecidedAndAddedLinesWithReasons_AndNoApprovedTotal()
    {
        var world = new World();
        var reduced = world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.", 100m, 60m);
        var disallowed = world.AddLine(2, InvoiceLineStatus.Disallowed, 80m, 0m);
        world.AddRecord("disallow-line", disallowed.Id, "Block billing.", InvoiceId);
        var decisionId = Guid.Parse("20000000-0000-4000-8000-0000000000a1");
        var added = world.AddReviewerLine(decisionId);
        world.AddRecord("add-line", added.Id, "Work the firm omitted.", InvoiceId);
        world.AddAdjustment(5, "Volume discount.", null);
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().NotBeNull();
        outcome!.Kind.Should().Be(FirmOutcomeKind.InvoiceInReview);
        outcome.StatusLabel.Should().Be("In review");
        outcome.TrackingNumber.Should().Be("TRK-1");
        outcome.BilledTotal.Should().Be(1000m);
        outcome.ApprovedTotal.Should().BeNull("an invoice In Review has no approved total, even when the column holds one");
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Select(i => i.Decision).Should().Equal(FirmOutcomeDecision.Reduced, FirmOutcomeDecision.Disallowed, FirmOutcomeDecision.Added);
        outcome.Items.Should().NotContain(i => i.Decision == FirmOutcomeDecision.Adjustment, "invoice-level adjustments are not part of the In Review view");

        var first = outcome.Items[0];
        first.LineId.Should().Be(reduced.Line.Id);
        first.Reason.Should().Be("Rate exceeds the agreed rate.");
        first.Line.Should().BeEquivalentTo(new FirmOutcomeLine(new DateOnly(2026, 9, 1), "J. Smith", "Draft the response", 100m, 60m));
        outcome.Items[1].Reason.Should().Be("Block billing.");
        outcome.Items[1].Line!.ApprovedAmount.Should().Be(0m);
        outcome.Items[2].Reason.Should().Be("Work the firm omitted.");
        outcome.Items[2].LineId.Should().Be(added.Id);
    }

    [Fact]
    public async Task NeverProjected_PendingFlaggedCleanAccepted_NorAForgedReviewerMarker_NorAnotherInvoicesLine_NorAnUnresolvedSignal()
    {
        var world = new World();
        world.AddLine(1, InvoiceLineStatus.Pending);
        world.AddLine(2, InvoiceLineStatus.Flagged);
        world.AddLine(3, InvoiceLineStatus.Clean);
        var accepted = world.AddLine(4, InvoiceLineStatus.Accepted);
        // An AI observation lives in the line's extraction JSON; the line stays Clean.
        var observed = world.AddLine(5, InvoiceLineStatus.Clean);
        observed["sprk_extractionconfidence"] = "{\"coding\":{\"status\":\"Coded\",\"observations\":[{\"type\":\"Vague\",\"quotedText\":\"misc\"}]}}";
        // The reviewer marker with an id that does not derive from the invoice and the decision: an ordinary line (EB-D-193).
        var forged = world.AddLine(6, InvoiceLineStatus.Accepted);
        forged["sprk_extractionconfidence"] = ExtractionProvenance.ForReviewerAddedLine(Guid.NewGuid());
        // A Reduced line of another invoice, returned by a stray query answer.
        var stray = world.AddLine(7, InvoiceLineStatus.Reduced);
        stray["sprk_invoice"] = new EntityReference("sprk_invoice", OtherInvoiceId);
        // An overridden-submission record and Signals no decision resolved.
        world.AddRecord("override-submission", accepted.Id, "Overridden.", InvoiceId);
        world.AddSignal(null, VersionA, "An open finding.", accepted.Id, InvoiceId);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().BeEmpty();
        world.Reads("sprk_decisionrecord").Should().Be(0, "no line was decided, so no reason is read");
        world.Reads("sprk_signal").Should().Be(0);
        world.ClauseCalls.Should().BeEmpty();
    }

    // ── Contract: submissions ──

    [Theory]
    [InlineData((int)InvoiceSubmissionStatus.Received, "Received")]
    [InlineData((int)InvoiceSubmissionStatus.Processing, "Processing")]
    public async Task SubmissionStillReceivedOrProcessing_ProjectsStatusAndBilledTotalOnly(int status, string label)
    {
        var world = new World();
        world.AddSubmission(status);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.SubmissionInProgress);
        outcome.StatusLabel.Should().Be(label);
        outcome.TrackingNumber.Should().Be("TRK-1");
        outcome.BilledTotal.Should().Be(1000m, "the submission's own invoice, once it exists");
        outcome.ApprovedTotal.Should().BeNull();
        outcome.Items.Should().BeEmpty();
        world.Reads("sprk_billingevent").Should().Be(0);
        world.Reads("sprk_decisionrecord").Should().Be(0);
        world.Reads("sprk_signal").Should().Be(0);
    }

    [Fact]
    public async Task SubmissionWithNoInvoiceYet_HasNoBilledTotal()
    {
        var world = new World();
        world.Invoices.Clear();
        world.AddSubmission((int)InvoiceSubmissionStatus.Processing);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.SubmissionInProgress);
        outcome.BilledTotal.Should().BeNull();
    }

    [Fact]
    public async Task AcceptedSubmission_ProjectsItsInvoicesOutcome()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);
        world.AddReducedWithCitation(1, "Reason.", VersionA, null);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.InvoiceInReview);
        outcome.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RejectedSubmission_ProjectsStatusOnly_AndFailedOrMissingProjectsNothing()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Rejected);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None))!
            .Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));

        world.Submissions[0]["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None))!.Kind.Should().Be(FirmOutcomeKind.Unavailable);

        world.Submissions.Clear();
        (await world.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None)).Should().BeNull();
        (await world.Projection().GetForInvoiceAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    // ── Contract: decided states and invoice-level adjustments ──

    [Fact]
    public async Task Approved_ProjectsTheApprovedTotal_TheLinesAndTheAdjustmentWithItsReasonAndRule()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.AddAdjustment(5, "Volume discount.", VersionB, amount: -50m, value: 5m);
        world.Clause = v => v == VersionA ? FlagOnText : FirmClauseText.None;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.ApprovedWithAdjustments);
        outcome.StatusLabel.Should().Be("Approved");
        outcome.ApprovedTotal.Should().Be(900m);
        outcome.Items.Select(i => i.Decision).Should().Equal(FirmOutcomeDecision.Reduced, FirmOutcomeDecision.Adjustment);
        var adjustment = outcome.Items[1];
        adjustment.LineId.Should().BeNull();
        adjustment.Line.Should().BeNull();
        adjustment.Adjustment.Should().BeEquivalentTo(new FirmOutcomeAdjustment("Percent", 5m, -50m));
        adjustment.Reason.Should().Be("Volume discount.");
        adjustment.Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FirmOutcomeCitation("Block billing", null, null, null, null, null), "version B's clause is not visible to firms");
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Approved, FirmOutcomeKind.Approved)]
    [InlineData(InvoiceStatusCodes.SentToAp, FirmOutcomeKind.SentToAp)]
    [InlineData(InvoiceStatusCodes.Paid, FirmOutcomeKind.Paid)]
    public async Task DecidedStatesWithNoItems_ProjectTheirKindAndTheApprovedTotal(int status, FirmOutcomeKind kind)
    {
        var world = new World(status);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Kind.Should().Be(kind);
        outcome.ApprovedTotal.Should().Be(900m);
        outcome.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Rejected, FirmOutcomeKind.Rejected)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmOutcomeKind.ReturnedToFirm)]
    public async Task RejectedOrReturned_ProjectStatusAndBilledTotalOnly_NoReasonIsRead(int status, FirmOutcomeKind kind)
    {
        var world = new World(status);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Kind.Should().Be(kind);
        outcome.BilledTotal.Should().Be(1000m);
        outcome.ApprovedTotal.Should().BeNull();
        outcome.Items.Should().BeEmpty();
        world.Reads("sprk_decisionrecord").Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Cancelled)]
    [InlineData(InvoiceStatusCodes.Superseded)]
    [InlineData(12345)]
    public async Task ClosedStatesThisContractDoesNotCover_AndUnknownCodes_ProjectNothing(int status)
    {
        var world = new World(status);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Unavailable, null, "Unavailable", null, null, [], true));
    }

    // ── Visibility rule (D-66, D-79) ──

    [Fact]
    public async Task FlagOff_ACitationCarriesTheRuleNameAndTheFirmReasonOnly()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.Clause = _ => FirmClauseText.None; // the rule set's flag is off (or the version was not applied)

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FirmOutcomeCitation("Travel time", "Your agreed rate is lower.", null, null, null, null));
    }

    [Fact]
    public async Task FlagOn_ACitationCarriesTheLabelVersionSectionAndQuote_AndTheRuleNameIsThePolicysNotTheRuleSets()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Single().Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FirmOutcomeCitation("Travel time", "Your agreed rate is lower.", FirmLabel, 3, ClauseSection, ClauseQuote));
        world.Reads("sprk_ruleset").Should().Be(0, "the rule set (and so its name) is never read");
    }

    [Fact]
    public async Task FlagOn_ALegacyRuleSetHasNoLabel_ButStillShowsVersionSectionAndQuote()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, null);
        world.Clause = _ => new FirmClauseText(ClauseQuote, null, null);

        var citation = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Citations.Single();

        citation.Label.Should().BeNull();
        citation.Version.Should().Be(3);
        citation.Quote.Should().Be(ClauseQuote);
        citation.SectionReference.Should().BeNull();
    }

    [Fact]
    public async Task ANullFirmReason_IsOmitted_AndNothingIsSubstitutedForIt()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, firmReason: null);
        world.Signals[0]["sprk_message"] = "Internal Signal sentence with confidence 0.42"; // never selected
        world.Clause = _ => FlagOnText;

        var citation = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Citations.Single();

        citation.FirmReason.Should().BeNull();
        JsonSerializer.Serialize(citation).Should().NotContain("Internal Signal").And.NotContain("0.42");
        world.Queries.Where(q => q.EntityName == "sprk_signal").Should().OnlyContain(q => !q.ColumnSet.Columns.Contains("sprk_message"));
    }

    [Fact]
    public async Task ABlankFirmReason_IsOmitted()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, firmReason: "   ");

        (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Citations.Single().FirmReason.Should().BeNull();
    }

    [Fact]
    public async Task ARuleThatIsNotABillingRule_IsNotCited()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.Policies.Single(p => p.Id == PolicyA)["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainOntology);

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.Citations.Should().BeEmpty();
        item.Reason.Should().Be("Reason.");
        world.ClauseCalls.Should().BeEmpty();
    }

    // ── Clause lookups ──

    [Fact]
    public async Task OneClauseLookupPerDistinctPolicyVersionPerInvoice()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "R1.", VersionA, "F1.");
        world.AddReducedWithCitation(2, "R2.", VersionA, "F2.");
        var third = world.AddReducedWithCitation(3, "R3.", VersionB, null);
        world.AddSignal(third.Record.Id, VersionA, "F3.", third.Line.Id, InvoiceId); // a second Signal of the same record, same version as lines 1 and 2
        world.AddAdjustment(5, "Volume discount.", VersionA);
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().HaveCount(4);
        world.ClauseCalls.Should().BeEquivalentTo(new[] { VersionA, VersionB }, "two versions are cited by five citations");
        world.ClauseCalls.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ARuleCitedByTwoSignalsOfOneDecision_IsOneCitation()
    {
        var world = new World();
        var pair = world.AddReducedWithCitation(1, "R.", VersionA, null);
        world.AddSignal(pair.Record.Id, VersionA, "Second reason.", pair.Line.Id, InvoiceId);

        var citations = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Citations;

        citations.Should().ContainSingle().Which.FirmReason.Should().Be("Second reason.", "the first Signal with a firm reason supplies it");
    }

    // ── Decision Record link check (EB-D-202) ──

    [Fact]
    public async Task ARecordWithoutThisInvoicesLineLink_IsNeverProjected()
    {
        var world = new World();
        var line1 = world.AddLine(1, InvoiceLineStatus.Reduced, 100m, 60m);
        var line2 = world.AddLine(2, InvoiceLineStatus.Reduced, 100m, 60m);
        var line3 = world.AddLine(3, InvoiceLineStatus.Reduced, 100m, 60m);
        var line4 = world.AddLine(4, InvoiceLineStatus.Reduced, 100m, 60m);
        world.AddRecord("reduce-line", null, "No link at all.");
        world.AddRecord("reduce-line", null, "Invoice link only.", InvoiceId);
        world.AddRecord("reduce-line", Guid.NewGuid(), "A line of another invoice.", InvoiceId);
        world.AddRecord("reduce-line", line1.Id, "Another invoice's record.", OtherInvoiceId);
        world.AddRecord("disallow-line", line2.Id, "The wrong decision for a Reduced line.", InvoiceId);
        world.AddRecord("override-submission", line3.Id, "Not a line decision.", InvoiceId);
        // The linked reduce record, on line 4 only.
        world.AddRecord("reduce-line", line4.Id, "Linked reason.", InvoiceId);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Select(i => i.Reason).Should().Equal(null, null, null, "Linked reason.");
    }

    // ── Decision Record authenticity: only the writer's records (EB-D-208, the rule 165b applies; 130b) ──

    [Fact(DisplayName = "130b must-fire: a linked record the writer did NOT create (a user's, or one created on the writer's behalf) is never projected; its line stays with no reason")]
    public async Task ALinkedRecordTheWriterDidNotCreate_IsNeverProjected_AndItsLineStaysWithNoReason()
    {
        var world = new World();
        var byUser = world.AddLine(1, InvoiceLineStatus.Reduced, 100m, 60m);
        var onBehalf = world.AddLine(2, InvoiceLineStatus.Disallowed, 100m, 0m);
        var byWriter = world.AddLine(3, InvoiceLineStatus.Reduced, 100m, 60m);
        var userCreator = Guid.Parse("20000000-0000-4000-8000-0000000000c9");
        world.AddRecord("reduce-line", byUser.Id, "A user forged this.", InvoiceId, createdBy: userCreator);
        world.AddRecord("disallow-line", onBehalf.Id, "Created for the writer by someone else.", InvoiceId, onBehalfOf: userCreator);
        world.AddRecord("reduce-line", byWriter.Id, "The server decided this.", InvoiceId);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Select(i => (i.LineId, i.Reason)).Should().Equal(
            (byUser.Id, (string?)null), (onBehalf.Id, (string?)null), (byWriter.Id, "The server decided this."));
        outcome.Degraded.Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(outcome).Should().NotContain("forged").And.NotContain("someone else");
    }

    [Fact(DisplayName = "130b must-not-fire: a record the writer created projects its reason, whatever its action code (add, reduce, disallow)")]
    public async Task ARecordTheWriterCreated_ProjectsItsReason()
    {
        var world = new World();
        var reduced = world.AddLine(1, InvoiceLineStatus.Reduced, 100m, 60m);
        var disallowed = world.AddLine(2, InvoiceLineStatus.Disallowed, 100m, 0m);
        var decisionId = Guid.NewGuid();
        var added = world.AddReviewerLine(decisionId);
        world.AddRecord("reduce-line", reduced.Id, "Reduce reason.", InvoiceId);
        world.AddRecord("disallow-line", disallowed.Id, "Disallow reason.", InvoiceId);
        world.AddRecord("add-line", added.Id, "Add reason.", InvoiceId);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Select(i => i.Reason).Should().BeEquivalentTo("Reduce reason.", "Disallow reason.", "Add reason.");
        world.WriterCalls.Should().Be(1, "the writer id is asked once per projection, however many lines there are");
    }

    [Theory(DisplayName = "130b: a writer id that cannot be obtained (a fault, or an empty id) gives the status only, Degraded; no record is read")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWriterIdThatCannotBeObtained_GivesTheStatusOnly_Degraded(bool emptyId)
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        if (emptyId)
        {
            world.WriterIdAnswered = Guid.Empty;
        }
        else
        {
            world.WriterFault = new InvalidOperationException("The writer connection is down.");
        }

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.InvoiceInReview, null, "In review", null, null, [], true));
        world.Reads("sprk_decisionrecord").Should().Be(0, "without a writer identity no record can be told from a user's, so none is read");
    }

    [Fact(DisplayName = "130b: with no decided or added lines the writer id is never asked (Accepted, Pending and Flagged lines only)")]
    public async Task WithNoDecidedLines_TheWriterIdIsNeverAsked()
    {
        var world = new World();
        world.AddLine(1, InvoiceLineStatus.Accepted);
        world.AddLine(2, InvoiceLineStatus.Pending);
        world.AddLine(3, InvoiceLineStatus.Flagged);
        world.WriterFault = new InvalidOperationException("Must not be asked.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().BeEmpty();
        outcome.Degraded.Should().BeFalse();
        world.WriterCalls.Should().Be(0);
    }

    [Fact(DisplayName = "130b: an invoice with only an invoice-level adjustment (its reason is on the line, not in a record) never asks for the writer id")]
    public async Task AnAdjustmentOnly_NeverAsksForTheWriterId()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddAdjustment(1, "Rate cap applied.", VersionA);
        world.WriterFault = new InvalidOperationException("Must not be asked.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Single().Reason.Should().Be("Rate cap applied.");
        outcome.Degraded.Should().BeFalse();
        world.WriterCalls.Should().Be(0);
    }

    [Fact]
    public async Task ALinkedAddDisallowAndReduceRecord_EachProjectsItsReason()
    {
        var world = new World();
        var reduced = world.AddLine(1, InvoiceLineStatus.Reduced, 100m, 60m);
        var disallowed = world.AddLine(2, InvoiceLineStatus.Disallowed, 100m, 0m);
        var added = world.AddReviewerLine(Guid.Parse("20000000-0000-4000-8000-0000000000a2"));
        world.AddRecord("reduce-line", reduced.Id, "Reduce reason.", InvoiceId);
        world.AddRecord("disallow-line", disallowed.Id, "Disallow reason.", InvoiceId);
        world.AddRecord("add-line", added.Id, "Add reason.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Select(i => (i.Decision, i.Reason)).Should().Equal(
            (FirmOutcomeDecision.Reduced, "Reduce reason."),
            (FirmOutcomeDecision.Disallowed, "Disallow reason."),
            (FirmOutcomeDecision.Added, "Add reason."));
    }

    [Fact]
    public async Task ALineRedecided_ShowsTheNewestRecordOfItsCurrentDecision()
    {
        var world = new World();
        var line = world.AddLine(1, InvoiceLineStatus.Disallowed, 100m, 0m);
        world.AddRecord("reduce-line", line.Id, "Earlier reduction.", InvoiceId, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        world.AddRecord("disallow-line", line.Id, "First disallow.", InvoiceId, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        world.AddRecord("disallow-line", line.Id, "Latest disallow.", InvoiceId, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));

        (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Reason.Should().Be("Latest disallow.");
    }

    [Fact]
    public async Task ASignalNotResolvedByTheLinkedRecord_IsNotACitation()
    {
        var world = new World();
        var pair = world.AddReducedWithCitation(1, "Reason.", VersionA, "Good reason.");
        world.AddSignal(null, VersionB, "Unlinked Signal.", pair.Line.Id, InvoiceId); // never resolved by a decision
        world.AddSignal(Guid.NewGuid(), VersionB, "Another record's Signal.", pair.Line.Id, InvoiceId);
        world.AddSignal(pair.Record.Id, VersionB, "Another invoice's Signal.", pair.Line.Id, OtherInvoiceId);
        world.AddSignal(pair.Record.Id, VersionB, "Another line's Signal.", Guid.NewGuid(), InvoiceId);

        var citations = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().Citations;

        citations.Should().ContainSingle().Which.FirmReason.Should().Be("Good reason.");
    }

    [Fact]
    public async Task NoReviewerIdentityOrInternalNoteIsReadOrProjected()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        JsonSerializer.Serialize(outcome).Should().NotContain("Rita").And.NotContain("Reviewer");
        world.Queries.Where(q => q.EntityName == "sprk_decisionrecord").Should().OnlyContain(
            q => !q.ColumnSet.Columns.Contains("sprk_confirmedby") && !q.ColumnSet.Columns.Contains("sprk_factsnapshot") && !q.ColumnSet.Columns.Contains("sprk_steps"));
    }

    // ── No Procedure Library link anywhere ──

    [Fact]
    public async Task NoMemberOfAFullyPopulatedProjection_IsALinkOrAnIdOfAProcedureRuleSetOrDocument()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.AddAdjustment(5, "Volume discount.", VersionA);
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        var json = JsonSerializer.Serialize(outcome);
        var names = new List<string>();
        CollectNames(JsonDocument.Parse(json).RootElement, names);
        names.Should().NotBeEmpty();
        names.Should().NotContain(n => new[] { "document", "container", "drive", "link", "url", "uri", "procedure", "ruleset" }
            .Any(bad => n.Contains(bad, StringComparison.OrdinalIgnoreCase)));
        json.Should().NotContainAny("http:", "https:", "/drives/", "sharepoint");

        // The same over the declared shape, so a member that is null in this fixture is also covered.
        var declared = new[] { typeof(FirmOutcome), typeof(FirmOutcomeItem), typeof(FirmOutcomeLine), typeof(FirmOutcomeAdjustment), typeof(FirmOutcomeCitation) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        declared.Should().NotContain(n => n.Contains("Document", StringComparison.Ordinal) || n.Contains("Container", StringComparison.Ordinal)
            || n.Contains("Drive", StringComparison.Ordinal) || n.Contains("Link", StringComparison.Ordinal) || n.Contains("Url", StringComparison.Ordinal)
            || n.Contains("Procedure", StringComparison.Ordinal) || n.Contains("RuleSet", StringComparison.Ordinal) || n.Contains("Reviewer", StringComparison.Ordinal));
    }

    private static void CollectNames(JsonElement element, List<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    CollectNames(property.Value, names);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectNames(item, names);
                }

                break;
        }
    }

    // ── Fail closed ──

    [Theory]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_policyversion")]
    [InlineData("sprk_policy")]
    public async Task AFaultAfterTheHeader_ProjectsTheStatusOnly_AndNothingPartial(string faulting)
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.AddReducedWithCitation(2, "Reason 2.", VersionA, "Firm reason 2.");
        world.FaultingEntities.Add(faulting);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.InvoiceInReview, null, "In review", null, null, [], true));
    }

    [Fact]
    public async Task AFaultInTheClauseReader_ProjectsTheStatusOnly()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.ClauseFault = true;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Approved, null, "Approved", null, null, [], true));
    }

    [Fact]
    public async Task AFaultReadingTheHeader_ProjectsNothing()
    {
        var world = new World();
        world.FaultingEntities.Add("sprk_invoice");

        (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Kind.Should().Be(FirmOutcomeKind.Unavailable);

        var other = new World();
        other.AddSubmission((int)InvoiceSubmissionStatus.Rejected);
        other.FaultingEntities.Add("sprk_invoicesubmission");
        (await other.Projection().GetForSubmissionAsync(SubmissionId, CancellationToken.None))!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, null);
        using var cancelled = new CancellationTokenSource();
        world.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken token) =>
            {
                cancelled.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new EntityCollection());
            });

        var act = () => world.Projection().GetForInvoiceAsync(InvoiceId, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Shape ──

    [Fact]
    public void TheExcerptCollapsesWhitespaceAndIsBounded()
    {
        FirmFindingsProjection.Excerpt("  one\n two   three ").Should().Be("one two three");
        FirmFindingsProjection.Excerpt(new string('x', 500))!.Length.Should().Be(FirmFindingsProjection.ExcerptLength + 3);
        FirmFindingsProjection.Excerpt("   ").Should().BeNull();
        FirmFindingsProjection.Excerpt(null).Should().BeNull();
    }

}
