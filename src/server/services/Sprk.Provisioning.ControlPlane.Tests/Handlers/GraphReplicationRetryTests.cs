// -----------------------------------------------------------------------------
// GraphReplicationRetryTests.cs
//
// T186 L26 — the first live H3 created spaarke-bff-api-acme and failed seconds
// later: the identifierUris PATCH by object id got Graph 404
// Request_ResourceNotFound because the new object had not replicated. H3's
// post-create calls (that PATCH, the optionalClaims read, the service principal
// ensure) now retry while Graph has not caught up. ADR-038 path #1.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Azure;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphReplicationRetryTests
{
    private static ODataError Error(int status, string code, string message) =>
        new() { ResponseStatusCode = status, Error = new MainError { Code = code, Message = message } };

    private static readonly ODataError NotFound = Error(404, "Request_ResourceNotFound",
        "Resource '88cfedd1-9a2a-400f-8bd7-7e6d3453e0d9' does not exist or one of its queried reference-property objects are not present.");

    private static Task<T> Retry<T>(Func<CancellationToken, Task<T>> call, int attempts = 4) =>
        GraphAppRegistrationProvisioner.RetryWhileReplicatingAsync(
            call, GraphAppRegistrationProvisioner.IsNotYetReplicated, attempts, TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5), NullLogger.Instance, "test", CancellationToken.None);

    [Fact]
    public async Task NotFoundWhileTheNewAppReplicates_IsRetriedUntilItSucceeds()
    {
        var calls = 0;

        var result = await Retry(_ => ++calls < 3 ? throw NotFound : Task.FromResult("patched"));

        result.Should().Be("patched", "a 404 seconds after the create is replication lag, not a missing app (T186 L26)");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task NotFoundOnEveryAttempt_PropagatesAfterTheLastAttempt()
    {
        var calls = 0;

        var act = () => Retry<string>(_ => { calls++; throw NotFound; }, attempts: 3);

        (await act.Should().ThrowAsync<ODataError>()).Which.ResponseStatusCode.Should().Be(404);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task AnotherError_IsNotRetried()
    {
        var calls = 0;

        var act = () => Retry<string>(_ => { calls++; throw Error(403, "Authorization_RequestDenied", "Insufficient privileges."); });

        await act.Should().ThrowAsync<ODataError>();
        calls.Should().Be(1, "only replication lag is waited out — a permission error fails at once");
    }

    [Theory]
    [InlineData(404, "Request_ResourceNotFound", "Resource 'x' does not exist.", true)]
    [InlineData(400, "Request_BadRequest", "The appId 'b4882848-d094-4f17-b87b-c9b9cd8f7ec6' of the service principal does not reference a valid application object.", true)]
    [InlineData(400, "Request_BadRequest", "Property identifierUris is invalid.", false)]
    [InlineData(403, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.", false)]
    public void IsNotYetReplicated_RecognisesOnlyReplicationLag(int status, string code, string message, bool expected)
        => GraphAppRegistrationProvisioner.IsNotYetReplicated(Error(status, code, message)).Should().Be(expected);

    // T186 L27: the first live H3 created its stamp credential, then the Worker credential's POST got 400
    // "Request contains a property with duplicate values" although no credential carried that triple.
    [Theory]
    [InlineData(400, "Request_BadRequest", "Request contains a property with duplicate values.", true)]
    [InlineData(404, "Request_ResourceNotFound", "Resource 'x' does not exist.", true)]
    [InlineData(400, "Request_BadRequest", "Property 'subject' is invalid.", false)]
    [InlineData(403, "Authorization_RequestDenied", "Insufficient privileges to complete the operation.", false)]
    public void IsFicWriteNotYetConsistent_RecognisesOnlyReplicationLag(int status, string code, string message, bool expected)
        => GraphAppRegistrationProvisioner.IsFicWriteNotYetConsistent(Error(status, code, message)).Should().Be(expected);

    [Fact]
    public async Task DuplicateValuesWhileTheCredentialCollectionReplicates_IsRetried()
    {
        var calls = 0;

        var result = await GraphAppRegistrationProvisioner.RetryWhileReplicatingAsync(
            _ => ++calls < 2 ? throw Error(400, "Request_BadRequest", "Request contains a property with duplicate values.") : Task.FromResult(true),
            GraphAppRegistrationProvisioner.IsFicWriteNotYetConsistent, 4, TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5), NullLogger.Instance, "test", CancellationToken.None);

        result.Should().BeTrue();
        calls.Should().Be(2);
    }

    // T186 audit: H3's deferred KV commit (after consent) had one 15 s budget for every write, and its own timeout
    // escaped as OperationCanceledException. Each write now gets its own timeout and transient failures are retried.
    private static readonly PendingKvSecretWrite[] TwoWrites =
    [
        new("sprk-acme-prod-kv", GraphAppRegistrationProvisioner.ClientIdSecretName, "b4882848-d094-4f17-b87b-c9b9cd8f7ec6"),
        new("sprk-acme-prod-kv", "BFF-API-Audience", "api://b4882848-d094-4f17-b87b-c9b9cd8f7ec6"),
    ];

    private static Task<string?> Commit(Func<PendingKvSecretWrite, CancellationToken, Task> write, int attempts = 4,
        CancellationToken ct = default) =>
        GraphAppRegistrationProvisioner.CommitWithRetryAsync(
            TwoWrites, write, attempts, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(200),
            NullLogger.Instance, ct);

    [Fact]
    public async Task KvCommit_ForbiddenWhileTheGrantPropagates_IsRetriedAndEveryWriteLands()
    {
        var calls = 0;
        var written = new List<string>();

        var diagnostic = await Commit((w, _) =>
        {
            if (++calls <= 2) throw new RequestFailedException(403, "ForbiddenByRbac");
            written.Add(w.SecretName);
            return Task.CompletedTask;
        });

        diagnostic.Should().BeNull();
        written.Should().Equal(TwoWrites.Select(w => w.SecretName));
    }

    [Fact]
    public async Task KvCommit_AnAttemptThatTimesOut_IsRetried_AndNoCancellationEscapes()
    {
        var calls = 0;

        var diagnostic = await Commit(async (_, ct) =>
        {
            if (++calls == 1) await Task.Delay(Timeout.Infinite, ct);
        });

        diagnostic.Should().BeNull("the attempt's own timeout is transient, not the caller cancelling");
        calls.Should().Be(3, "one timed-out attempt, then both writes");
    }

    [Fact]
    public async Task KvCommit_PersistentServiceError_ReturnsTheDiagnosticAfterTheLastAttempt()
    {
        var calls = 0;

        var diagnostic = await Commit((_, _) => { calls++; throw new RequestFailedException(503, "Service Unavailable"); }, attempts: 3);

        diagnostic.Should().Contain("503").And.Contain("after 3 attempts");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task KvCommit_NonTransientError_IsNotRetried()
    {
        var calls = 0;

        var diagnostic = await Commit((_, _) => { calls++; throw new RequestFailedException(400, "BadParameter"); });

        diagnostic.Should().StartWith("KV commit failed: RequestFailedException");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task KvCommit_CallerCancellation_StillThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Commit((_, ct) => Task.Delay(Timeout.Infinite, ct), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(403, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    public void IsTransientKvStatus_RecognisesOnlyTransientFailures(int status, bool expected)
        => GraphAppRegistrationProvisioner.IsTransientKvStatus(status).Should().Be(expected);
}
