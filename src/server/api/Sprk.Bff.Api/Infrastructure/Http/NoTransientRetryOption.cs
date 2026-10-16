using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Kiota.Abstractions;

namespace Sprk.Bff.Api.Infrastructure.Http;

/// <summary>
/// Marks one Graph request as non-idempotent: <see cref="GraphHttpMessageHandler"/> does not re-send it after a 5xx, a
/// 408 or a transport fault, because Graph may already have carried it out (#1755). A 429 is still retried: Graph
/// answers 429 to a request it did not process.
/// </summary>
/// <remarks>
/// <para>Set it on the Kiota request configuration (<c>rc =&gt; rc.Options.Add(option)</c>). Kiota copies each request
/// option into <see cref="HttpRequestMessage.Options"/> under its type's full name, which is where the handler reads
/// it. It is the same instance, so the handler records on it what the caller then reads.</para>
/// <para><b>What it records</b> (#1755 round 2): the handler marks each attempt as it goes to the wire and records the
/// answer it got. A failure that never reached Graph — an open circuit, a token that could not be acquired, a timeout
/// while waiting out a 429, a refused connect or a name that did not resolve (round 3) — leaves nothing sent, so the
/// caller can say nothing was created. Only the LAST attempt
/// matters: the request is retried only after a 429, which Graph did not process.</para>
/// <para>One option per request. <see cref="CloseAndReadOutcomeUnknown"/> closes it: an attempt not yet sent when the
/// caller reads the outcome is not sent at all, so the answer the caller acts on cannot be overtaken by a late send.</para>
/// </remarks>
public sealed class NoTransientRetryOption : IRequestOption
{
    internal static readonly HttpRequestOptionsKey<IRequestOption> Key = new(typeof(NoTransientRetryOption).FullName!);

    private const int NothingSent = 0;
    private const int InFlight = 1;       // sent; no answer yet, or none ever (a fault or a timeout)
    private const int Refused = 2;        // answered with a settled refusal: a 4xx other than 408, or a 429
    private const int Answered = 3;       // answered with anything else: a 2xx, a 5xx or a 408
    private const int Closed = -1;        // the caller has read the outcome; nothing more is sent

    private int _lastAttempt = NothingSent;

    /// <summary>The option this request carries, if any. Allocation-free: one <c>TryGetValue</c>.</summary>
    internal static bool TryGetFrom(HttpRequestMessage request, [NotNullWhen(true)] out NoTransientRetryOption? option)
    {
        option = request.Options.TryGetValue(Key, out var found) ? found as NoTransientRetryOption : null;
        return option is not null;
    }

    /// <summary>
    /// Records that an attempt is about to be sent. False when the caller has already read the outcome: the attempt
    /// must then not be sent.
    /// </summary>
    internal bool TryBeginAttempt()
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref _lastAttempt);
            if (seen == Closed)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _lastAttempt, InFlight, seen) != seen);

        return true;
    }

    /// <summary>
    /// Records that the attempt in flight never reached the server: the connection was refused or the name did not
    /// resolve (#1755 round 3). Nothing was sent, so nothing was carried out.
    /// </summary>
    internal void RecordNotSent() => Interlocked.CompareExchange(ref _lastAttempt, NothingSent, InFlight);

    /// <summary>Records the answer the attempt in flight got.</summary>
    internal void RecordAnswer(HttpStatusCode status) =>
        Interlocked.CompareExchange(ref _lastAttempt, IsSettledRefusal(status) ? Refused : Answered, InFlight);

    /// <summary>
    /// Closes the option and returns true when an attempt was sent and its answer did not settle that Graph refused
    /// it: no answer (a fault, a timeout), or an answer Graph may send after carrying the request out (a 2xx, a 5xx, a
    /// 408). False when nothing was sent, or the last attempt was refused (a 4xx other than 408, or a 429).
    /// </summary>
    public bool CloseAndReadOutcomeUnknown() => Interlocked.Exchange(ref _lastAttempt, Closed) is InFlight or Answered;

    private static bool IsSettledRefusal(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 and not 408;
}
