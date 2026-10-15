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
}
