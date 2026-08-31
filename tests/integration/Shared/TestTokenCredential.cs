using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// A <see cref="TokenCredential"/> for test hosts, and the one-line registration that installs it.
///
/// <para><b>The defect this exists to prevent.</b> <c>Program.cs</c> registers
/// <c>TokenCredential</c> as <c>ManagedIdentityCredentialFactory.Create(...)</c>, which returns a
/// <see cref="Azure.Identity.DefaultAzureCredential"/>. A <c>WebApplicationFactory&lt;Program&gt;</c>
/// that does not replace it inherits the REAL credential, so the first outbound-authenticating
/// request in that host runs the real probe chain — environment, workload identity, IMDS
/// (169.254.169.254), Azure CLI. On a machine that is not in Azure the IMDS leg does not fail fast,
/// and the request blocks until <c>HttpClient</c>'s 100-second default timeout aborts it.</para>
///
/// <para><b>Why it presents as something else entirely.</b> DefaultAzureCredential caches which
/// source answered, so only the FIRST caller in a host pays. Whichever test reached an outbound
/// path first timed out; every later one returned fast. The observable result is a failing set that
/// rotates between runs, failures that all last ~100s regardless of what they assert, and — the
/// tell — a test that PASSES in the full suite and FAILS on its own. Read one at a time these look
/// like unrelated assertion bugs in unrelated subsystems, which is how this survived being
/// investigated as five separate problems.</para>
///
/// <para><b>Diagnosis, for the next person.</b> A ~100s duration is not a slow test; it is
/// <c>HttpClient</c>'s default timeout, which means a hang, not slowness. The decisive experiment is
/// to run a PASSING sibling test alone: if it then fails at ~100s, the subject of the test is
/// irrelevant and the host is reaching the network.</para>
///
/// <para>Fixed at the fixture per <c>bff-extensions.md</c> §F.2 (Fixture-Config-FIRST): a real
/// credential in a test host is a non-contract fixture value, so the fixture is the defect. No
/// assertion is relaxed and no production code changes.</para>
/// </summary>
internal static class TestTokenCredential
{
    /// <summary>
    /// Replaces the host's <see cref="TokenCredential"/> with one that answers instantly and never
    /// touches the network. Call this from EVERY <c>WebApplicationFactory&lt;Program&gt;</c>'s
    /// service configuration; <c>TestHostCredentialGuardTests</c> fails the build if a fixture
    /// forgets.
    /// </summary>
    public static IServiceCollection UseStubTokenCredential(this IServiceCollection services)
    {
        services.RemoveAll<TokenCredential>();
        services.AddSingleton<TokenCredential>(new StubTokenCredential());
        return services;
    }

    /// <summary>
    /// Returns a syntactically valid but meaningless bearer token.
    ///
    /// <para>Returning rather than throwing is deliberate. These hosts point at fake Dataverse /
    /// OpenAI URLs, and the tests assert on routing, binding and status codes rather than on the
    /// token. Throwing would convert a hang into an exception without letting the request reach the
    /// handler the test is actually about.</para>
    /// </summary>
    private sealed class StubTokenCredential : TokenCredential
    {
        // Far enough out that DataverseHttpServiceBase's five-minute refresh window never re-requests.
        private static AccessToken Token =>
            new("stub-token-not-a-real-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
