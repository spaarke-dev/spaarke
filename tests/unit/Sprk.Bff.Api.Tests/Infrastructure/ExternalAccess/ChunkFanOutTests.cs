// GitHub #2845 round 2 review — the per-type compositions' chunk fan-outs (ExternalParticipationService.ForEachChunkAsync and
// NoAccessListReader.QueryAllAsync).
//
// R3: the first chunk that fails the batch closed (a throw; for the deny list also an unreadable null) cancels its siblings, so
// they stop instead of loading Dataverse; the outcome is unchanged (fail closed). T3: the fan-outs stay within their caps, and
// through the real HTTP stack (the F1 gate + throttle handler) a chunk still throttled after the retries folds exactly as a
// failed chunk does: that chunk's ids are unreadable (never granted), the other chunks' answers stand. Only the HTTP exchange
// and the deny-list query seam are substituted (testing.md B1: never Mock<HttpMessageHandler>).

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public sealed class ChunkFanOutTests
{
    private static readonly TimeSpan SiblingWait = TimeSpan.FromSeconds(5);

    // ── R3: ExternalParticipationService.ForEachChunkAsync ──────────────────────────────────────────

    [Fact]
    public async Task ForEachChunk_TheFirstChunkThrows_TheLaterChunksObserveCancellation_AndTheThrowPropagates()
    {
        var ids = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList(); // 3 chunks of 50
        var firstChunk = ids[0];
        var cancelled = new ConcurrentBag<Guid>();
        var siblingsStarted = new CountdownEvent(2);

        var run = ExternalParticipationService.ForEachChunkAsync(ids, async (chunk, token) =>
        {
            if (chunk[0] == firstChunk)
            {
                await Task.Run(() => siblingsStarted.Wait(SiblingWait)); // fail once both siblings are reading
                throw new HttpRequestException("chunk 1 faulted");
            }

            siblingsStarted.Signal();
            try
            {
                await Task.Delay(SiblingWait, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.Add(chunk[0]);
                throw;
            }

            return chunk.Count;
        }, CancellationToken.None);

        await FluentActions.Awaiting(() => run).Should().ThrowAsync<HttpRequestException>(
            "the caller's fail-closed catch still sees the chunk's fault");
        cancelled.Should().BeEquivalentTo(new[] { ids[50], ids[100] }, "the siblings stop once the batch has failed closed");
    }

    // ── R3 + T3: NoAccessListReader.QueryAllAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task QueryAll_TheFirstQueryIsUnreadable_TheLaterQueriesObserveCancellation_AndTheAnswerFailsClosed()
    {
        var candidates = Candidates(150); // 3 record-object queries
        var siblingsStarted = new CountdownEvent(2);
        var reader = new SeamReader(async (objectFilter, token, self) =>
        {
            if (objectFilter.Contains(candidates[0].RecordId.ToString("D"), StringComparison.Ordinal))
            {
                await Task.Run(() => siblingsStarted.Wait(SiblingWait)); // fail once both siblings are reading
                return null; // a non-success status: unreadable
            }

            siblingsStarted.Signal();
            try
            {
                await Task.Delay(SiblingWait, token);
            }
            catch (OperationCanceledException)
            {
                self.Cancelled.Add(objectFilter);
                throw;
            }

            return new List<NoAccessEntryRow>();
        });

        var result = await reader.GetDeniedRecordsAsync(Guid.NewGuid(), Array.Empty<Guid>(), candidates);

        result.FailedClosed.Should().BeTrue("one unreadable query fails the whole answer closed, as before");
        result.DeniedRecordIds.Should().BeEquivalentTo(candidates.Select(c => c.RecordId));
        reader.Cancelled.Should().HaveCount(2, "the two sibling queries stop once the answer has failed closed");
    }

    [Fact]
    public async Task QueryAll_KeepsAtMostItsCapOfQueriesInFlight()
    {
        var candidates = Candidates(1000); // 20 record-object queries
        var inFlight = 0;
        var peak = 0;
        var reader = new SeamReader(async (_, token, _) =>
        {
            DataverseThrottlingHandlerTests.InterlockedMax(ref peak, Interlocked.Increment(ref inFlight));
            await Task.Delay(20, token);
            Interlocked.Decrement(ref inFlight);
            return new List<NoAccessEntryRow>();
        });

        var result = await reader.GetDeniedRecordsAsync(Guid.NewGuid(), Array.Empty<Guid>(), candidates);

        result.FailedClosed.Should().BeFalse();
        reader.Queries.Should().Be(20);
        peak.Should().BeLessOrEqualTo(NoAccessListReader.MaxConcurrentChunkQueries);
    }

    // ── T3: the HTTP-level fold of a throttled chunk ────────────────────────────────────────────────

    [Fact]
    public async Task RootFlags_120Ids_Chunk2StillThrottledAfterTheRetries_ItsIdsAreUnreadable_TheOthersStand_WithinTheCap()
    {
        var ids = Enumerable.Range(0, 120).Select(i => Guid.Parse($"28453000-0000-4000-8000-{i:D12}")).ToList();
        var chunk2 = ids.Skip(50).Take(50).ToHashSet();
        var wire = new FlagWire(new[] { ids[50] }); // the first id of chunk 2 names chunk 2's query
        const int permits = 2;
        var handler = DataverseThrottlingHandlerTests.Handler(new DataverseConcurrencyGate(permits), wire, new List<TimeSpan>());
        var service = new ExternalParticipationService(
            new HttpClient(handler), Mock.Of<ITenantCache>(), DataverseThrottlingHandlerTests.Configuration(),
            new DataverseThrottlingHandlerTests.StaticTokenCredential(), Mock.Of<IHttpContextAccessor>(),
            NullLogger<ExternalParticipationService>.Instance, filing: null);

        var flags = await service.GetRootRecordFlagsAsync("sprk_matter", ids);

        flags.Should().HaveCount(120, "every asked id is answered");
        flags.Where(kv => chunk2.Contains(kv.Key)).Should().OnlyContain(kv => kv.Value.IsUnreadable,
            "the throttled chunk folds fail-closed: unreadable = secure AND restricted, never granted");
        flags.Where(kv => !chunk2.Contains(kv.Key)).Should().OnlyContain(kv => !kv.Value.IsUnreadable && !kv.Value.IsSecure,
            "the other chunks' answers stand");
        wire.ThrottledSends.Should().Be(1 + DataverseThrottlingHandler.MaxRetries, "chunk 2 was retried, then given up");
        wire.Peak.Should().BeLessOrEqualTo(permits, "the process-wide gate bounds the chunks in flight");
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────

    private static List<NoAccessCandidateRecord> Candidates(int count) => Enumerable.Range(0, count)
        .Select(_ => new NoAccessCandidateRecord("sprk_matter", Guid.NewGuid(), Array.Empty<Guid>()))
        .ToList();

    /// <summary>The deny-list reader with its query seam answered by <c>answer</c> (object filter, token, this reader).</summary>
    private sealed class SeamReader(Func<string, CancellationToken, SeamReader, Task<List<NoAccessEntryRow>?>> answer)
        : NoAccessListReader(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
    {
        private int _queries;

        public ConcurrentBag<string> Cancelled { get; } = new();

        public int Queries => Volatile.Read(ref _queries);

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            Interlocked.Increment(ref _queries);
            return answer(objectFilter, ct, this);
        }
    }

    /// <summary>The root-flag query's Dataverse: every asked matter exists, open; the chunk naming <c>throttled</c> answers 429.</summary>
    private sealed class FlagWire(Guid[] throttled) : HttpMessageHandler
    {
        private int _inFlight, _peak, _throttledSends;

        public int Peak => Volatile.Read(ref _peak);

        public int ThrottledSends => Volatile.Read(ref _throttledSends);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            DataverseThrottlingHandlerTests.InterlockedMax(ref _peak, Interlocked.Increment(ref _inFlight));
            try
            {
                await Task.Delay(20, ct);
                var query = Uri.UnescapeDataString(request.RequestUri!.ToString());
                if (throttled.Any(id => query.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase)))
                {
                    Interlocked.Increment(ref _throttledSends);
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }

                var rows = System.Text.RegularExpressions.Regex
                    .Matches(query, "sprk_matterid eq ([0-9a-fA-F-]{36})")
                    .Select(m => new Dictionary<string, object?>
                    {
                        ["sprk_matterid"] = m.Groups[1].Value,
                        ["sprk_issecure"] = false,
                        ["sprk_accesspermission"] = 100000000,
                        ["statecode"] = 0,
                    });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = rows }), System.Text.Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }
}
