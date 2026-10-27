// -----------------------------------------------------------------------------
// H10PropagationRetryTests.cs
//
// T186 audit (H10). H10 creates objects in Dataverse and Entra and re-reads them seconds later; both platforms are
// eventually consistent and answer 429 / 5xx under load. The verifiers and the role lookup used to judge the first
// answer — a late Graph assignment or a not-yet-copied role became a T3 / T2 quarantine or a Failure. ADR-038 path #1:
// real collaborators over a hand-written HttpMessageHandler, delays injected through the options (never real waits).
//
// COVERAGE (each: waits it out / still reports a real mismatch / non-transient is not retried):
//   Finding 1  GraphRestAppRoleParityVerifier.VerifyAsync
//   Finding 2  DataverseWebApiAppUserVerifier.VerifyAsync
//   Finding 3  DataverseWebApiAppUserCreator.EnsureAppUserAsync — the role-copy lookup in the just-created business unit
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H10PropagationRetryTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string AppId = "40000000-0000-0000-0000-000000000004";
    private const string CustomerBu = "20000000-0000-0000-0000-000000000002";
    private const string SystemUser = "60000000-0000-0000-0000-000000000006";
    private const string RoleCopy = "70000000-0000-0000-0000-000000000007";
    private const string AadObjectId = "50000000-0000-0000-0000-000000000005";

    private const string StampSpId = "66666666-7777-8888-9999-000000000000";
    private const string GraphSpId = "99999999-0000-0000-0000-000000000001";
    private const string FscSelected = "40dc41bc-0f7e-42ff-89bd-d9516947e474";

    private static readonly IReadOnlyList<GraphAppRoleEntry> Expected = new[]
    {
        new GraphAppRoleEntry("FileStorageContainer.Selected", FscSelected),
    };

    // Small injected delays: the production defaults wait seconds.
    private static H10DataverseAppUserGraphParityOptions FastOptions() => new()
    {
        PropagationRetryAttempts = 4,
        PropagationRetryDelay = TimeSpan.FromMilliseconds(1),
        TransientRetryAttempts = 3,
        TransientRetryDelay = TimeSpan.FromMilliseconds(1),
    };

    // =========================================================================
    // Finding 1 — Graph app-role parity verifier
    // =========================================================================

    [Fact]
    public async Task Graph_AssignmentListedLate_IsLookedAgain_AndVerified()
    {
        // Seconds after H10 POSTed the grant Graph does not list it yet (replication); the third read shows it.
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[]
        {
            () => Assignments(), () => Assignments(), () => Assignments(FscSelected),
        });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Verified>();
        graph.AssignmentReads.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Graph_TransientFault_IsRetried_ThenVerified(HttpStatusCode transient)
    {
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(transient), () => new HttpResponseMessage(transient), () => Assignments(FscSelected),
        });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Verified>();
        graph.AssignmentReads.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Graph_TransientFaultThatPersists_IsUnverified_NeverPartial(HttpStatusCode transient)
    {
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(transient) });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        var unverified = result.Should().BeOfType<GraphAppRoleParityResult.Unverified>(
            "an outage is not evidence of a wrong GUID — Partial quarantines the run").Subject;
        unverified.ExpectedCount.Should().Be(1);
        unverified.Diagnostic.Should().Contain(((int)transient).ToString());
        graph.AssignmentReads.Should().Be(3, "TransientRetryAttempts bounds the retries");
    }

    [Fact]
    public async Task Graph_DroppedConnection_IsRetried_ThenUnverified()
    {
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => throw new HttpRequestException("connection reset") });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Unverified>();
        graph.AssignmentReads.Should().Be(3);
    }

    [Fact]
    public async Task Graph_HttpClientTimeout_IsRetried_ThenUnverified()
    {
        // The HttpClient timeout surfaces as TaskCanceledException while the CALLER's token is still live.
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => throw new TaskCanceledException("timed out") });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Unverified>();
        graph.AssignmentReads.Should().Be(3);
    }

    [Fact]
    public async Task Graph_CallerCancellation_IsNotARetry_ItPropagates()
    {
        using var cts = new CancellationTokenSource();
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[]
        {
            () => { cts.Cancel(); throw new TaskCanceledException("caller cancelled"); },
        });

        var act = () => GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        graph.AssignmentReads.Should().Be(1);
    }

    [Fact]
    public async Task Graph_RoleReallyMissing_IsStillPartial_AfterTheBoundedLooks()
    {
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => Assignments() });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        var partial = result.Should().BeOfType<GraphAppRoleParityResult.Partial>(
            "a role that never shows up keeps its old T3 outcome").Subject;
        partial.MissingRoleValues.Should().Equal("FileStorageContainer.Selected");
        graph.AssignmentReads.Should().Be(4, "PropagationRetryAttempts bounds the looks");
    }

    [Fact]
    public async Task Graph_Forbidden_IsNotRetried_AndKeepsTheOldPartialOutcome()
    {
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(HttpStatusCode.Forbidden) });

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, Expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Partial>("403 is a real answer, not replication");
        graph.AssignmentReads.Should().Be(1);
    }

    [Fact]
    public async Task Graph_NullAppRoleIdInTheExpectedSet_IsNotWaitedOn()
    {
        // A null id can never appear by waiting.
        var graph = new ScriptedGraph(assignmentResponses: new Func<HttpResponseMessage>[] { () => Assignments() });
        var expected = new[] { new GraphAppRoleEntry("Some.Role", null) };

        var result = await GraphVerifier(graph).VerifyAsync(StampSpId, TenantId, expected, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleParityResult.Partial>();
        graph.AssignmentReads.Should().Be(1);
    }

    // =========================================================================
    // Finding 2 — Dataverse application-user verifier
    // =========================================================================

    [Fact]
    public async Task Dataverse_ZeroRowsRightAfterCreate_IsLookedAgain_AndVerified()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[]
        {
            () => Rows(0), () => Rows(0), () => Rows(1),
        });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        var verified = result.Should().BeOfType<DataverseAppUserVerificationResult.Verified>().Subject;
        verified.SystemUserId.Should().Be(SystemUser);
        verified.AzureActiveDirectoryObjectId.Should().Be(AadObjectId);
        http.Requests.Should().Be(3);
    }

    [Fact]
    public async Task Dataverse_NotFoundRightAfterCreate_IsLookedAgain_AndVerified()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(HttpStatusCode.NotFound), () => Rows(1) });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().BeOfType<DataverseAppUserVerificationResult.Verified>();
        http.Requests.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Dataverse_TransientFault_IsRetried_ThenVerified(HttpStatusCode transient)
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[]
        {
            () => new HttpResponseMessage(transient), () => new HttpResponseMessage(transient), () => Rows(1),
        });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().BeOfType<DataverseAppUserVerificationResult.Verified>();
        http.Requests.Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Dataverse_TransientFaultThatPersists_IsUnverified_NeverCountMismatch(HttpStatusCode transient)
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(transient) });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().BeOfType<DataverseAppUserVerificationResult.Unverified>(
            "CountMismatch(0) quarantines the run as a T2 silent-fail trap")
            .Which.Diagnostic.Should().Contain(((int)transient).ToString());
        http.Requests.Should().Be(3, "TransientRetryAttempts bounds the retries");
    }

    [Fact]
    public async Task Dataverse_DroppedConnection_ThenTimeout_IsRetried_ThenUnverified()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[]
        {
            () => throw new HttpRequestException("connection reset"),
            () => throw new TaskCanceledException("timed out"),
            () => throw new HttpRequestException("connection reset"),
        });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().BeOfType<DataverseAppUserVerificationResult.Unverified>();
        http.Requests.Should().Be(3);
    }

    [Fact]
    public async Task Dataverse_RowReallyAbsent_IsStillCountMismatchZero_AfterTheBoundedLooks()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[] { () => Rows(0) });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().Be(new DataverseAppUserVerificationResult.CountMismatch(0));
        http.Requests.Should().Be(4, "PropagationRetryAttempts bounds the looks");
    }

    [Fact]
    public async Task Dataverse_Forbidden_IsNotRetried_AndKeepsTheOldCountMismatchOutcome()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(HttpStatusCode.Forbidden) });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().Be(new DataverseAppUserVerificationResult.CountMismatch(0));
        http.Requests.Should().Be(1, "403 is a real answer, not propagation");
    }

    [Fact]
    public async Task Dataverse_DuplicateRows_AreNotRetried_AndKeepTheOldCountMismatchOutcome()
    {
        var http = new FakeHttp(new Func<HttpResponseMessage>[] { () => Rows(2) });

        var result = await DataverseVerifier(http).VerifyAsync(EnvUrl, TenantId, AppId, CancellationToken.None);

        result.Should().Be(new DataverseAppUserVerificationResult.CountMismatch(2));
        http.Requests.Should().Be(1, "a duplicate does not go away by waiting");
    }

    // =========================================================================
    // Finding 3 — role-copy lookup in the just-created business unit
    // =========================================================================

    [Fact]
    public async Task RoleCopy_EmptyRightAfterTheUnitIsCreated_IsLookedAgain_UntilTheCopyLands()
    {
        var roleResponses = new Func<HttpResponseMessage>[] { () => Json("""{"value":[]}"""), () => Json("""{"value":[]}"""), () => RoleRows(1) };
        var http = AppUserFake(roleResponses);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Count("/roles?").Should().Be(3);
        http.Count("/systemuserroles_association/$ref").Should().Be(1, "the role is associated once the copy exists");
    }

    [Fact]
    public async Task RoleCopy_NotFoundRightAfterTheUnitIsCreated_IsLookedAgain()
    {
        var roleResponses = new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(HttpStatusCode.NotFound), () => RoleRows(1) };
        var http = AppUserFake(roleResponses);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Count("/roles?").Should().Be(2);
    }

    [Fact]
    public async Task RoleCopy_NeverAppears_IsStillAFailure_AfterTheBoundedLooks()
    {
        var http = AppUserFake(new Func<HttpResponseMessage>[] { () => Json("""{"value":[]}""") });

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic.Should().Contain("not found in business unit");
        http.Count("/roles?").Should().Be(4, "PropagationRetryAttempts bounds the looks");
        http.Count("/systemuserroles_association").Should().Be(0);
    }

    [Fact]
    public async Task RoleCopy_AmbiguousCopies_AreNotRetried()
    {
        var http = AppUserFake(new Func<HttpResponseMessage>[] { () => RoleRows(2) });

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic.Should().Contain("More than one role");
        http.Count("/roles?").Should().Be(1, "two copies is a real answer — retrying cannot fix it");
    }

    [Fact]
    public async Task RoleCopy_Forbidden_IsNotRetried_AndKeepsTheOldFailure()
    {
        var http = AppUserFake(new Func<HttpResponseMessage>[] { () => new HttpResponseMessage(HttpStatusCode.Forbidden) });

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic.Should().Contain("403");
        http.Count("/roles?").Should().Be(1);
    }

    // ---------------- helpers ----------------

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Rows(int count)
        => Json($$"""{"value":[{{string.Join(",", Enumerable.Repeat($$"""{"systemuserid":"{{SystemUser}}","azureactivedirectoryobjectid":"{{AadObjectId}}"}""", count))}}]}""");

    private static HttpResponseMessage RoleRows(int count)
        => Json($$"""{"value":[{{string.Join(",", Enumerable.Repeat($$"""{"roleid":"{{RoleCopy}}"}""", count))}}]}""");

    private static HttpResponseMessage Assignments(params string[] appRoleIds)
    {
        var entries = appRoleIds.Select((id, i) => "{\"id\":\"a-" + i + "\",\"appRoleId\":\"" + id + "\",\"resourceId\":\"" + GraphSpId + "\"}");
        return Json("{\"value\":[" + string.Join(",", entries) + "]}");
    }

    private static DataverseAppUserCreationRequest Request()
        => new(EnvUrl, TenantId, AppId, "System Administrator", Guid.Parse(CustomerBu), null);

    private static DataverseWebApiAppUserCreator Creator(FakeHttp http)
        => new(new HttpClient(http), Options.Create(FastOptions()), NullLogger<DataverseWebApiAppUserCreator>.Instance, _ => new FakeCredential());

    private static DataverseWebApiAppUserVerifier DataverseVerifier(FakeHttp http)
        => new(new HttpClient(http), Options.Create(FastOptions()), NullLogger<DataverseWebApiAppUserVerifier>.Instance, _ => new FakeCredential());

    private static GraphRestAppRoleParityVerifier GraphVerifier(ScriptedGraph graph)
        => new(new HttpClient(graph), new L2GraphAppRolesRegistry(), Options.Create(FastOptions()),
            NullLogger<GraphRestAppRoleParityVerifier>.Instance, _ => new FakeCredential());

    /// <summary>A new application user (nothing exists yet) with scripted answers to the role-copy lookup.</summary>
    private static FakeHttp AppUserFake(Func<HttpResponseMessage>[] roleResponses)
        => new(roleResponses, route: (req, next) =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (req.Method == HttpMethod.Post)
            {
                if (uri.EndsWith("/api/data/v9.2/systemusers", StringComparison.Ordinal))
                {
                    var created = new HttpResponseMessage(HttpStatusCode.NoContent);
                    created.Headers.Add("OData-EntityId", $"{EnvUrl}api/data/v9.2/systemusers({SystemUser})");
                    return created;
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (uri.Contains("/systemusers?")) return Json("""{"value":[]}""");
            if (uri.Contains("/roles?")) return next();
            if (uri.Contains("/systemuserroles_association?")) return Json("""{"value":[]}""");
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"unexpected {req.Method} {uri}") };
        });

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Hand-rolled fake handler (NOT Mock&lt;HttpMessageHandler&gt;). The scripted responses are served in order to the
    /// requests <c>route</c> hands to <c>next()</c> (all requests when no route is given); the last one repeats.
    /// </summary>
    private sealed class FakeHttp(
        Func<HttpResponseMessage>[] scripted,
        Func<HttpRequestMessage, Func<HttpResponseMessage>, HttpResponseMessage>? route = null) : HttpMessageHandler
    {
        private readonly List<string> _uris = [];
        private int _served;

        /// <summary>Requests that consumed a scripted response.</summary>
        public int Requests => _served;

        public int Count(string uriFragment) => _uris.Count(u => u.Contains(uriFragment, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _uris.Add(request.RequestUri!.AbsoluteUri);
            HttpResponseMessage Next()
            {
                var script = scripted[Math.Min(_served, scripted.Length - 1)];
                _served++;   // counted before the script runs: a scripted fault throws
                return script();
            }
            return Task.FromResult(route is null ? Next() : route(request, Next));
        }
    }

    /// <summary>A scripted Microsoft Graph: the Graph resource SP, and scripted answers to the appRoleAssignments read.</summary>
    private sealed class ScriptedGraph(Func<HttpResponseMessage>[] assignmentResponses) : HttpMessageHandler
    {
        public int AssignmentReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.Contains("/appRoleAssignments", StringComparison.Ordinal))
            {
                var script = assignmentResponses[Math.Min(AssignmentReads, assignmentResponses.Length - 1)];
                AssignmentReads++;   // counted before the script runs: a scripted fault throws
                return Task.FromResult(script());
            }
            if (uri.Contains("/servicePrincipals?", StringComparison.Ordinal))
            {
                return Task.FromResult(Json($$"""{"value":[{"id":"{{GraphSpId}}","appRoles":[{"id":"{{FscSelected}}","value":"FileStorageContainer.Selected"}]}]}"""));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
