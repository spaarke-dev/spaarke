// CONTRACT — spaarke_list_documents and spaarke_get_document over /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 023, FR-03, NFR-02,
// NFR-07). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints on a real Kestrel listener behind a real JwtBearer scheme, serving the two
// production tool classes, behind the real tool code, the real authorization seams (the document decision, the matter Read decision, the
// external narrowing helper) and the real link steps of FileAccessEndpoints, over the module-boundary doubles of DocumentToolsWorld
// (the Dataverse document reads, the per-document Read decision, the caller's rights probe, the app-only SPE facade).
// "Equals the REST routes" is asserted against the same constants FileAccessLinkRoutesFrozenBytesTests freezes for the REST routes.
// NOT HERE: forbidden vs missing, hidden and no-caller refusals (tests/integration/auth/Mcp/Documents), the token shapes (task 021).

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Errors;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Documents;

public sealed class DocumentToolsContractTests
{
    private const string ListTool = "spaarke_list_documents";
    private const string GetTool = "spaarke_get_document";

    // ---- tools/list ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShowsBothToolsReadOnly_ModelVisible_WithATitleAnAdditionalPropertiesFalseSchemaAndAUseForNotForDescription()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var list = await host.RpcAsync("tools/list", new { });

        var tools = list.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo([ListTool, GetTool]);
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();
            var annotations = tool.GetProperty("annotations");
            annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue($"{name} reads only");
            annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
            annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
            tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
            var description = tool.GetProperty("description").GetString()!;
            description.Length.Should().BeLessThanOrEqualTo(1024, $"{name}'s description");
            description.Should().Contain("Use for:").And.Contain("Not for:");
            tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse($"{name}'s arguments are a closed set");

            // Visible to the model (FR-03): no app-only visibility is declared.
            if (tool.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("ui", out var ui) && ui.TryGetProperty("visibility", out var visibility))
            {
                visibility.EnumerateArray().Select(v => v.GetString()).Should().Contain("model", $"{name} is for the model and the widget");
            }
        }

        PropertyNames(tools.Single(t => t.GetProperty("name").GetString() == ListTool)).Should().BeEquivalentTo("regardingEntityType", "regardingId");
        PropertyNames(tools.Single(t => t.GetProperty("name").GetString() == GetTool)).Should().Equal("documentId");
    }

    // ---- spaarke_list_documents ------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ByRegardingMatter_ReturnsTheSourceIdsNewestFirst_InTheShapeTheEmailHostReads()
    {
        var world = new DocumentToolsWorld();
        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_matter", regardingId = DocumentToolsWorld.MatterId });

        IsError(result).Should().BeFalse(result.GetRawText());
        var body = result.GetProperty("structuredContent");
        var documents = body.GetProperty("documents").EnumerateArray().ToList();
        documents.Select(d => d.GetProperty("id").GetString()).Should().Equal(
            [DocumentToolsWorld.ReadableDocumentId, DocumentToolsWorld.SecondReadableDocumentId],
            "the ids the source returns for the matter, newest first");
        var first = documents[0];
        first.GetProperty("name").GetString().Should().Be("Master services agreement.docx");
        first.GetProperty("fileType").GetString().Should().Be("docx");
        first.GetProperty("sizeBytes").GetInt64().Should().Be(120_345);
        first.GetProperty("hasFile").GetBoolean().Should().BeTrue();
        body.GetProperty("count").GetInt32().Should().Be(2);
        body.GetProperty("hasMore").GetBoolean().Should().BeFalse("two rows are not the service's row limit");
        var regarding = body.GetProperty("regarding");
        regarding.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        regarding.GetProperty("id").GetString().Should().Be(DocumentToolsWorld.MatterId.ToString("D"));
        regarding.GetProperty("name").GetString().Should().Be("Acme v. Roadrunner");

        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        text.Should().Contain("Master services agreement.docx").And.Contain(DocumentToolsWorld.ReadableDocumentId);
        world.Probe.Asked.Should().ContainSingle().Which.Should().Be(DocumentToolsWorld.MatterId, "Read on the matter is asked once, as the caller");
    }

    [Fact]
    public async Task List_NeverCarriesAStoragePointerASummaryOrADescription()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var result = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_matter", regardingId = DocumentToolsWorld.MatterId });

        var raw = result.GetRawText();
        raw.Should().NotContain("b!drive-").And.NotContain("item-").And.NotContain("SECRET-", "ADR-015: no pointer, summary or description reaches the model");
    }

    [Fact]
    public async Task List_AtTheServiceLimit_With850CharacterNonAsciiNames_StaysUnder64KiB_AndSaysThereMayBeMore()
    {
        var world = new DocumentToolsWorld();
        var crowded = Guid.Parse("23000000-0000-4000-8000-0000000000c1");
        world.ReadableMatters.Add(crowded);
        for (var i = 0; i < ListDocumentsTool.ServiceRowLimit; i++)
        {
            var id = $"23000000-0000-4000-8000-{i + 1000:D12}";
            world.Add(
                DocumentToolsWorld.Document(id, new string('語', 850), 1_000_000_000_000L + i, DocumentToolsWorld.Newest.AddMinutes(-i)),
                crowded,
                readable: true);
        }

        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_matter", regardingId = crowded });

        IsError(result).Should().BeFalse(result.GetRawText());
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(DocumentToolSupport.ResultLimitBytes, "NFR-02: a result under 64 KiB at the list maximum");
        var body = result.GetProperty("structuredContent");
        body.GetProperty("count").GetInt32().Should().Be(ListDocumentsTool.ServiceRowLimit);
        body.GetProperty("hasMore").GetBoolean().Should().BeTrue("the service returned its row limit, so the matter may hold more");
    }

    [Fact]
    public async Task List_ForAMatterWithNoDocuments_IsAnEmptyListNotAnError()
    {
        var world = new DocumentToolsWorld();
        var empty = Guid.Parse("23000000-0000-4000-8000-0000000000c2");
        world.ReadableMatters.Add(empty);
        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_matter", regardingId = empty });

        IsError(result).Should().BeFalse();
        result.GetProperty("structuredContent").GetProperty("documents").GetArrayLength().Should().Be(0);
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("No documents are filed under this matter.");
    }

    [Fact]
    public async Task List_WithAnArgumentThatIsNotInTheSchema_IsRefusedWithTheFix_AndReadsNothing()
    {
        var world = new DocumentToolsWorld();
        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(
            ListTool, new { regardingEntityType = "sprk_matter", regardingId = DocumentToolsWorld.MatterId, containerId = "b!x" });

        IsError(result).Should().BeTrue();
        Code(result).Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        world.Probe.Asked.Should().BeEmpty();
        world.Documents.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task List_ForARegardingTypeOtherThanMatter_OrAnIdThatIsNotAGuid_IsRefused()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var wrongType = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_project", regardingId = DocumentToolsWorld.MatterId });
        var badId = await host.CallAsync(ListTool, new { regardingEntityType = "sprk_matter", regardingId = "not-a-guid" });

        IsError(wrongType).Should().BeTrue();
        Code(wrongType).Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        IsError(badId).Should().BeTrue();
        Code(badId).Should().Be(DocumentToolSupport.InvalidIdReasonCode);
    }

    // ---- spaarke_get_document --------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsTheSourceMetadata_AndThePreviewAndOpenLinksTheRestRoutesReturn()
    {
        var world = new DocumentToolsWorld();
        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.ReadableDocumentId });

        IsError(result).Should().BeFalse(result.GetRawText());
        var body = result.GetProperty("structuredContent");
        var document = body.GetProperty("document");
        var source = world.Rows[DocumentToolsWorld.ReadableDocumentId];
        document.GetProperty("id").GetString().Should().Be(source.Id);
        document.GetProperty("name").GetString().Should().Be(source.Name);
        document.GetProperty("fileType").GetString().Should().Be("docx");
        document.GetProperty("sizeBytes").GetInt64().Should().Be(source.FileSize);
        document.GetProperty("modifiedOn").GetDateTime().Should().Be(source.ModifiedOn);
        document.GetProperty("hasFile").GetBoolean().Should().BeTrue();
        body.GetProperty("regardingMatter").GetProperty("id").GetString().Should().Be(source.MatterId);
        body.GetProperty("regardingMatter").GetProperty("name").GetString().Should().Be("Acme v. Roadrunner");
        body.TryGetProperty("regardingProject", out _).Should().BeFalse("the document is not filed under a project");
        body.TryGetProperty("linksError", out _).Should().BeFalse();

        var links = body.GetProperty("links");
        links.GetProperty("previewUrl").GetString().Should().Be(DocumentToolsWorld.ExpectedPreviewUrl, "the preview-url route's previewUrl, byte for byte");
        links.GetProperty("webUrl").GetString().Should().Be(DocumentToolsWorld.ExpectedWebUrl, "the open-links route's webUrl");
        links.GetProperty("desktopUrl").GetString().Should().Be(DocumentToolsWorld.ExpectedDesktopUrl, "the open-links route's desktopUrl");
        world.SpeCalls.Should().OnlyContain(c => c.Contains($"b!drive-{DocumentToolsWorld.ReadableDocumentId}"), "the pointer is followed, app-only, for this document only");
    }

    [Fact]
    public async Task Get_NeverCarriesTheStoragePointerASummaryOrADescription()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var result = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.ReadableDocumentId });

        var raw = result.GetRawText();
        raw.Should().NotContain("b!drive-").And.NotContain("item-").And.NotContain("SECRET-", "ADR-015: only the model-visible fields are returned");
    }

    [Fact]
    public async Task Get_ForADocumentWithNoFile_ReturnsTheMetadata_AndNamesWhyThereAreNoLinks()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var result = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.NoFileDocumentId });

        IsError(result).Should().BeFalse("a missing file costs the links, not the call");
        var body = result.GetProperty("structuredContent");
        body.GetProperty("document").GetProperty("name").GetString().Should().Be("Scan placeholder");
        body.GetProperty("document").GetProperty("hasFile").GetBoolean().Should().BeFalse();
        body.TryGetProperty("links", out _).Should().BeFalse();
        body.GetProperty("linksError").GetProperty("reasonCode").GetString().Should().Be("no_file_attached");
        body.GetProperty("linksError").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_ForADocumentTheCallerMayNotRead_IsTheUniformNotFound_AndReadsNothingElse()
    {
        var world = new DocumentToolsWorld();
        await using var host = await DocumentToolsHost.StartAsync(world);

        var result = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.ForbiddenDocumentId });

        IsError(result).Should().BeTrue();
        Code(result).Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        result.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(404);
        world.Documents.Invocations.Should().BeEmpty("a document the caller may not read is not read");
        world.SpeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ForAForbiddenAndAnUnknownId_ReturnsByteIdenticalResults_ApartFromTheCorrelationId()
    {
        await using var host = await DocumentToolsHost.StartAsync(new DocumentToolsWorld());

        var forbidden = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.ForbiddenDocumentId });
        var missing = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.MissingDocumentId });

        IsError(forbidden).Should().BeTrue();
        Normalised(forbidden).Should().Be(Normalised(missing), "FR-03: nothing in the result tells a forbidden document from one that does not exist");
        forbidden.GetRawText().Should().NotContain(DocumentToolsWorld.ForbiddenDocumentId, "the not-found names no id");
    }

    [Fact]
    public async Task Get_WithoutADocumentId_OrWithAnExtraArgument_OrANonGuid_IsRefusedWithTheFix()
    {
        var world = new DocumentToolsWorld();
        await using var host = await DocumentToolsHost.StartAsync(world);

        var none = await host.CallAsync(GetTool, new { });
        var extra = await host.CallAsync(GetTool, new { documentId = DocumentToolsWorld.ReadableDocumentId, containerId = "b!x" });
        var badId = await host.CallAsync(GetTool, new { documentId = "not-a-guid" });

        Code(none).Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        Code(extra).Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        Code(badId).Should().Be(DocumentToolSupport.InvalidIdReasonCode);
        world.Authorization.Asked.Should().BeEmpty("nothing is decided for a call that cannot be read");
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static string? Code(JsonElement result) => result.GetProperty("structuredContent").GetProperty("reasonCode").GetString();

    private static string[] PropertyNames(JsonElement tool) =>
        tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

    /// <summary>The result as bytes with the per-request correlation id blanked (the one thing two requests differ in).</summary>
    private static string Normalised(JsonElement result) =>
        System.Text.RegularExpressions.Regex.Replace(result.GetRawText(), "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"<id>\"");

    /// <summary>The production MCP route on a real listener over <see cref="DocumentToolsWorld"/>.</summary>
    private sealed class DocumentToolsHost : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly string _token;

        private DocumentToolsHost(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
            _token = McpTestHost.MintToken(DocumentToolsWorld.CallerOid);
        }

        public static async Task<DocumentToolsHost> StartAsync(DocumentToolsWorld world)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
                ["AzureAd:TenantId"] = McpTestHost.TenantId,
                ["AzureAd:ClientId"] = McpTestHost.BffClientId,
            });

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = $"https://login.microsoftonline.com/{McpTestHost.TenantId}/v2.0",
                    ValidAudience = McpTestHost.BffClientId,
                    IssuerSigningKey = SigningKey,
                });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimitingModule();
            builder.Services.AddMcpModule(builder.Configuration, [typeof(ListDocumentsTool), typeof(GetDocumentTool)]);
            world.Register(builder.Services);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapMcpEndpoints();
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new DocumentToolsHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public Task<JsonElement> CallAsync(string tool, object arguments) => RpcAsync("tools/call", new { name = tool, arguments });

        /// <summary>One JSON-RPC call; returns the <c>result</c> (the transport answers JSON or an SSE stream).</summary>
        public async Task<JsonElement> RpcAsync(string method, object parameters)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var response = await _client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            var json = body.TrimStart().StartsWith('{')
                ? body
                : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
            using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
            doc.RootElement.TryGetProperty("error", out var error).Should().BeFalse($"a JSON-RPC error is a transport failure, not a domain result: {error}");
            return doc.RootElement.GetProperty("result").Clone();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
