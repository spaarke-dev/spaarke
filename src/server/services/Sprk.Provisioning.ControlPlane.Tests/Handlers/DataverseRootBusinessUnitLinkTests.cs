// -----------------------------------------------------------------------------
// DataverseRootBusinessUnitLinkTests.cs
//
// Task 227g — H7 links the customer environment's ROOT business unit to H8's container
// (businessunit.sprk_containerid). unified-access-control-r2 task 076 resolves a record that is not secure to its owning
// business unit's sprk_containerid (RecordContainerResolver), so a fresh stamp without this link stores no non-secure file.
// Real HTTP against a hand-written HttpMessageHandler (never Mock<HttpMessageHandler>, ADR-038).
//
//   L1 empty → PATCH {sprk_containerid} then read back → linked.
//   L2 already this container → linked, nothing written.
//   L3 another container → RootBusinessUnitContainerConflict naming both, nothing written.
//   L4 no root / two roots → RootBusinessUnitUnresolved, nothing written.
//   L5 the PATCH is not kept (read back differs) → failure.
//   L6 400 on the read (solution column missing) → classified, names H6; 403 → AuthFailure.
//   L7 a transport fault → typed failure (never an exception).
//   L8 the PATCH refused (403) → AuthFailure; the read-back GET failing → failure.
//   L9 an HttpClient timeout → typed failure; the caller's cancellation propagates.
//   T259 (ISS-010) — the CUSTOMER's business unit (H10) is linked to the same container with the same rules:
//   C1 empty → PATCH then read back; C2 already this container → nothing written; C3 another container → conflict naming
//   both, nothing written; C4 the unit absent (404) → CustomerBusinessUnitUnresolved; C5 the PATCH not kept → failure.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseRootBusinessUnitLinkTests
{
    private static readonly Uri EnvUri = new("https://acme.crm.dynamics.com/");
    private const string Container = "b!acmeRootContainer";
    private static readonly Guid RootUnit = Guid.Parse("5a5a5a5a-0000-0000-0000-000000000001");

    [Fact]
    public async Task L1_EmptyRootUnit_IsPatchedAndReadBack()
    {
        var dataverse = new ScriptedDataverse()
            .OnGet("/businessunits?", Ok(Roots((RootUnit, null))))
            .OnPatch($"/businessunits({RootUnit})", new HttpResponseMessage(HttpStatusCode.NoContent))
            .OnGet($"/businessunits({RootUnit})?", Ok(new { sprk_containerid = Container }));

        var result = await LinkAsync(dataverse);

        result.Failure.Should().BeNull();
        result.RootBusinessUnitId.Should().Be(RootUnit);
        var patch = dataverse.Requests.Single(r => r.Method == HttpMethod.Patch);
        JsonDocument.Parse(patch.Body!).RootElement.GetProperty("sprk_containerid").GetString().Should().Be(Container);
        dataverse.Requests[0].Uri.Should().Contain("parentbusinessunitid eq null").And.Contain("$top=2");
    }

    [Fact]
    public async Task L2_RootUnitAlreadyNamesTheContainer_NothingIsWritten()
    {
        var dataverse = new ScriptedDataverse().OnGet("/businessunits?", Ok(Roots((RootUnit, Container))));

        var result = await LinkAsync(dataverse);

        result.Failure.Should().BeNull();
        result.RootBusinessUnitId.Should().Be(RootUnit);
        dataverse.Requests.Should().ContainSingle("a resume or a later run finds the link already made");
    }

    [Fact]
    public async Task L3_RootUnitNamesAnotherContainer_IsRefused_NamingBoth_NothingWritten()
    {
        var dataverse = new ScriptedDataverse().OnGet("/businessunits?", Ok(Roots((RootUnit, "b!someOtherContainer"))));

        var result = await LinkAsync(dataverse);

        result.RootBusinessUnitId.Should().BeNull();
        result.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.RootBusinessUnitContainerConflict);
        result.Failure.Diagnostic.Should().Contain("b!someOtherContainer").And.Contain(Container);
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch,
            "replacing it would move where the customer's non-secure files go");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task L4_NotExactlyOneRoot_IsRefused_NothingWritten(int roots)
    {
        var rows = Enumerable.Range(0, roots).Select(i => (Guid.NewGuid(), (string?)null)).ToArray();
        var dataverse = new ScriptedDataverse().OnGet("/businessunits?", Ok(Roots(rows)));

        var result = await LinkAsync(dataverse);

        result.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.RootBusinessUnitUnresolved);
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task L5_APatchTheEnvironmentDidNotKeep_IsAFailure()
    {
        var dataverse = new ScriptedDataverse()
            .OnGet("/businessunits?", Ok(Roots((RootUnit, null))))
            .OnPatch($"/businessunits({RootUnit})", new HttpResponseMessage(HttpStatusCode.NoContent))
            .OnGet($"/businessunits({RootUnit})?", Ok(new { sprk_containerid = (string?)null }));

        var result = await LinkAsync(dataverse);

        result.RootBusinessUnitId.Should().BeNull();
        result.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);
        result.Failure.Diagnostic.Should().Contain("did not keep");
    }

    [Fact]
    public async Task L6_ReadStatuses_AreClassified()
    {
        var missingColumn = await LinkAsync(new ScriptedDataverse()
            .OnGet("/businessunits?", new HttpResponseMessage(HttpStatusCode.BadRequest)));
        missingColumn.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);
        missingColumn.Failure.Diagnostic.Should().Contain("H6");

        var forbidden = await LinkAsync(new ScriptedDataverse()
            .OnGet("/businessunits?", new HttpResponseMessage(HttpStatusCode.Forbidden)));
        forbidden.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.AuthFailure);
    }

    [Fact]
    public async Task L7_ATransportFault_IsATypedFailure()
    {
        var result = await LinkAsync(new ScriptedDataverse { Fault = new HttpRequestException("connection reset") });

        result.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);
        result.Failure.Diagnostic.Should().Contain("connection reset");
    }

    [Fact]
    public async Task L8_APatchOrReadBackFailure_IsTyped()
    {
        var refused = await LinkAsync(new ScriptedDataverse()
            .OnGet("/businessunits?", Ok(Roots((RootUnit, null))))
            .OnPatch($"/businessunits({RootUnit})", new HttpResponseMessage(HttpStatusCode.Forbidden)));
        refused.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.AuthFailure,
            "an identity without Write on businessunit is the likeliest live failure");

        var readBackFails = await LinkAsync(new ScriptedDataverse()
            .OnGet("/businessunits?", Ok(Roots((RootUnit, null))))
            .OnPatch($"/businessunits({RootUnit})", new HttpResponseMessage(HttpStatusCode.NoContent)));
        readBackFails.RootBusinessUnitId.Should().BeNull();
        readBackFails.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);
    }

    [Fact]
    public async Task L9_ATimeoutIsTyped_TheCallersCancellationPropagates()
    {
        var timedOut = await LinkAsync(new ScriptedDataverse { Fault = new TaskCanceledException("timeout") });
        timedOut.Failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => DataverseWebApiEnvVarValuesWriter.LinkRootBusinessUnitContainerAsync(
            new HttpClient(new ScriptedDataverse()), EnvUri, "token", Container, NullLogger.Instance, cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- T259: the customer's business unit ----------

    private static readonly Guid CustomerUnit = Guid.Parse("dddddddd-3333-3333-3333-333333333333");

    [Fact]
    public async Task C1_EmptyCustomerUnit_IsPatchedAndReadBack()
    {
        var dataverse = new ScriptedDataverse()
            .OnGet($"/businessunits({CustomerUnit})?", Ok(new { sprk_containerid = (string?)null }))
            .OnPatch($"/businessunits({CustomerUnit})", new HttpResponseMessage(HttpStatusCode.NoContent));
        var calls = 0;
        dataverse.Rewrite = (method, path) => method == HttpMethod.Get && path.StartsWith($"/businessunits({CustomerUnit})?") && calls++ > 0
            ? Ok(new { sprk_containerid = Container })
            : null;

        (await LinkCustomerAsync(dataverse)).Should().BeNull();

        var patch = dataverse.Requests.Single(r => r.Method == HttpMethod.Patch);
        patch.Uri.Should().EndWith($"/businessunits({CustomerUnit})");
        JsonDocument.Parse(patch.Body!).RootElement.GetProperty("sprk_containerid").GetString().Should().Be(Container);
        dataverse.Requests.Should().HaveCount(3, "read, PATCH, read back");
    }

    [Fact]
    public async Task C2_CustomerUnitAlreadyNamesTheContainer_NothingIsWritten()
    {
        var dataverse = new ScriptedDataverse().OnGet($"/businessunits({CustomerUnit})?", Ok(new { sprk_containerid = Container }));

        (await LinkCustomerAsync(dataverse)).Should().BeNull();
        dataverse.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task C3_CustomerUnitNamesAnotherContainer_IsRefused_NamingBoth_NothingWritten()
    {
        var dataverse = new ScriptedDataverse().OnGet($"/businessunits({CustomerUnit})?", Ok(new { sprk_containerid = "b!other" }));

        var failure = await LinkCustomerAsync(dataverse);

        failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.CustomerBusinessUnitContainerConflict);
        failure!.Diagnostic.Should().Contain("b!other").And.Contain(Container);
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task C4_AbsentCustomerUnit_IsUnresolved_NothingWritten()
    {
        var dataverse = new ScriptedDataverse()
            .OnGet($"/businessunits({CustomerUnit})?", new HttpResponseMessage(HttpStatusCode.NotFound));

        (await LinkCustomerAsync(dataverse))!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.CustomerBusinessUnitUnresolved);
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task C5_APatchTheEnvironmentDidNotKeep_IsAFailure()
    {
        var dataverse = new ScriptedDataverse()
            .OnPatch($"/businessunits({CustomerUnit})", new HttpResponseMessage(HttpStatusCode.NoContent));
        dataverse.Rewrite = (method, path) => method == HttpMethod.Get && path.StartsWith($"/businessunits({CustomerUnit})?")
            ? Ok(new { sprk_containerid = (string?)null })   // a fresh response each read: the PATCH did not stick
            : null;

        var failure = await LinkCustomerAsync(dataverse);

        failure!.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.UnknownInvocationFailure);
        failure!.Diagnostic.Should().Contain("did not keep");
    }

    private static Task<EnvVarValuesWriteOutcome.Failure?> LinkCustomerAsync(ScriptedDataverse dataverse)
        => DataverseWebApiEnvVarValuesWriter.LinkCustomerBusinessUnitContainerAsync(
            new HttpClient(dataverse), EnvUri, "token", CustomerUnit, Container, NullLogger.Instance, CancellationToken.None);

    // ---------- helpers ----------

    private static Task<RootBusinessUnitLinkResult> LinkAsync(ScriptedDataverse dataverse)
        => DataverseWebApiEnvVarValuesWriter.LinkRootBusinessUnitContainerAsync(
            new HttpClient(dataverse), EnvUri, "token", Container, NullLogger.Instance, CancellationToken.None);

    private static object Roots(params (Guid Id, string? Container)[] rows)
        => new { value = rows.Select(r => new { businessunitid = r.Id.ToString(), sprk_containerid = r.Container }).ToArray() };

    private static HttpResponseMessage Ok(object body)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    /// <summary>Answers by method + path prefix (after /api/data/v9.2), recording each request; unmatched → 500.</summary>
    private sealed class ScriptedDataverse : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string PathPrefix, HttpResponseMessage Response)> _routes = [];
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];
        public Exception? Fault { get; init; }

        /// <summary>Optional per-request override (method, path after /api/data/v9.2) — null falls through to the routes.</summary>
        public Func<HttpMethod, string, HttpResponseMessage?>? Rewrite { get; set; }

        public ScriptedDataverse OnGet(string pathPrefix, HttpResponseMessage response) => On(HttpMethod.Get, pathPrefix, response);
        public ScriptedDataverse OnPatch(string pathPrefix, HttpResponseMessage response) => On(HttpMethod.Patch, pathPrefix, response);

        private ScriptedDataverse On(HttpMethod method, string pathPrefix, HttpResponseMessage response)
        {
            _routes.Add((method, pathPrefix, response));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            Requests.Add((request.Method, uri, body));
            if (Fault is not null)
            {
                throw Fault;
            }

            var path = uri.Replace("/api/data/v9.2", string.Empty, StringComparison.Ordinal);
            if (Rewrite?.Invoke(request.Method, path) is { } rewritten)
            {
                return rewritten;
            }
            var route = _routes.FirstOrDefault(r => r.Method == request.Method && path.StartsWith(r.PathPrefix, StringComparison.Ordinal));
            return route.Response ?? new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
