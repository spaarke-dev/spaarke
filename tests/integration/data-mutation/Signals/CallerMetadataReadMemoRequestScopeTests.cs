using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ISS-010 fix 1 (#3184): the request-scoped table-metadata memo (<see cref="CallerMetadataReadMemo.RequestScope"/>) at the
/// caller-client boundary. Within one request and one caller client, a successful <c>EntityDefinitions(…)</c> read is asked
/// once across memos and across row writes; a failure, a row, a catalog row and an access answer are asked every time; a
/// different request or client shares nothing; and a create with no scope (the chat tool, jobs) keeps the per-create memo.
/// </summary>
public sealed class CallerMetadataReadMemoRequestScopeTests
{
    private const string Metadata = "EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName";

    /// <summary>The caller's Dataverse, answered in memory and counted per path.</summary>
    private sealed class CountingClient : IDataverseUserClient
    {
        public Dictionary<string, int> Gets { get; } = new(StringComparer.Ordinal);

        public Func<string, DataverseUserResponse?>? Answer { get; set; }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Gets[relativePath] = Gets.GetValueOrDefault(relativePath) + 1;
            return Task.FromResult(Answer?.Invoke(relativePath)
                ?? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { EntitySetName = "sprk_matters" })));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));
    }

    [Fact]
    public async Task InOneRequest_TwoMemosOfTheSameClient_ReadAMetadataPathOnce_EvenAcrossARowWrite()
    {
        var client = new CountingClient();
        var scope = CallerMetadataReadMemo.RequestScope.Of(new DefaultHttpContext());

        var first = CallerMetadataReadMemo.For(client, scope);
        (await first.GetAsync(Metadata, default)).IsSuccess.Should().BeTrue();
        await first.PostAsync("/api/data/v9.2/sprk_todos", "{}", default); // a row write does not change a table's schema
        var second = CallerMetadataReadMemo.For(client, scope);
        var answer = await second.GetAsync(Metadata, default);

        client.Gets[Metadata].Should().Be(1);
        answer.Body!.Value.GetProperty("EntitySetName").GetString().Should().Be("sprk_matters");
    }

    [Fact]
    public async Task InOneRequest_AFailedMetadataRead_IsAskedAgain()
    {
        var client = new CountingClient();
        var faults = 1;
        client.Answer = path => path == Metadata && faults-- > 0 ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "x") : null;
        var scope = CallerMetadataReadMemo.RequestScope.Of(new DefaultHttpContext());

        (await CallerMetadataReadMemo.For(client, scope).GetAsync(Metadata, default)).IsSuccess.Should().BeFalse();
        (await CallerMetadataReadMemo.For(client, scope).GetAsync(Metadata, default)).IsSuccess.Should().BeTrue();

        client.Gets[Metadata].Should().Be(2, "a failure is never remembered");
    }

    [Theory]
    [InlineData("sprk_recordtype_refs(c0430000-0000-4000-8000-000000000010)?$select=sprk_recordtype_refid")]
    [InlineData("sprk_matters(c0430000-0000-4000-8000-000000000011)?$select=sprk_matterid")]
    [InlineData("systemusers(c0430000-0000-4000-8000-000000000001)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1=x")]
    [InlineData("systemusers(c0430000-0000-4000-8000-000000000001)/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1=x")]
    public async Task InOneRequest_ARowACatalogRowOrAnAccessQuestion_IsAskedEveryTime(string path)
    {
        var client = new CountingClient();
        var scope = CallerMetadataReadMemo.RequestScope.Of(new DefaultHttpContext());

        await CallerMetadataReadMemo.For(client, scope).GetAsync(path, default);
        await CallerMetadataReadMemo.For(client, scope).GetAsync(path, default);

        client.Gets[path].Should().Be(2);
    }

    [Fact]
    public async Task AnotherRequest_OrAnotherClient_SharesNothing()
    {
        var client = new CountingClient();
        var other = new CountingClient();
        var request = CallerMetadataReadMemo.RequestScope.Of(new DefaultHttpContext());

        await CallerMetadataReadMemo.For(client, request).GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(client, CallerMetadataReadMemo.RequestScope.Of(new DefaultHttpContext())).GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(other, request).GetAsync(Metadata, default);

        client.Gets[Metadata].Should().Be(2, "a second request asks again");
        other.Gets[Metadata].Should().Be(1, "another caller client of the same request asks for itself");
    }

    [Fact]
    public async Task WithNoRequestScope_TheMemoIsPerInstance_AndForgetsOnAWrite_AsBefore()
    {
        var client = new CountingClient();
        CallerMetadataReadMemo.RequestScope.Of(null).Should().BeNull("off-request (the chat tool, jobs) there is no scope");

        var memo = CallerMetadataReadMemo.For(client, null);
        await memo.GetAsync(Metadata, default);
        await memo.GetAsync(Metadata, default);
        await memo.PatchAsync("sprk_todos(c0430000-0000-4000-8000-000000000051)", "{}", default);
        await memo.GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(client, null).GetAsync(Metadata, default);

        client.Gets[Metadata].Should().Be(3, "once, again after the write, and again by another memo");
    }

    [Fact]
    public void OneRequest_HasOneScope()
    {
        var http = new DefaultHttpContext();

        CallerMetadataReadMemo.RequestScope.Of(http).Should().BeSameAs(CallerMetadataReadMemo.RequestScope.Of(http));
    }
}
