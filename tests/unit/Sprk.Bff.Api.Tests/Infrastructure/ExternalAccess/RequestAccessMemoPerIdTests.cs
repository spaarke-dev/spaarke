// GitHub #2845 round 2 — RequestAccessMemo.GetOrReadManyAsync: the per-ID memo of a batch read.
//
// Contracts: overlapping id sets read only the missing ids; a batch that throws, leaves an id out, or answers any id with an
// unprovable value stores NOTHING (the next ask reads again); a caller waiting on an id of such a batch reads it itself; keys of
// different readers (and principals) never meet; Clear() forgets per-id answers too.

using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public sealed class RequestAccessMemoPerIdTests
{
    private static readonly Guid A = Guid.Parse("28452100-0000-4000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("28452100-0000-4000-8000-00000000000b");
    private static readonly Guid C = Guid.Parse("28452100-0000-4000-8000-00000000000c");

    private const string Reader = "test-reader:sprk_matter";

    /// <summary>A batch read that answers each id with its own name, recording what it was asked.</summary>
    private sealed class Batch
    {
        public List<IReadOnlyList<Guid>> Asked { get; } = new();

        public Func<Guid, string?> Answer { get; set; } = id => id.ToString("N")[^1..];

        public Exception? Throw { get; set; }

        public Task<IReadOnlyDictionary<Guid, string>> ReadAsync(IReadOnlyList<Guid> ids, CancellationToken _)
        {
            lock (Asked)
            {
                Asked.Add(ids.ToList());
            }

            if (Throw is { } ex)
            {
                return Task.FromException<IReadOnlyDictionary<Guid, string>>(ex);
            }

            IReadOnlyDictionary<Guid, string> answer = ids
                .Select(id => (id, value: Answer(id)))
                .Where(x => x.value is not null)
                .ToDictionary(x => x.id, x => x.value!);
            return Task.FromResult(answer);
        }
    }

    private static Task<IReadOnlyDictionary<Guid, string>> Ask(RequestAccessMemo memo, Batch batch, params Guid[] ids)
        => memo.GetOrReadManyAsync<string>(Reader, ids, batch.ReadAsync, value => value != "unknown", CancellationToken.None);

    [Fact]
    public async Task OverlappingIdSets_ReadOnlyTheMissingIds()
    {
        var memo = new RequestAccessMemo();
        var batch = new Batch();

        (await Ask(memo, batch, A, B)).Should().Equal(new Dictionary<Guid, string> { [A] = "a", [B] = "b" });
        (await Ask(memo, batch, B, C)).Should().Equal(new Dictionary<Guid, string> { [B] = "b", [C] = "c" });
        (await Ask(memo, batch, A, B, C)).Should().HaveCount(3);

        batch.Asked.Should().HaveCount(2);
        batch.Asked[0].Should().BeEquivalentTo(new[] { A, B });
        batch.Asked[1].Should().Equal(new[] { C }, "B was answered by the first batch; only C was read");
    }

    [Fact]
    public async Task ABatchThatThrows_StoresNothing_AndTheNextAskReadsEveryIdAgain()
    {
        var memo = new RequestAccessMemo();
        var batch = new Batch { Throw = new HttpRequestException("faulted") };

        await FluentActions.Awaiting(() => Ask(memo, batch, A, B)).Should().ThrowAsync<HttpRequestException>();
        memo.Count.Should().Be(0, "a faulted batch is remembered for no id");

        batch.Throw = null;
        (await Ask(memo, batch, A, B)).Should().HaveCount(2);
        batch.Asked[1].Should().BeEquivalentTo(new[] { A, B });
    }

    [Theory]
    [InlineData("an id left out")]
    [InlineData("an unprovable answer")]
    public async Task APartialBatch_StoresNothing_TheCallerGetsWhatWasRead_AndTheNextAskReadsAgain(string partial)
    {
        var memo = new RequestAccessMemo();
        var batch = new Batch
        {
            Answer = id => id == B ? (partial == "an id left out" ? null : "unknown") : id.ToString("N")[^1..],
        };

        var first = await Ask(memo, batch, A, B);

        first[A].Should().Be("a", "the caller that read is answered with exactly what the read returned");
        if (partial == "an id left out")
        {
            first.Should().NotContainKey(B);
        }
        else
        {
            first[B].Should().Be("unknown");
        }

        memo.Count.Should().Be(0, $"a batch with {partial} is remembered for NO id — not even A");

        batch.Answer = id => id.ToString("N")[^1..];
        (await Ask(memo, batch, A)).Should().Equal(new Dictionary<Guid, string> { [A] = "a" });
        batch.Asked.Should().HaveCount(2, "A was read again: the partial batch stored nothing");
    }

    [Fact]
    public async Task ACallerWaitingOnAnIdOfAFaultedBatch_ReadsItItself()
    {
        var memo = new RequestAccessMemo();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new List<IReadOnlyList<Guid>>();

        var owner = memo.GetOrReadManyAsync<string>(Reader, new[] { A, B }, async (ids, _) =>
        {
            lock (reads) reads.Add(ids.ToList());
            await release.Task;
            throw new HttpRequestException("faulted");
        }, _ => true, CancellationToken.None);

        var waiter = memo.GetOrReadManyAsync<string>(Reader, new[] { B, C }, (ids, _) =>
        {
            lock (reads) reads.Add(ids.ToList());
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, id => id.ToString("N")[^1..]));
        }, _ => true, CancellationToken.None);

        release.SetResult();

        await FluentActions.Awaiting(() => owner).Should().ThrowAsync<HttpRequestException>();
        (await waiter).Should().Equal(new Dictionary<Guid, string> { [B] = "b", [C] = "c" },
            "the waiter did not inherit the fault on B; it read B itself");
        reads.Should().HaveCount(3);
        reads[1].Should().Equal(new[] { C }, "the waiter first read only its own missing id");
        reads[2].Should().Equal(new[] { B }, "then B, once the batch it waited on faulted");
    }

    [Fact]
    public async Task DifferentReadersAndPrincipals_NeverShareAnAnswer_AndClearForgetsPerIdAnswers()
    {
        var memo = new RequestAccessMemo();
        var batch = new Batch();

        await memo.GetOrReadManyAsync<string>("reader-1:sprk_matter", new[] { A }, batch.ReadAsync, _ => true, CancellationToken.None);
        await memo.GetOrReadManyAsync<string>("reader-1:sprk_project", new[] { A }, batch.ReadAsync, _ => true, CancellationToken.None);
        await memo.GetOrReadManyAsync<string>("reader-2:sprk_matter", new[] { A }, batch.ReadAsync, _ => true, CancellationToken.None);
        batch.Asked.Should().HaveCount(3, "the reader and the table are part of every key");

        RequestAccessMemo.SecureVetoSubjectsKey(A, B, new[] { C })
            .Should().NotBe(RequestAccessMemo.SecureVetoSubjectsKey(B, B, new[] { C }), "the principal is part of the key");
        RequestAccessMemo.SecureVetoSubjectsKey(A, B, new[] { C })
            .Should().NotBe(RequestAccessMemo.SecureVetoSubjectsKey(A, null, new[] { C }));

        memo.Clear();
        await memo.GetOrReadManyAsync<string>("reader-1:sprk_matter", new[] { A }, batch.ReadAsync, _ => true, CancellationToken.None);
        batch.Asked.Should().HaveCount(4, "a write forgets per-id answers too");
    }
}
