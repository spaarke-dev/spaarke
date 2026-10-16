// spaarke-ontology-platform-r1 task 126 (#1399; uac-r2's rule, #1355 comment 6089838200 §6, D-113) — LIVE proof that a G5
// create binding a BUSINESS-OWNED lookup target (a systemuser) is allowed exactly when the caller's AppendTo privilege
// depth covers the target row's business unit, through the REAL route POST /api/v1/child-records/sprk_analysis
// (its live sprk_ReviewerBy systemuser lookup), and that Dataverse's own run-as-user create agrees.
//
// Opt-in only (skip-via-return, the repo's convention): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_126_DATAVERSE_URL=https://<env>.crm.dynamics.com      (spaarkedev1)
//   SPAARKE_LIVE_126_ACTING_USER_ID=<systemuserid of a low-privilege user in a CHILD business unit holding prvAppendToUser
//                                    at Local — spaarkedev1: "# UAC Child BU Test User", BU1, Spaarke Basic User + Core User>
//   SPAARKE_LIVE_126_SAME_BU_USER_ID=<systemuserid of another user in the acting user's business unit>
//   SPAARKE_LIVE_126_OTHER_BU_USER_ID=<systemuserid of a user in a business unit that is NOT the acting user's>
// and an `az login` session for an identity that may create sprk_analysis and impersonate (System Administrator).
//
// Substitutions are WorkAssignmentCreateLiveTests' (its factory is reused): the inbound token carries the acting user's
// real oid; the caller's Dataverse is the operator's token + MSCRMCallerID (WhoAmI answered with the acting user and their
// business unit, as their own token answers it); the BFF's outbound identity is AzureCliCredential.
//
// Every record is named "zz-126-test …" and deleted in `finally`; ids are registered BEFORE anything is asserted, every
// delete is checked, and the zz-126 count is confirmed zero by query.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using FluentAssertions;
using Sprk.Bff.Api.IntegrationTests.WorkAssignments;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.ChildRecords;

public sealed class BusinessOwnedAppendToLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_126_DATAVERSE_URL";
    private const string Route = "/api/v1/child-records/sprk_analysis";
    private readonly ITestOutputHelper _out;

    public BusinessOwnedAppendToLiveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task LiveMode_ASystemuserLookup_IsBoundExactlyWhenTheCallersAppendToDepthCoversTheUsersBusinessUnit()
    {
        var dataverseUrl = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(dataverseUrl))
            return; // not opted in

        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_126_ACTING_USER_ID"), out var acting)
            || !Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_126_SAME_BU_USER_ID"), out var sameBu)
            || !Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_126_OTHER_BU_USER_ID"), out var otherBu))
        {
            _out.WriteLine("SPAARKE_LIVE_126_ACTING_USER_ID / _SAME_BU_USER_ID / _OTHER_BU_USER_ID are not all set; skipped");
            return;
        }

        var credential = new AzureCliCredential();
        using var dv = await WorkAssignmentCreateLiveTests.DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        var actingBu = await BusinessUnitOfAsync(dv, acting);
        (await BusinessUnitOfAsync(dv, sameBu)).Should().Be(actingBu, "the same-BU user must share the acting user's business unit");
        (await BusinessUnitOfAsync(dv, otherBu)).Should().NotBe(actingBu, "the other-BU user must sit in another business unit");
        _out.WriteLine($"operator = {operatorId}; acting = {acting} (BU {actingBu}); same BU = {sameBu}; other BU = {otherBu}");
        _out.WriteLine($"acting user's prvAppendToUser: {await PrivilegeAsync(dv, acting, "prvAppendToUser")}");

        await using var factory = new WorkAssignmentCreateLiveTests.LiveWorkAssignmentFactory(dataverseUrl, credential, _out);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var created = new List<Guid>();
        var leaks = new List<string>();
        _out.WriteLine($"zz-126 analyses before = {await CountZzAsync(dv)}");
        try
        {
            await factory.ActAsAsync(acting);

            // 1. Dataverse's own run-as-user create (the behaviour the rule mirrors), both ways.
            var directSame = await CreateAsUserAsync(dv, Payload("zz-126-test direct same BU", sameBu), acting, created);
            var directOther = await CreateAsUserAsync(dv, Payload("zz-126-test direct other BU", otherBu), acting, created);
            _out.WriteLine($"[direct] as the user, reviewer in the same BU -> {directSame}; in another BU -> {directOther}");

            // 2. The route, as the same user: allowed in the same business unit.
            var allowed = await bff.PostAsJsonAsync(Route, Payload("zz-126-test route same BU", sameBu));
            var allowedId = await RegisterCreatedAsync(allowed, created);
            _out.WriteLine($"[route] same BU -> HTTP {(int)allowed.StatusCode} {await allowed.Content.ReadAsStringAsync()}");

            // 3. The route, as the same user: refused across business units, the uniform 404, nothing created.
            var count = await CountZzAsync(dv);
            var refused = await bff.PostAsJsonAsync(Route, Payload("zz-126-test route other BU", otherBu));
            await RegisterCreatedAsync(refused, created); // registered in case it was (wrongly) created
            var refusedBody = await refused.Content.ReadAsStringAsync();
            _out.WriteLine($"[route] other BU -> HTTP {(int)refused.StatusCode} {refusedBody}");

            // 4. The operator (System Administrator: prvAppendToUser at Global) binds the other-BU user through the route.
            await factory.ActAsAsync(operatorId);
            var global = await bff.PostAsJsonAsync(Route, Payload("zz-126-test route operator", otherBu));
            await RegisterCreatedAsync(global, created);
            _out.WriteLine($"[route] operator, other BU -> HTTP {(int)global.StatusCode} {await global.Content.ReadAsStringAsync()}");

            directSame.Should().Be(HttpStatusCode.Created, "Dataverse lets a Local holder bind a user in their own business unit");
            directOther.Should().NotBe(HttpStatusCode.Created,
                "Dataverse refuses a Local holder a user in another business unit — the rule must match it (escalate if not)");
            allowed.StatusCode.Should().Be(HttpStatusCode.Created);
            Value(await dv.GetFromJsonAsync<JsonElement>($"sprk_analysises({allowedId})?$select=_sprk_reviewerby_value"),
                "_sprk_reviewerby_value").Should().Be(sameBu.ToString("D"));
            refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "no AppendTo reaching the user's business unit is the uniform 404");
            refusedBody.Should().Contain("child_record.not_found");
            (await CountZzAsync(dv)).Should().Be(count, "a refused create writes nothing");
            global.StatusCode.Should().Be(HttpStatusCode.Created, "Global covers any business unit");
        }
        finally
        {
            foreach (var id in created)
                leaks.AddRange(await DeleteAsync(dv, $"sprk_analysises({id})"));
            var remaining = await CountZzAsync(dv);
            _out.WriteLine($"cleanup: deleted {created.Count - leaks.Count} of {created.Count} ({string.Join(", ", created)}); " +
                           $"zz-126 rows remaining = {remaining}");
            leaks.Should().BeEmpty("every zz-126 row is removed");
            remaining.Should().Be(0, "no zz-126 row is left behind");
        }
    }

    private static Dictionary<string, object?> Payload(string name, Guid reviewer) => new()
    {
        ["sprk_name"] = name,
        ["sprk_ReviewerBy@odata.bind"] = $"/systemusers({reviewer:D})",
    };

    /// <summary>A run-as-user create (MSCRMCallerID); returns its status, registering any created id for cleanup.</summary>
    private static async Task<HttpStatusCode> CreateAsUserAsync(HttpClient dv, Dictionary<string, object?> payload, Guid user, List<Guid> created)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "sprk_analysises")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("MSCRMCallerID", user.ToString("D"));
        using var response = await dv.SendAsync(request);
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
            created.Add(Guid.Parse(values.First().Split('(', ')')[1]));
        return response.StatusCode == HttpStatusCode.NoContent ? HttpStatusCode.Created : response.StatusCode;
    }

    private static async Task<Guid> RegisterCreatedAsync(HttpResponseMessage response, List<Guid> created)
    {
        if (response.StatusCode != HttpStatusCode.Created)
            return Guid.Empty;
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Add(id);
        return id;
    }

    private static async Task<Guid> BusinessUnitOfAsync(HttpClient dv, Guid user) =>
        (await dv.GetFromJsonAsync<JsonElement>($"systemusers({user})?$select=_businessunitid_value"))
        .GetProperty("_businessunitid_value").GetGuid();

    private static async Task<string> PrivilegeAsync(HttpClient dv, Guid user, string privilege) =>
        await dv.GetStringAsync(
            $"systemusers({user})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { privilege }))}");

    private static string? Value(JsonElement row, string column) =>
        row.TryGetProperty(column, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static async Task<int> CountZzAsync(HttpClient dv) =>
        (await dv.GetFromJsonAsync<JsonElement>("sprk_analysises?$select=sprk_analysisid&$filter=startswith(sprk_name,'zz-126')"))
        .GetProperty("value").GetArrayLength();

    private static async Task<List<string>> DeleteAsync(HttpClient dv, string path)
    {
        try
        {
            var r = await dv.DeleteAsync(path);
            return r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound
                ? new List<string>()
                : new List<string> { $"{path}: HTTP {(int)r.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new List<string> { $"{path}: {ex.Message}" };
        }
    }
}
