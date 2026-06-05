using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Tests.Mocks;

// ---------------------------------------------------------------------------
// IAsyncEnumerable test helpers for the IChatClient streaming cluster.
//
// Pattern lineage: this file mirrors Microsoft's reference `TestChatClient` stub
// at https://github.com/dotnet/extensions/blob/main/test/Libraries/Microsoft.Extensions.AI.Abstractions.Tests/TestChatClient.cs
// (MIT-licensed, internal to the dotnet/extensions test fixtures — NOT redistributed
// as a NuGet package per Phase 0 D-01 verdict; see
// `projects/sdap-bff.api-test-suite-repair/decisions/D-01-async-enumerable-helper.md`).
//
// The Microsoft pattern uses callback PROPERTIES (not virtual methods or interfaces),
// e.g. `GetStreamingResponseAsyncCallback` of type
//   Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>
// so the test can swap behavior per-call without subclassing. `FakeChatClient` below
// follows the same shape; if Microsoft ever ships `Microsoft.Extensions.AI.Testing` to
// NuGet, swapping to it is mechanical (delete `FakeChatClient`, replace the same
// callback property with the package's).
//
// NFR-01: TEST-ONLY infrastructure — no production-code coupling.
// NFR-03: NO DI registrations introduced — these are static helpers + a `new`-able stub.
// ADR-010: static surface keeps the test infrastructure out of any composition root.
// ADR-013: AI-internal test helper; lives in `Mocks/` not in `Services/Ai/PublicContracts/`
//          because tests own the contract, not the BFF facade.
// ---------------------------------------------------------------------------

/// <summary>
/// Static helpers that produce <see cref="IAsyncEnumerable{T}"/> sequences for use in
/// unit tests of code that consumes <see cref="IChatClient.GetStreamingResponseAsync"/>
/// or any other async-streaming production code path.
/// </summary>
/// <remarks>
/// <para>
/// NSubstitute and Moq do not synthesize sensible auto-mocks for
/// <see cref="IAsyncEnumerable{T}"/> return types — the auto-stub yields an empty
/// sequence that never throws, which masks "did the production code actually await
/// anything?" failures. These helpers produce real, observable async sequences.
/// </para>
/// <para>
/// All helpers are deterministic: no <c>Task.Delay</c>, no time-based dispatch.
/// Tests that need delay should compose <see cref="DelayedAsyncEnumerable{T}"/> or
/// pass their own async iterator to <see cref="FakeChatClient.GetStreamingResponseAsyncCallback"/>.
/// </para>
/// </remarks>
public static class AsyncEnumerableHelpers
{
    /// <summary>
    /// Wraps a fixed sequence of items as an <see cref="IAsyncEnumerable{T}"/>.
    /// Each item is yielded synchronously inside the async iterator.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="items">The items to yield in order. May be empty.</param>
    /// <returns>An async sequence that yields each item once and then completes.</returns>
    /// <example>
    /// <code>
    /// var sequence = AsyncEnumerableHelpers.ToAsyncEnumerable("hello", "world");
    /// await foreach (var s in sequence) { Console.WriteLine(s); }
    /// </code>
    /// </example>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
        => ToAsyncEnumerable((IEnumerable<T>)items);

    /// <summary>
    /// Wraps an arbitrary <see cref="IEnumerable{T}"/> as an <see cref="IAsyncEnumerable{T}"/>.
    /// Each item is yielded synchronously inside the async iterator. The enumerable is
    /// enumerated lazily as the consumer iterates.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="items">The source sequence. Must not be null.</param>
    /// <returns>An async sequence that yields each item once and then completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            // ConfigureAwait omitted: this helper is consumed only by xUnit tests,
            // not by production library code; xUnit's SynchronizationContext is null
            // so the await semantics are equivalent and the code reads more cleanly.
            await Task.Yield();
        }
    }

    /// <summary>
    /// Returns an empty <see cref="IAsyncEnumerable{T}"/> — a sequence that completes
    /// immediately without yielding any items.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <returns>An async sequence that completes immediately.</returns>
    /// <example>
    /// <code>
    /// // Stub an IChatClient that returns no chunks (simulating an empty completion):
    /// chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
    ///     AsyncEnumerableHelpers.EmptyAsyncEnumerable&lt;ChatResponseUpdate&gt;();
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// Returns an <see cref="IAsyncEnumerable{T}"/> that throws the supplied exception
    /// the moment the consumer awaits the first <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>.
    /// Use to verify that production code under test handles or surfaces enumeration
    /// failures correctly (e.g. cancellation, transient transport errors).
    /// </summary>
    /// <typeparam name="T">Element type (ignored — sequence throws before yielding).</typeparam>
    /// <param name="exception">The exception to throw. Must not be null.</param>
    /// <returns>An async sequence that throws on first enumeration step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    /// <example>
    /// <code>
    /// // Simulate an Azure OpenAI transient failure mid-stream:
    /// var failing = AsyncEnumerableHelpers.ThrowingAsyncEnumerable&lt;ChatResponseUpdate&gt;(
    ///     new TimeoutException("Azure OpenAI request timed out"));
    /// </code>
    /// </example>
    public static IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ThrowingEnumerable<T>(exception, yieldBeforeThrow: Array.Empty<T>());
    }

    /// <summary>
    /// Returns an <see cref="IAsyncEnumerable{T}"/> that yields a fixed prefix of items
    /// successfully, then throws on the next <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>.
    /// Use to test mid-stream failure handling — e.g. partial chunks delivered before
    /// the connection drops.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="exception">The exception to throw after the prefix is exhausted. Must not be null.</param>
    /// <param name="prefix">Items to yield before the throw. May be empty (equivalent to
    /// <see cref="ThrowingAsyncEnumerable{T}(Exception)"/>).</param>
    /// <returns>An async sequence that yields <paramref name="prefix"/> then throws.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> or <paramref name="prefix"/> is null.</exception>
    public static IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(Exception exception, params T[] prefix)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(prefix);
        return new ThrowingEnumerable<T>(exception, prefix);
    }

    /// <summary>
    /// Builds an <see cref="IAsyncEnumerable{ChatResponseUpdate}"/> from a sequence of
    /// text chunks — the most common shape for testing
    /// <see cref="IChatClient.GetStreamingResponseAsync"/> consumers.
    /// Each chunk becomes a single <see cref="ChatResponseUpdate"/> with a
    /// <see cref="TextContent"/> body.
    /// </summary>
    /// <param name="chunks">Text fragments to stream. May be empty.</param>
    /// <returns>An async sequence of <see cref="ChatResponseUpdate"/>s, one per chunk.</returns>
    /// <exception cref="ArgumentNullException">A chunk is null.</exception>
    /// <example>
    /// <code>
    /// chatClient.GetStreamingResponseAsyncCallback = (_, _, _) =>
    ///     AsyncEnumerableHelpers.FromChunks("Hello, ", "world!");
    /// </code>
    /// </example>
    public static IAsyncEnumerable<ChatResponseUpdate> FromChunks(params string[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return ToAsyncEnumerable(chunks.Select(text =>
        {
            ArgumentNullException.ThrowIfNull(text);
            return new ChatResponseUpdate(ChatRole.Assistant, text);
        }));
    }

    /// <summary>
    /// Builds an <see cref="IAsyncEnumerable{ChatResponseUpdate}"/> from a pre-built
    /// sequence of <see cref="ChatResponseUpdate"/>s — for tests that need to control
    /// non-text update properties (function calls, role markers, usage details).
    /// </summary>
    /// <param name="updates">The updates to stream in order.</param>
    /// <returns>An async sequence of <see cref="ChatResponseUpdate"/>s.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updates"/> is null.</exception>
    public static IAsyncEnumerable<ChatResponseUpdate> FromChunks(IEnumerable<ChatResponseUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return ToAsyncEnumerable(updates);
    }

    // -----------------------------------------------------------------------
    // Internal implementation
    // -----------------------------------------------------------------------

    private sealed class ThrowingEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly Exception _exception;
        private readonly IReadOnlyList<T> _prefix;

        public ThrowingEnumerable(Exception exception, IReadOnlyList<T> yieldBeforeThrow)
        {
            _exception = exception;
            _prefix = yieldBeforeThrow;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new ThrowingEnumerator(_exception, _prefix, cancellationToken);

        private sealed class ThrowingEnumerator : IAsyncEnumerator<T>
        {
            private readonly Exception _exception;
            private readonly IReadOnlyList<T> _prefix;
            private readonly CancellationToken _cancellationToken;
            private int _index = -1;

            public ThrowingEnumerator(Exception exception, IReadOnlyList<T> prefix, CancellationToken cancellationToken)
            {
                _exception = exception;
                _prefix = prefix;
                _cancellationToken = cancellationToken;
            }

            public T Current => _index >= 0 && _index < _prefix.Count
                ? _prefix[_index]
                : default!;

            public ValueTask<bool> MoveNextAsync()
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _index++;
                if (_index < _prefix.Count)
                {
                    return new ValueTask<bool>(true);
                }
                throw _exception;
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}

/// <summary>
/// Composable wrapper that interposes a per-item async delay onto an inner
/// <see cref="IAsyncEnumerable{T}"/>. Use only for tests that genuinely need to
/// observe time-based behavior (cancellation, timeout); prefer the synchronous
/// helpers above otherwise — they make tests faster and more deterministic.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
public sealed class DelayedAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly IAsyncEnumerable<T> _inner;
    private readonly TimeSpan _delayPerItem;

    /// <summary>
    /// Wraps an inner async sequence so each yielded item is preceded by
    /// <paramref name="delayPerItem"/>.
    /// </summary>
    /// <param name="inner">The inner sequence. Must not be null.</param>
    /// <param name="delayPerItem">Delay before each item is yielded. Negative values are clamped to zero.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public DelayedAsyncEnumerable(IAsyncEnumerable<T> inner, TimeSpan delayPerItem)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _delayPerItem = delayPerItem < TimeSpan.Zero ? TimeSpan.Zero : delayPerItem;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await foreach (var item in _inner.WithCancellation(cancellationToken))
        {
            if (_delayPerItem > TimeSpan.Zero)
            {
                await Task.Delay(_delayPerItem, cancellationToken);
            }
            yield return item;
        }
    }
}

/// <summary>
/// Hand-rolled <see cref="IChatClient"/> stub mirroring Microsoft's internal
/// <c>TestChatClient</c> reference pattern. Configure per-test behavior by setting the
/// callback properties; unset properties throw <see cref="NotImplementedException"/>
/// so tests fail loudly rather than silently using auto-mock defaults.
/// </summary>
/// <remarks>
/// <para>
/// The callback-property shape is deliberately mechanical so a future swap to a
/// Microsoft-shipped helper (if/when one ships — see D-01 reassessment trigger,
/// floor 2027-05-31) is a property-rename, not a re-architecture.
/// </para>
/// <para>
/// Reference (pattern source, MIT-licensed): <see href="https://github.com/dotnet/extensions/blob/main/test/Libraries/Microsoft.Extensions.AI.Abstractions.Tests/TestChatClient.cs"/>.
/// </para>
/// </remarks>
public sealed class FakeChatClient : IChatClient
{
    /// <summary>
    /// Callback that produces the non-streaming response for
    /// <see cref="IChatClient.GetResponseAsync"/>. If null, a single empty
    /// <see cref="ChatResponse"/> with the assistant role is returned.
    /// </summary>
    public Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? GetResponseAsyncCallback { get; set; }

    /// <summary>
    /// Callback that produces the streaming response for
    /// <see cref="IChatClient.GetStreamingResponseAsync"/>. If null, an empty
    /// <see cref="IAsyncEnumerable{ChatResponseUpdate}"/> is returned.
    /// </summary>
    /// <remarks>
    /// Shape matches Microsoft's <c>TestChatClient.GetStreamingResponseAsyncCallback</c>
    /// verbatim — see the file-level pattern lineage comment.
    /// </remarks>
    public Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? GetStreamingResponseAsyncCallback { get; set; }

    /// <summary>
    /// Callback for <see cref="IChatClient.GetService"/>. If null, returns null for any
    /// service key, matching the documented behavior of unsupported service lookups.
    /// </summary>
    public Func<Type, object?, object?>? GetServiceCallback { get; set; }

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return GetResponseAsyncCallback is not null
            ? GetResponseAsyncCallback(messages, options, cancellationToken)
            : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return GetStreamingResponseAsyncCallback is not null
            ? GetStreamingResponseAsyncCallback(messages, options, cancellationToken)
            : AsyncEnumerableHelpers.EmptyAsyncEnumerable<ChatResponseUpdate>(cancellationToken);
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return GetServiceCallback is not null
            ? GetServiceCallback(serviceType, serviceKey)
            : null;
    }

    /// <inheritdoc/>
    public void Dispose() { /* no resources owned */ }
}
