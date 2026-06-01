using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Sprk.Bff.Api.Tests.Mocks;

/// <summary>
/// Verification tests for <see cref="AsyncEnumerableHelpers"/>, <see cref="DelayedAsyncEnumerable{T}"/>,
/// and <see cref="FakeChatClient"/>.
/// Per P1.B Track exit (design.md §7): the helper must have its own tests before
/// Phase 2+3 P23.A cluster migration consumes it.
/// </summary>
/// <remarks>
/// Pattern lineage and rationale: see file-level comment in
/// <c>tests/unit/Sprk.Bff.Api.Tests/Mocks/AsyncEnumerableHelpers.cs</c> and
/// <c>projects/sdap-bff.api-test-suite-repair/decisions/D-01-async-enumerable-helper.md</c>.
/// Traited <c>status=repaired</c> per project CLAUDE.md §6.2 (infrastructure verification).
/// </remarks>
[Trait("status", "repaired")]
public sealed class AsyncEnumerableHelpersTests
{
    // -----------------------------------------------------------------------
    // ToAsyncEnumerable<T>(params T[])
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ToAsyncEnumerable_Params_PreservesOrder()
    {
        var collected = new List<int>();
        await foreach (var item in AsyncEnumerableHelpers.ToAsyncEnumerable(1, 2, 3))
        {
            collected.Add(item);
        }

        collected.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ToAsyncEnumerable_Params_EmptyInput_YieldsZero()
    {
        var count = 0;
        await foreach (var _ in AsyncEnumerableHelpers.ToAsyncEnumerable<int>())
        {
            count++;
        }

        count.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ToAsyncEnumerable<T>(IEnumerable<T>, CancellationToken)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ToAsyncEnumerable_Enumerable_HonorsCancellation_MidEnumeration()
    {
        using var cts = new CancellationTokenSource();
        var collected = new List<int>();

        Func<Task> act = async () =>
        {
            await foreach (var item in AsyncEnumerableHelpers.ToAsyncEnumerable(Enumerable.Range(1, 100), cts.Token))
            {
                collected.Add(item);
                if (collected.Count == 2)
                {
                    cts.Cancel();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        collected.Should().Equal(1, 2);
    }

    [Fact]
    public async Task ToAsyncEnumerable_Enumerable_NullSource_Throws()
    {
        Func<Task> act = async () =>
        {
            await foreach (var _ in AsyncEnumerableHelpers.ToAsyncEnumerable<int>((IEnumerable<int>)null!))
            {
            }
        };

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // EmptyAsyncEnumerable<T>(CancellationToken)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmptyAsyncEnumerable_YieldsZero()
    {
        var count = 0;
        await foreach (var _ in AsyncEnumerableHelpers.EmptyAsyncEnumerable<int>())
        {
            count++;
        }

        count.Should().Be(0);
    }

    [Fact]
    public async Task EmptyAsyncEnumerable_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () =>
        {
            await foreach (var _ in AsyncEnumerableHelpers.EmptyAsyncEnumerable<int>(cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // ThrowingAsyncEnumerable<T>(Exception)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ThrowingAsyncEnumerable_ThrowsOnEnumeration_NotOnConstruction()
    {
        var expected = new InvalidOperationException("boom");

        // Construction must NOT throw.
        var sequence = AsyncEnumerableHelpers.ThrowingAsyncEnumerable<int>(expected);

        Func<Task> act = async () =>
        {
            await foreach (var _ in sequence)
            {
            }
        };

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(expected);
    }

    // -----------------------------------------------------------------------
    // ThrowingAsyncEnumerable<T>(Exception, params T[] prefix)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ThrowingAsyncEnumerable_WithPrefix_YieldsPrefixThenThrows()
    {
        var expected = new TimeoutException("transient");
        var sequence = AsyncEnumerableHelpers.ThrowingAsyncEnumerable<int>(expected, 1, 2);
        var collected = new List<int>();

        Func<Task> act = async () =>
        {
            await foreach (var item in sequence)
            {
                collected.Add(item);
            }
        };

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(expected);
        collected.Should().Equal(1, 2);
    }

    // -----------------------------------------------------------------------
    // FromChunks(params string[])
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FromChunks_Params_ProducesOneChatResponseUpdatePerChunk()
    {
        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in AsyncEnumerableHelpers.FromChunks("hello, ", "world!"))
        {
            collected.Add(update);
        }

        collected.Should().HaveCount(2);
        collected[0].Role.Should().Be(ChatRole.Assistant);
        collected[0].Text.Should().Be("hello, ");
        collected[1].Text.Should().Be("world!");
    }

    // -----------------------------------------------------------------------
    // FromChunks(IEnumerable<ChatResponseUpdate>) — passthrough
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FromChunks_Enumerable_PassesThroughUpdates()
    {
        var input = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "alpha"),
            new ChatResponseUpdate(ChatRole.Assistant, "beta"),
        };

        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in AsyncEnumerableHelpers.FromChunks(input))
        {
            collected.Add(update);
        }

        collected.Should().HaveCount(2);
        collected[0].Text.Should().Be("alpha");
        collected[1].Text.Should().Be("beta");
    }

    // -----------------------------------------------------------------------
    // DelayedAsyncEnumerable<T> — cancellation honored mid-delay
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DelayedAsyncEnumerable_CancellationDuringDelay_Throws()
    {
        var inner = AsyncEnumerableHelpers.ToAsyncEnumerable(1, 2, 3);
        var delayed = new DelayedAsyncEnumerable<int>(inner, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        Func<Task> act = async () =>
        {
            await foreach (var _ in delayed.GetAsyncEnumerator(cts.Token).AsEnumerable())
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "cancellation should abort the per-item Task.Delay quickly");
    }

    // -----------------------------------------------------------------------
    // FakeChatClient — callback property invocation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FakeChatClient_GetResponseAsyncCallback_IsInvoked()
    {
        var sentinel = new ChatResponse(new ChatMessage(ChatRole.Assistant, "from-callback"));
        var client = new FakeChatClient
        {
            GetResponseAsyncCallback = (_, _, _) => Task.FromResult(sentinel),
        };

        var result = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        result.Should().BeSameAs(sentinel);
    }

    [Fact]
    public async Task FakeChatClient_GetStreamingResponseAsyncCallback_IsInvoked()
    {
        var client = new FakeChatClient
        {
            GetStreamingResponseAsyncCallback = (_, _, _) =>
                AsyncEnumerableHelpers.FromChunks("first", "second"),
        };

        var collected = new List<string?>();
        await foreach (var update in client.GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "go") }))
        {
            collected.Add(update.Text);
        }

        collected.Should().Equal("first", "second");
    }

    [Fact]
    public void FakeChatClient_GetServiceCallback_IsInvoked()
    {
        var token = new object();
        var client = new FakeChatClient
        {
            GetServiceCallback = (type, key) => type == typeof(string) ? token : null,
        };

        client.GetService(typeof(string)).Should().BeSameAs(token);
        client.GetService(typeof(int)).Should().BeNull();
    }
}

/// <summary>
/// Local extension to flatten an <see cref="IAsyncEnumerator{T}"/> into an
/// <see cref="IAsyncEnumerable{T}"/> for ergonomic test consumption.
/// </summary>
internal static class TestEnumeratorExtensions
{
    public static async IAsyncEnumerable<T> AsEnumerable<T>(this IAsyncEnumerator<T> enumerator)
    {
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
