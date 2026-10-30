// ebilling-r1 task 045 (FR-10, D-37, D-87) - LIVE proof that the tracking-number allocator is concurrency-safe and idempotent against the real
// keys of sprk_invoicesubmission in Dataverse dev: the (sprk_vendororg, sprk_trackingnumber) alternate key and the primary key.
//
// Opt-in: reports Skipped (never a vacuous Passed) unless
//   SPAARKE_LIVE_TRACKING_DATAVERSE_URL=https://<env>.crm.dynamics.com      (spaarkedev1)
// is set, and needs an `az login` session whose identity may create and delete sprk_invoicesubmission rows.
// Optional: SPAARKE_LIVE_TRACKING_CALLERS (default 10) sets the number of parallel creates.
//
// WHAT IS REAL: the production TrackingNumberAllocator over the production DataverseServiceClientImpl (the BFF's SDK client, fed
// the operator's az login instead of the managed identity), against the live keys. Nothing is doubled.
//
// Every row it creates is named "ZZTEST-045 ..." and is deleted in `finally`; the leftover count is asserted zero by query.
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// Maintain-class (ADR-038, data-mutation: optimistic concurrency): the unit tests prove the retry and idempotency rules against an in-memory
/// pair of keys; this proves the same properties against the real ones, which is the only place "two parallel submissions never share a number"
/// and "a redelivered create never mints a second number" are actually decided.
/// </summary>
[Trait("Category", "Live")]
public sealed class TrackingNumberAllocatorLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_TRACKING_DATAVERSE_URL";
    private const string CallersVar = "SPAARKE_LIVE_TRACKING_CALLERS";
    private const string NamePrefix = "ZZTEST-045";

    private readonly ITestOutputHelper _out;

    public TrackingNumberAllocatorLiveTests(ITestOutputHelper output) => _out = output;

    [SkippableFact]
    public async Task ParallelCreates_ForOneFirmAndFirmInvoiceNumber_AllGetADistinctTrackingNumber()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        Skip.If(string.IsNullOrWhiteSpace(url), $"{UrlVar} is not set.");

        var callers = int.TryParse(Environment.GetEnvironmentVariable(CallersVar), out var n) && n > 0 ? n : 10;
        using var dataverse = CreateClient(url!);
        IGenericEntityService entities = dataverse;
        var firmId = await FirstFirmAsync(entities);
        var invoiceNumber = $"{NamePrefix}-{Guid.NewGuid():N}";
        var ids = Enumerable.Range(0, callers).Select(_ => Guid.NewGuid()).ToList();
        try
        {
            var logs = ids.ToDictionary(id => id, _ => new CountingLogger());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = ids.Select((id, i) => Task.Run(async () =>
            {
                await start.Task;
                return await new TrackingNumberAllocator(entities, logs[id]).CreateSubmissionAsync(
                    id, firmId, invoiceNumber, new Dictionary<string, object> { ["sprk_name"] = $"{NamePrefix} parallel {i}" });
            })).ToList();
            start.SetResult();
            var results = await Task.WhenAll(tasks);

            var attempts = ids.Select(id => logs[id].Warnings + 1).ToList();
            var created = results.Where(r => r.Outcome == TrackingNumberOutcome.Created).ToList();
            var refused = results.Where(r => r.Outcome == TrackingNumberOutcome.Refused).ToList();
            _out.WriteLine($"{callers} parallel creates: {created.Count} created, {refused.Count} refused (ebilling.tracking.unavailable)");
            _out.WriteLine($"attempts per caller (1 = no lost race): {string.Join(", ", attempts.OrderBy(a => a))}");
            _out.WriteLine($"sequences created: {string.Join(", ", created.Select(r => r.Sequence!.Value).OrderBy(s => s))}");

            // Read the rows back: what Dataverse holds is the proof, not what the allocator returned.
            var stored = await ReadRowsAsync(entities, invoiceNumber);
            stored.Select(s => s.Tracking).Should().OnlyHaveUniqueItems("two parallel submissions must never share a tracking number");
            stored.Select(s => s.Sequence!.Value).OrderBy(s => s).Should().Equal(Enumerable.Range(1, stored.Count));
            stored.Should().OnlyContain(s => s.Tracking == $"{invoiceNumber}.{s.Sequence}");
            stored.Select(s => s.Id).Should().BeSubsetOf(ids, "each row carries the id its caller chose");
            stored.Count.Should().Be(created.Count, "every Created result is a stored row, and nothing else was created");
            refused.Should().BeEmpty($"{callers} parallel creates must all get a number (exhaustion at this concurrency is the escalation trigger)");
        }
        finally
        {
            await CleanUpAsync(entities, ids);
        }
    }

    [SkippableFact]
    public async Task ARedeliveredCreate_ReturnsTheSameNumber_AndMintsNoSecondRow()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        Skip.If(string.IsNullOrWhiteSpace(url), $"{UrlVar} is not set.");

        using var dataverse = CreateClient(url!);
        IGenericEntityService entities = dataverse;
        var firmId = await FirstFirmAsync(entities);
        var invoiceNumber = $"{NamePrefix}-{Guid.NewGuid():N}";
        var sequential = Guid.NewGuid();
        var racing = Guid.NewGuid();
        try
        {
            TrackingNumberAllocator NewAllocator() => new(entities, NullLogger<TrackingNumberAllocator>.Instance);
            Dictionary<string, object> Fields(string label) => new() { ["sprk_name"] = $"{NamePrefix} {label}" };

            // A redelivery after the first create finished: found by its id before anything is written.
            var first = await NewAllocator().CreateSubmissionAsync(sequential, firmId, invoiceNumber, Fields("sequential"));
            var again = await NewAllocator().CreateSubmissionAsync(sequential, firmId, invoiceNumber, Fields("sequential redelivery"));
            first.Outcome.Should().Be(TrackingNumberOutcome.Created);
            again.Outcome.Should().Be(TrackingNumberOutcome.AlreadyCreated);
            again.TrackingNumber.Should().Be(first.TrackingNumber);

            // Four calls with ONE id at once: the primary key refuses all but one create, and the others settle on the row that won.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var racers = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
            {
                await start.Task;
                return await NewAllocator().CreateSubmissionAsync(racing, firmId, invoiceNumber, Fields($"racing {i}"));
            })).ToList();
            start.SetResult();
            var raced = await Task.WhenAll(racers);
            _out.WriteLine($"4 racing calls with one id: {string.Join(", ", raced.Select(r => $"{r.Outcome} {r.TrackingNumber}"))}");

            raced.Count(r => r.Outcome == TrackingNumberOutcome.Created).Should().Be(1, "one create wins the id");
            raced.Count(r => r.Outcome == TrackingNumberOutcome.AlreadyCreated).Should().Be(3);
            raced.Select(r => r.TrackingNumber).Distinct().Should().ContainSingle("every racer reports the one number that was stored");

            var stored = await ReadRowsAsync(entities, invoiceNumber);
            stored.Should().HaveCount(2, "one row for the sequential id and one for the racing id: a redelivery mints no second row");
            stored.Select(s => s.Tracking).Should().OnlyHaveUniqueItems();
            stored.Select(s => s.Sequence!.Value).OrderBy(s => s).Should().Equal(1, 2);
        }
        finally
        {
            await CleanUpAsync(entities, [sequential, racing]);
        }
    }

    private static DataverseServiceClientImpl CreateClient(string url)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = url,
            // The production SDK client's token-provider branch, fed the operator's az login (no secret is created).
            ["Graph:ManagedIdentity:Enabled"] = "true",
        }).Build();
        return new DataverseServiceClientImpl(configuration, NullLogger<DataverseServiceClientImpl>.Instance, null, new AzureCliCredential());
    }

    private static async Task<Guid> FirstFirmAsync(IGenericEntityService entities)
    {
        var firms = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_organization") { ColumnSet = new ColumnSet(false), TopCount = 1 });
        firms.Entities.Should().NotBeEmpty("the dev environment needs at least one sprk_organization to act as the firm");
        return firms.Entities[0].Id;
    }

    private static async Task<List<(Guid Id, string? Tracking, int? Sequence)>> ReadRowsAsync(IGenericEntityService entities, string invoiceNumber)
    {
        var found = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_invoicesubmission")
        {
            ColumnSet = new ColumnSet("sprk_trackingnumber", "sprk_submissionsequence"),
            Criteria = { Conditions = { new ConditionExpression("sprk_firminvoicenumber", ConditionOperator.Equal, invoiceNumber) } },
        });
        return found.Entities
            .Select(e => (e.Id, (string?)e.GetAttributeValue<string>("sprk_trackingnumber"), e.GetAttributeValue<int?>("sprk_submissionsequence")))
            .ToList();
    }

    /// <summary>Deletes every row this test created (found by id and by name prefix) and asserts none is left.</summary>
    private async Task CleanUpAsync(IGenericEntityService entities, IEnumerable<Guid> ids)
    {
        var wanted = ids.ToHashSet();
        var leftovers = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_invoicesubmission")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("sprk_name", ConditionOperator.BeginsWith, NamePrefix) } },
        });
        wanted.UnionWith(leftovers.Entities.Select(e => e.Id));

        var failures = new List<string>();
        var deleted = 0;
        foreach (var id in wanted)
        {
            try
            {
                await entities.DeleteAsync("sprk_invoicesubmission", id);
                deleted++;
            }
            catch (Exception ex) when (ex.Message.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                // never created (a refused call): nothing to delete
            }
            catch (Exception ex)
            {
                failures.Add($"{id}: {ex.Message}");
            }
        }

        var left = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_invoicesubmission")
        {
            ColumnSet = new ColumnSet(false),
            Criteria = { Conditions = { new ConditionExpression("sprk_name", ConditionOperator.BeginsWith, NamePrefix) } },
        });
        _out.WriteLine($"cleanup: {deleted} deleted, {failures.Count} delete(s) failed, {left.Entities.Count} {NamePrefix} row(s) left");
        failures.Should().BeEmpty();
        left.Entities.Should().BeEmpty();
    }

    /// <summary>Counts the allocator's lost-race warnings (one per refused create), thread-safely.</summary>
    private sealed class CountingLogger : ILogger<TrackingNumberAllocator>
    {
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
            }
        }
    }
}
