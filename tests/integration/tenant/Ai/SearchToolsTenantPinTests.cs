// TENANT PIN — the two MCP search tools search the CALLER's tenant and no other (spaarkeai-mcp-bff-api-r2 task 022 verifier fix F4).
// tests/integration/tenant/** is the tenant-isolation KEEP category (ADR-038 section 2, path 4).
//
// WHAT IS PINNED. The document tool gives the index the tenant id the shared method (SemanticSearchAuthorizationFilter) reads from the token's
// `tid` claim: the tool never reads a tenant itself, takes none from its arguments and has none to substitute. The record index takes no tenant
// argument; the production RecordSearchService derives its `tenantId eq '...'` filter from the `tid` claim of the request it runs in
// (IHttpContextAccessor), so the record stub records that claim. A tool that searched another tenant (a hard-coded id, a configured default,
// an argument) would fail both tests.
//
// WHAT RUNS. The production MCP route and tools over module-boundary doubles (SearchToolsHost), the real filters and AuthorizationService.
// NOT HERE: that the real index applies its tenant filter (tests/integration/regression/RecordSearch/RecordSearchQueryConstructionTests.cs
// and the index tests own that).

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Sprk.Bff.Api.Tests.Api.Mcp.Search;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Mcp.Search.SearchToolsTestSupport;

namespace Sprk.Bff.Api.Tests.Tenant.Ai;

public sealed class SearchToolsTenantPinTests
{
    [Fact]
    public async Task SearchDocuments_PassesTheTokensTenantToTheIndex_OnBothScopes()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent));

        var everywhere = await host.CallToolAsync("spaarke_search_documents", new { query = "indemnity" });
        var insideOne = await host.CallToolAsync("spaarke_search_documents", new { query = "indemnity", entityType = "matter", entityId = parent });

        IsError(everywhere).Should().BeFalse(everywhere.GetRawText());
        IsError(insideOne).Should().BeFalse(insideOne.GetRawText());
        host.DocumentIndex.TenantIds.Should().HaveCount(2).And.OnlyContain(t => t == McpTestHost.TenantId, "the index is asked about the token's tenant and no other");
    }

    [Fact]
    public async Task SearchDocuments_SearchesWhicheverTenantTheCallersTokenCarries_NotAFixedOne()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent));
        const string otherTenant = "99999999-aaaa-4bbb-8ccc-000000000099";
        var http = host.DirectContext(withTenant: false);
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", SearchToolsHost.CallerOid.ToString("D")), new Claim("tid", otherTenant)], "Test"));

        var result = JsonOf(await SearchDocumentsTool.RunAsync(
            Args(new { query = "indemnity" }), http, SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));

        IsError(result).Should().BeFalse(result.GetRawText());
        host.DocumentIndex.TenantIds.Should().ContainSingle().Which.Should().Be(
            otherTenant, "the tenant is the caller's tid claim, whatever it is: nothing in the tool or its configuration names one");
    }

    [Fact]
    public async Task SearchDocuments_ACallerWithNoTenantClaim_SearchesNothing()
    {
        await using var host = await SearchToolsHost.StartAsync();
        host.DocumentIndex.Returns(Doc(Guid.NewGuid()));

        var result = JsonOf(await SearchDocumentsTool.RunAsync(
            Args(new { query = "indemnity" }), host.DirectContext(withTenant: false), SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));

        IsError(result).Should().BeTrue();
        Code(result).Should().Be("MISSING_TENANT_IDENTITY");
        host.DocumentIndex.TenantIds.Should().BeEmpty("no tenant, no search: there is no default tenant to fall back to");
    }

    [Fact]
    public async Task FindRecords_RunsTheIndexInARequestCarryingTheTokensTenant()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var id = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, id, AccessRights.Read);
        host.RecordIndex.Returns(Record(id));

        var result = await host.CallToolAsync("spaarke_find_records", new { query = "acme" });

        IsError(result).Should().BeFalse(result.GetRawText());
        host.RecordIndex.TenantIds.Should().ContainSingle().Which.Should().Be(McpTestHost.TenantId,
            "the record index derives its tenant filter from the tid of the request it runs in: the tool's own request, the caller's token");
    }
}
