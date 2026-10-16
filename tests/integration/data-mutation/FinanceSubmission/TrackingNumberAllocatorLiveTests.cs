// ebilling-r1 task 045 (FR-10, D-37) - LIVE proof that the tracking-number allocator is concurrency-safe against the real
// (sprk_vendororg, sprk_trackingnumber) alternate key of sprk_invoicesubmission in Dataverse dev.
//
// Opt-in: reports Skipped (never a vacuous Passed) unless
//   SPAARKE_LIVE_TRACKING_DATAVERSE_URL=https://<env>.crm.dynamics.com      (spaarkedev1)
// is set, and needs an `az login` session whose identity may create and delete sprk_invoicesubmission rows.
// Optional: SPAARKE_LIVE_TRACKING_CALLERS (default 10) sets the number of parallel allocations.
//
// WHAT IS REAL: the production TrackingNumberAllocator over the production DataverseServiceClientImpl (the BFF's SDK client, fed
// the operator's az login instead of the managed identity), against the live key. Nothing is doubled.
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
/// Maintain-class (ADR-038, data-mutation: optimistic concurrency): the unit tests prove the retry rule against an in-memory key; this proves
/// the same property against the real one, which is the only place "two parallel submissions never share a number" is actually decided.
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
    public async Task ParallelAllocations_ForOneFirmAndFirmInvoiceNumber_YieldDistinctTrackingNumbers()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        Skip.If(string.IsNullOrWhiteSpace(url), $"{UrlVar} is not set.");

        var callers = int.TryParse(Environment.GetEnvironmentVariable(CallersVar), out var n) && n > 0 ? n : 10;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = url,
            // The production SDK client's token-provider branch, fed the operator's az login (no secret is created).
            ["Graph:ManagedIdentity:Enabled"] = "true",
        }).Build();

        using var dataverse = new DataverseServiceClientImpl(configuration, NullLogger<DataverseServiceClientImpl>.Instance, null, new AzureCliCredential());
        IGenericEntityService entities = dataverse;

        var firms = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_organization") { ColumnSet = new ColumnSet(false), TopCount = 1 });
        firms.Entities.Should().NotBeEmpty("the dev environment needs at least one sprk_organization to act as the firm");
        var firmId = firms.Entities[0].Id;

        var invoiceNumber = $"{NamePrefix}-{Guid.NewGuid():N}";
        var created = new List<Guid>();
        try
        {
            // The intake creates a submission with a placeholder tracking number (the column is SystemRequired); the allocator replaces it.
            for (var i = 0; i < callers; i++)
            {
                var row = new Entity("sprk_invoicesubmission")
                {
                    ["sprk_name"] = $"{NamePrefix} parallel {i}",
                    ["sprk_vendororg"] = new EntityReference("sprk_organization", firmId),
                    ["sprk_trackingnumber"] = $"{NamePrefix}-PENDING-{Guid.NewGuid():N}",
                };
                created.Add(await entities.CreateAsync(row));
            }

            var logs = created.ToDictionary(id => id, _ => new CountingLogger());
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = created.Select(id => Task.Run(async () =>
            {
                await start.Task;
                return await new TrackingNumberAllocator(entities, logs[id]).AllocateAsync(id, firmId, invoiceNumber);
            })).ToList();
            start.SetResult();
            var results = await Task.WhenAll(tasks);

            var attempts = created.Select(id => logs[id].Warnings + 1).ToList();
            var allocated = results.Where(r => r.Outcome == TrackingNumberOutcome.Allocated).ToList();
            var refused = results.Where(r => r.Outcome == TrackingNumberOutcome.Refused).ToList();
            _out.WriteLine($"{callers} parallel allocations: {allocated.Count} allocated, {refused.Count} refused (ebilling.tracking.unavailable)");
            _out.WriteLine($"attempts per caller (1 = no lost race): {string.Join(", ", attempts.OrderBy(a => a))}");
            _out.WriteLine($"sequences allocated: {string.Join(", ", allocated.Select(r => r.Sequence!.Value).OrderBy(s => s))}");

            // Read the rows back: what Dataverse holds is the proof, not what the allocator returned.
            var stored = new List<(Guid Id, string? Tracking, int? Sequence)>();
            foreach (var id in created)
            {
                var row = await entities.RetrieveAsync("sprk_invoicesubmission", id, ["sprk_trackingnumber", "sprk_submissionsequence"]);
                stored.Add((id, row.GetAttributeValue<string>("sprk_trackingnumber"), row.GetAttributeValue<int?>("sprk_submissionsequence")));
            }

            var numbered = stored.Where(s => s.Sequence is not null).ToList();
            numbered.Select(s => s.Tracking).Should().OnlyHaveUniqueItems("two parallel submissions must never share a tracking number");
            numbered.Select(s => s.Sequence!.Value).OrderBy(s => s).Should().Equal(Enumerable.Range(1, numbered.Count));
            numbered.Should().OnlyContain(s => s.Tracking == $"{invoiceNumber}.{s.Sequence}");
            numbered.Count.Should().Be(allocated.Count, "every Allocated result is a stored number, and nothing else was numbered");
            refused.Should().OnlyContain(r => r.Reason == "ebilling.tracking.unavailable");
            refused.Should().BeEmpty($"{callers} parallel allocations must all get a number (exhaustion at this concurrency is the escalation trigger)");
        }
        finally
        {
            var leaks = new List<string>();
            foreach (var id in created)
            {
                try
                {
                    await entities.DeleteAsync("sprk_invoicesubmission", id);
                }
                catch (Exception ex)
                {
                    leaks.Add($"{id}: {ex.Message}");
                }
            }

            var left = await entities.RetrieveMultipleAsync(new QueryExpression("sprk_invoicesubmission")
            {
                ColumnSet = new ColumnSet(false),
                Criteria = { Conditions = { new ConditionExpression("sprk_name", ConditionOperator.BeginsWith, NamePrefix) } },
            });
            _out.WriteLine($"cleanup: {created.Count} created, {leaks.Count} delete(s) failed, {left.Entities.Count} {NamePrefix} row(s) left");
            leaks.Should().BeEmpty();
            left.Entities.Should().BeEmpty();
        }
    }

    /// <summary>Counts the allocator's lost-race warnings (one per refused write), thread-safely.</summary>
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
