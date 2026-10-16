using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="TrackingNumberAllocator"/> (ebilling-r1 task 045, FR-10, D-37): the only writer of a submission's tracking number and sequence.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): this decides whether two firms' (or one firm's two parallel) submissions can share a number, and whether a
/// lost race double-writes or loses a row. The Dataverse boundary is a Moq double of <see cref="IGenericEntityService"/> backed by a small
/// in-memory table that evaluates the allocator's own query (so a dropped firm filter or an added state filter changes the outcome, not just
/// a call count) and enforces the (firm, tracking number) alternate key the way Dataverse does: case-insensitively, refusing the write whole
/// with the wrapped <c>0x80060892</c> fault the SDK client raises. The same property against the real key is proven live in
/// <c>TrackingNumberAllocatorLiveTests</c> (tests/integration/data-mutation).
/// </remarks>
public sealed class TrackingNumberAllocatorTests
{
    private static readonly Guid FirmA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid FirmB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly SubmissionStore _store = new();

    private TrackingNumberAllocator CreateService(ILogger<TrackingNumberAllocator>? logger = null) =>
        new(_store.Entities.Object, logger ?? NullLogger<TrackingNumberAllocator>.Instance);

    // ── Numbering (acceptance 1 and 2) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstSubmissionOfAFirmInvoiceNumber_GetsDotOne()
    {
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Allocated);
        result.TrackingNumber.Should().Be("INV-002.1");
        result.Sequence.Should().Be(1);
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
        _store.Row(id).GetAttributeValue<int?>("sprk_submissionsequence").Should().Be(1);
        _store.Row(id).GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be("INV-002");
        _store.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task SecondSubmissionOfTheSameFirmInvoiceNumber_GetsDotTwo()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.2");
        result.Sequence.Should().Be(2);
    }

    [Fact]
    public async Task WithDotOneAndDotTwoHeld_TheNextIsDotThree_AndAClosedSubmissionStillOwnsItsNumber()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        // Returned to Firm / Rejected: statecode 1. A closed attempt keeps its number, so a state filter would hand .2 out again.
        _store.AddNumbered(FirmA, "INV-002", 2, stateCode: 1);
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.3");
        result.Sequence.Should().Be(3);
    }

    [Fact]
    public async Task TheSameFirmInvoiceNumberUnderADifferentFirm_StartsAtDotOne()
    {
        _store.AddNumbered(FirmA, "INV-002", 1);
        _store.AddNumbered(FirmA, "INV-002", 2);
        var id = _store.AddRow(FirmB);

        var result = await CreateService().AllocateAsync(id, FirmB, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.1");
        result.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task TheSequenceIsPerFirmInvoiceNumber_NotGlobalAndNotPerFirm()
    {
        _store.AddNumbered(FirmA, "INV-002", 5);
        _store.AddNumbered(FirmA, "INV-003", 9);
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-004");

        result.TrackingNumber.Should().Be("INV-004.1");
    }

    [Fact]
    public async Task TheFirmInvoiceNumberIsTrimmedAsSubmitted_AndMatchedWithoutRegardToCase()
    {
        _store.AddNumbered(FirmA, "inv-002", 1);
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, "  INV-002  ");

        // Dataverse compares strings, and its key compares tracking numbers, without regard to case: INV-002.1 and inv-002.1 are one number.
        result.TrackingNumber.Should().Be("INV-002.2");
        _store.Row(id).GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be("INV-002");
    }

    // ── Retry rule (acceptance 3 and 4) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADuplicateOnTheFirstWrite_ReReadsAndWritesTheNextNumber()
    {
        var id = _store.AddRow(FirmA);
        // Another request commits INV-002.1 between this call's read and its write.
        _store.BeforeUpdate = (call, fields) =>
        {
            if (call == 1)
            {
                _store.AddNumbered(FirmA, "INV-002", 1);
            }
        };

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        result.Outcome.Should().Be(TrackingNumberOutcome.Allocated);
        result.TrackingNumber.Should().Be("INV-002.2");
        _store.UpdateCalls.Should().Be(2, "the refused write, then the retry");
        _store.QueryCalls.Should().Be(2, "the retry re-reads the highest sequence instead of incrementing a stale one");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.2");
    }

    [Theory]
    [InlineData(4, true, 5)]
    [InlineData(5, false, 5)]
    public async Task TheRetryIsBoundedAtFiveAttempts(int duplicates, bool expectAllocated, int expectedWrites)
    {
        var id = _store.AddRow(FirmA);
        // Every one of the first `duplicates` writes loses to a competitor that takes exactly the number being written.
        _store.BeforeUpdate = (call, fields) =>
        {
            if (call <= duplicates)
            {
                _store.AddNumbered(FirmA, "INV-002", (int)fields["sprk_submissionsequence"]);
            }
        };

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        _store.UpdateCalls.Should().Be(expectedWrites);
        _store.QueryCalls.Should().Be(expectedWrites);
        if (expectAllocated)
        {
            result.Outcome.Should().Be(TrackingNumberOutcome.Allocated);
            result.TrackingNumber.Should().Be("INV-002.5");
        }
        else
        {
            result.Outcome.Should().Be(TrackingNumberOutcome.Refused);
            result.Succeeded.Should().BeFalse();
            result.Reason.Should().Be("ebilling.tracking.unavailable");
            result.TrackingNumber.Should().BeNull();
            result.Sequence.Should().BeNull();
            var row = _store.Row(id);
            row.Contains("sprk_submissionsequence").Should().BeFalse("a refusal writes nothing");
            row.Contains("sprk_firminvoicenumber").Should().BeFalse("a refusal writes nothing");
            row.GetAttributeValue<string>("sprk_trackingnumber").Should().Be(SubmissionStore.Placeholder(id));
        }
    }

    [Fact]
    public async Task AFaultThatIsNotAnAlternateKeyDuplicate_PropagatesOnTheFirstAttempt_WithoutARetry()
    {
        var id = _store.AddRow(FirmA);
        // A timeout may have written the row; a retry could number it twice, so the caller sees the fault.
        _store.UpdateFault = new InvalidOperationException("Failed to update sprk_invoicesubmission record: The operation timed out.");

        var act = () => CreateService().AllocateAsync(id, FirmA, "INV-002");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*timed out*");
        _store.UpdateCalls.Should().Be(1);
        _store.QueryCalls.Should().Be(1, "no re-read: a retry was never attempted");
    }

    [Fact]
    public async Task ADataverseFaultWithAnotherErrorCode_PropagatesOnTheFirstAttempt_WithoutARetry()
    {
        var id = _store.AddRow(FirmA);
        _store.UpdateFault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x8004431A) }, new FaultReason("Attribute value is outside the valid range."));

        var act = () => CreateService().AllocateAsync(id, FirmA, "INV-002");

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        _store.UpdateCalls.Should().Be(1);
    }

    // ── Safe to repeat ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("INV-002")]
    [InlineData("  inv-002 ")]
    public async Task ASubmissionThatAlreadyHoldsANumberForTheSameFirmInvoiceNumber_IsReturnedUnchanged(string requested)
    {
        var id = _store.AddNumbered(FirmA, "INV-002", 2);

        var result = await CreateService().AllocateAsync(id, FirmA, requested);

        result.Outcome.Should().Be(TrackingNumberOutcome.AlreadyAllocated);
        result.Succeeded.Should().BeTrue();
        result.TrackingNumber.Should().Be("INV-002.2");
        result.Sequence.Should().Be(2);
        _store.UpdateCalls.Should().Be(0, "a redelivered job must not renumber the submission .3 and leave .2 as a gap");
        _store.QueryCalls.Should().Be(0);
    }

    [Fact]
    public async Task ARedeliveredCallThatRacesItsOwnEarlierWrite_ComputesTheSameNumber_NotTheNextOne()
    {
        var id = _store.AddRow(FirmA);
        // This call read the row before it was numbered; a concurrent call for the SAME submission then wrote INV-002.1 onto it, before
        // this call's highest-sequence read. Counting the submission's own row would renumber it .2 and leave .1 as a gap.
        _store.BeforeRead = () =>
        {
            _store.NumberRow(id, FirmA, "INV-002", 1);
            return Task.CompletedTask;
        };

        var result = await CreateService().AllocateAsync(id, FirmA, "INV-002");

        result.TrackingNumber.Should().Be("INV-002.1");
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
    }

    [Fact]
    public async Task ASubmissionThatHoldsANumberForADifferentFirmInvoiceNumber_IsNotRenumbered()
    {
        var id = _store.AddNumbered(FirmA, "INV-002", 1);

        var act = () => CreateService().AllocateAsync(id, FirmA, "INV-003");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different firm invoice number*");
        _store.UpdateCalls.Should().Be(0);
        _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-002.1");
    }

    // ── Caller bugs write nothing ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASubmissionOfAnotherFirm_IsNotNumbered()
    {
        // The key only guards rows that carry the firm the sequence was counted for.
        var id = _store.AddRow(FirmB);

        var act = () => CreateService().AllocateAsync(id, FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong to the firm*");
        _store.UpdateCalls.Should().Be(0);
        _store.QueryCalls.Should().Be(0);
    }

    [Fact]
    public async Task ASubmissionWithNoFirm_IsNotNumbered()
    {
        // A null firm escapes the (firm, tracking number) key, so numbering it would not be unique.
        var id = _store.AddRow(firmId: null);

        var act = () => CreateService().AllocateAsync(id, FirmA, "INV-002");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _store.UpdateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankFirmInvoiceNumber_IsRefusedBeforeAnyRead(string? blank)
    {
        var id = _store.AddRow(FirmA);

        var act = () => CreateService().AllocateAsync(id, FirmA, blank!);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.QueryCalls.Should().Be(0);
        _store.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task AFirmInvoiceNumberThatCannotFitTheTrackingNumberColumn_IsRefusedBeforeAnyRead()
    {
        var id = _store.AddRow(FirmA);
        var tooLong = new string('7', TrackingNumberAllocator.MaxFirmInvoiceNumberLength + 1);

        var act = () => CreateService().AllocateAsync(id, FirmA, tooLong);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task TheLongestAcceptedFirmInvoiceNumber_WithTheLongestSequence_StillFitsTheTrackingNumberColumn()
    {
        var longest = new string('7', TrackingNumberAllocator.MaxFirmInvoiceNumberLength);
        _store.AddNumbered(FirmA, longest, int.MaxValue - 1);
        var id = _store.AddRow(FirmA);

        var result = await CreateService().AllocateAsync(id, FirmA, longest);

        result.TrackingNumber.Should().HaveLength(100, "89 characters, a dot and a 10-digit sequence is exactly the column");
    }

    // ── Parallel callers ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenParallelAllocations_NeverShareANumber_AndARefusedOneWritesNothing()
    {
        const int callers = 10;
        var ids = Enumerable.Range(0, callers).Select(_ => _store.AddRow(FirmA)).ToList();

        // Hold every caller's FIRST read until all ten have read, so all ten pick .1 and contend for it (the worst case).
        var firstReads = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeRead = async () =>
        {
            var arrival = Interlocked.Increment(ref firstReads);
            if (arrival <= callers)
            {
                if (arrival == callers)
                {
                    release.TrySetResult();
                }

                await release.Task;
            }
        };

        var results = await Task.WhenAll(ids.Select(id => Task.Run(() => CreateService().AllocateAsync(id, FirmA, "INV-002"))));

        var allocated = results.Where(r => r.Outcome == TrackingNumberOutcome.Allocated).ToList();
        allocated.Should().NotBeEmpty();
        allocated.Select(r => r.TrackingNumber).Should().OnlyHaveUniqueItems("two parallel submissions must never share a tracking number");
        allocated.Select(r => r.Sequence!.Value).OrderBy(n => n).Should().Equal(Enumerable.Range(1, allocated.Count),
            "the sequence has no gaps: each winner wrote the highest + 1 it re-read");
        _store.UpdateCalls.Should().BeGreaterThan(callers, "the ten contended for .1, so the nine losers retried");

        foreach (var (id, result) in ids.Zip(results))
        {
            if (result.Outcome == TrackingNumberOutcome.Refused)
            {
                result.Reason.Should().Be("ebilling.tracking.unavailable");
                _store.Row(id).Contains("sprk_submissionsequence").Should().BeFalse();
                _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be(SubmissionStore.Placeholder(id));
            }
            else
            {
                _store.Row(id).GetAttributeValue<string>("sprk_trackingnumber").Should().Be(result.TrackingNumber);
            }
        }
    }

    // ── Logging (ADR-015) ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirmInvoiceNumberIsBusinessData_AndIsLoggedAtDebugOnly()
    {
        const string secret = "SECRET-INV-7731";
        const string other = "SECRET-INV-9999";
        var log = new CapturingLogger();
        var raced = _store.AddRow(FirmA);
        var exhausted = _store.AddRow(FirmA);
        var repeated = _store.AddNumbered(FirmA, other, 40);

        // a win after one lost race
        _store.BeforeUpdate = (call, fields) =>
        {
            if (call == 1)
            {
                _store.AddNumbered(FirmA, secret, 1);
            }
        };
        (await CreateService(log).AllocateAsync(raced, FirmA, secret)).TrackingNumber.Should().Be(secret + ".2");

        // exhaustion: every write loses to a competitor that takes exactly the number being written
        _store.BeforeUpdate = (call, fields) => _store.AddNumbered(FirmA, secret, (int)fields["sprk_submissionsequence"]);
        (await CreateService(log).AllocateAsync(exhausted, FirmA, secret)).Outcome.Should().Be(TrackingNumberOutcome.Refused);

        // an already-numbered submission
        _store.BeforeUpdate = null;
        (await CreateService(log).AllocateAsync(repeated, FirmA, other)).Outcome.Should().Be(TrackingNumberOutcome.AlreadyAllocated);

        var important = log.Entries.Where(e => e.Level >= LogLevel.Information).ToList();
        important.Should().Contain(e => e.Level == LogLevel.Warning, "the lost race is logged");
        important.Should().Contain(e => e.Level == LogLevel.Error, "the exhaustion is logged");
        important.Should().OnlyContain(
            e => !e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase) && !e.Message.Contains(other, StringComparison.OrdinalIgnoreCase),
            "the firm invoice number and the tracking number built from it are business data");
        important.Should().Contain(e => e.Message.Contains(raced.ToString()), "the submission id is what an operator searches by");
    }

    private sealed class CapturingLogger : ILogger<TrackingNumberAllocator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " | " + exception)));
    }

    /// <summary>
    /// An in-memory <c>sprk_invoicesubmission</c> table behind a Moq <see cref="IGenericEntityService"/>. Strict: a call the allocator is not
    /// meant to make fails the test.
    /// </summary>
    private sealed class SubmissionStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Entity> _rows = [];
        private int _updateCalls;
        private int _queryCalls;

        public SubmissionStore()
        {
            Entities
                .Setup(e => e.RetrieveAsync("sprk_invoicesubmission", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        return Task.FromResult(Clone(_rows[id]));
                    }
                });

            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns(async (QueryExpression query, CancellationToken _) =>
                {
                    if (BeforeRead is { } gate)
                    {
                        await gate().ConfigureAwait(false);
                    }

                    await Task.Yield();
                    lock (_gate)
                    {
                        _queryCalls++;
                        return Run(query);
                    }
                });

            Entities
                .Setup(e => e.UpdateAsync("sprk_invoicesubmission", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    await Task.Yield();
                    lock (_gate)
                    {
                        var call = ++_updateCalls;
                        BeforeUpdate?.Invoke(call, fields);
                        if (UpdateFault is not null)
                        {
                            throw UpdateFault;
                        }

                        var row = _rows[id];
                        if (fields.TryGetValue("sprk_trackingnumber", out var tracking))
                        {
                            var firm = row.GetAttributeValue<EntityReference>("sprk_vendororg")?.Id;
                            var clash = _rows.Values.Any(other =>
                                other.Id != id
                                && other.GetAttributeValue<EntityReference>("sprk_vendororg")?.Id == firm
                                && string.Equals(other.GetAttributeValue<string>("sprk_trackingnumber"), (string)tracking, StringComparison.OrdinalIgnoreCase));
                            if (clash)
                            {
                                // What DataverseServiceClientImpl.UpdateAsync raises for the key violation: the SDK fault, wrapped.
                                throw new InvalidOperationException(
                                    $"Failed to update sprk_invoicesubmission record {id}: Entity Key Firm and Tracking Number violated.",
                                    new FaultException<OrganizationServiceFault>(
                                        new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060892) },
                                        new FaultReason("A record with the same value for Tracking Number, Firm already exists.")));
                            }
                        }

                        foreach (var field in fields)
                        {
                            row[field.Key] = field.Value;
                        }
                    }
                });
        }

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);

        /// <summary>Runs with the table locked just before an update is checked against the key (another request committing first).</summary>
        public Action<int, Dictionary<string, object>>? BeforeUpdate { get; set; }

        /// <summary>Awaited at the start of every highest-sequence read.</summary>
        public Func<Task>? BeforeRead { get; set; }

        /// <summary>When set, every update raises it.</summary>
        public Exception? UpdateFault { get; set; }

        public int UpdateCalls => Volatile.Read(ref _updateCalls);

        public int QueryCalls => Volatile.Read(ref _queryCalls);

        public static string Placeholder(Guid id) => $"PENDING-{id:N}";

        /// <summary>A submission as the intake creates it when no number is known yet: a firm and a unique placeholder tracking number.</summary>
        public Guid AddRow(Guid? firmId)
        {
            lock (_gate)
            {
                var row = new Entity("sprk_invoicesubmission", Guid.NewGuid());
                if (firmId is { } firm)
                {
                    row["sprk_vendororg"] = new EntityReference("sprk_organization", firm);
                }

                row["sprk_trackingnumber"] = Placeholder(row.Id);
                _rows[row.Id] = row;
                return row.Id;
            }
        }

        /// <summary>A submission that already holds <c>{number}.{sequence}</c>.</summary>
        public Guid AddNumbered(Guid firmId, string firmInvoiceNumber, int sequence, int stateCode = 0)
        {
            lock (_gate)
            {
                var row = new Entity("sprk_invoicesubmission", Guid.NewGuid())
                {
                    ["sprk_vendororg"] = new EntityReference("sprk_organization", firmId),
                    ["statecode"] = new OptionSetValue(stateCode),
                };
                _rows[row.Id] = row;
                NumberRow(row.Id, firmId, firmInvoiceNumber, sequence);
                return row.Id;
            }
        }

        /// <summary>Writes <c>{number}.{sequence}</c> onto an existing row, as a committed allocation would.</summary>
        public void NumberRow(Guid id, Guid firmId, string firmInvoiceNumber, int sequence)
        {
            lock (_gate)
            {
                var row = _rows[id];
                row["sprk_vendororg"] = new EntityReference("sprk_organization", firmId);
                row["sprk_firminvoicenumber"] = firmInvoiceNumber;
                row["sprk_submissionsequence"] = sequence;
                row["sprk_trackingnumber"] = $"{firmInvoiceNumber}.{sequence}";
            }
        }

        public Entity Row(Guid id)
        {
            lock (_gate)
            {
                return Clone(_rows[id]);
            }
        }

        private static Entity Clone(Entity source)
        {
            var copy = new Entity(source.LogicalName, source.Id);
            foreach (var attribute in source.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return copy;
        }

        /// <summary>Evaluates the allocator's query: AND of Equal / NotNull conditions, one descending order, TopCount. Anything else fails loudly.</summary>
        private EntityCollection Run(QueryExpression query)
        {
            query.EntityName.Should().Be("sprk_invoicesubmission");
            query.Criteria.Filters.Should().BeEmpty();
            query.Criteria.FilterOperator.Should().Be(LogicalOperator.And);
            query.LinkEntities.Should().BeEmpty();

            IEnumerable<Entity> rows = _rows.Values;
            foreach (var condition in query.Criteria.Conditions)
            {
                rows = rows.Where(row => Matches(row, condition));
            }

            foreach (var order in query.Orders.Reverse())
            {
                order.OrderType.Should().Be(OrderType.Descending);
                rows = rows.OrderByDescending(row => row.GetAttributeValue<int?>(order.AttributeName) ?? int.MinValue);
            }

            if (query.TopCount is { } top)
            {
                rows = rows.Take(top);
            }

            var found = new EntityCollection();
            foreach (var row in rows)
            {
                found.Entities.Add(Clone(row));
            }

            return found;
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            if (condition.AttributeName == "sprk_invoicesubmissionid")
            {
                // The primary key is the row's Id, not an attribute.
                return condition.Operator switch
                {
                    ConditionOperator.NotEqual => row.Id != (Guid)condition.Values.Single(),
                    ConditionOperator.Equal => row.Id == (Guid)condition.Values.Single(),
                    _ => throw new NotSupportedException($"The in-memory table does not model {condition.Operator} on the primary key."),
                };
            }

            var has = row.Contains(condition.AttributeName) && row[condition.AttributeName] is not null;
            return condition.Operator switch
            {
                ConditionOperator.NotNull => has,
                ConditionOperator.Equal => has && ValueEquals(row[condition.AttributeName], condition.Values.Single()),
                _ => throw new NotSupportedException($"The in-memory table does not model {condition.Operator}."),
            };
        }

        private static bool ValueEquals(object actual, object expected) => actual switch
        {
            EntityReference reference => expected is Guid id && reference.Id == id,
            string text => expected is string other && string.Equals(text, other, StringComparison.OrdinalIgnoreCase),
            OptionSetValue option => expected is int value && option.Value == value,
            _ => Equals(actual, expected),
        };
    }
}
