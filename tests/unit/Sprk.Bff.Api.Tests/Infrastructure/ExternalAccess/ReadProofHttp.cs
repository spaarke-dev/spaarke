using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// A hand-written fake Dataverse Web API for the identity store's readability proof (testing.md B1 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, not a fake; the same shape as <c>DataverseContactIdentityStoreTests.ScriptedHttp</c>).
/// It answers by route, so a store's own sequence of reads is exercised as the store makes it:
/// <list type="bullet">
///   <item><c>WhoAmI</c>: the BFF's user (<see cref="Me"/>);</item>
///   <item><c>roles?fetchXml=</c>: evaluates the query's role condition (by <c>name</c> or <c>roletemplateid</c>) against
///     the roles the user holds (<see cref="Roles"/>), so it is the QUERY that decides;</item>
///   <item><c>fieldpermissions?fetchXml=</c>: the asked-for columns the user's own (<see cref="DirectGrants"/>) or team
///     (<see cref="TeamGrants"/>) profiles grant Read on;</item>
///   <item><c>contacts(id)</c> and <c>contacts?</c>: <see cref="ContactBody"/> and <see cref="ContactsBody"/>.</item>
/// </list>
/// With <see cref="ProofFaults"/> every proof read answers 503. Every request URI is recorded in <see cref="Requests"/>.
/// </summary>
internal sealed class ReadProofHttp : IHttpClientFactory
{
    public static readonly Guid Me = Guid.Parse("cccccccc-0000-4000-8000-0000000000f1");

    public List<(string Name, Guid Template)> Roles { get; } = new();

    public HashSet<string> DirectGrants { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> TeamGrants { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ProofFaults { get; set; }

    public string ContactBody { get; set; } = "{}";

    public string ContactsBody { get; set; } = "{\"value\":[]}";

    public List<string> Requests { get; } = new();

    public int WhoAmICount => Requests.Count(r => r.EndsWith("/WhoAmI", StringComparison.Ordinal));

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private (HttpStatusCode Code, string Body) Answer(string uri)
    {
        var path = Uri.UnescapeDataString(uri[(uri.IndexOf("/api/data/v9.2/", StringComparison.Ordinal) + "/api/data/v9.2/".Length)..]);
        var proofRead = path == "WhoAmI" || path.StartsWith("roles?", StringComparison.Ordinal)
            || path.StartsWith("fieldpermissions?", StringComparison.Ordinal);
        if (proofRead && ProofFaults)
        {
            return (HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"throttled\"}}");
        }

        if (path == "WhoAmI")
        {
            return (HttpStatusCode.OK, JsonSerializer.Serialize(new { UserId = Me }));
        }

        if (path.StartsWith("roles?", StringComparison.Ordinal))
        {
            var condition = Regex.Match(path, "<entity name=\"role\">.*?<filter><condition attribute=\"(\\w+)\" operator=\"eq\" value=\"([^\"]+)\" />");
            var attribute = condition.Groups[1].Value;
            var value = condition.Groups[2].Value;
            var held = Roles.Where(r => attribute switch
            {
                "name" => r.Name == value,
                "roletemplateid" => Guid.TryParse(value, out var t) && t == r.Template,
                _ => false,
            });
            return (HttpStatusCode.OK, Values(held.Select(_ => (object)new { roleid = Guid.NewGuid() })));
        }

        if (path.StartsWith("fieldpermissions?", StringComparison.Ordinal))
        {
            var grants = path.Contains("teamprofiles", StringComparison.Ordinal) ? TeamGrants : DirectGrants;
            var asked = Regex.Matches(path, "<value>([^<]+)</value>").Select(m => m.Groups[1].Value);
            return (HttpStatusCode.OK, Values(asked.Where(grants.Contains)
                .Select(c => (object)new { attributelogicalname = c })));
        }

        if (path.StartsWith("contacts(", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, ContactBody);
        }

        if (path.StartsWith("contacts?", StringComparison.Ordinal))
        {
            return (HttpStatusCode.OK, ContactsBody);
        }

        return (HttpStatusCode.NotFound, "{}");
    }

    private static string Values(IEnumerable<object> rows) => JsonSerializer.Serialize(new { value = rows.ToArray() });

    private sealed class Handler(ReadProofHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!.ToString();
            lock (owner.Requests)
            {
                owner.Requests.Add(uri);
            }

            var (code, body) = owner.Answer(uri);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }
}
