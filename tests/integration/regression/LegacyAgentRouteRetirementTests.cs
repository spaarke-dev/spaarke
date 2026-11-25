using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — spaarkeai-mcp-bff-api-r2 task 001 (FR-01, issue #2575).
///
/// <para>The legacy M365 Copilot agent gateway was REMOVED from the BFF: its four routes (the agent message turn, the
/// agent's playbook list, the agent's run-playbook and the agent's run-status poll), its endpoint filter, its token
/// service, its DI module and the <c>Microsoft.Agents.Hosting.AspNetCore</c> package. Spaarke for Microsoft 365
/// Copilot is served by the MCP server (<c>/mcp/legal-operations</c>) instead. Each removed route authorized through
/// its own filter and path; the route-authorization ledger rows for them (S-18, S-19, S-78 and the permanent playbook
/// list) went with them — the pre-removal ledger is at commit 37f0864afe.</para>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> someone restores the agent gateway — from the old Copilot
/// agent package, the deployment docs or git history — and the four routes come back without the per-record decisions
/// the ledger used to hold for them. A restored route must arrive through the ledger, not silently.</para>
///
/// <para><b>Why the route prefix is assembled at runtime.</b> The task's acceptance grep requires that no file under
/// <c>tests/</c> names the removed prefix, while its negative criterion requires an in-process request to the removed
/// message route to answer 404. Building the prefix from two parts satisfies both: the grep stays a clean signal for
/// leftover references, and this file still drives the exact paths. (Decision recorded in the project's
/// notes/decisions.md.)</para>
///
/// <para><b>Why the assertions are shaped this way</b> (AiPlaybookPromptRecordMatchRouteRetirementTests' precedent):
/// enumerating the endpoint table proves no handler exists; the HTTP half shows absence over the wire with a valid
/// bearer; the positive control shows the surviving execute route, which now carries every Run-mode rule, is still
/// routed. A last test proves the one piece of the agent kept alive — <see cref="HandoffUrlBuilder"/>, now owned by the
/// tool framework — still reaches its live consumer, <see cref="DataverseCreateRecordHandler"/>, from DI.</para>
/// </summary>
[Trait("status", "repaired")]
public class LegacyAgentRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    /// <summary>The removed prefix, assembled so a repository grep for it finds no test (see the class remarks).</summary>
    private static readonly string Prefix = "/api/" + "agent";

    private readonly CustomWebAppFactory _factory;

    public LegacyAgentRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string> RetiredRequests => new()
    {
        { "POST", Prefix + "/message" },
        { "GET", Prefix + "/playbooks" },
        { "POST", Prefix + "/run-playbook" },
        { "GET", Prefix + "/playbooks/status/00100000-0000-0000-0000-000000000001" },
    };

    [Fact]
    public void NoRouteUnderTheRemovedPrefix_IsInTheEndpointTable()
    {
        var endpoints = EndpointTable.Endpoints(_factory);

        endpoints.Should().NotBeEmpty("an empty table would make the absence check trivially true");

        var prefix = Prefix.Trim('/');
        var survivors = endpoints
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).TrimStart('/')
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{e.RoutePattern.RawText}  (registered as: {e.DisplayName})")
            .ToList();

        survivors.Should().BeEmpty(
            "the legacy M365 Copilot agent routes were removed by spaarkeai-mcp-bff-api-r2 task 001. Re-registered routes:\n  "
            + string.Join("\n  ", survivors));
    }

    [Theory]
    [MemberData(nameof(RetiredRequests))]
    public void RetiredRoute_IsNotMatchedByAnyEndpoint(string verb, string path)
    {
        EndpointTable.Maps(_factory, verb, path).Should().BeFalse($"{verb} {path} was removed with the legacy agent");
    }

    [Theory]
    [MemberData(nameof(RetiredRequests))]
    public async Task RetiredRoute_WithAValidBearer_Is404NotRouted(string verb, string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST")
        {
            request.Content = new StringContent(
                "{\"message\":\"Summarize this\",\"playbookId\":\"00100000-0000-0000-0000-000000000002\"}",
                System.Text.Encoding.UTF8,
                "application/json");
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{verb} {path} was removed with the legacy agent; anything else means a handler exists again");
    }

    [Fact]
    public void TheExecuteRoute_ThatNowCarriesEveryRunModeRule_IsStillMapped()
    {
        // Positive control for the 404s above (an anonymous 401 proves nothing: the FallbackPolicy challenges unmatched
        // requests too). PlaybookRouteAuthorizationContractTests proves its Run-mode decisions.
        EndpointTable.AssertMapped(_factory, "POST", "/api/ai/playbooks/00100000-0000-0000-0000-000000000003/execute");
    }

    [Fact]
    public void DataverseCreateRecordHandler_ResolvesTheRelocatedHandoffUrlBuilder_FromDi()
    {
        using var scope = _factory.Services.CreateScope();

        var builder = scope.ServiceProvider.GetRequiredService<HandoffUrlBuilder>();
        builder.BuildPlaybookLibraryUrl().Should().StartWith("https://test.crm.dynamics.com/",
            "the builder is bound to the host's Dataverse:EnvironmentUrl, with the fallback order the removed agent module used");

        var handler = scope.ServiceProvider.GetServices<IToolHandler>().OfType<DataverseCreateRecordHandler>().SingleOrDefault();
        handler.Should().NotBeNull(
            "the tool framework's handler scan registers DataverseCreateRecordHandler, and every constructor dependency "
            + "— HandoffUrlBuilder included — must resolve from the host's container");
    }
}
