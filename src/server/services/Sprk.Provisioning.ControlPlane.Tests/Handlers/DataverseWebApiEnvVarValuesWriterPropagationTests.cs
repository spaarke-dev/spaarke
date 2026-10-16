// -----------------------------------------------------------------------------
// DataverseWebApiEnvVarValuesWriterPropagationTests.cs
//
// T186 audit — H7's writer is the first consumer of the H3-created BFF app registration / FIC (token exchange) and the
// H10-created application user (first Dataverse read). It waits both out, bounded, and otherwise keeps its failure
// mapping (AuthFailure -> Resumable at the handler).
//
// ADR-038 CATEGORY: pure C# unit tests; hand-rolled fake HttpMessageHandler / IHttpClientFactory / TokenCredential;
// real TimeProvider.System with tiny real TimeSpans.
//
//   W1  First token exchange AADSTS70025 (FIC not replicated) -> waited out -> values written.
//   W2  Unrelated AADSTS code -> one attempt -> AuthFailure "Token acquisition failed", no Dataverse call.
//   W3  First definition lookup 403 (app user / role not effective) -> waited out -> values written.
//   W4  The same 403 on the root business unit read (the first call when a container is linked) -> waited out.
//   W5  403 that never clears -> AuthFailure after the budget (old mapping), nothing upserted.
//   W6  Once the first read answered, a later 403 (the upsert) is genuine -> AuthFailure at once, no retry.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiEnvVarValuesWriterPropagationTests
{
    private const string EnvUrl = "https://acme.crm.dynamics.com/";
    private const string DefinitionId = "d3d3d3d3-0000-0000-0000-000000000001";
    private const string RootUnit = "5a5a5a5a-0000-0000-0000-000000000001";

    private static WorkerCredentialSelectionOptions Propagation(TimeSpan budget) => new()
    {
        EntraPropagationTimeout = budget,
        EntraPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
        DataverseAppUserPropagationTimeout = budget,
        DataverseAppUserPropagationRetryInterval = TimeSpan.FromMilliseconds(5),
    };

    private static EnvVarValuesWriteRequest Request(string? rootContainer = null) => new(
        TargetDataverseUrl: EnvUrl,
        TenantId: "00000000-1111-2222-3333-444444444444",
        ClientId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        ClientSecret: null,
        Values: new[] { new KeyValuePair<string, string>("sprk_TestVariable", "value-1") },
        RootBusinessUnitContainerId: rootContainer);

    // ---------- Finding 1: Entra ----------

    [Fact]
    public async Task W1_FicNotReplicatedOnTheFirstExchange_IsWaitedOut_ThenValuesAreWritten()
    {
        var credential = new FlakyCredential(failures: 2, "AADSTS70025: The client has no configured federated identity credentials.");
        var dataverse = new Dataverse();

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromSeconds(10)), credential)
            .WriteAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Success>();
        credential.Calls.Should().Be(3);
    }

    [Fact]
    public async Task W2_UnrelatedAadstsCode_IsNotRetried_AuthFailure_NoDataverseCall()
    {
        var credential = new FlakyCredential(failures: int.MaxValue, "AADSTS7000215: Invalid client secret provided.");
        var dataverse = new Dataverse();

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromSeconds(10)), credential)
            .WriteAsync(Request(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Failure>().Which;
        failure.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.AuthFailure);
        failure.Diagnostic.Should().StartWith("Token acquisition failed").And.Contain("AADSTS7000215");
        credential.Calls.Should().Be(1);
        dataverse.Requests.Should().BeEmpty();
    }

    // ---------- Finding 2: Dataverse application user ----------

    [Fact]
    public async Task W3_FirstDefinitionLookupForbidden_IsWaitedOut_ThenValuesAreWritten()
    {
        var gets = 0;
        var dataverse = new Dataverse
        {
            Rewrite = (method, path) => method == HttpMethod.Get && path.StartsWith("/environmentvariabledefinitions", StringComparison.Ordinal) && ++gets <= 2
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : null,
        };

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromSeconds(10)))
            .WriteAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Success>();
        gets.Should().Be(3);
        dataverse.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task W4_RootBusinessUnitReadForbidden_IsWaitedOut_ThenLinkedAndWritten()
    {
        var reads = 0;
        var dataverse = new Dataverse
        {
            Rewrite = (method, path) => method == HttpMethod.Get && path.StartsWith("/businessunits?", StringComparison.Ordinal)
                ? ++reads <= 2
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    : Json($$"""{"value":[{"businessunitid":"{{RootUnit}}","sprk_containerid":"b!container"}]}""")
                : null,
        };

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromSeconds(10)))
            .WriteAsync(Request(rootContainer: "b!container"), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Success>();
        reads.Should().Be(3);
    }

    [Fact]
    public async Task W5_ForbiddenThatNeverClears_IsAuthFailureAfterTheBudget_NothingUpserted()
    {
        var dataverse = new Dataverse
        {
            Rewrite = (method, path) => path.StartsWith("/environmentvariabledefinitions", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : null,
        };

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromMilliseconds(80)))
            .WriteAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Failure>()
            .Which.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.AuthFailure);
        dataverse.Requests.Count.Should().BeGreaterThan(1, "the first call was retried within the budget");
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Post || r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task W6_AfterTheFirstReadAnswered_ALater403IsGenuine_NotRetried()
    {
        var dataverse = new Dataverse
        {
            Rewrite = (method, _) => method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null,
        };

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.FromSeconds(10)))
            .WriteAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Failure>()
            .Which.FailureKind.Should().Be(EnvVarValuesWriteFailureKind.AuthFailure);
        dataverse.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    // ---------- helpers ----------

    // ---------- T186 L38: the definition -> values navigation property ----------

    [Fact]
    public async Task L38_DefinitionLookup_ExpandsTheRealNavigationProperty()
    {
        var dataverse = new Dataverse();

        await NewWriter(dataverse, Propagation(TimeSpan.Zero)).WriteAsync(Request(), CancellationToken.None);

        var lookup = dataverse.Requests.First(r => r.Method == HttpMethod.Get && r.Path.StartsWith("/environmentvariabledefinitions", StringComparison.Ordinal));
        lookup.Path.Should().Contain("$expand=environmentvariabledefinition_environmentvariablevalue(",
            "Dataverse answers 400 to $expand=environmentvariablevalues (the entity-set name), verified live on spaarke-acme");
        lookup.Path.Should().NotContain("$expand=environmentvariablevalues(");
    }

    [Fact]
    public async Task L38_AnExistingValueUnderTheNavigationProperty_IsPatched_NotDuplicated()
    {
        const string ValueId = "e5e5e5e5-0000-0000-0000-000000000001";
        var dataverse = new Dataverse
        {
            Rewrite = (method, path) => method == HttpMethod.Get && path.StartsWith("/environmentvariabledefinitions", StringComparison.Ordinal)
                ? Json($$"""{"value":[{"environmentvariabledefinitionid":"{{DefinitionId}}","schemaname":"sprk_TestVariable","environmentvariabledefinition_environmentvariablevalue":[{"environmentvariablevalueid":"{{ValueId}}","value":"old"}]}]}""")
                : null,
        };

        var outcome = await NewWriter(dataverse, Propagation(TimeSpan.Zero)).WriteAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<EnvVarValuesWriteOutcome.Success>();
        dataverse.Requests.Should().Contain(r => r.Method == HttpMethod.Patch && r.Path == $"/environmentvariablevalues({ValueId})");
        dataverse.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Path == "/environmentvariablevalues",
            "a value that already exists is updated, never created a second time");
    }

    private static DataverseWebApiEnvVarValuesWriter NewWriter(
        Dataverse dataverse, WorkerCredentialSelectionOptions credentials, TokenCredential? credential = null)
        => new(
            new SingleClientFactory(new HttpClient(dataverse)),
            Options.Create(new EnvVarValuesOptions { Credentials = credentials }),
            NullLogger<DataverseWebApiEnvVarValuesWriter>.Instance,
            TimeProvider.System,
            (_, _, _) => credential ?? new FlakyCredential(failures: 0, string.Empty));

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FlakyCredential(int failures, string message) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls <= failures
                ? throw new AuthenticationFailedException(message)
                : new AccessToken("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Happy-path env-var Dataverse: one definition without a value (POST creates it). <see cref="Rewrite"/> overrides
    /// per (method, path-after-/api/data/v9.2); null falls through to the happy path.
    /// </summary>
    private sealed class Dataverse : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        public Func<HttpMethod, string, HttpResponseMessage?>? Rewrite { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery).Replace("/api/data/v9.2", string.Empty, StringComparison.Ordinal);
            Requests.Add((request.Method, path));

            if (Rewrite?.Invoke(request.Method, path) is { } overridden)
            {
                return Task.FromResult(overridden);
            }
            if (request.Method == HttpMethod.Get && path.StartsWith("/environmentvariabledefinitions", StringComparison.Ordinal))
            {
                return Task.FromResult(Json($$"""{"value":[{"environmentvariabledefinitionid":"{{DefinitionId}}","schemaname":"sprk_TestVariable"}]}"""));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
