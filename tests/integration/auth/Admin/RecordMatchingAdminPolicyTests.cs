using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Admin;

/// <summary>
/// unified-access-control-r2 task 165 — <c>/api/admin/record-matching/{sync,sync-incremental,status}</c>
/// require the "SystemAdmin" policy (sweep findings #47, #48, #77), and their 500s carry a redacted cause.
/// </summary>
/// <remarks>
/// <para>
/// Before task 165 the group carried a bare <c>RequireAuthorization()</c> — in this BFF, "any signed-in
/// caller" — so any user could start a full app-only re-read of every matter, project and invoice plus a
/// MergeOrUpload into the shared records index, and read the index's name and counts. The routes take no
/// record id, so the admin policy is the whole decision.
/// </para>
/// <para>Real host, routes mapped (<c>DocumentIntelligence:RecordMatchingEnabled=true</c>), the sync
/// service a recording fake: "the service was never called" is a count of zero. ADR-038 §2 path #1.</para>
/// </remarks>
public sealed class RecordMatchingAdminPolicyTests : IClassFixture<AdminSurfaceHostFixture>
{
    private readonly AdminSurfaceHostFixture _fixture;

    public RecordMatchingAdminPolicyTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        // method, url, the route's existing 500 title
        { "POST", "/api/admin/record-matching/sync", "Bulk sync failed" },
        { "POST", "/api/admin/record-matching/sync-incremental", "Incremental sync failed" },
        { "GET", "/api/admin/record-matching/status", "Failed to get sync status" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ACallerWithAUserRoleOnly_IsForbidden_AndTheSyncServiceIsNeverCalled(
        string method, string url, string _)
    {
        using var client = _fixture.CreateCaller(new[] { "User" });

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.IndexSync.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ADelegatedCallerWithNoAppRole_IsForbidden_AndTheSyncServiceIsNeverCalled(
        string method, string url, string _)
    {
        using var client = _fixture.CreateCaller(scopes: "user_impersonation");

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.IndexSync.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AnAdministrator_ReachesTheHandler(string method, string url, string _)
    {
        foreach (var role in new[] { "SystemAdmin", "Admin" })
        {
            using var client = _fixture.CreateCaller(new[] { role });

            var response = await client.SendAsync(Request(method, url));

            response.StatusCode.Should().Be(HttpStatusCode.OK, "role {0} must satisfy the SystemAdmin policy", role);
        }

        _fixture.IndexSync.Calls.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AnAnonymousCaller_IsUnauthorized(string method, string url, string _)
    {
        using var client = _fixture.CreateAnonymous();

        var response = await client.SendAsync(Request(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AFailure_Answers500WithTheRedactedCause_NeverTheRawMessage(string method, string url, string title)
    {
        const string bearer = "Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl";
        const string secret = "s3cr3t-Value-123";
        var thrown = new InvalidOperationException($"upstream said {bearer} and client_secret={secret}");
        _fixture.IndexSync.ThrowOnCall = thrown;
        using var client = _fixture.CreateCaller(new[] { "SystemAdmin" });

        var response = await client.SendAsync(Request(method, url));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var detail = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!["detail"].GetString();
        detail.Should().Be(ProblemDetailsHelper.Explain(title, thrown));
        detail.Should().NotContain(secret).And.NotContain("eyJzdWIiOiJ4In0");
    }

    [Fact]
    public void TheGroupIsBuiltWithTheSystemAdminPolicy_AndNoBareRequireAuthorization()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Api", "Admin", "RecordMatchingAdminEndpoints.cs"));
        var code = Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        code.Should().Contain(".RequireAuthorization(\"SystemAdmin\")");
        code.Should().NotContain(".RequireAuthorization()");
    }

    private static HttpRequestMessage Request(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = url.EndsWith("sync-incremental", StringComparison.Ordinal)
                ? JsonContent.Create(new { since = "2026-01-01T00:00:00Z" })
                : JsonContent.Create(new { });
        }

        return request;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        return dir!.FullName;
    }
}

/// <summary>
/// unified-access-control-r2 task 165 (owner amendment) — the "SystemAdmin" policy admits ONLY the Admin
/// or SystemAdmin app role. It used to admit any token whose delegated scope claim CONTAINED "admin", so
/// a scope such as <c>Files.ReadAdmin</c> unlocked every operator group sharing the policy.
/// </summary>
/// <remarks>
/// One route per group that shares the policy, each driven through the real pipeline: a real administrator
/// is admitted (anything but 401/403 — the handlers' own outcomes in a fake host vary), a non-admin is
/// refused, and a delegated token carrying an "admin"-containing scope with no role is refused.
/// </remarks>
public sealed class SystemAdminPolicyGroupTests : IClassFixture<AdminSurfaceHostFixture>
{
    private readonly AdminSurfaceHostFixture _fixture;

    public SystemAdminPolicyGroupTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>Every group carrying <c>RequireAuthorization("SystemAdmin")</c>, one route each.</summary>
    public static TheoryData<string> OneRoutePerSystemAdminGroup() => new()
    {
        "/api/admin/jobs",
        "/api/admin/membership/discovered/sprk_matter",
        "/api/ai/rag/admin/bulk-index/3f2504e0-4f89-11d3-9a0c-0305e82c3301/status",
        "/api/admin/record-matching/status",
    };

    /// <summary>
    /// One route per group, each with an answer ONLY its handler can produce — so "admitted" is proven by
    /// the handler having run, not inferred from "not 401/403" (a routing 404 or an unrelated 500 would
    /// pass that). The membership route is called with a blank entity type, which its handler answers
    /// with its own 400 before touching any service.
    /// </summary>
    public static TheoryData<string, HttpStatusCode, string> OneRoutePerSystemAdminGroupWithItsHandlersAnswer() => new()
    {
        { "/api/admin/jobs", HttpStatusCode.OK, "\"jobId\"" },
        { "/api/admin/membership/discovered/%20", HttpStatusCode.BadRequest, "entityType route parameter is required" },
        {
            "/api/ai/rag/admin/bulk-index/3f2504e0-4f89-11d3-9a0c-0305e82c3301/status",
            HttpStatusCode.NotFound,
            "Bulk indexing job '3f2504e0-4f89-11d3-9a0c-0305e82c3301' not found"
        },
        { "/api/admin/record-matching/status", HttpStatusCode.OK, "spaarke-records-test" },
    };

    [Theory]
    [MemberData(nameof(OneRoutePerSystemAdminGroupWithItsHandlersAnswer))]
    public async Task ARealAdministrator_IsAdmitted_AndReachesTheHandler(
        string url, HttpStatusCode handlersStatus, string handlersBodyFragment)
    {
        foreach (var role in new[] { "Admin", "SystemAdmin" })
        {
            using var client = _fixture.CreateCaller(new[] { role });

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(handlersStatus, "role {0} is the admin signal; body: {1}", role, body);
            body.Should().Contain(handlersBodyFragment, "only the route's handler produces this answer");
        }

        if (url.StartsWith("/api/admin/record-matching/", StringComparison.Ordinal))
        {
            _fixture.IndexSync.Calls.Should().Contain("GetStatusAsync");
        }
    }

    [Theory]
    [MemberData(nameof(OneRoutePerSystemAdminGroup))]
    public async Task ANonAdministrator_IsRefused(string url)
    {
        using var client = _fixture.CreateCaller(new[] { "User" });

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(OneRoutePerSystemAdminGroup))]
    public async Task ADelegatedScopeThatMerelyContainsAdmin_IsRefused(string url)
    {
        using var client = _fixture.CreateCaller(scopes: "user_impersonation Files.ReadAdmin");

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a scope NAME containing \"admin\" is not the admin signal; only the app role is");
    }
}
