using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 187, fix round 2 (K1): one request's live-parent walk logs its cost, the filing queries it
/// sent and the records it asked for, at Debug, and once at Information when the query count passes
/// <see cref="ExternalDataService.WalkQueriesWorthReporting"/>. Counts only: no id reaches the log.
/// </summary>
/// <remarks>Driven through the REAL <see cref="ExternalDataService.GetLiveParentRootsAsync"/> and the REAL walk over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>'s filing reader, which answers by the id conditions each query
/// carries (200 ids per query).</remarks>
[Trait("category", "external-access")]
public sealed class ExternalLiveParentWalkCostLogTests
{
    [Fact]
    public async Task AUsualPage_LogsItsWalkCost_AtDebug_AndNothingAtInformation()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var log = new CapturingLogger();
        var parents = Enumerable.Range(0, 3).Select(_ => ("sprk_communication", Guid.NewGuid())).ToList();

        await NewService(data, log).GetLiveParentRootsAsync(parents);

        data.FilingReadLog.Should().HaveCount(1);
        log.Entries.Should().ContainSingle(e => e.Message.Contains("[EXT-LIVEPARENT] Walk cost"))
            .Which.Should().Be((LogLevel.Debug, "[EXT-LIVEPARENT] Walk cost: 1 filing queries for 3 records (3 parents)."));
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Information && e.Message.Contains("Walk cost"));
    }

    [Fact]
    public async Task AWalkPastTheThreshold_LogsOneInformationLine_WithCountsOnly()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var log = new CapturingLogger();
        // 200 ids per query: one more chunk than the threshold.
        var count = 200 * ExternalDataService.WalkQueriesWorthReporting + 1;
        var parents = Enumerable.Range(0, count).Select(_ => ("sprk_communication", Guid.NewGuid())).ToList();

        await NewService(data, log).GetLiveParentRootsAsync(parents);

        var queries = ExternalDataService.WalkQueriesWorthReporting + 1;
        data.FilingReadLog.Should().HaveCount(queries);
        var cost = log.Entries.Where(e => e.Message.Contains("Walk cost")).ToList();
        cost.Should().ContainSingle().Which.Should().Be((LogLevel.Information,
            $"[EXT-LIVEPARENT] Walk cost: {queries} filing queries for {count} records ({count} parents) in one request."));
        cost.Single().Message.Should().NotContain(parents[0].Item2.ToString(), "counts only");
    }

    [Fact]
    public async Task AtTheThreshold_TheWalkCostStaysAtDebug()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var log = new CapturingLogger();
        var count = 200 * ExternalDataService.WalkQueriesWorthReporting;
        var parents = Enumerable.Range(0, count).Select(_ => ("sprk_communication", Guid.NewGuid())).ToList();

        await NewService(data, log).GetLiveParentRootsAsync(parents);

        log.Entries.Where(e => e.Message.Contains("Walk cost")).Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Debug);
    }

    private static ExternalDataService NewService(ExternalRootEventReadTests.FakeDataverse data, CapturingLogger log) => new(
        new HttpClient(data),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
        log,
        data.FilingReader);

    private sealed class CapturingLogger : ILogger<ExternalDataService>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
