// -----------------------------------------------------------------------------
// GraphRestAppRoleReconcileTests.cs
//
// Task 261 (G31): HTTP-shape tests for H10's Graph REST collaborators —
// GraphRestAppRoleGranter.RemoveUnexpectedRolesAsync / GrantRolesAsync and
// GraphRestAppRoleParityVerifier.FindUnexpectedRolesAsync — against a scripted
// HttpMessageHandler (ADR-038 path #1: no live Graph, no Mock<T>).
//
// What these pin down:
//   - only Graph-resource assignments outside the allowed set are DELETEd;
//     allowed roles and other resources' assignments are left alone;
//   - every page of appRoleAssignments is read before deciding;
//   - removal refuses (no DELETE) when the target is not the stamp managed
//     identity, or the allowed set is empty / has a null AppRoleId;
//   - DELETE 404 counts as removed; any other failure is reported by name;
//   - the extras check reports names, None, or Unknown (never None on a fault).
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

public sealed class GraphRestAppRoleReconcileTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string StampSpId = "66666666-7777-8888-9999-000000000000";
    private const string StampClientId = "11111111-2222-3333-4444-555555555555";
    private const string GraphSpId = "99999999-0000-0000-0000-000000000001";
    private const string OtherResourceSpId = "99999999-0000-0000-0000-000000000002";

    private const string FscSelected = "40dc41bc-0f7e-42ff-89bd-d9516947e474";
    private const string DirectoryReadWriteAll = "19dbc75e-c2e2-444c-a770-ec69d8559fc7";
    private const string UserInviteAll = "09850681-111b-4a89-9bed-3f2cae46d706";
    private const string SitesReadWriteAll = "9492366f-7969-46a4-8d15-ed1a20078fff";

    private static readonly IReadOnlyList<GraphAppRoleEntry> Allowed = new[]
    {
        new GraphAppRoleEntry("FileStorageContainer.Selected", FscSelected),
    };

    [Fact]
    public async Task Remove_DeletesOnlyGraphRolesOutsideTheSet_AcrossPages_AndNamesThem()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(
            page1: new[] { ("a-fsc", FscSelected, GraphSpId), ("a-dir", DirectoryReadWriteAll, GraphSpId), ("a-other", DirectoryReadWriteAll, OtherResourceSpId) },
            page2: new[] { ("a-inv", UserInviteAll, GraphSpId) });

        var outcome = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        outcome.Should().BeOfType<GraphAppRoleRemovalOutcome.Success>()
            .Which.RemovedRoleValues.Should().BeEquivalentTo("Directory.ReadWrite.All", "User.Invite.All");
        graph.Deletes.Should().BeEquivalentTo(
            $"/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments/a-dir",
            $"/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments/a-inv");
        graph.Deletes.Should().NotContain(d => d.EndsWith("a-fsc") || d.EndsWith("a-other"),
            "the allowed role and another resource's assignment are never touched");
    }

    [Fact]
    public async Task Remove_NothingExtra_DeletesNothing()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[] { ("a-fsc", FscSelected, GraphSpId) });

        var outcome = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        outcome.Should().BeOfType<GraphAppRoleRemovalOutcome.Success>().Which.RemovedRoleValues.Should().BeEmpty();
        graph.Deletes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000000", "ManagedIdentity")]   // another identity's appId
    [InlineData(StampClientId, "Application")]                               // right appId, not a managed identity
    public async Task Remove_TargetIsNotTheStampManagedIdentity_RefusesAndDeletesNothing(string appId, string type)
    {
        var graph = new ScriptedGraph { PrincipalAppId = appId, PrincipalType = type };
        graph.Assignments(page1: new[] { ("a-dir", DirectoryReadWriteAll, GraphSpId) });

        var outcome = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        outcome.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>().Which.Diagnostic.Should().Contain("Nothing was removed");
        graph.Deletes.Should().BeEmpty();
        graph.AssignmentReads.Should().Be(0, "the target is checked before anything else is read");
    }

    [Fact]
    public async Task Remove_EmptyOrNullAllowedSet_RefusesWithoutAnyHttpCall()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[] { ("a-fsc", FscSelected, GraphSpId) });

        var empty = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Array.Empty<GraphAppRoleEntry>(), CancellationToken.None);
        var nullId = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId,
            new[] { new GraphAppRoleEntry("FileStorageContainer.Selected", null) }, CancellationToken.None);

        empty.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>();
        nullId.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>();
        graph.Requests.Should().BeEmpty("an incomplete allowed set would make every role look extra");
    }

    [Fact]
    public async Task Remove_Delete404IsRemoved_Delete403IsReportedByName()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[] { ("a-dir", DirectoryReadWriteAll, GraphSpId), ("a-sites", SitesReadWriteAll, GraphSpId) });
        graph.DeleteStatus["a-dir"] = HttpStatusCode.NotFound;
        graph.DeleteStatus["a-sites"] = HttpStatusCode.Forbidden;

        var outcome = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        var failure = outcome.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>().Subject;
        failure.RemovedRoleValues.Should().Equal("Directory.ReadWrite.All");
        failure.FailedRoleValues.Should().Equal("Sites.ReadWrite.All");
    }

    [Fact]
    public async Task Remove_AssignmentReadFails_IsFailure_NotSuccess()
    {
        var graph = new ScriptedGraph { AssignmentsStatus = HttpStatusCode.ServiceUnavailable };

        var outcome = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        outcome.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>().Which.Diagnostic.Should().Contain("Nothing was removed");
        graph.Deletes.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_PostsOnlyTheMissingRole()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[] { ("a-dir", DirectoryReadWriteAll, GraphSpId) });

        var outcome = await Granter(graph).GrantRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        outcome.Should().BeOfType<GraphAppRoleGrantOutcome.Success>().Which.GrantedCount.Should().Be(1);
        graph.PostBodies.Should().ContainSingle().Which.Should().Contain(FscSelected).And.Contain(GraphSpId);
    }

    [Fact]
    public async Task FindUnexpected_NamesExtras_IgnoresOtherResources()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[]
        {
            ("a-fsc", FscSelected, GraphSpId), ("a-sites", SitesReadWriteAll, GraphSpId), ("a-other", UserInviteAll, OtherResourceSpId),
        });

        var result = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleExtrasResult.Found>().Which.RoleValues.Should().Equal("Sites.ReadWrite.All");
    }

    [Fact]
    public async Task A200WithoutAValueArray_IsNotAnEmptyList_ItFailsClosed()
    {
        // "Nothing extra" must never be concluded from an answer we cannot read.
        var graph = new ScriptedGraph { RawAssignmentsBody = "{\"@odata.context\":\"x\"}" };

        var extras = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);
        var removal = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);
        var parity = await Verifier(graph).VerifyAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        extras.Should().BeOfType<GraphAppRoleExtrasResult.Unknown>();
        removal.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>();
        parity.Should().BeOfType<GraphAppRoleParityResult.Partial>("an unreadable list is not a verified grant");
        graph.Deletes.Should().BeEmpty();
    }

    [Fact]
    public async Task PagingThatNeverEnds_StopsAtFiftyPages_AndFailsClosed()
    {
        var graph = new ScriptedGraph { EndlessPages = true };

        var extras = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);
        var removal = await Granter(graph).RemoveUnexpectedRolesAsync(StampSpId, StampClientId, TenantId, Allowed, CancellationToken.None);

        extras.Should().BeOfType<GraphAppRoleExtrasResult.Unknown>().Which.Diagnostic.Should().Contain("50 pages");
        removal.Should().BeOfType<GraphAppRoleRemovalOutcome.Failure>();
        graph.AssignmentReads.Should().Be(100, "50 pages per call, two calls — no more");
        graph.Deletes.Should().BeEmpty("a partial list never drives a removal");
    }

    [Fact]
    public async Task ANextLinkOffMicrosoftGraph_IsNeverFollowed_WithTheBearerToken()
    {
        var graph = new ScriptedGraph { NextLinkHost = "evil.example.com" };
        graph.Assignments(page1: new[] { ("a-fsc", FscSelected, GraphSpId) });

        var extras = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        extras.Should().BeOfType<GraphAppRoleExtrasResult.Unknown>().Which.Diagnostic.Should().Contain("not on https://graph.microsoft.com");
        graph.Hosts.Should().OnlyContain(h => h == "graph.microsoft.com");
    }

    [Fact]
    public async Task APrincipalIdWithPathCharacters_IsEscaped_NotInterpretedAsAPath()
    {
        var graph = new ScriptedGraph();

        await Verifier(graph).FindUnexpectedRolesAsync("x/../servicePrincipals/other", TenantId, Allowed, CancellationToken.None);

        graph.Requests.Should().Contain(r => r.Contains("/servicePrincipals/x%2F..%2FservicePrincipals%2Fother/appRoleAssignments"));
    }

    [Fact]
    public async Task FindUnexpected_OnlyAllowed_IsNone()
    {
        var graph = new ScriptedGraph();
        graph.Assignments(page1: new[] { ("a-fsc", FscSelected, GraphSpId) });

        var result = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleExtrasResult.None>();
    }

    [Fact]
    public async Task FindUnexpected_ReadFails_IsUnknown_NeverNone()
    {
        var graph = new ScriptedGraph { AssignmentsStatus = HttpStatusCode.InternalServerError };

        var result = await Verifier(graph).FindUnexpectedRolesAsync(StampSpId, TenantId, Allowed, CancellationToken.None);

        result.Should().BeOfType<GraphAppRoleExtrasResult.Unknown>();
    }

    // ---------- helpers ----------

    private static GraphRestAppRoleGranter Granter(ScriptedGraph graph) => new(
        new HttpClient(graph), new L2GraphAppRolesRegistry(),
        Options.Create(new H10DataverseAppUserGraphParityOptions()),
        NullLogger<GraphRestAppRoleGranter>.Instance, _ => new StaticToken());

    private static GraphRestAppRoleParityVerifier Verifier(ScriptedGraph graph) => new(
        new HttpClient(graph), new L2GraphAppRolesRegistry(),
        Options.Create(new H10DataverseAppUserGraphParityOptions()),
        NullLogger<GraphRestAppRoleParityVerifier>.Instance, _ => new StaticToken());

    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>A scripted Microsoft Graph: the Graph resource SP, one principal, its paged appRoleAssignments.</summary>
    private sealed class ScriptedGraph : HttpMessageHandler
    {
        private string _page1 = "{\"value\":[]}";
        private string? _page2;

        public string PrincipalAppId { get; init; } = StampClientId;
        public string PrincipalType { get; init; } = "ManagedIdentity";
        public HttpStatusCode AssignmentsStatus { get; init; } = HttpStatusCode.OK;
        public Dictionary<string, HttpStatusCode> DeleteStatus { get; } = new();
        public List<string> Requests { get; } = new();
        public List<string> Deletes { get; } = new();
        public List<string> PostBodies { get; } = new();
        public int AssignmentReads { get; private set; }
        public string? RawAssignmentsBody { get; init; }
        public bool EndlessPages { get; init; }
        public string? NextLinkHost { get; init; }
        public List<string> Hosts { get; } = new();

        public void Assignments((string Id, string RoleId, string ResourceId)[] page1, (string Id, string RoleId, string ResourceId)[]? page2 = null)
        {
            static string Rows((string Id, string RoleId, string ResourceId)[] rows) => string.Join(",", rows.Select(r =>
                $"{{\"id\":\"{r.Id}\",\"appRoleId\":\"{r.RoleId}\",\"resourceId\":\"{r.ResourceId}\"}}"));
            _page1 = page2 is null && NextLinkHost is null
                ? $"{{\"value\":[{Rows(page1)}]}}"
                : $"{{\"value\":[{Rows(page1)}],\"@odata.nextLink\":\"https://{NextLinkHost ?? "graph.microsoft.com"}/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments?$skiptoken=p2\"}}";
            _page2 = page2 is null ? null : $"{{\"value\":[{Rows(page2)}]}}";
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Requests.Add($"{request.Method} {request.RequestUri.AbsolutePath}{query}");
            Hosts.Add(request.RequestUri.Host);

            if (request.Method == HttpMethod.Get && path == "/v1.0/servicePrincipals" && query.Contains("00000003-0000-0000-c000-000000000000"))
            {
                return Json(HttpStatusCode.OK, "{\"value\":[{\"id\":\"" + GraphSpId + "\",\"appRoles\":[" +
                    $"{{\"id\":\"{FscSelected}\",\"value\":\"FileStorageContainer.Selected\"}}," +
                    $"{{\"id\":\"{DirectoryReadWriteAll}\",\"value\":\"Directory.ReadWrite.All\"}}," +
                    $"{{\"id\":\"{UserInviteAll}\",\"value\":\"User.Invite.All\"}}," +
                    $"{{\"id\":\"{SitesReadWriteAll}\",\"value\":\"Sites.ReadWrite.All\"}}]}}]}}");
            }
            if (request.Method == HttpMethod.Get && path == $"/v1.0/servicePrincipals/{StampSpId}")
            {
                return Json(HttpStatusCode.OK, $"{{\"appId\":\"{PrincipalAppId}\",\"servicePrincipalType\":\"{PrincipalType}\"}}");
            }
            if (request.Method == HttpMethod.Get && path == $"/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments")
            {
                AssignmentReads++;
                if (AssignmentsStatus != HttpStatusCode.OK) return Json(AssignmentsStatus, "{\"error\":{\"code\":\"x\"}}");
                if (RawAssignmentsBody is not null) return Json(HttpStatusCode.OK, RawAssignmentsBody);
                if (EndlessPages)
                {
                    return Json(HttpStatusCode.OK, "{\"value\":[],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/servicePrincipals/" +
                        StampSpId + "/appRoleAssignments?$skiptoken=again\"}");
                }
                return Json(HttpStatusCode.OK, query.Contains("skiptoken=p2") ? _page2! : _page1);
            }
            if (request.Method == HttpMethod.Post && path == $"/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments")
            {
                PostBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(HttpStatusCode.Created, "{}");
            }
            if (request.Method == HttpMethod.Delete && path.StartsWith($"/v1.0/servicePrincipals/{StampSpId}/appRoleAssignments/", StringComparison.Ordinal))
            {
                Deletes.Add(path);
                var id = path[(path.LastIndexOf('/') + 1)..];
                return new HttpResponseMessage(DeleteStatus.TryGetValue(id, out var status) ? status : HttpStatusCode.NoContent);
            }
            return Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"unscripted\"}}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
