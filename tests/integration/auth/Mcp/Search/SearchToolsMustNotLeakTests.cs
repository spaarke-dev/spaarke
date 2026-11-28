// AUTH — what spaarke_search_documents and spaarke_find_records must never show, and what they do with no caller (spaarkeai-mcp-bff-api-r2
// task 022, FR-02, FR-15, NFR-07, ADR-028 A10). KEEP path: auth.
//
// WHAT RUNS: the production MCP module serving the two production tools over the real shared access methods (task 027), the real
// AuthorizationService and the real external narrowing, substituted only at the module boundaries (the access-data source, the two indexes
// and the retrieval trim): see tests/integration/contract/Api/Mcp/Search/SearchToolsHost.cs. The parity and shape cases are in
// tests/integration/contract/Api/Mcp/Search/SearchToolsContractTests.cs.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.Api.Mcp.Search;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Mcp.Search.SearchToolsTestSupport;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Mcp.Search;

[Trait("category", "authorization")]
public sealed class SearchToolsMustNotLeakTests
{
    private const string DocumentsTool = "spaarke_search_documents";
    private const string RecordsTool = "spaarke_find_records";

    // ---- a document the access decision removes never appears -------------------------------------------------------

    [Fact]
    public async Task SearchDocuments_ADocumentTheTrimRemoves_NeverAppearsInContentOrStructuredContent()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        var visible = Doc(parent, "Visible agreement");
        var trimmed = Doc(parent, "SECRET-TRIMMED-NAME", "SECRET-TRIMMED-PARENT") with { Highlights = ["SECRET-TRIMMED-EXCERPT"] };
        host.Trim.Unreadable.Add(trimmed.DocumentId!);
        host.DocumentIndex.Returns(trimmed, visible);

        var result = await host.CallToolAsync(DocumentsTool, new { query = "agreement", entityType = "matter", entityId = parent });

        IsError(result).Should().BeFalse(result.GetRawText());
        ItemIds(result).Should().Equal(visible.DocumentId!);
        var everything = result.GetRawText();
        everything.Should().NotContain(trimmed.DocumentId!).And.NotContain("SECRET-TRIMMED", "neither the id, the name, the parent nor the excerpt of a trimmed document");
        host.Trim.Calls.Should().BeGreaterThan(0, "the route's trim ran for this call");
    }

    [Fact]
    public async Task SearchDocuments_ScopeAll_ARowUnderAParentTheCallerCannotRead_NeverAppears()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, readable, AccessRights.Read);
        var hidden = Doc(unreadable, "SECRET-HIDDEN-NAME", "SECRET-HIDDEN-MATTER");
        host.DocumentIndex.Returns(hidden, Doc(readable, "Visible agreement"));

        var result = await host.CallToolAsync(DocumentsTool, new { query = "agreement" });

        ItemIds(result).Should().HaveCount(1);
        result.GetRawText().Should().NotContain(hidden.DocumentId!).And.NotContain("SECRET-HIDDEN").And.NotContain(unreadable.ToString("D"));
    }

    [Fact]
    public async Task SearchDocuments_AParentTheCallerCannotRead_AndOneThatDoesNotExist_GiveTheSameUniformRefusal_AndNothingIsSearched()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var unreadable = Guid.NewGuid();
        var missing = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, unreadable, AccessRights.None);
        host.DocumentIndex.Returns(Doc(unreadable, "SECRET-HIDDEN-NAME"));

        var forbidden = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = unreadable });
        var absent = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = missing });

        IsError(forbidden).Should().BeTrue();
        Code(forbidden).Should().Be(SearchErrorCodes.EntityAccessDenied);
        Status(forbidden).Should().Be(403);
        Normalised(forbidden).Should().Be(Normalised(absent), "FR-15: nothing in the result tells an unreadable record from one that does not exist");
        forbidden.GetRawText().Should().NotContain(unreadable.ToString("D")).And.NotContain("SECRET-HIDDEN");
        host.DocumentIndex.Requests.Should().BeEmpty("a refused scope never reaches the index");
    }

    // ---- a record the access decision removes never appears ---------------------------------------------------------

    [Fact]
    public async Task FindRecords_ARecordTheCallerCannotRead_NeverAppears_NorAnythingTheIndexExtractedFromIt()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, readable, AccessRights.Read);
        var hidden = Record(unreadable, "SECRET-HIDDEN-MATTER") with { RecordDescription = "SECRET-HIDDEN-DESCRIPTION", ReferenceNumbers = ["SECRET-HIDDEN-REF"] };
        host.RecordIndex.Returns(hidden, Record(readable, "Visible matter"));

        var result = await host.CallToolAsync(RecordsTool, new { query = "matter" });

        ItemIds(result).Should().Equal(readable.ToString("D"));
        result.GetRawText().Should().NotContain(unreadable.ToString("D")).And.NotContain("SECRET-HIDDEN").And.NotContain("SECRET-ORG");
    }

    // ---- no caller: the tools search nothing -----------------------------------------------------------------------

    public enum NoCaller
    {
        /// <summary>No request context at all.</summary>
        NullContext,

        /// <summary>A principal whose identity carries claims but no authentication type.</summary>
        Unauthenticated,
    }

    [Theory]
    [InlineData(NoCaller.NullContext)]
    [InlineData(NoCaller.Unauthenticated)]
    public async Task BothTools_WithNoSignedInCaller_AreUserContextRequired_AndCallNoServiceAndAskNoRightsQuestion(NoCaller @case)
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent));
        host.RecordIndex.Returns(Record(parent));
        var http = @case == NoCaller.NullContext ? null : host.DirectContext(authenticated: false);

        var documents = JsonOf(await SearchDocumentsTool.RunAsync(Args(new { query = "q" }), http, SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));
        var records = JsonOf(await FindRecordsTool.RunAsync(Args(new { query = "q" }), http, SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));

        foreach (var result in new[] { documents, records })
        {
            IsError(result).Should().BeTrue(result.GetRawText());
            Code(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
            Status(result).Should().Be(401);
        }

        host.Access.Calls.Should().BeEmpty("no rights question runs without a signed-in caller");
        host.DocumentIndex.Requests.Should().BeEmpty();
        host.RecordIndex.Requests.Should().BeEmpty();
        host.Trim.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, true, "MISSING_CALLER_TOKEN")]
    [InlineData(true, false, "MISSING_TENANT_IDENTITY")]
    public async Task BothTools_ASignedInCallerWithoutABearerTokenOrATenant_IsRefusedWithTheRoutesCode_AndNothingIsSearched(
        bool withToken, bool withTenant, string code)
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent));
        host.RecordIndex.Returns(Record(parent));

        var http = () => host.DirectContext(withToken: withToken, withTenant: withTenant);
        var documents = JsonOf(await SearchDocumentsTool.RunAsync(Args(new { query = "q" }), http(), SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));
        var records = JsonOf(await FindRecordsTool.RunAsync(Args(new { query = "q" }), http(), SearchToolsHost.EnvironmentUrl, NullLogger.Instance, default));

        foreach (var result in new[] { documents, records })
        {
            IsError(result).Should().BeTrue(result.GetRawText());
            Code(result).Should().Be(code, "the route's own code for the same condition");
            Status(result).Should().Be(401);
        }

        host.Access.Calls.Should().BeEmpty();
        host.DocumentIndex.Requests.Should().BeEmpty();
        host.RecordIndex.Requests.Should().BeEmpty();
    }
}
