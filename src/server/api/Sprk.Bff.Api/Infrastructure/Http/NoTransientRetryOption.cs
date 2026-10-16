using Microsoft.Kiota.Abstractions;

namespace Sprk.Bff.Api.Infrastructure.Http;

/// <summary>
/// Marks one Graph request as non-idempotent: <see cref="GraphHttpMessageHandler"/> does not re-send it after a 5xx, a
/// 408 or a transport fault, because Graph may already have carried it out (#1755). A 429 is still retried: Graph
/// answers 429 to a request it did not process.
/// </summary>
/// <remarks>
/// Set it on the Kiota request configuration (<c>rc =&gt; rc.Options.Add(new NoTransientRetryOption())</c>). Kiota copies
/// each request option into <see cref="HttpRequestMessage.Options"/> under its type's full name, which is where the
/// handler reads it.
/// </remarks>
public sealed class NoTransientRetryOption : IRequestOption
{
    internal static readonly HttpRequestOptionsKey<IRequestOption> Key = new(typeof(NoTransientRetryOption).FullName!);

    /// <summary>True when <paramref name="request"/> carries this option.</summary>
    internal static bool IsSetOn(HttpRequestMessage request) =>
        request.Options.TryGetValue(Key, out var option) && option is NoTransientRetryOption;
}
