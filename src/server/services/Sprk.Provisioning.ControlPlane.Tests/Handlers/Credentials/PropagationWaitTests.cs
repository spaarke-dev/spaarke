// -----------------------------------------------------------------------------
// PropagationWaitTests.cs
//
// T186 audit — the shared propagation waits H6 / H7 use for the FIRST use of a just-created app registration / FIC
// (EntraPropagationRetry) and of a just-created Dataverse application user (DataverseAppUserPropagationGate).
//
// ADR-038 CATEGORY: pure C# unit tests. Real TimeProvider.System with tiny real TimeSpans (parity with the importer
// polling tests); hand-rolled fake TokenCredential / HttpMessageHandler (never Mock<HttpMessageHandler>).
//
//   E1  Each propagation code (70025, 700211, 700212, 700213, 700016) is waited out, then the token is returned.
//   E2  An unrelated AADSTS code (7000215 bad secret, 50034 ...) is NOT retried — one attempt, same exception.
//   E3  A propagation code that never clears is retried only for the budget, then the SAME exception surfaces.
//   E4  Zero budget disables the wait; a non-auth exception carrying the text is not retried; cancellation propagates.
//   E5  The code is found in an inner exception; a longer code (AADSTS7002111) is not a match.
//   G1  Dataverse 403/401 then 200 -> the 200 is returned (the first call waits the app user / role out).
//   G2  403 that never clears -> the 403 response itself after the budget (caller's old classification stands).
//   G3  Once any non-401/403 answer arrived, a later 403 is genuine: returned at once.
//   G4  Other statuses (404 / 500) are never retried; zero budget sends once.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.Credentials;

public sealed class PropagationWaitTests
{
    private static readonly TokenRequestContext Context = new(new[] { "https://acme.crm.dynamics.com/.default" });

    private static WorkerCredentialSelectionOptions Budget(TimeSpan timeout) => new()
    {
        EntraPropagationTimeout = timeout,
        EntraPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
        DataverseAppUserPropagationTimeout = timeout,
        DataverseAppUserPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
    };

    private static Task<AccessToken> AcquireAsync(
        TokenCredential credential, WorkerCredentialSelectionOptions options, CancellationToken ct = default)
        => EntraPropagationRetry.GetTokenAsync(
            credential, Context, options, TimeProvider.System, NullLogger.Instance, "test", ct);

    // ---------- E: Entra propagation ----------

    [Theory]
    [InlineData("AADSTS70025: The client has no configured federated identity credentials.")]
    [InlineData("AADSTS700211: No matching federated identity record found for presented assertion issuer.")]
    [InlineData("AADSTS700212: No matching federated identity record found for presented assertion audience.")]
    [InlineData("AADSTS700213: No matching federated identity record found for presented assertion subject.")]
    [InlineData("AADSTS700016: Application with identifier 'x' was not found in the directory.")]
    public async Task E1_PropagationCode_IsWaitedOut_ThenTheTokenIsReturned(string message)
    {
        var credential = new ScriptedCredential(failures: 2, () => new AuthenticationFailedException(message));

        var token = await AcquireAsync(credential, Budget(TimeSpan.FromSeconds(10)));

        token.Token.Should().Be("token-3");
        credential.Calls.Should().Be(3, "two propagation failures, then the exchange that works");
    }

    [Theory]
    [InlineData("AADSTS7000215: Invalid client secret provided.")]
    [InlineData("AADSTS50034: The user account does not exist in the directory.")]
    [InlineData("AADSTS700024: Client assertion is not within its valid time range.")]
    public async Task E2_UnrelatedAadstsCode_IsNotRetried_SameExceptionSurfaces(string message)
    {
        var failure = new AuthenticationFailedException(message);
        var credential = new ScriptedCredential(failures: int.MaxValue, () => failure);

        var act = () => AcquireAsync(credential, Budget(TimeSpan.FromSeconds(10)));

        (await act.Should().ThrowAsync<AuthenticationFailedException>()).Which.Should().BeSameAs(failure);
        credential.Calls.Should().Be(1, "any other auth error fails as before — no waiting");
    }

    [Fact]
    public async Task E3_PropagationCodeThatNeverClears_IsRetriedOnlyForTheBudget_ThenTheSameExceptionSurfaces()
    {
        var failure = new AuthenticationFailedException("AADSTS70025: no federated identity credentials.");
        var credential = new ScriptedCredential(failures: int.MaxValue, () => failure);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var act = () => AcquireAsync(credential, Budget(TimeSpan.FromMilliseconds(80)));

        (await act.Should().ThrowAsync<AuthenticationFailedException>()).Which.Should().BeSameAs(failure);
        credential.Calls.Should().BeGreaterThan(1).And.BeLessThan(100, "bounded — never forever");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task E4_ZeroBudget_DoesNotWait_NonAuthExceptionIsNotRetried_CancellationPropagates()
    {
        var zero = new ScriptedCredential(failures: int.MaxValue, () => new AuthenticationFailedException("AADSTS70025: x"));
        await FluentActions.Awaiting(() => AcquireAsync(zero, Budget(TimeSpan.Zero))).Should().ThrowAsync<AuthenticationFailedException>();
        zero.Calls.Should().Be(1);

        var wrongType = new ScriptedCredential(failures: int.MaxValue, () => new InvalidOperationException("AADSTS70025: x"));
        await FluentActions.Awaiting(() => AcquireAsync(wrongType, Budget(TimeSpan.FromSeconds(10)))).Should().ThrowAsync<InvalidOperationException>();
        wrongType.Calls.Should().Be(1, "only an authentication failure is a propagation candidate");

        using var cancelled = new CancellationTokenSource();
        var cancelling = new ScriptedCredential(failures: int.MaxValue, () =>
        {
            cancelled.Cancel();
            return new AuthenticationFailedException("AADSTS70025: x");
        });
        await FluentActions.Awaiting(() => AcquireAsync(cancelling, Budget(TimeSpan.FromSeconds(10)), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void E5_CodeIsFoundInAnInnerException_ALongerCodeIsNotAMatch()
    {
        var wrapped = new AuthenticationFailedException(
            "ClientAssertionCredential authentication failed", new InvalidOperationException("... AADSTS700213: no match ..."));
        EntraPropagationRetry.IsPropagationFailure(wrapped).Should().BeTrue();

        EntraPropagationRetry.IsPropagationFailure(new AuthenticationFailedException("AADSTS7002111: something else")).Should().BeFalse();
        EntraPropagationRetry.IsPropagationFailure(new AuthenticationFailedException("AADSTS700210: something else")).Should().BeFalse();
    }

    // ---------- G: Dataverse application-user propagation ----------

    private static DataverseAppUserPropagationGate Gate(TimeSpan timeout)
        => new(Budget(timeout), TimeProvider.System, NullLogger.Instance, "test");

    private static Task<HttpResponseMessage> SendAsync(DataverseAppUserPropagationGate gate, HttpClient client)
        => gate.SendAsync(client, () => new HttpRequestMessage(HttpMethod.Get, "https://acme.crm.dynamics.com/api/data/v9.2/x"), default);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task G1_UnauthorizedOrForbidden_ThenOk_ReturnsTheOk(HttpStatusCode transient)
    {
        var handler = new SequenceHandler(transient, transient, HttpStatusCode.OK);

        using var response = await SendAsync(Gate(TimeSpan.FromSeconds(10)), new HttpClient(handler));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task G2_ForbiddenThatNeverClears_ReturnsTheForbiddenAfterTheBudget()
    {
        var handler = new SequenceHandler(HttpStatusCode.Forbidden);

        using var response = await SendAsync(Gate(TimeSpan.FromMilliseconds(80)), new HttpClient(handler));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an exhausted budget leaves the caller's classification intact");
        handler.Calls.Should().BeGreaterThan(1).And.BeLessThan(100);
    }

    [Fact]
    public async Task G3_OnceAnAnswerArrived_ALater403IsGenuine_ReturnedAtOnce()
    {
        var handler = new SequenceHandler(HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.Forbidden);
        var gate = Gate(TimeSpan.FromSeconds(10));
        var client = new HttpClient(handler);

        using (await SendAsync(gate, client)) { }   // 403 -> waited out -> 200 (established)
        using var later = await SendAsync(gate, client);

        later.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Calls.Should().Be(3, "the third call is a real answer — no retry");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task G4_OtherStatuses_AreNeverRetried_ZeroBudgetSendsOnce(HttpStatusCode status)
    {
        var handler = new SequenceHandler(status);
        using var response = await SendAsync(Gate(TimeSpan.FromSeconds(10)), new HttpClient(handler));
        response.StatusCode.Should().Be(status);
        handler.Calls.Should().Be(1);

        var zeroHandler = new SequenceHandler(HttpStatusCode.Forbidden);
        using var zero = await SendAsync(Gate(TimeSpan.Zero), new HttpClient(zeroHandler));
        zero.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        zeroHandler.Calls.Should().Be(1);
    }

    // ---------- fakes ----------

    private sealed class ScriptedCredential(int failures, Func<Exception> failure) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= failures)
            {
                throw failure();
            }
            return new AccessToken($"token-{Calls}", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Answers the scripted statuses in order; the last one repeats.</summary>
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
        }
    }
}
