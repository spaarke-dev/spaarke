using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;
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
    private static readonly Guid RuleSetA = Guid.Parse("20000000-0000-4000-8000-0000000000e1");
    private static readonly Guid ProcedureA = Guid.Parse("20000000-0000-4000-8000-0000000000e2");

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
        public readonly List<Entity> RuleSets = [];
        public readonly List<Entity> Procedures = [];
        public readonly List<QueryExpression> Queries = [];
        public readonly ListLogger Logger = new();
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
            // Both policies belong to one rule set of a Guideline Procedure (a document firms receive); a test changes the type or the link.
            RuleSets.Add(new Entity("sprk_ruleset", RuleSetA)
            {
                ["sprk_name"] = "Internal rule set name",
                ["sprk_procedure"] = new EntityReference("sprk_procedure", ProcedureA),
            });
            Procedures.Add(new Entity("sprk_procedure", ProcedureA)
            {
                ["sprk_name"] = "Internal procedure name",
                ["sprk_proceduretype"] = new OptionSetValue((int)ProcedureType.Guideline),
            });

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
                        "sprk_billingevent" => Lines.Where(r => SatisfiesNestedFilters(r, query.Criteria)).ToList(),
                        "sprk_decisionrecord" => Records.Where(r => SatisfiesWriterConditions(r, query.Criteria)).ToList(),
                        "sprk_signal" => Signals.Where(r => SatisfiesWriterConditions(r, query.Criteria)).ToList(),
                        "sprk_policyversion" => Versions,
                        "sprk_policy" => Policies,
                        "sprk_invoicesubmission" => Submissions,
                        "sprk_ruleset" => RuleSets,
                        "sprk_procedure" => Procedures,
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

        public FirmFindingsProjection Projection(Func<string?, ILogger, Guid, FirmRejectionFindings.ParseResult>? parse = null) => new(
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
            Logger)
        {
            ParseRejectionFindings = parse ?? FirmRejectionFindings.Parse,
        };

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

        /// <summary>
        /// The store applies the query's NESTED filters (the line query's <c>ExcludeInactiveAdjustments</c>: not an Adjustment line, or no cost
        /// type, or <c>statecode</c> Active) to the stored row, as Dataverse does, so a projection whose line query lost that filter shows a
        /// removed adjustment. The top-level conditions are left alone (the invoice link is checked by the projection itself, and a stray line
        /// of another invoice must still reach it). An operator this double cannot evaluate throws, so a new criterion is never silently ignored.
        /// </summary>
        private static bool SatisfiesNestedFilters(Entity row, FilterExpression criteria) =>
            criteria.Filters.All(filter => Evaluate(row, filter));

        private static bool Evaluate(Entity row, FilterExpression filter)
        {
            var results = filter.Conditions.Select(c => Evaluate(row, c)).Concat(filter.Filters.Select(f => Evaluate(row, f))).ToList();
            return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
        }

        private static bool Evaluate(Entity row, ConditionExpression condition)
        {
            var value = row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
            // A row with no statecode is active (Dataverse always has one; the fixtures set it only on a removed line).
            if (value is null && condition.AttributeName == "statecode")
            {
                value = new OptionSetValue(0);
            }

            object? raw = value switch
            {
                OptionSetValue option => option.Value,
                EntityReference reference => reference.Id,
                _ => value,
            };
            return condition.Operator switch
            {
                ConditionOperator.Null => raw is null,
                ConditionOperator.NotNull => raw is not null,
                ConditionOperator.Equal => raw is not null && Equals(raw, condition.Values[0]),
                ConditionOperator.NotEqual => raw is not null && !Equals(raw, condition.Values[0]),
                _ => throw new InvalidOperationException($"The test double cannot evaluate {condition.Operator} on {condition.AttributeName}."),
            };
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

        /// <summary>A Rejected submission whose stored findings are <paramref name="findingsJson"/> (130c).</summary>
        public Entity AddRejectedSubmission(string? findingsJson)
        {
            var submission = AddSubmission((int)InvoiceSubmissionStatus.Rejected);
            if (findingsJson is not null)
            {
                submission["sprk_findingsjson"] = findingsJson;
            }

            return submission;
        }

        /// <summary>A Signal raised for a rejection finding (130c): on the derived invoice (and the finding's line), by the BFF writer unless told otherwise.</summary>
        public Entity AddRejectionSignal(
            Guid version, int? lineIndex, string? firmReason, Guid? createdBy = null, Guid? onBehalfOf = null, Guid? invoice = null)
        {
            var signal = new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_firmreason"] = firmReason,
                ["createdon"] = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                ["sprk_policyversion"] = new EntityReference("sprk_policyversion", version),
                ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice ?? InvoiceId),
                ["createdby"] = new EntityReference("systemuser", createdBy ?? WriterId),
            };
            if (onBehalfOf is not null)
            {
                signal["createdonbehalfby"] = new EntityReference("systemuser", onBehalfOf.Value);
            }

            if (lineIndex is { } index)
            {
                signal["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", LineId(index + 1));
            }

            Signals.Add(signal);
            return signal;
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
            ["sprk_ruleset"] = new EntityReference("sprk_ruleset", RuleSetA),
        };
    }

    /// <summary>Records every log line (level and rendered message) the projection writes.</summary>
    private sealed class ListLogger : ILogger<FirmFindingsProjection>
    {
        public readonly List<(LogLevel Level, string Message)> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add((logLevel, formatter(state, exception)));
            }
        }
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

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

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

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.SubmissionInProgress);
        outcome.BilledTotal.Should().BeNull();
    }

    [Theory(DisplayName = "130b fix 2 (F1) must-fire: a Received or Processing submission whose invoice the caller did NOT decide shows no billed total and reads nothing of the invoice, even though it exists")]
    [InlineData((int)InvoiceSubmissionStatus.Received, "Received")]
    [InlineData((int)InvoiceSubmissionStatus.Processing, "Processing")]
    public async Task AnUndecidedInvoice_IsNeverRead_InProgressIsStatusOnly(int status, string label)
    {
        var world = new World(); // the invoice exists in the store (created after the caller's check)
        world.AddSubmission(status);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionInProgress, "TRK-1", label, null, null, [], false));
        world.Reads("sprk_invoice").Should().Be(0, "no invoice decision covers it, so it is not read");
        JsonSerializer.Serialize(outcome).Should().NotContain("1000");
    }

    [Fact(DisplayName = "130b fix 2 (F1) must-fire: an Accepted submission whose invoice the caller did NOT decide is Unavailable (503: the retry decides it), and the invoice is not read")]
    public async Task AnUndecidedInvoice_OfAnAcceptedSubmission_IsUnavailable()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
        outcome.Degraded.Should().BeTrue();
        world.Reads("sprk_invoice").Should().Be(0);
        world.Reads("sprk_billingevent").Should().Be(0);
    }

    [Fact(DisplayName = "130b fix 2 (F1) must-not-fire: with the invoice decided, a Processing submission shows its invoice's billed total")]
    public async Task ADecidedInvoice_ShowsTheBilledTotal()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Processing);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.BilledTotal.Should().Be(1000m);
    }

    [Fact(DisplayName = "130b fix 2 (K1): an Accepted submission whose invoice does not exist is Unavailable, logged at Error with a stable code")]
    public async Task AnAcceptedSubmission_WithNoInvoice_IsUnavailable_AndLoggedWithAStableCode()
    {
        var world = new World();
        world.Invoices.Clear();
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
        world.Logger.Lines.Should().ContainSingle(l => l.Level == LogLevel.Error)
            .Which.Message.Should().Contain(FirmFindingsProjection.AcceptedWithoutInvoiceCode);
    }

    [Fact(DisplayName = "130b fix 3 (F2): an Accepted submission whose invoice is missing and was NOT decided is Unavailable, logged at Warning with the stable code, and no invoice is read")]
    public async Task AnAcceptedSubmission_WithNoInvoice_Undecided_IsUnavailable_AndWarnedWithTheStableCode()
    {
        var world = new World();
        world.Invoices.Clear();
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
        world.Logger.Lines.Should().ContainSingle(l => l.Level == LogLevel.Warning)
            .Which.Message.Should().Contain(FirmFindingsProjection.AcceptedWithoutInvoiceCode);
        world.Logger.Lines.Should().NotContain(l => l.Level == LogLevel.Information);
        world.Reads("sprk_invoice").Should().Be(0);
        world.Reads("sprk_billingevent").Should().Be(0);
    }

    [Fact(DisplayName = "130b fix 3 (K4): the firm reasons are ordered by the creation of the Signal each rule version's reason came from (its first non-blank), not by the version's first Signal")]
    public async Task TheFirmReasons_AreOrderedByTheChosenSignal_NotTheVersionsFirstSignal()
    {
        var world = new World();
        var pair = world.AddReducedWithCitation(1, "R.", VersionA, null); // version A: Signal 1 is blank
        pair.Record.Id.Should().NotBeEmpty();
        world.Signals.Single()["createdon"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = world.AddSignal(pair.Record.Id, VersionB, "y", pair.Line.Id, InvoiceId); // version B: "y" on Signal 2
        second["createdon"] = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
        var third = world.AddSignal(pair.Record.Id, VersionA, "x", pair.Line.Id, InvoiceId); // version A: "x" on Signal 3
        third["createdon"] = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.FirmReasons.Should().Equal("y", "x");
    }

    [Fact]
    public async Task AcceptedSubmission_ProjectsItsInvoicesOutcome()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);
        world.AddReducedWithCitation(1, "Reason.", VersionA, null);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.InvoiceInReview);
        outcome.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RejectedSubmission_ProjectsStatusOnly_FailedIsClosed_AndMissingProjectsNothing()
    {
        var world = new World();
        world.AddSubmission((int)InvoiceSubmissionStatus.Rejected);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!
            .Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));

        // 130b fix round 1: a failed submission is a permanent state, not a fault: Closed, status only, not degraded (the route answers 200).
        world.Submissions[0]["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!
            .Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Closed, "TRK-1", "Failed", null, null, [], false));

        world.Submissions[0]["sprk_processingstatus"] = new OptionSetValue(424242);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!
            .Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Closed, "TRK-1", "Closed", null, null, [], false));

        world.Submissions.Clear();
        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None)).Should().BeNull();
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
        adjustment.Citations.Should().BeEmpty("version B's clause is not published to firms, so the rule is not cited at all (fix round 2)");
        outcome.Items[0].Citations.Should().ContainSingle().Which.RuleName.Should().Be("Travel time", "version A's clause is published");
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
    [InlineData(InvoiceStatusCodes.Cancelled, "Cancelled")]
    [InlineData(InvoiceStatusCodes.Superseded, "Replaced by a correction")]
    [InlineData(12345, "Closed")]
    public async Task PermanentStates_AndUnknownCodes_AreClosed_StatusOnly_NotDegraded(int status, string label)
    {
        // 130b fix round 1: a permanent state is not a fault. Status only (no items, no totals), not degraded, nothing below the header read.
        var world = new World(status);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Closed, "TRK-1", label, null, null, [], false));
        world.Reads("sprk_billingevent").Should().Be(0);
        world.Reads("sprk_decisionrecord").Should().Be(0);
    }

    [Fact]
    public async Task AnAcceptedSubmission_WhoseInvoiceWasSuperseded_IsClosed()
    {
        var world = new World(InvoiceStatusCodes.Superseded);
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Closed, "TRK-1", "Replaced by a correction", null, null, [], false));
    }

    [Theory]
    [InlineData((int)InvoiceSubmissionStatus.Received, "Received")]
    [InlineData((int)InvoiceSubmissionStatus.Processing, "Processing")]
    public async Task ASubmissionWhoseBilledTotalReadFaults_IsDegraded_NeverANullTotal(int status, string label)
    {
        // 130b fix round 1 (F2): a total that could not be read is not "no total yet".
        var world = new World();
        world.AddSubmission(status);
        world.FaultingEntities.Add("sprk_invoice");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionInProgress, null, label, null, null, [], true));
    }

    [Fact]
    public async Task ARemovedAdjustment_AndItsReason_NeverReachTheFirm()
    {
        // 130b fix round 1 (K3): a removed invoice-level adjustment is deactivated, never deleted (task 102); the line query excludes it.
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddAdjustment(5, "Volume discount.", VersionB, amount: -50m, value: 5m);
        world.AddAdjustment(6, "Removed write-down.", VersionA, amount: -70m, value: 7m)["statecode"] = new OptionSetValue(1);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.Reason.Should().Be("Volume discount.");
        JsonSerializer.Serialize(outcome).Should().NotContain("Removed write-down.").And.NotContain("-70");
        world.ClauseCalls.Should().NotContain(VersionA, "the removed adjustment's rule is not cited");
    }

    // ── Visibility rule (D-66, D-79) ──

    [Fact(DisplayName = "130b fix 2 must-fire (citation) / must-not-fire (firm reason): with the policy's firm flag off (or no snapshot) the rule is not cited at all (no name, label or clause), and the firm reason is still shown")]
    public async Task FlagOff_TheRuleIsNotCited_NoNameNoLabelNoClause_ButTheFirmReasonIsShown()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.Clause = _ => FirmClauseText.None; // the rule set's flag is off, or the invoice's snapshot did not apply the version

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        var item = outcome!.Items.Should().ContainSingle().Subject;
        item.Citations.Should().BeEmpty();
        item.Reason.Should().Be("Rate exceeds the agreed rate.");
        item.FirmReasons.Should().Equal("Your agreed rate is lower.");
        outcome.Degraded.Should().BeFalse();
        var json = JsonSerializer.Serialize(outcome);
        json.Should().Contain("Your agreed rate is lower.");
        json.Should().NotContain("Travel time").And.NotContain(FirmLabel).And.NotContain(ClauseSection).And.NotContain(ClauseQuote);
        world.Reads("sprk_ruleset").Should().Be(0, "nothing is published, so no Procedure type needs deciding");
    }

    [Fact(DisplayName = "130b fix 2: the firm reasons do not follow the citation gate: two rules, one published and one not, give one citation and both firm reasons; the same reason twice is shown once")]
    public async Task TheFirmReasons_AreIndependentOfTheCitationGate()
    {
        var world = new World();
        var pair = world.AddReducedWithCitation(1, "Reason.", VersionA, "Your agreed rate is lower.");
        world.AddSignal(pair.Record.Id, VersionB, "Block billing is not allowed.", pair.Line.Id, InvoiceId);
        var second = world.AddReducedWithCitation(2, "Reason 2.", VersionA, "Same reason.");
        world.AddSignal(second.Record.Id, VersionB, "Same reason.", second.Line.Id, InvoiceId);
        world.Clause = v => v == VersionA ? FlagOnText : FirmClauseText.None;

        var items = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items;

        items[0].Citations.Should().ContainSingle().Which.RuleName.Should().Be("Travel time");
        items[0].FirmReasons.Should().BeEquivalentTo("Your agreed rate is lower.", "Block billing is not allowed."); // same createdon in the fixture
        items[1].FirmReasons.Should().Equal("Same reason.");
        JsonSerializer.Serialize(items).Should().NotContain("\"Block billing\"", "version B's rule is not published, so its name is not shown");
    }

    [Fact(DisplayName = "130b fix 2 must-fire: an InternalStandard Procedure's rule is never cited, even with its flag on and the clause published")]
    public async Task AnInternalStandardProceduresRule_IsNeverCited_WhateverItsFlag()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.AddAdjustment(5, "Volume discount.", VersionA);
        world.Procedures[0]["sprk_proceduretype"] = new OptionSetValue((int)ProcedureType.InternalStandard);
        world.Clause = _ => FlagOnText; // the flag says on (a native write could set it); the type still decides

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2).And.OnlyContain(i => i.Citations.Count == 0);
        outcome.Items[0].FirmReasons.Should().Equal(new[] { "Your agreed rate is lower." }, "the firm reason is not a citation field");
        outcome.Degraded.Should().BeFalse();
        JsonSerializer.Serialize(outcome).Should().NotContain("Travel time").And.NotContain(ClauseQuote).And.NotContain(FirmLabel);
        world.Queries.Where(q => q.EntityName is "sprk_ruleset" or "sprk_procedure").Should().NotBeEmpty()
            .And.OnlyContain(q => !q.ColumnSet.Columns.Contains("sprk_name"), "a rule set's or Procedure's name is never read");
    }

    [Theory(DisplayName = "130b fix 2: a rule set or Procedure that cannot be read, or a Procedure with no type, cites nothing (fail closed)")]
    [InlineData("no-rule-set-link")]
    [InlineData("rule-set-missing")]
    [InlineData("procedure-missing")]
    [InlineData("procedure-without-type")]
    public async Task AnUndecidableProcedureType_CitesNothing(string kind)
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.Clause = _ => FlagOnText;
        switch (kind)
        {
            case "no-rule-set-link":
                world.Policies.Single(p => p.Id == PolicyA).Attributes.Remove("sprk_ruleset");
                break;
            case "rule-set-missing":
                world.RuleSets.Clear();
                break;
            case "procedure-missing":
                world.Procedures.Clear();
                break;
            default:
                world.Procedures[0].Attributes.Remove("sprk_proceduretype");
                break;
        }

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Single().Citations.Should().BeEmpty();
        outcome.Degraded.Should().BeFalse();
    }

    [Theory(DisplayName = "130b fix 2 (fault): a fault reading the firm flag (the clause reader), the rule set or the Procedure omits the citation; the line, its reason and the firm reason stay, not degraded")]
    [InlineData("clause")]
    [InlineData("sprk_ruleset")]
    [InlineData("sprk_procedure")]
    public async Task AFaultDecidingACitation_OmitsIt(string faulting)
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.Clause = _ => FlagOnText;
        if (faulting == "clause")
        {
            world.ClauseFault = true;
        }
        else
        {
            world.FaultingEntities.Add(faulting);
        }

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.ApprovedWithAdjustments);
        outcome.Degraded.Should().BeFalse();
        var item = outcome.Items.Single();
        item.Reason.Should().Be("Reason.");
        item.Citations.Should().BeEmpty();
        item.FirmReasons.Should().Equal("Firm reason.");
        JsonSerializer.Serialize(outcome).Should().NotContain("Travel time").And.NotContain(ClauseQuote);
    }

    [Fact(DisplayName = "130b fix 2: no id of a policy, version, rule set or Procedure appears anywhere in the serialized outcome")]
    public async Task NoPolicyVersionRuleSetOrProcedureId_IsInTheSerializedBody()
    {
        var world = new World(InvoiceStatusCodes.Approved);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.AddAdjustment(5, "Volume discount.", VersionB);
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.SelectMany(i => i.Citations).Should().HaveCount(2, "both rules are published, so the body carries their citations");
        var json = JsonSerializer.Serialize(outcome);
        foreach (var id in new[] { PolicyA, PolicyB, VersionA, VersionB, RuleSetA, ProcedureA })
        {
            json.Should().NotContainEquivalentOf(id.ToString("D")).And.NotContainEquivalentOf(id.ToString("N"));
        }
    }

    [Fact]
    public async Task FlagOn_ACitationCarriesTheLabelVersionSectionAndQuote_AndTheRuleNameIsThePolicysNotTheRuleSets()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.");
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome!.Items.Single().Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FirmOutcomeCitation("Travel time", FirmLabel, 3, ClauseSection, ClauseQuote));
        outcome.Items.Single().FirmReasons.Should().Equal(new[] { "Your agreed rate is lower." }, "flag on: the citation and the firm reason are both shown");
        world.Queries.Where(q => q.EntityName is "sprk_ruleset" or "sprk_procedure").Should().OnlyContain(
            q => !q.ColumnSet.Columns.Contains("sprk_name"), "the rule set's (and Procedure's) name is never read: only the Procedure link and its type");
        JsonSerializer.Serialize(outcome).Should().NotContain("Internal rule set name").And.NotContain("Internal procedure name");
    }

    [Fact]
    public async Task FlagOn_ALegacyRuleSetHasNoLabel_ButStillShowsVersionSectionAndQuote()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, null);
        world.Clause = _ => new FirmClauseText(ClauseQuote, null, null);
        world.RuleSets[0].Attributes.Remove("sprk_procedure"); // a legacy rule set: no Procedure, so its flag alone decides

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

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.FirmReasons.Should().BeEmpty();
        item.Citations.Should().ContainSingle();
        JsonSerializer.Serialize(item).Should().NotContain("Internal Signal").And.NotContain("0.42");
        world.Queries.Where(q => q.EntityName == "sprk_signal").Should().OnlyContain(q => !q.ColumnSet.Columns.Contains("sprk_message"));
    }

    [Fact]
    public async Task ABlankFirmReason_IsOmitted()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, firmReason: "   ");
        world.Clause = _ => FlagOnText;

        (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single().FirmReasons.Should().BeEmpty();
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
        world.Clause = _ => FlagOnText;

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.Citations.Should().ContainSingle();
        item.FirmReasons.Should().Equal(new[] { "Second reason." }, "the first Signal with a firm reason supplies it");
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
        world.Clause = _ => FlagOnText;

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.Citations.Should().ContainSingle();
        item.FirmReasons.Should().Equal("Good reason.");
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
        world.Clause = _ => FlagOnText;
        world.FaultingEntities.Add(faulting);

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.InvoiceInReview, null, "In review", null, null, [], true));
    }

    [Fact(DisplayName = "130b fix 2 (K1): an invoice read with no status code is Unavailable (503), never an unknown code read as Closed")]
    public async Task AnInvoiceWithNoStatusCode_IsUnavailable_NotClosed()
    {
        var world = new World();
        world.Invoices[0].Attributes.Remove("statuscode");
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var outcome = await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.Unavailable, null, "Unavailable", null, null, [], true));
        world.Reads("sprk_billingevent").Should().Be(0);

        // The same through an Accepted submission.
        world.AddSubmission((int)InvoiceSubmissionStatus.Accepted);
        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
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
        (await other.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!.Kind.Should().Be(FirmOutcomeKind.Unavailable);
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

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 130c (FR-18): a Rejected submission projects its findings from its OWN stored findings
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private const string InternalClause = "INTERNAL stored clause quote";
    private const string InternalSentence = "INTERNAL Signal plan sentence";

    /// <summary>A real reject-capable reason code of the rule type (the catalogue's first, in ordinal order); a fixed one for a type that has none.</summary>
    private static string RealReason(string ruleType) =>
        Enum.TryParse<RuleType>(ruleType, out var type) && Enum.IsDefined(type) && RuleDomains.IsBilling(type)
            && BillingRuleCatalogue.RejectCapableReasons(type).OrderBy(c => c, StringComparer.Ordinal).FirstOrDefault() is { } reason
            ? reason
            : BillingReasonCodes.RateAboveApproved;

    /// <summary>The writer records a reference value (read from the system's own tables) with no field; an extracted value has one.</summary>
    private static bool IsReferenceName(string name) =>
        name.StartsWith("approvedRate", StringComparison.Ordinal) || name.StartsWith("rateCard", StringComparison.Ordinal)
        || name.StartsWith("duplicateOf", StringComparison.Ordinal);

    /// <summary>One stored <c>findings[]</c> entry, in the shape <c>InvoiceSubmissionPersister.FindingEntryJson</c> writes.</summary>
    private static JsonObject Stored(
        string ruleType, Guid version, int? line, bool rejects = true, bool withheld = false, string? reason = null,
        params (string Name, string? Value)[] values) => new()
    {
        ["index"] = 0,
        ["ruleType"] = ruleType,
        ["policyId"] = PolicyA.ToString("D"),
        ["policyCode"] = "BILL-INTERNAL-CODE",
        ["policyVersionId"] = version.ToString("D"),
        ["clause"] = InternalClause,
        ["clauseLocation"] = "INTERNAL location",
        ["subject"] = line is null ? "Invoice" : "Line",
        ["line"] = line,
        ["reasonCode"] = reason ?? RealReason(ruleType),
        ["detailWithheld"] = withheld,
        ["consequence"] = rejects ? "Reject" : "Flag",
        ["rejects"] = rejects,
        ["heldBecause"] = null,
        ["valuesRead"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject
        {
            ["name"] = v.Name,
            ["value"] = v.Value,
            ["line"] = line,
            ["field"] = IsReferenceName(v.Name) ? null : "Rate",
            ["confidence"] = 0.42m,
            ["belowReliabilityThreshold"] = false,
        }).ToArray()),
    };

    private static string StoredDocument(string kind, params JsonNode?[] entries) => new JsonObject
    {
        ["schema"] = 1,
        ["kind"] = kind,
        // A code that can never reject: a test that the document's own top-level code is never shown must be able to tell it from a finding's.
        ["reasonCode"] = BillingReasonCodes.CurrencyNotUsd,
        ["findings"] = new JsonArray(entries),
        ["signalPlan"] = new JsonArray(new JsonObject { ["sentence"] = InternalSentence }),
    }.ToJsonString();

    private static string RejectedDocument(params JsonNode?[] entries) => StoredDocument("AutoRejected", entries);

    private static List<string> Warnings(World world) => world.Logger.Lines.Where(l => l.Level == LogLevel.Warning).Select(l => l.Message).ToList();

    private static World TwoFindingsWorld()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("RequiredData", VersionB, null, values: ("invoice.firmInvoiceNumber", null)),
            Stored("ApprovedRate", VersionA, 2, values: [("line.rate", "450.00"), ("line.hours", "2.5"), ("approvedRate", "400.00")])));
        return world;
    }

    [Fact(DisplayName = "130c must-fire: a rejected submission with two stored findings projects both with line, values read, rule and clause (flag on)")]
    public async Task Rejected_TwoStoredFindings_ProjectLineValuesReadRuleAndClause_WhenTheFlagIsOn()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().NotBeNull();
        outcome!.Kind.Should().Be(FirmOutcomeKind.SubmissionRejected);
        outcome.Degraded.Should().BeFalse();
        outcome.StatusLabel.Should().Be("Rejected");
        outcome.TrackingNumber.Should().Be("TRK-1");
        outcome.BilledTotal.Should().BeNull();
        outcome.Items.Should().HaveCount(2);

        var line = outcome.Items[0]; // line findings first, the invoice-level finding last
        line.Decision.Should().Be(FirmOutcomeDecision.RejectionFinding);
        line.LineNumber.Should().Be(3, "the stored line index is zero-based; the firm's invoice numbers its lines from one");
        line.LineId.Should().BeNull();
        line.Line.Should().BeNull("a rejected submission's live lines are never read");
        line.ValuesRead.Should().Equal(new FirmOutcomeValueRead("line.rate", "450.00"), new FirmOutcomeValueRead("line.hours", "2.5"));
        line.Citations.Should().ContainSingle().Which.Should().BeEquivalentTo(new FirmOutcomeCitation("Travel time", FirmLabel, 3, ClauseSection, ClauseQuote));

        var invoiceLevel = outcome.Items[1];
        invoiceLevel.LineNumber.Should().BeNull();
        invoiceLevel.ValuesRead.Should().Equal(
            [new FirmOutcomeValueRead("invoice.firmInvoiceNumber", "")], "a missing field is shown by its name, with an empty value");
        invoiceLevel.Citations.Should().ContainSingle().Which.RuleName.Should().Be("Block billing");

        world.Reads("sprk_billingevent").Should().Be(0, "a rejected submission's outcome never reads the live invoice lines");
        world.Reads("sprk_invoice").Should().Be(0);
        world.Reads("sprk_decisionrecord").Should().Be(0);
        world.ClauseCalls.Should().BeEquivalentTo([VersionA, VersionB]);
    }

    [Fact(DisplayName = "130c: only the stored values and rule fields are projected; the stored clause, sentence, policy code and confidence never are")]
    public async Task Rejected_NothingInternalInTheStoredFindingsIsProjected()
    {
        // The document's top-level code (CurrencyNotUsd, which cannot reject) and a finding that did not reject (its own distinct code) are
        // internal: neither may reach the partner, while the rejecting findings' own codes do.
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("RequiredData", VersionB, null, values: ("invoice.firmInvoiceNumber", null)),
            Stored("ApprovedRate", VersionA, 2, values: [("line.rate", "450.00"), ("line.hours", "2.5"), ("approvedRate", "400.00")]),
            Stored("ApprovedRate", VersionA, 5, rejects: false, reason: BillingReasonCodes.RateUnresolved)));
        world.Clause = _ => FlagOnText;
        world.AddRejectionSignal(VersionA, 2, "Your rate is above the agreed rate.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);
        var json = JsonSerializer.Serialize(outcome);

        json.Should().Contain(ClauseQuote).And.Contain("Your rate is above the agreed rate.");
        outcome!.Items.Select(i => i.ReasonCode).Should().Equal(BillingReasonCodes.RateAboveApproved, BillingReasonCodes.RequiredFieldMissing);
        json.Should().Contain(BillingReasonCodes.RateAboveApproved, "130d: the rejecting finding's own reason code is projected, as reasonCode");
        json.Should().NotContain(BillingReasonCodes.CurrencyNotUsd, "the document's top-level reason code is never shown")
            .And.NotContain(BillingReasonCodes.RateUnresolved, "a finding that did not reject is never shown, nor its code");
        json.Should().NotContain(InternalClause).And.NotContain(InternalSentence).And.NotContain("INTERNAL location")
            .And.NotContain("BILL-INTERNAL-CODE").And.NotContain("0.42").And.NotContain("400.00").And.NotContain("Internal rule set name")
            .And.NotContain("Internal procedure name");
        world.Queries.Where(q => q.EntityName == "sprk_invoicesubmission").Should().OnlyContain(
            q => q.ColumnSet.Columns.All(c => c == "sprk_processingstatus" || c == "sprk_trackingnumber" || c == "sprk_findingsjson"));
    }

    [Fact(DisplayName = "130c D-79: flag off gives no clause, section, label, version or rule name; the line, the values and the firm reason stay")]
    public async Task Rejected_FlagOff_ShowsNoCitation_ButTheLineValuesAndTheFirmReason()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FirmClauseText.None;
        world.AddRejectionSignal(VersionA, 2, "Your rate is above the agreed rate.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.Citations.Count == 0);
        outcome.Items[0].LineNumber.Should().Be(3);
        outcome.Items[0].ValuesRead.Should().HaveCount(2);
        outcome.Items[0].FirmReasons.Should().Equal("Your rate is above the agreed rate.");
        outcome.Items[1].FirmReasons.Should().Equal([FirmRejectionReasonText.Table[BillingReasonCodes.RequiredFieldMissing]]);
        JsonSerializer.Serialize(outcome).Should().NotContain(ClauseQuote).And.NotContain(ClauseSection).And.NotContain(FirmLabel)
            .And.NotContain("Travel time").And.NotContain("Block billing").And.NotContain("Internal rule set name");
    }

    [Fact(DisplayName = "130c: an InternalStandard Procedure's rule is never cited, whatever its flag; its firm reason still shows")]
    public async Task Rejected_AnInternalStandardRule_IsNeverCited()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        world.Procedures[0]["sprk_proceduretype"] = new OptionSetValue((int)ProcedureType.InternalStandard);
        world.AddRejectionSignal(VersionA, 2, "Your rate is above the agreed rate.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.Citations.Count == 0);
        outcome.Items[0].FirmReasons.Should().ContainSingle();
        JsonSerializer.Serialize(outcome).Should().NotContain(ClauseQuote).And.NotContain("Travel time").And.NotContain(FirmLabel);
    }

    [Fact(DisplayName = "130c: a clause-reader fault cites nothing and is not a fault of the outcome (still the findings, not Degraded)")]
    public async Task Rejected_AClauseReaderFault_CitesNothing_AndStillProjects()
    {
        var world = TwoFindingsWorld();
        world.ClauseFault = true;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Degraded.Should().BeFalse();
        outcome.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.Citations.Count == 0);
        outcome.Items[0].ValuesRead.Should().HaveCount(2);
    }

    [Fact(DisplayName = "130c: a clause the invoice's snapshot did not apply gives no text, so no citation (version-keyed, per finding)")]
    public async Task Rejected_OnlyTheVersionTheClauseReaderPublishes_IsCited()
    {
        var world = TwoFindingsWorld();
        world.Clause = v => v == VersionA ? FlagOnText : FirmClauseText.None;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items[0].Citations.Should().ContainSingle();
        outcome.Items[1].Citations.Should().BeEmpty();
    }

    [Fact(DisplayName = "130c: an invoice the caller did not decide is never asked about: findings keep their line and values, but no citation, no firm reason")]
    public async Task Rejected_AnUndecidedInvoice_ShowsNoCitationAndNoReason_AndReadsNothingOfIt()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        world.AddRejectionSignal(VersionA, 2, "Your rate is above the agreed rate.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.Citations.Count == 0);
        outcome.Items[0].FirmReasons.Should().Equal([FirmRejectionReasonText.Table[BillingReasonCodes.RateAboveApproved]]);
        outcome.Items[1].FirmReasons.Should().Equal(FirmRejectionReasonText.Table[BillingReasonCodes.RequiredFieldMissing]);
        outcome.Items[0].ValuesRead.Should().HaveCount(2);
        world.ClauseCalls.Should().BeEmpty();
        world.WriterCalls.Should().Be(0);
        foreach (var entity in new[] { "sprk_invoice", "sprk_billingevent", "sprk_signal", "sprk_policyversion", "sprk_policy", "sprk_ruleset", "sprk_procedure" })
        {
            world.Reads(entity).Should().Be(0, entity);
        }
    }

    [Fact(DisplayName = "130c FR-24 (security-class, seeded): an unknown rule type is refused fail-closed, however the entry is shaped, and never shown raw")]
    public async Task Rejected_AnUnknownRuleType_IsRefused_WithAWarningThatCarriesNoContent()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("SECRET-NEW-RULE-TYPE", VersionA, 1, values: ("secret.name", "SECRET-VALUE-1")),       // a name this build does not know
            Stored("100000004", VersionA, 1, values: ("secret.name", "SECRET-VALUE-2")),                    // a number (ApprovedRate's), never accepted as a name
            Stored("999999999", VersionA, 1, values: ("secret.name", "SECRET-VALUE-3")),                    // an undefined number
            Stored("Threshold", VersionA, 1, values: ("secret.name", "SECRET-VALUE-4")),                    // a known type of another domain
            Stored("SubmissionIntegrity", VersionA, 1, values: ("secret.name", "SECRET-VALUE-5")),          // the system marker: no rule body, no clause
            Stored("SECRET-NEW-NONREJECT", VersionA, 1, rejects: false, values: ("secret.name", "SECRET-VALUE-6")), // unknown even when it does not reject
            Stored("ApprovedTimekeeper, ApprovedRate", VersionA, 1, values: ("secret.name", "SECRET-VALUE-7")),  // a joined list whose OR is another defined member
            Stored("ApprovedRate", VersionA, 4, values: ("line.rate", "450.00"))));                          // the one known finding
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().ContainSingle("only the finding of a known Billing rule type is shown").Which.LineNumber.Should().Be(5);
        JsonSerializer.Serialize(outcome).Should().NotContain("SECRET");
        var warning = Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode)).Subject;
        warning.Should().Contain("7 stored finding(s)").And.Contain("7 of an unknown rule type").And.Contain("0 malformed");
        outcome.NotShownCount.Should().Be(6, "six of the seven refused entries claim to reject");
        Warnings(world).Concat(world.Logger.Lines.Select(l => l.Message)).Should().NotContain(m => m.Contains("SECRET") || m.Contains("450.00"));
    }

    [Fact(DisplayName = "130c: a malformed or unknown-shaped entry is dropped (fail closed) with a Warning that carries a stable code and counts only")]
    public async Task Rejected_AMalformedEntry_IsDropped_WithAWarningThatCarriesNoContent()
    {
        var world = new World();
        var noVersion = Stored("ApprovedRate", VersionA, 1, values: ("secret.name", "SECRET-VALUE-A"));
        noVersion.Remove("policyVersionId");
        var badLine = Stored("ApprovedRate", VersionA, 1, values: ("secret.name", "SECRET-VALUE-B"));
        badLine["line"] = "one";
        var lineSubjectNoLine = Stored("ApprovedRate", VersionA, null, values: ("secret.name", "SECRET-VALUE-C"));
        lineSubjectNoLine["subject"] = "Line";
        var invoiceSubjectWithLine = Stored("ApprovedRate", VersionA, 2, values: ("secret.name", "SECRET-VALUE-D"));
        invoiceSubjectWithLine["subject"] = "Invoice";
        var badValues = Stored("ApprovedRate", VersionA, 1);
        badValues["valuesRead"] = "SECRET-VALUE-E";
        var numericValue = Stored("ApprovedRate", VersionA, 1);
        numericValue["valuesRead"] = new JsonArray(new JsonObject { ["name"] = "secret.name", ["value"] = 999999 });
        var emptyVersion = Stored("ApprovedRate", Guid.Empty, 1, values: ("secret.name", "SECRET-VALUE-F"));
        var rejectsNotBool = Stored("ApprovedRate", VersionA, 1, values: ("secret.name", "SECRET-VALUE-G"));
        rejectsNotBool["rejects"] = "yes";
        var aiRejects = Stored("AiObservation", VersionA, 1, values: ("secret.name", "SECRET-VALUE-H")); // an AI observation never rejects (I-5)
        var noReason = Stored("ApprovedRate", VersionA, 1, values: ("secret.name", "SECRET-VALUE-I"));
        noReason.Remove("reasonCode");
        world.AddRejectedSubmission(RejectedDocument(
            noVersion, badLine, lineSubjectNoLine, invoiceSubjectWithLine, badValues, numericValue, emptyVersion, rejectsNotBool, aiRejects, noReason,
            JsonValue.Create("SECRET-VALUE-J"), JsonValue.Create(7), null, new JsonArray(),
            Stored("ApprovedRate", VersionA, 0, values: ("line.rate", "450.00"))));
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.LineNumber.Should().Be(1);
        outcome.Degraded.Should().BeFalse();
        JsonSerializer.Serialize(outcome).Should().NotContain("SECRET");
        var warning = Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode)).Subject;
        warning.Should().Contain("14 stored finding(s)").And.Contain("14 malformed").And.Contain("0 of an unknown rule type");
        outcome.NotShownCount.Should().Be(9, "nine of the fourteen claim to reject; the 'yes' flag and the four non-objects do not");
        world.Logger.Lines.Select(l => l.Message).Should().NotContain(m => m.Contains("SECRET") || m.Contains("450.00"));
    }

    [Theory(DisplayName = "130c: a stored document that cannot be read gives the status only (not a fault), with a stable-coded Warning and no content")]
    [InlineData("{ \"kind\": \"AutoRejected\", \"findings\": [ SECRET-BROKEN")]
    [InlineData("[ \"SECRET-NOT-AN-OBJECT\" ]")]
    [InlineData("{ \"kind\": \"AutoRejected\", \"findings\": \"SECRET-NOT-AN-ARRAY\" }")]
    public async Task Rejected_AnUnreadableStoredDocument_IsStatusOnly_WithAStableCodedWarning(string stored)
    {
        var world = new World();
        world.AddRejectedSubmission(stored);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));
        world.Logger.Lines.Should().ContainSingle(l => l.Level == LogLevel.Warning && l.Message.Contains(FirmRejectionFindings.UnreadableCode));
        world.Logger.Lines.Select(l => l.Message).Should().NotContain(m => m.Contains("SECRET"));
    }

    [Theory(DisplayName = "130c: no stored findings, an empty list, or a column that was never written is the status only")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ \"kind\": \"AutoRejected\", \"findings\": [] }")]
    public async Task Rejected_WithNoStoredFindings_IsStatusOnly(string? stored)
    {
        var world = new World();
        world.AddRejectedSubmission(stored);

        (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))
            .Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));
        Warnings(world).Should().BeEmpty();
    }

    [Fact(DisplayName = "130c FR-38: a Blocked submission (an internal hold, Rejected in status) and non-rejecting findings project no finding")]
    public async Task Rejected_ABlockedHold_AndFindingsThatDidNotReject_AreNeverProjected()
    {
        var blocked = new World();
        blocked.AddRejectedSubmission(StoredDocument("Blocked", Stored("ApprovedRate", VersionA, 1, values: ("line.rate", "450.00"))));
        (await blocked.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!.Items.Should().BeEmpty();
        blocked.Reads("sprk_policyversion").Should().Be(0);

        var accepted = new World();
        accepted.AddRejectedSubmission(StoredDocument("Accepted", Stored("ApprovedRate", VersionA, 1, values: ("line.rate", "450.00"))));
        (await accepted.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!.Items.Should().BeEmpty();

        var flagged = new World();
        flagged.AddRejectedSubmission(RejectedDocument(
            Stored("ApprovedRate", VersionA, 1, rejects: false, values: ("line.rate", "450.00")),
            Stored("ExpenseRule", VersionA, 2, values: ("expense", "12.00"))));
        var outcome = await flagged.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);
        outcome!.Items.Should().ContainSingle().Which.LineNumber.Should().Be(3);
        Warnings(flagged).Should().BeEmpty("a finding that did not reject is not a defect");
    }

    [Fact(DisplayName = "130c D-112/D-176: a Duplicate finding and a withheld detail project no values read")]
    public async Task Rejected_ADuplicateAndAWithheldDetail_ProjectNoValues()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("Duplicate", VersionA, null, values: ("duplicateOf.trackingNumber", "TRK-EARLIER")),
            Stored("ApprovedRate", VersionB, 1, withheld: true, values: ("line.rate", "450.00")),
            Stored("ExpenseRule", VersionB, 2, values: ("expense", "12.00"))));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(3);
        outcome.Items[0].ValuesRead.Should().BeEmpty(); // line 1, withheld
        outcome.Items[1].ValuesRead.Should().ContainSingle().Which.Should().BeEquivalentTo(new FirmOutcomeValueRead("expense", "12.00"));
        outcome.Items[2].ValuesRead.Should().BeEmpty(); // the Duplicate (invoice level, last)
        JsonSerializer.Serialize(outcome).Should().NotContain("TRK-EARLIER").And.NotContain("450.00");
    }

    [Fact(DisplayName = "130c: values read are bounded in count and length")]
    public async Task Rejected_ValuesRead_AreBounded()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("ApprovedRate", VersionA, 0, values: Enumerable.Range(0, 30).Select(i => ($"value.{i}", (string?)new string('x', 500))).ToArray())));

        var item = (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!.Items.Single();

        item.ValuesRead.Should().HaveCount(FirmRejectionFindings.MaxValuesPerFinding);
        item.ValuesRead!.Should().OnlyContain(v => v.Value.Length <= FirmRejectionFindings.MaxValueTextLength + 3);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact(DisplayName = "130c fix 2 (F1, must-fire): 250 rejecting findings show 200 and the outcome says 50 are not shown (in the body and the Warning)")]
    public async Task Rejected_FindingsOverTheCap_AreShownUpToTheCap_AndTheOutcomeCountsTheRest()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Enumerable.Range(0, 250).Select(i => (JsonNode?)Stored("ApprovedRate", VersionA, i)).ToArray()));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        FirmRejectionFindings.MaxFindings.Should().Be(200);
        outcome!.Items.Should().HaveCount(200);
        outcome.NotShownCount.Should().Be(50);
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Select(i => i.LineNumber).Should().Equal(Enumerable.Range(1, 200).Select(i => (int?)i), "the first 200 in stored order");
        JsonSerializer.Serialize(outcome, WebJson).Should().Contain("\"notShownCount\":50");
        Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode) && m.Contains("50 over the cap"));
    }

    [Theory(DisplayName = "130c fix 2 (F1, must-not-fire): at or under the cap the member is absent from the outcome and from the JSON")]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(200)]
    public async Task Rejected_FindingsAtOrUnderTheCap_HaveNoNotShownCount(int count)
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Enumerable.Range(0, count).Select(i => (JsonNode?)Stored("ApprovedRate", VersionA, i)).ToArray()));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(count);
        outcome.NotShownCount.Should().BeNull();
        JsonSerializer.Serialize(outcome, WebJson).Should().NotContainEquivalentOf("notShownCount");
        Warnings(world).Should().BeEmpty();
    }

    [Fact(DisplayName = "130c fix 2 (F1): past the cap, only valid rejecting findings are counted; an entry that does not claim to reject adds nothing")]
    public async Task Rejected_TheNotShownCount_CountsOnlyValidRejectingFindingsPastTheCap()
    {
        var world = new World();
        var entries = Enumerable.Range(0, 200).Select(i => (JsonNode?)Stored("ApprovedRate", VersionA, i)).ToList();
        entries.Add(JsonValue.Create("not an entry"));                                   // malformed
        entries.Add(Stored("ApprovedRate", VersionA, 300, rejects: false));              // not a rejection
        entries.AddRange(Enumerable.Range(400, 3).Select(i => (JsonNode?)Stored("ApprovedRate", VersionA, i)));
        world.AddRejectedSubmission(RejectedDocument(entries.ToArray()));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(200);
        outcome.NotShownCount.Should().Be(3);
    }

    [Fact(DisplayName = "130c fix 2 (F1): every other outcome omits the member (existing shapes serialize as before)")]
    public async Task OtherOutcomes_OmitTheNotShownCount()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var json = JsonSerializer.Serialize(await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None), WebJson);

        json.Should().NotContainEquivalentOf("notShownCount");
    }

    [Fact(DisplayName = "130c EB-D-208: a firm reason comes only from a Signal the BFF writer created, for this invoice, rule version and line")]
    public async Task Rejected_TheFirmReason_ComesOnlyFromAWriterCreatedSignalOfThisFinding()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        var user = Guid.Parse("20000000-0000-4000-8000-0000000000c9");
        world.AddRejectionSignal(VersionA, 2, "A USER's reason.", createdBy: user);
        world.AddRejectionSignal(VersionA, 2, "ON BEHALF reason.", onBehalfOf: user);
        world.AddRejectionSignal(VersionA, 2, "ANOTHER INVOICE reason.", invoice: OtherInvoiceId);
        world.AddRejectionSignal(VersionA, 3, "ANOTHER LINE reason.");
        world.AddRejectionSignal(VersionA, null, "INVOICE-LEVEL signal on a line finding.");
        world.AddRejectionSignal(VersionB, 2, "ANOTHER RULE reason.");
        world.AddRejectionSignal(VersionA, 2, "   ");
        world.AddRejectionSignal(VersionA, 2, "The writer's reason.");
        world.AddRejectionSignal(VersionB, null, "The invoice-level reason.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items[0].FirmReasons.Should().Equal("The writer's reason.");
        outcome.Items[1].FirmReasons.Should().Equal("The invoice-level reason.");
        JsonSerializer.Serialize(outcome).Should().NotContainAny("A USER", "ON BEHALF", "ANOTHER", "INVOICE-LEVEL signal");
    }

    [Theory(DisplayName = "130c: a writer id that cannot be obtained (a fault, or an empty id) gives the status only, Degraded; no Signal is read")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejected_AWriterIdThatCannotBeObtained_IsDegraded(bool fault)
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        world.AddRejectionSignal(VersionA, 2, "The writer's reason.");
        if (fault)
        {
            world.WriterFault = new InvalidOperationException("writer connection down");
        }
        else
        {
            world.WriterIdAnswered = Guid.Empty;
        }

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], true));
        world.Reads("sprk_signal").Should().Be(0);
    }

    [Theory(DisplayName = "130c: a fault reading the rules or the Signals after the findings gives the status only, Degraded")]
    [InlineData("sprk_policyversion")]
    [InlineData("sprk_policy")]
    [InlineData("sprk_signal")]
    public async Task Rejected_AFaultAfterTheFindings_IsDegraded_NeverPartial(string faulting)
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        world.FaultingEntities.Add(faulting);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], true));
    }

    [Fact(DisplayName = "130c: the rejection projection carries no id, no document link and no Procedure or rule-set member")]
    public async Task Rejected_TheProjection_CarriesNoIds_AndNoLink()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FlagOnText;
        world.AddRejectionSignal(VersionA, 2, "The writer's reason.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        var json = JsonSerializer.Serialize(outcome);
        Regex.IsMatch(json, "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}").Should().BeFalse("no policy, version, rule set, Procedure, line or invoice id");
        foreach (var id in new[] { PolicyA, PolicyB, VersionA, VersionB, RuleSetA, ProcedureA, InvoiceId, SubmissionId, LineId(3) })
        {
            json.Should().NotContain(id.ToString("D")).And.NotContain(id.ToString("N"));
        }

        var names = new List<string>();
        CollectNames(JsonDocument.Parse(json).RootElement, names);
        names.Should().Contain(["LineNumber", "ValuesRead", "Citations", "FirmReasons", "ReasonCode"]);
        names.Should().NotContain(n => new[] { "document", "container", "drive", "link", "url", "uri", "procedure", "ruleset" }
            .Any(bad => n.Contains(bad, StringComparison.OrdinalIgnoreCase)));
        json.Should().NotContainAny("http:", "https:", "/drives/", "sharepoint");

        // Every declared member of the new shapes too.
        var declared = new[] { typeof(FirmOutcomeItem), typeof(FirmOutcomeValueRead) }.SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        declared.Should().NotContain(n => n.Contains("Document", StringComparison.Ordinal) || n.Contains("Link", StringComparison.Ordinal)
            || n.Contains("Url", StringComparison.Ordinal) || n.Contains("Procedure", StringComparison.Ordinal) || n.Contains("RuleSet", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "130c: the two new item members are omitted from an invoice item's JSON (the existing contract is unchanged)")]
    public async Task AnInvoiceItem_OmitsTheRejectionOnlyMembers()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var json = JsonSerializer.Serialize((await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single());

        json.Should().NotContain("lineNumber").And.NotContain("valuesRead").And.NotContain("LineNumber").And.NotContain("ValuesRead")
            .And.NotContain("reasonCode").And.NotContain("ReasonCode");
    }

    [Fact(DisplayName = "130c: a rejected submission's outcome is built from stored findings alone (no live line, no invoice, no Decision Record read)")]
    public async Task Rejected_ReadsOnlyTheStoredFindings_NeverTheLiveInvoice()
    {
        var world = TwoFindingsWorld();
        world.AddLine(3, InvoiceLineStatus.Reduced, 100m, 60m); // a live line the stored findings must not be replaced by
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.Line == null && i.LineId == null && i.Decision == FirmOutcomeDecision.RejectionFinding);
        world.Reads("sprk_billingevent").Should().Be(0);
        world.Reads("sprk_invoice").Should().Be(0);
        world.Reads("sprk_decisionrecord").Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 130c fix round 1
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The stored entry the persister's OWN builder writes for a real handler finding that rejected the submission (no copy of its logic).</summary>
    private static JsonObject RealEntry(BillingFinding finding, int index)
    {
        var decision = new FindingDecision
        {
            Index = index,
            Finding = finding,
            Consequence = BillingConsequence.Reject,
            Rejects = true,
            ValuesRead = finding.ValuesRead,
        };
        var entry = InvoiceSubmissionPersister.FindingEntryJson(index, finding, decision, scrub: false);
        entry["policyVersionId"] = VersionA.ToString("D"); // the fixture's rule version (the handler kit's own id is not a fixture row)
        return entry;
    }

    [Fact(DisplayName = "130c fix 1 (F3, F1-a): a real ApprovedRate finding, stored by the persister's own builder, projects only values extracted from the invoice: no identifier, no rate-card or approved-rate reference value")]
    public async Task Rejected_ARealApprovedRateFinding_ProjectsOnlyTheValuesExtractedFromTheInvoice()
    {
        var finding = BillingHandlerKit.Run(
                BillingHandlerKit.Rule("""{"type":"ApprovedRate","tolerancePercent":0}"""),
                BillingHandlerKit.Facts(BillingHandlerKit.Fee(2) with { BilledRate = 650m, ApprovedRate = BillingHandlerKit.Rate(575m) }))
            .Should().ContainSingle().Subject;
        finding.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);
        // Generic precondition (it does not name the handler's reference values, which later tasks may rename): the finding reads values that
        // have no extracted field, and at least one of them is an identifier.
        var referenceNames = finding.ValuesRead.Where(v => !v.ExtractedField.HasValue).Select(v => v.Name).ToList();
        referenceNames.Should().NotBeEmpty("the fixture must carry the reference values the writer stores, or this test proves nothing");
        finding.ValuesRead.Any(v => !v.ExtractedField.HasValue && Guid.TryParse(v.Value, out _))
            .Should().BeTrue("a stored reference value is an identifier");

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(RealEntry(finding, 0)));
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        var item = outcome!.Items.Should().ContainSingle().Subject;
        item.LineNumber.Should().Be(3);
        item.ValuesRead.Should().Contain(new FirmOutcomeValueRead("line.rate", "650.00"), "the rate extracted from the firm's own invoice is shown");
        item.ValuesRead!.Count(v => Guid.TryParse(v.Value, out _)).Should().Be(0, "no value is an identifier");
        item.ValuesRead!.Select(v => v.Name).Should().NotContain(referenceNames, "no value the handler read without an extracted field is projected");
        item.ValuesRead!.Select(v => v.Name).Should().OnlyContain(n => finding.ValuesRead.Any(v => v.Name == n && v.ExtractedField.HasValue));
        var json = JsonSerializer.Serialize(outcome);
        json.Should().NotContain("07100000-0000-4000-8000-0000000000c1").And.NotContain("07100000-0000-4000-8000-0000000000c2");
        Regex.IsMatch(json, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").Should().BeFalse();
    }

    [Fact(DisplayName = "130c fix 1 (F1-a): a stored value with no field, or whose text is an identifier, is never projected; a value with a field is")]
    public async Task Rejected_AValueWithNoField_OrAnIdentifierText_IsDropped()
    {
        var entry = Stored("ApprovedRate", VersionA, 0, values: [("line.rate", "450.00"), ("reference.value", "REF-1"), ("line.timekeeperName", "11111111-2222-4333-8444-555555555555")]);
        var values = (JsonArray)entry["valuesRead"]!;
        ((JsonObject)values[1]!)["field"] = null;                       // read from the system's own tables
        var emptyField = new JsonObject { ["name"] = "empty.field", ["value"] = "X-1", ["field"] = "  " };
        values.Add(emptyField);
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entry));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Single().ValuesRead.Should().Equal(new FirmOutcomeValueRead("line.rate", "450.00"));
        JsonSerializer.Serialize(outcome).Should().NotContain("REF-1").And.NotContain("X-1").And.NotContain("11111111-2222");
    }

    [Fact(DisplayName = "130c fix 1 (F1-b): two real RequiredData missing-field findings project two items, each naming its own missing field with an empty value")]
    public async Task Rejected_TwoRealRequiredDataFindings_EachNameTheirOwnMissingField()
    {
        var facts = BillingHandlerKit.Facts(BillingHandlerKit.Fee(0)) with
        {
            Invoice = BillingHandlerKit.Invoice() with { FirmInvoiceNumber = null, InvoiceDate = null },
        };
        var findings = BillingHandlerKit.Run(BillingHandlerKit.Rule("""{"type":"RequiredData","fields":["firmInvoiceNumber","invoiceDate"]}"""), facts);
        findings.Should().HaveCount(2);
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(findings.Select((f, i) => (JsonNode?)RealEntry(f, i)).ToArray()));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Select(i => i.ValuesRead!.Single()).Should().BeEquivalentTo(
            [new FirmOutcomeValueRead("invoice.firmInvoiceNumber", ""), new FirmOutcomeValueRead("invoice.invoiceDate", "")]);
    }

    [Fact(DisplayName = "130c fix 1 (F1-b): a Duplicate or withheld finding still projects no value, empty or not")]
    public async Task Rejected_ADuplicateOrWithheldFinding_ProjectsNoEmptyValueEither()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("Duplicate", VersionA, null, values: ("invoice.firmInvoiceNumber", null)),
            Stored("RequiredData", VersionB, 1, withheld: true, values: ("line.narrative", null))));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        outcome.Items.Should().OnlyContain(i => i.ValuesRead!.Count == 0);
    }

    [Fact(DisplayName = "130c fix 1 (K1): a fault of the parser itself gives the status only with the stable UNREADABLE code, never an exception")]
    public async Task Rejected_AParserFault_IsStatusOnly_WithTheUnreadableCode()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(Stored("ApprovedRate", VersionA, 0, values: ("line.rate", "450.00"))));

        var outcome = await world.Projection((_, _, _) => throw new InvalidOperationException("SECRET parser fault"))
            .GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));
        world.Logger.Lines.Should().ContainSingle(l => l.Level == LogLevel.Warning && l.Message.Contains(FirmRejectionFindings.UnreadableCode));
        world.Logger.Lines.Where(l => l.Level == LogLevel.Warning).Select(l => l.Message).Should().NotContain(m => m.Contains("SECRET"));
    }

    [Fact(DisplayName = "130c fix 1 (K1): the caller's own cancellation during the parse is not swallowed")]
    public async Task Rejected_ACancellationDuringTheParse_Propagates()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(Stored("ApprovedRate", VersionA, 0, values: ("line.rate", "450.00"))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => world.Projection((_, _, _) => throw new OperationCanceledException(cts.Token))
            .GetForSubmissionAsync(SubmissionId, invoiceDecided: true, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(DisplayName = "130c fix 1 (K2): a hold-only type, or a reason the type cannot reject on, is a damaged entry and is dropped; a real reject-capable reason is kept")]
    public async Task Rejected_OnlyARejectCapableReasonOfARejectingType_IsKept()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("ApprovedTimekeeper", VersionA, 1, reason: "approved_timekeeper.not_approved", values: ("line.timekeeperName", "Jane")), // hold-only
            Stored("ApprovedRate", VersionA, 2, reason: BillingReasonCodes.RateNoRateInForce, values: ("line.rate", "450.00")),              // not reject-capable
            Stored("ApprovedRate", VersionA, 3, reason: "rate.over", values: ("line.rate", "450.00")),                                      // not a reason at all
            Stored("ApprovedRate", VersionA, 4, reason: BillingReasonCodes.RateAboveApproved, values: ("line.rate", "450.00"))));            // real
        world.Clause = _ => FlagOnText;

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.LineNumber.Should().Be(5);
        Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode)).Which.Should().Contain("3 malformed");
    }

    [Fact(DisplayName = "130c fix 1 (K3): a value name over the cap is dropped; one at the cap is kept")]
    public async Task Rejected_AValueNameOverTheCap_IsDropped()
    {
        var atCap = new string('n', FirmRejectionFindings.MaxValueNameLength);
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("ApprovedRate", VersionA, 0, values: [(atCap, "1"), (atCap + "x", "2")])));

        var item = (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None))!.Items.Single();

        item.ValuesRead.Should().Equal(new FirmOutcomeValueRead(atCap, "1"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 130c fix round 2
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130c fix 2 (K1, must-fire): a value whose field the AI assigned is never projected; a firm-billed code and a plain extracted field are")]
    public async Task Rejected_AnAiAssignedValue_IsNeverProjected()
    {
        var entry = Stored("ApprovedRate", VersionA, 0, values:
        [
            ("line.rate", "450.00"),
            ("line.taskCode", "AI-TASK"),
            ("line.activityCode", "AI-ACTIVITY"),
            ("line.expenseCode", "AI-EXPENSE"),
            ("observation.blockBilling", "AI-OBSERVATION"),
            ("observation.padded", "AI-PADDED"),
            ("line.billedTaskCode", "L110"),
        ]);
        var fields = new[]
        {
            BillingFieldNames.Rate, BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode, BillingFieldNames.ExpenseCode,
            BillingFieldNames.ObservationBlockBilling, " " + BillingFieldNames.ObservationVagueDescription + " ", BillingFieldNames.BilledTaskCode,
        };
        var values = (JsonArray)entry["valuesRead"]!;
        for (var i = 0; i < fields.Length; i++)
        {
            ((JsonObject)values[i]!)["field"] = fields[i];
        }

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entry));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Single().ValuesRead.Should().Equal(
            new FirmOutcomeValueRead("line.rate", "450.00"), new FirmOutcomeValueRead("line.billedTaskCode", "L110"));
        JsonSerializer.Serialize(outcome).Should().NotContain("AI-");
    }

    // ── K5: reachability through the real decision (SubmissionRejectionRule.Decide) ──

    private static readonly EbillingSettings DecisionSettings = new() { ReliabilityThreshold = 0.90m, ReferenceMissingCap = 0.80m, ArithmeticFailCap = 0.50m };

    private static JsonObject DecidedEntry(FindingDecision decided)
    {
        var entry = InvoiceSubmissionPersister.FindingEntryJson(decided.Index, decided.Finding, decided, scrub: false);
        entry["policyVersionId"] = VersionA.ToString("D");
        return entry;
    }

    [Fact(DisplayName = "130c fix 2 (K5): an ApprovedRate finding carrying the credit-test placeholders is held by the real decision, never rejected, so it is not projected")]
    public async Task Rejected_AnApprovedRateFindingWithCreditTestPlaceholders_IsHeldByTheDecision_AndNeverProjected()
    {
        // A fee line with neither hours nor amount: the credit test has nothing to judge, so the finding lists both as unknown reads.
        var line = BillingHandlerKit.Fee(2) with { BilledRate = 650m, ApprovedRate = BillingHandlerKit.Rate(575m), Hours = null, Amount = null };
        var finding = BillingHandlerKit.Run(
                BillingHandlerKit.Rule("""{"type":"ApprovedRate","tolerancePercent":0}"""), BillingHandlerKit.Facts(line))
            .Should().ContainSingle().Subject;
        finding.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);
        var placeholders = finding.ValuesRead.Where(v => v.ExtractedField.HasValue && v.Confidence is null && v.Value is null).ToList();
        placeholders.Should().HaveCount(2, "the credit test lists hours and amount as null-valued reads with unknown confidence");

        var decision = SubmissionRejectionRule.Decide([finding], _ => (int)BillingConsequence.Reject, DecisionSettings);

        decision.Outcome.Should().Be(SubmissionOutcome.Accepted);
        var held = decision.Findings.Should().ContainSingle().Subject;
        held.Rejects.Should().BeFalse();
        held.HeldBecause.Should().Be(SubmissionHoldReasons.ReadFieldConfidenceUnknown);

        // Stored as the persister stores it, beside a finding that did reject: only the rejecting one is projected.
        var rejecting = BillingHandlerKit.Run(
            BillingHandlerKit.Rule("""{"type":"RequiredData","fields":["firmInvoiceNumber"]}"""),
            BillingHandlerKit.Facts(BillingHandlerKit.Fee(0)) with { Invoice = BillingHandlerKit.Invoice() with { FirmInvoiceNumber = null } });
        var rejectingDecision = SubmissionRejectionRule.Decide(rejecting, _ => (int)BillingConsequence.Reject, DecisionSettings);
        rejectingDecision.Findings.Should().ContainSingle().Which.Rejects.Should().BeTrue();

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(DecidedEntry(decision.Findings[0]), DecidedEntry(rejectingDecision.Findings[0] with { Index = 1 })));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        var shown = outcome!.Items.Should().ContainSingle("the held ApprovedRate finding is a Flag, not a rejection").Subject;
        shown.ValuesRead.Should().ContainSingle().Which.Name.Should().Be("invoice.firmInvoiceNumber");
        shown.LineNumber.Should().BeNull("the one shown finding is the RequiredData rejection, which is about the invoice, not a line");
    }

    [Fact(DisplayName = "130c fix 2 (K5): a RequiredData missing-field finding with a known confidence rejects through the real decision, and projects the empty value")]
    public async Task Rejected_ARequiredDataMissingFieldWithKnownConfidence_RejectsThroughTheDecision_AndProjectsTheEmptyValue()
    {
        var facts = BillingHandlerKit.Facts(BillingHandlerKit.Fee(0)) with
        {
            Invoice = BillingHandlerKit.Invoice() with { FirmInvoiceNumber = null },
        };
        var findings = BillingHandlerKit.Run(BillingHandlerKit.Rule("""{"type":"RequiredData","fields":["firmInvoiceNumber"]}"""), facts);
        var finding = findings.Should().ContainSingle().Subject;
        finding.ValuesRead.Should().ContainSingle(v => v.ExtractedField.HasValue && v.Value == null && v.Confidence >= 0.90m,
            "the missing field is an extracted read with no text and a known confidence");

        var decision = SubmissionRejectionRule.Decide(findings, _ => (int)BillingConsequence.Reject, DecisionSettings);

        decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        var decided = decision.DecidingFindings.Should().ContainSingle().Subject;
        decided.HeldBecause.Should().BeNull();

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(DecidedEntry(decided)));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.ValuesRead.Should().Equal(new FirmOutcomeValueRead("invoice.firmInvoiceNumber", ""));
    }

    // ── K6: robustness ──

    [Fact(DisplayName = "130c fix 2 (K6, must-fire): a 201-character value ending in an emoji is cut without splitting the surrogate pair")]
    public async Task Rejected_AValueEndingInAnEmoji_IsTruncatedWithoutSplittingThePair()
    {
        const string emoji = "\U0001F600";
        var tooLong = new string('x', FirmRejectionFindings.MaxValueTextLength - 1) + emoji; // 199 + 2 = 201 characters; the cut would fall inside the pair
        tooLong.Length.Should().Be(201);
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(Stored("ApprovedRate", VersionA, 0, values: ("line.narrative", tooLong))));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        var value = outcome!.Items.Single().ValuesRead!.Single().Value;
        value.Should().Be(new string('x', FirmRejectionFindings.MaxValueTextLength - 1) + "...");
        value.Any(char.IsSurrogate).Should().BeFalse("no half of a pair is left");
        var strict = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);
        var act = () => strict.GetBytes(JsonSerializer.Serialize(outcome));
        act.Should().NotThrow("the outcome serializes to valid text");
    }

    [Fact(DisplayName = "130c fix 2 (K6, must-not-fire): a whole emoji inside the cut is kept; a value at the cap is not touched")]
    public async Task Rejected_AnEmojiInsideTheCut_IsKept()
    {
        const string emoji = "\U0001F600";
        var inside = new string('x', FirmRejectionFindings.MaxValueTextLength - 2) + emoji + "y";   // 198 + 2 + 1 = 201: the cut falls after the pair
        var atCap = new string('z', FirmRejectionFindings.MaxValueTextLength - 2) + emoji;           // exactly 200
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(Stored("ApprovedRate", VersionA, 0, values: [("line.a", inside), ("line.b", atCap)])));

        var values = (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None))!
            .Items.Single().ValuesRead!;

        values[0].Value.Should().Be(new string('x', FirmRejectionFindings.MaxValueTextLength - 2) + emoji + "...");
        values[1].Value.Should().Be(atCap);
    }

    private const string BadSurrogateMarker = "BADSURROGATEMARKER";

    [Fact(DisplayName = "130c fix 2 (K6, must-fire): one entry whose value cannot be decoded is dropped and counted on its own; the others are shown and the document stays readable")]
    public async Task Rejected_OneEntryThatCannotBeDecoded_DropsOnlyThatEntry()
    {
        var document = RejectedDocument(
            Stored("ApprovedRate", VersionA, 0, values: ("line.rate", "450.00")),
            Stored("ApprovedRate", VersionA, 1, values: ("line.rate", BadSurrogateMarker)),
            Stored("ApprovedRate", VersionA, 2, values: ("line.rate", "475.00")))
            .Replace(BadSurrogateMarker, "\\ud800"); // a lone surrogate escape: the document parses, reading the string fails
        var world = new World();
        world.AddRejectedSubmission(document);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Degraded.Should().BeFalse();
        outcome.Items.Select(i => i.LineNumber).Should().Equal(1, 3);
        outcome.Items.SelectMany(i => i.ValuesRead!).Select(v => v.Value).Should().Equal("450.00", "475.00");
        Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode)).Which.Should().Contain("1 stored finding(s)").And.Contain("1 malformed");
        Warnings(world).Should().NotContain(m => m.Contains(FirmRejectionFindings.UnreadableCode));
        outcome.NotShownCount.Should().Be(1, "the dropped entry was a rejecting one");
    }

    [Fact(DisplayName = "130c fix 2 (K6, must-not-fire): a document that is malformed as a whole is still unreadable: status only, the stable UNREADABLE code, no entry shown")]
    public async Task Rejected_AWholeDocumentThatCannotBeParsed_IsStillUnreadable()
    {
        var good = Stored("ApprovedRate", VersionA, 0, values: ("line.rate", "450.00")).ToJsonString();
        var stored = "{ \"kind\": \"AutoRejected\", \"findings\": [ " + good + ", { \"ruleType\": \"ApprovedRate\", \"valuesRead\": [ SECRET-BROKEN";
        var world = new World();
        world.AddRejectedSubmission(stored);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false));
        world.Logger.Lines.Should().ContainSingle(l => l.Level == LogLevel.Warning && l.Message.Contains(FirmRejectionFindings.UnreadableCode));
        Warnings(world).Should().NotContain(m => m.Contains(FirmRejectionFindings.DroppedCode) || m.Contains("SECRET"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 130c fix round 3
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "130c fix 3 (K-a, must-fire): an AI-assigned field is skipped whatever its casing; a name that only resembles one is not an extracted field, so it is not projected either (fix 4)")]
    public async Task Rejected_TheAiAssignedSkip_IgnoresCase_ButNeedsTheExactNameOrPrefix()
    {
        var entry = Stored("ApprovedRate", VersionA, 0, values:
        [
            ("line.lower", "AI-LOWER"),
            ("line.upper", "AI-UPPER"),
            ("line.observation", "AI-OBSERVATION"),
            ("line.observationUpper", "AI-OBSERVATION-UPPER"),
            ("line.noDot", "NO-DOT"),
            ("line.billed", "L110"),
            ("line.rate", "450.00"),
        ]);
        var fields = new[] { "taskcode", "ACTIVITYCODE", "observation.x", " OBSERVATION.BlockBilling ", "observationx", "BilledTaskCode", "Rate" };
        var values = (JsonArray)entry["valuesRead"]!;
        for (var i = 0; i < fields.Length; i++)
        {
            ((JsonObject)values[i]!)["field"] = fields[i];
        }

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entry));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Single().ValuesRead.Should().Equal(
            new FirmOutcomeValueRead("line.billed", "L110"), new FirmOutcomeValueRead("line.rate", "450.00"));
        JsonSerializer.Serialize(outcome).Should().NotContain("AI-").And.NotContain("NO-DOT");
    }

    [Fact(DisplayName = "130c fix 4 (must-fire): a value whose field is not a known extracted field is not shown, while its entry still is")]
    public async Task Rejected_AValueWithAnUnknownField_IsNotProjected_ButTheEntryStillIs()
    {
        var entry = Stored("ApprovedRate", VersionA, 4, values:
        [
            ("line.internalScore", "UNKNOWN-FIELD-VALUE"),
            ("line.rate", "450.00"),
            ("line.blank", "BLANK-FIELD-VALUE"),
            ("line.prefixOnly", "PREFIX-VALUE"),
        ]);
        var fields = new[] { "line.internalScore", "Rate", "Rat", BillingFieldNames.ObservationPrefix };
        var values = (JsonArray)entry["valuesRead"]!;
        for (var i = 0; i < fields.Length; i++)
        {
            ((JsonObject)values[i]!)["field"] = fields[i];
        }

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entry));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        var item = outcome!.Items.Should().ContainSingle().Subject;
        item.LineNumber.Should().Be(5);
        item.ValuesRead.Should().Equal(new FirmOutcomeValueRead("line.rate", "450.00"));
        JsonSerializer.Serialize(outcome).Should().NotContain("UNKNOWN-FIELD-VALUE").And.NotContain("BLANK-FIELD-VALUE").And.NotContain("PREFIX-VALUE");
    }

    [Fact(DisplayName = "130c fix 4 (must-not-fire): every extracted field constant still shows its value, whatever its case or padding")]
    public async Task Rejected_EveryExtractedFieldConstant_StillShows_IncludingCaseAndPaddingVariants()
    {
        var extracted = typeof(BillingFieldNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(n => n != BillingFieldNames.ObservationPrefix && !BillingFieldNames.IsAiAssigned(n))
            .ToList();
        extracted.Should().NotBeEmpty().And.Contain([BillingFieldNames.Rate, BillingFieldNames.Hours, BillingFieldNames.BilledTaskCode]);

        // Each constant three ways: as named, upper-cased, and lower-cased with padding. Entries hold at most MaxValuesPerFinding values.
        var fields = extracted.SelectMany(n => new[] { n, n.ToUpperInvariant(), "  " + n.ToLowerInvariant() + " " }).ToList();
        var chunks = fields.Chunk(FirmRejectionFindings.MaxValuesPerFinding).ToList();
        var entries = new List<JsonNode?>();
        var expected = new List<string>();
        for (var c = 0; c < chunks.Count; c++)
        {
            var entry = Stored("ApprovedRate", VersionA, c, values: chunks[c].Select((f, i) => ($"c{c}v{i}", (string?)$"SHOWN-{c}-{i}")).ToArray());
            var array = (JsonArray)entry["valuesRead"]!;
            for (var i = 0; i < chunks[c].Length; i++)
            {
                ((JsonObject)array[i]!)["field"] = chunks[c][i];
                expected.Add($"c{c}v{i}");
            }

            entries.Add(entry);
        }

        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entries.ToArray()));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.SelectMany(item => item.ValuesRead ?? []).Select(v => v.Name).Should().Equal(expected);
    }

    [Fact(DisplayName = "130c fix 3 (K-b, must-fire): 3 valid and 1 malformed rejecting entry show 3 and count 1 not shown")]
    public async Task Rejected_ADroppedRejectingEntry_IsCounted_NextToTheShownOnes()
    {
        var malformed = Stored("ApprovedRate", VersionA, 9, values: ("secret.name", "SECRET-VALUE"));
        malformed.Remove("policyVersionId");
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("ApprovedRate", VersionA, 0), Stored("ApprovedRate", VersionA, 1), malformed, Stored("ApprovedRate", VersionA, 2)));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Select(i => i.LineNumber).Should().Equal(1, 2, 3);
        outcome.NotShownCount.Should().Be(1);
        outcome.Degraded.Should().BeFalse();
        JsonSerializer.Serialize(outcome, WebJson).Should().Contain("\"notShownCount\":1").And.NotContain("SECRET");
    }

    [Fact(DisplayName = "130c fix 3 (K-b, must-fire): every kind of dropped rejecting entry is counted (malformed, unknown type, hold-only type, non-rejecting reason, undecodable)")]
    public async Task Rejected_EveryKindOfDroppedRejectingEntry_IsCounted()
    {
        var undecodable = Stored("ApprovedRate", VersionA, 5, values: ("line.rate", BadSurrogateMarker));
        var noSubject = Stored("ApprovedRate", VersionA, 6);
        noSubject.Remove("subject");
        var document = RejectedDocument(
            Stored("SECRET-NEW-RULE-TYPE", VersionA, 1),                                                // unknown type
            Stored("ApprovedTimekeeper", VersionA, 2, reason: "approved_timekeeper.not_approved"),      // hold-only
            Stored("ApprovedRate", VersionA, 3, reason: BillingReasonCodes.RateNoRateInForce),          // reason cannot reject
            noSubject,                                                                                  // malformed
            undecodable)                                                                                // cannot be decoded
            .Replace(BadSurrogateMarker, "\\ud800");
        var world = new World();
        world.AddRejectedSubmission(document);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().BeEmpty();
        outcome.NotShownCount.Should().Be(5);
    }

    [Fact(DisplayName = "130c fix 3 (K-b, must-fire): when every rejecting entry is malformed the outcome shows none and counts them all (status only, not degraded)")]
    public async Task Rejected_AllRejectingEntriesMalformed_ShowsNone_AndCountsAll()
    {
        var entries = Enumerable.Range(0, 4).Select(i =>
        {
            var entry = Stored("ApprovedRate", VersionA, i, values: ("secret.name", "SECRET-VALUE"));
            entry["policyVersionId"] = "not a guid";
            return (JsonNode?)entry;
        }).ToArray();
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(entries));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-1", "Rejected", null, null, [], false, 4));
        JsonSerializer.Serialize(outcome, WebJson).Should().Contain("\"notShownCount\":4").And.NotContain("SECRET");
        Warnings(world).Should().ContainSingle(m => m.Contains(FirmRejectionFindings.DroppedCode)).Which.Should().Contain("4 malformed");
    }

    [Fact(DisplayName = "130c fix 3 (K-b, must-not-fire): a held or non-rejecting entry, or a non-rejecting document, adds nothing to the count")]
    public async Task Rejected_EntriesThatDoNotReject_AreNeverCounted()
    {
        var held = Stored("ApprovedRate", VersionA, 1, rejects: false);
        held["heldBecause"] = "read_field_confidence_unknown";
        var notRejectingMalformed = Stored("ApprovedRate", VersionA, 2, rejects: false);
        notRejectingMalformed.Remove("policyVersionId");
        var noRejectsMember = Stored("ApprovedRate", VersionA, 3);
        noRejectsMember.Remove("rejects");
        var rejectsNotBool = Stored("ApprovedRate", VersionA, 4);
        rejectsNotBool["rejects"] = "yes";
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            held,
            notRejectingMalformed,
            noRejectsMember,
            rejectsNotBool,
            Stored("SECRET-NEW-NONREJECT", VersionA, 5, rejects: false),                                // unknown type that does not reject
            JsonValue.Create("not an entry"),
            Stored("ApprovedRate", VersionA, 6)));

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        outcome!.Items.Should().ContainSingle().Which.LineNumber.Should().Be(7);
        outcome.NotShownCount.Should().BeNull();
        JsonSerializer.Serialize(outcome, WebJson).Should().NotContainEquivalentOf("notShownCount");

        var blocked = new World();
        var broken = Stored("ApprovedRate", VersionA, 1);
        broken.Remove("policyVersionId");
        blocked.AddRejectedSubmission(StoredDocument("Blocked", broken, Stored("SECRET-NEW-RULE-TYPE", VersionA, 2)));
        var hold = await blocked.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);
        hold!.Items.Should().BeEmpty();
        hold.NotShownCount.Should().BeNull("an internal hold is not a rejection the firm was given");
    }

    // ── 130d: the firm-safe reason text ──

    /// <summary>Every reject-capable (type, reason) of the catalogue, read from the catalogue.</summary>
    private static IEnumerable<(string RuleType, string Reason)> CatalogueReasons() =>
        Enum.GetValues<RuleType>().Where(t => RuleDomains.IsBilling(t) && !BillingRuleCatalogue.IsSystemType(t))
            .SelectMany(t => BillingRuleCatalogue.RejectCapableReasons(t).OrderBy(r => r, StringComparer.Ordinal).Select(r => (t.ToString(), r)));

    public static IEnumerable<object[]> CatalogueReasonCases() => CatalogueReasons().Select(c => new object[] { c.RuleType, c.Reason });

    [Theory(DisplayName = "130d must-fire: every reject-capable reason shows its table text and its code, with the flag on and with it off")]
    [MemberData(nameof(CatalogueReasonCases))]
    public async Task Rejected_EveryRejectCapableReason_ShowsItsTableText_FlagOnAndOff(string ruleType, string reason)
    {
        foreach (var flagOn in new[] { true, false })
        {
            var world = new World();
            world.Clause = _ => flagOn ? FlagOnText : FirmClauseText.None;
            world.AddRejectedSubmission(RejectedDocument(Stored(ruleType, VersionA, 1, reason: reason)));

            var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

            var item = outcome!.Items.Should().ContainSingle().Subject;
            item.ReasonCode.Should().Be(reason);
            item.FirmReasons.Should().Equal([FirmRejectionReasonText.Table[reason]]);
            Warnings(world).Should().BeEmpty();
            if (!flagOn)
            {
                JsonSerializer.Serialize(outcome).Should().NotContain(ClauseQuote).And.NotContain(ClauseSection).And.NotContain(FirmLabel)
                    .And.NotContain("Internal rule set name").And.NotContain("Travel time");
            }
        }
    }

    [Fact(DisplayName = "130d: a non-blank firm reason from the writer's Signal replaces the default, trimmed and cut to 200 characters")]
    public async Task Rejected_ANonBlankFirmReason_ReplacesTheDefault_TrimmedAndCut()
    {
        var world = TwoFindingsWorld();
        world.AddRejectionSignal(VersionA, 2, "   Your rate is above the agreed rate.  ");
        world.AddRejectionSignal(VersionB, null, new string('x', 199) + "\U0001F600" + new string('y', 100)); // the emoji straddles the cut

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items[0].FirmReasons.Should().Equal("Your rate is above the agreed rate.");
        var cut = outcome.Items[1].FirmReasons.Should().ContainSingle().Subject;
        cut.Should().EndWith("...").And.StartWith(new string('x', 199));
        cut.Length.Should().BeLessThanOrEqualTo(FirmRejectionFindings.MaxValueTextLength + 3);
        Regex.IsMatch(cut, "[\\uD800-\\uDFFF]").Should().BeFalse("a cut inside the pair is stepped back whole, never leaving half of it");
        outcome.Items.Should().OnlyContain(i => i.FirmReasons.Count == 1 && i.ReasonCode != null);
    }

    [Theory(DisplayName = "130d must-not-fire: a blank or whitespace firm reason falls back to the default text")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(null)]
    public async Task Rejected_ABlankFirmReason_FallsBackToTheDefault(string? written)
    {
        var world = TwoFindingsWorld();
        world.AddRejectionSignal(VersionA, 2, written);

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items[0].FirmReasons.Should().Equal(FirmRejectionReasonText.Table[BillingReasonCodes.RateAboveApproved]);
    }

    [Fact(DisplayName = "130d: a reject-capable reason with no table entry fails closed to the generic sentence, with a Warning that names the code")]
    public async Task Rejected_AReasonWithNoEntry_ShowsTheGenericSentence_AndLogsAWarning()
    {
        var onlyRate = new Dictionary<string, string> { [BillingReasonCodes.RateAboveApproved] = FirmRejectionReasonText.Table[BillingReasonCodes.RateAboveApproved] };
        var blankEntries = new Dictionary<string, string>
        {
            [BillingReasonCodes.RateAboveApproved] = "   ",
            [BillingReasonCodes.RequiredFieldMissing] = "",
        };

        foreach (var texts in new[] { onlyRate, blankEntries })
        {
            var world = TwoFindingsWorld();
            var projection = new FirmFindingsProjection(
                world.Entities.Object, (_, _, _) => Task.FromResult(FirmClauseText.None), _ => Task.FromResult(world.WriterId), world.Logger)
            {
                RejectionReasonTexts = texts,
            };

            var outcome = await projection.GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

            var missing = outcome!.Items.Where(i => i.FirmReasons.SequenceEqual([FirmRejectionReasonText.GenericSentence])).ToList();
            missing.Should().NotBeEmpty();
            outcome.Items.Should().OnlyContain(i => i.FirmReasons.Count == 1 && !string.IsNullOrWhiteSpace(i.FirmReasons[0]));
            var warnings = Warnings(world).Where(w => w.Contains(FirmRejectionReasonText.MissingEntryCode)).ToList();
            warnings.Should().HaveCount(missing.Count);
            warnings.Should().Contain(w => w.Contains(missing[0].ReasonCode!));
            warnings.Should().NotContain(w => w.Contains("450.00") || w.Contains("400.00") || w.Contains(FirmRejectionReasonText.GenericSentence)
                || w.Contains(FirmRejectionReasonText.Table[BillingReasonCodes.RateAboveApproved]), "the Warning carries the code only, never a value or text");
            outcome.Items.SelectMany(i => i.FirmReasons).Should().NotContain(r => r.Contains(BillingReasonCodes.RateAboveApproved) || r.Contains(BillingReasonCodes.RequiredFieldMissing));
        }

        // Nothing missing, nothing logged.
        var full = TwoFindingsWorld();
        await full.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);
        Warnings(full).Should().NotContain(w => w.Contains(FirmRejectionReasonText.MissingEntryCode));
    }

    [Fact(DisplayName = "130d: a non-rejecting item (a reviewer-decided line) has no reasonCode and keeps its firm reasons exactly as before")]
    public async Task ANonRejectingItem_IsUnchanged_ByTheReasonText()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");

        var item = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items.Single();

        item.ReasonCode.Should().BeNull();
        item.FirmReasons.Should().Equal("Firm reason.");
        JsonSerializer.Serialize(item).Should().NotContain("reasonCode");
    }

    [Fact(DisplayName = "130d: on the wire a rejection finding carries reasonCode and the one firmReasons entry, and no citation when the flag is off")]
    public async Task Rejected_TheWireShape_CarriesTheCodeAndTheText()
    {
        var world = TwoFindingsWorld();
        world.Clause = _ => FirmClauseText.None;

        var json = JsonSerializer.Serialize(await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None), WebJson);

        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("items")[0];
        first.GetProperty("reasonCode").GetString().Should().Be(BillingReasonCodes.RateAboveApproved);
        first.GetProperty("firmReasons").EnumerateArray().Select(e => e.GetString()).Should().Equal(FirmRejectionReasonText.Table[BillingReasonCodes.RateAboveApproved]);
        first.GetProperty("citations").GetArrayLength().Should().Be(0);
    }

    [Fact(DisplayName = "130d K3: a Duplicate rejection reads byte-identically whether its detail is withheld or in scope (reasonCode and description)")]
    public async Task Rejected_ADuplicate_HasOneUniformReason_WithheldOrNot()
    {
        async Task<FirmOutcome> Project(bool withheld, string? signalText = null)
        {
            var world = new World();
            if (signalText is not null)
            {
                world.AddRejectionSignal(VersionA, null, signalText); // the writer's Signal for this finding
            }

            // The writer's real difference between the two: a withheld group stores its values without text; an in-scope one stores them with it.
            world.AddRejectedSubmission(RejectedDocument(
                Stored("Duplicate", VersionA, null, withheld: withheld, reason: BillingReasonCodes.DuplicateInvoiceNumber,
                    values: [("invoice.firmInvoiceNumber", withheld ? null : "INV-100"), ("duplicateOf.trackingNumber", withheld ? null : "TRK-0")])));
            world.Clause = _ => FlagOnText;
            return (await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None))!;
        }

        var hidden = await Project(withheld: true);
        var shown = await Project(withheld: false);
        // F-1: only the in-scope Signal carries firm-reason text; a Duplicate never takes the override, so the outcomes still match.
        var shownWithText = await Project(withheld: false, signalText: "A Signal-written explanation that must not replace the Duplicate text.");

        var expected = "This invoice number has already been used with this client. Please check it or contact the client.";
        foreach (var outcome in new[] { hidden, shown })
        {
            var item = outcome.Items.Should().ContainSingle().Subject;
            item.ReasonCode.Should().Be(BillingReasonCodes.DuplicateInvoiceNumber);
            item.FirmReasons.Should().Equal([expected]);
        }

        JsonSerializer.Serialize(hidden.Items[0].FirmReasons).Should().Be(JsonSerializer.Serialize(shown.Items[0].FirmReasons));
        hidden.Items[0].ReasonCode.Should().Be(shown.Items[0].ReasonCode);

        // What else could tell the two cases apart: the whole response, member by member. Nothing in the outcome differs (the projection shows no
        // value for a Duplicate either way; NotShownCount, the item count, citations, line number and degraded flag are the same).
        hidden.NotShownCount.Should().Be(shown.NotShownCount);
        hidden.Degraded.Should().Be(shown.Degraded);
        hidden.Items.Count.Should().Be(shown.Items.Count);
        JsonSerializer.Serialize(hidden, WebJson).Should().Be(JsonSerializer.Serialize(shown, WebJson));
        JsonSerializer.Serialize(hidden, WebJson).Should().Be(JsonSerializer.Serialize(shownWithText, WebJson));
    }

    [Fact(DisplayName = "130d F-1: a Duplicate whose Signal carries firm-reason text still shows exactly the table text; a non-Duplicate rejection takes the override")]
    public async Task Rejected_ADuplicate_NeverTakesTheOverride_ANonDuplicateDoes()
    {
        var world = new World();
        world.AddRejectedSubmission(RejectedDocument(
            Stored("Duplicate", VersionA, null, reason: BillingReasonCodes.DuplicateInvoiceNumber),
            Stored("ApprovedRate", VersionB, 1, reason: BillingReasonCodes.RateAboveApproved)));
        world.AddRejectionSignal(VersionA, null, "Override text for the Duplicate.");
        world.AddRejectionSignal(VersionB, 1, "Override text for the rate.");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items.Should().HaveCount(2);
        var rate = outcome.Items.Single(i => i.ReasonCode == BillingReasonCodes.RateAboveApproved);
        var duplicate = outcome.Items.Single(i => i.ReasonCode == BillingReasonCodes.DuplicateInvoiceNumber);
        rate.FirmReasons.Should().Equal(["Override text for the rate."]);
        duplicate.FirmReasons.Should().Equal([FirmRejectionReasonText.Table[BillingReasonCodes.DuplicateInvoiceNumber]]);
    }

    private const string DirtyReason = "Your\u202E rate\u0000 is\u200F too\u2066 high\u007F\u0085\t.\r\n";

    [Fact(DisplayName = "130d K-b: control and bidi characters are stripped from a rejection finding's firm reason; control-only text falls back to the table")]
    public async Task Rejected_TheFirmReason_HasControlAndBidiCharactersStripped_AndControlOnlyFallsBack()
    {
        var world = TwoFindingsWorld();
        world.AddRejectionSignal(VersionA, 2, DirtyReason);                       // the rate finding
        world.AddRejectionSignal(VersionB, null, "\u0001\u202E\u2069\u200F  \u0085"); // the required-data finding: nothing printable

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Items[0].FirmReasons.Should().Equal(["Your rate is too high."]);
        outcome.Items[1].FirmReasons.Should().Equal([FirmRejectionReasonText.Table[BillingReasonCodes.RequiredFieldMissing]]);
        outcome.Items.SelectMany(i => i.FirmReasons).Should().OnlyContain(t => !Regex.IsMatch(t, "[ --‎‏‪-‮⁦-⁩]"));
    }

    [Fact(DisplayName = "130d K-b: control and bidi characters are stripped from an invoice item's firm reason, which is now capped; control-only text is omitted")]
    public async Task AnInvoiceItem_FirmReason_HasControlAndBidiCharactersStripped_Capped_AndControlOnlyIsOmitted()
    {
        var world = new World();
        world.AddReducedWithCitation(1, "Reason.", VersionA, DirtyReason);
        world.AddReducedWithCitation(2, "Reason.", VersionA, "\u0001\u202E\u2069 \u0085");
        world.AddReducedWithCitation(3, "Reason.", VersionA, new string('z', 199) + "\U0001F600" + new string('y', 100));

        var items = (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!.Items;

        items.Should().HaveCount(3);
        var texts = items.SelectMany(i => i.FirmReasons).ToList();
        texts.Should().Contain("Your rate is too high.");
        items.Count(i => i.FirmReasons.Count == 0).Should().Be(1, "the control-only text is omitted, never substituted");
        var cut = texts.Single(t => t.StartsWith("zzzz", StringComparison.Ordinal));
        cut.Should().EndWith("...");
        cut.Length.Should().BeLessThanOrEqualTo(FirmRejectionFindings.MaxValueTextLength + 3);
        Regex.IsMatch(cut, "[\\uD800-\\uDFFF]").Should().BeFalse();
    }

    [Theory(DisplayName = "130d fix 3: one rule - every Control, Format, LineSeparator and ParagraphSeparator character is dropped from a firm reason; ordinary text is kept")]
    [InlineData("\u061C")]   // Arabic letter mark (Format)
    [InlineData("\uFEFF")]   // byte order mark / zero-width no-break space (Format)
    [InlineData("\u200B")]   // zero-width space (Format)
    [InlineData("\u200D")]   // zero-width joiner (Format)
    [InlineData("\u2060")]   // word joiner (Format)
    [InlineData("\u00AD")]   // soft hyphen (Format)
    [InlineData("\u2028")]   // line separator
    [InlineData("\u2029")]   // paragraph separator
    [InlineData("\u202E")]   // right-to-left override (Format)
    [InlineData("\u0085")]   // next line (Control)
    public void TheFirmReasonFilter_DropsEveryInvisibleCategory(string invisible)
    {
        FirmRejectionFindings.ShownFirmReason($"Your{invisible} rate{invisible} is high.{invisible}").Should().Be("Your rate is high.");
        FirmRejectionFindings.ShownFirmReason($"{invisible}{invisible} ").Should().BeNull("nothing printable is left");
        FirmRejectionFindings.ShownFirmReason("Plain text: it's 100% fine, \u00E9\u00E8 \u4E2D\u6587 \U0001F600.").Should().Be("Plain text: it's 100% fine, \u00E9\u00E8 \u4E2D\u6587 \U0001F600.");
    }
}
