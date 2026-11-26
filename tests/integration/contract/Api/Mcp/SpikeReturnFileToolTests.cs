// CONTRACT — SpaarkeMCP_spike_return_file on POST /mcp/legal-operations (THROWAWAY SPIKE, issue #3257 gap 1; branch spike/spe-file-probe,
// never merged to master).
//
// WHAT RUNS: the REAL BFF host (McpProgramHostFactory: Program, the production AddMcpModule scan, a real A10 JwtBearer token), so the tool and
// the two resources variant c points at are served exactly as production serves them. The caller-gate cases call the tool's own method
// with a hand-made context, because the route refuses a caller without a token (401) before the tool is reached.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class SpikeReturnFileToolTests(McpProgramHostFactory factory) : IClassFixture<McpProgramHostFactory>
{
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string SmallSha256 = "2c05271788b4988a7b5a86ade067548889e34d733f7b0ff859ecf6a524c26b87";
    private const string LargeSha256 = "6d7939541ab07a4e3f928c91ea8301d71b3f4e1422eb0b46cd612187b1da19fc";

    private static readonly Guid Caller = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaa01");

    public static TheoryData<string, string> VariantsAndSizes => new()
    {
        { "a", "small" }, { "b", "small" }, { "c", "small" },
        { "a", "large" }, { "b", "large" }, { "c", "large" },
    };

    // ---- the bytes ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SpikeFileProbeFiles.SmallResourceName, 5673, SmallSha256)]
    [InlineData(SpikeFileProbeFiles.LargeResourceName, 191602, LargeSha256)]
    public void EmbeddedFile_HasExactlyTheOwnersBytes(string resourceName, int length, string sha256)
    {
        using var stream = typeof(SpikeFileProbeFiles).Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the .docx is an EmbeddedResource of the BFF");
        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);

        buffer.Length.Should().Be(length);
        Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())).Should().Be(sha256);
    }

    [Fact]
    public void SampleFiles_AreTheEmbeddedBytes_UnderTheirNamesAndUris()
    {
        var small = SpikeFileProbeFiles.ForSize("small")!;
        var large = SpikeFileProbeFiles.ForSize("large")!;

        Convert.ToHexStringLower(SHA256.HashData(small.Bytes)).Should().Be(SmallSha256);
        Convert.ToHexStringLower(SHA256.HashData(large.Bytes)).Should().Be(LargeSha256);
        small.FileName.Should().Be("spaarke-file-probe-small.docx");
        large.Uri.Should().Be("spaarke-probe://file/large.docx");
        SpikeFileProbeFiles.ForSize("medium").Should().BeNull();
    }

    // ---- tools/list --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_HasTheSpikeTool_ReadOnly_IdempotentNotDestructive_WithTheOwnersNameAndDescription()
    {
        var list = await RpcAsync("tools/list", new { });

        var tool = list.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "SpaarkeMCP_spike_return_file");
        tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        tool.GetProperty("description").GetString().Should().Be("Spike for a file-delivery test only; returns a fixed sample .docx. Do not use for real documents.");
        var annotations = tool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
        var schema = tool.GetProperty("inputSchema");
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("variant", "size");
        schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Should().Equal("variant", "size");
    }

    // ---- result shapes -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(VariantsAndSizes))]
    public async Task ToolsCall_ReturnsTheVariantsShape_ForTheSizesFile(string variant, string size)
    {
        var expected = size == "small" ? SpikeFileProbeFiles.Small() : SpikeFileProbeFiles.Large();

        var result = await CallAsync(variant, size);

        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse();
        var content = result.GetProperty("content").EnumerateArray().ToList();
        content.Should().ContainSingle("each variant is exactly one content block");
        var block = content[0];
        switch (variant)
        {
            case "a":
                block.GetProperty("type").GetString().Should().Be("text");
                block.GetProperty("text").GetString().Should().Be(
                    $"name=spaarke-file-probe-{size}.docx mimeType={Docx} base64={Convert.ToBase64String(expected.Bytes)}");
                break;
            case "b":
                block.GetProperty("type").GetString().Should().Be("resource");
                var resource = block.GetProperty("resource");
                resource.GetProperty("uri").GetString().Should().Be($"spaarke-probe://file/{size}.docx");
                resource.GetProperty("mimeType").GetString().Should().Be(Docx);
                Convert.FromBase64String(resource.GetProperty("blob").GetString()!).Should().Equal(expected.Bytes);
                break;
            default:
                block.GetProperty("type").GetString().Should().Be("resource_link");
                block.GetProperty("uri").GetString().Should().Be($"spaarke-probe://file/{size}.docx");
                block.GetProperty("name").GetString().Should().Be($"spaarke-file-probe-{size}.docx");
                block.GetProperty("mimeType").GetString().Should().Be(Docx);
                block.GetProperty("size").GetInt64().Should().Be(expected.Bytes.Length);
                break;
        }
    }

    // ---- resources/read (variant c's target) ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("small")]
    [InlineData("large")]
    public async Task ResourcesRead_ReturnsTheSameBytesTheToolReturnsAndTheEmbeddedFileHolds(string size)
    {
        var uri = $"spaarke-probe://file/{size}.docx";

        var read = await RpcAsync("resources/read", new { uri });
        var link = (await CallAsync("c", size)).GetProperty("content")[0];
        var inline = (await CallAsync("b", size)).GetProperty("content")[0].GetProperty("resource");

        var item = read.GetProperty("contents").EnumerateArray().Single();
        item.GetProperty("uri").GetString().Should().Be(uri).And.Be(link.GetProperty("uri").GetString());
        item.GetProperty("mimeType").GetString().Should().Be(Docx);
        var bytes = Convert.FromBase64String(item.GetProperty("blob").GetString()!);
        bytes.Should().Equal((size == "small" ? SpikeFileProbeFiles.Small() : SpikeFileProbeFiles.Large()).Bytes);
        bytes.Should().Equal(Convert.FromBase64String(inline.GetProperty("blob").GetString()!), "variant b and the resource carry identical bytes");
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(size == "small" ? SmallSha256 : LargeSha256);
        link.GetProperty("size").GetInt64().Should().Be(bytes.Length);
    }

    [Fact]
    public async Task ResourcesList_HasTheTwoProbeFiles_BesideTheDecisionsWidget()
    {
        var list = await RpcAsync("resources/list", new { });

        list.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("uri").GetString()).Should().BeEquivalentTo(
            "ui://spaarke/decisions.html", "spaarke-probe://file/small.docx", "spaarke-probe://file/large.docx");
    }

    [Fact]
    public async Task ResourcesRead_WithoutAToken_Returns401()
    {
        using var response = await PostAsync("resources/read", new { uri = "spaarke-probe://file/small.docx" }, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the probe files are served to authenticated callers only");
    }

    // ---- bad arguments -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("d", "small")]
    [InlineData("A", "small")]
    [InlineData("", "large")]
    [InlineData("a", "medium")]
    [InlineData("a", "")]
    [InlineData("a", "Small")]
    [InlineData(null, "small")]
    [InlineData("a", null)]
    [InlineData(null, null)]
    public async Task ToolsCall_ForAVariantOrSizeOutsideTheClosedSets_IsAnIsErrorResult_WithNoFile(string? variant, string? size)
    {
        var arguments = new Dictionary<string, string?>();
        if (variant is not null)
        {
            arguments["variant"] = variant;
        }

        if (size is not null)
        {
            arguments["size"] = size;
        }

        var result = await RpcAsync("tools/call", new { name = SpikeReturnFileTool.ToolName, arguments });

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(SpikeReturnFileTool.InvalidArgumentReasonCode);
        result.GetProperty("content").EnumerateArray().Should().ContainSingle().Which.GetProperty("type").GetString().Should().Be("text");
        result.GetRawText().Should().NotContain("base64=").And.NotContain("\"blob\"");
    }

    // ---- the caller gate ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ReturnFile_WithNoHttpContext_IsUserContextRequired_AndReturnsNoFile()
    {
        var result = SpikeReturnFileTool.ReturnFile(new HttpContextAccessor { HttpContext = null }, "a", "small");

        AssertUserContextRequired(result);
    }

    [Fact]
    public void ReturnFile_WithAnUnauthenticatedCaller_IsUserContextRequired_AndReturnsNoFile()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        AssertUserContextRequired(SpikeReturnFileTool.ReturnFile(accessor, "b", "large"));
        AssertUserContextRequired(SpikeReturnFileTool.ReturnFile(accessor, "c", "small"));
    }

    [Fact]
    public async Task ToolsCall_WithoutAToken_Returns401_BeforeTheToolRuns()
    {
        using var response = await PostAsync(
            "tools/call", new { name = SpikeReturnFileTool.ToolName, arguments = new { variant = "a", size = "small" } }, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------

    private static void AssertUserContextRequired(ModelContextProtocol.Protocol.CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle().Which.Should().BeOfType<ModelContextProtocol.Protocol.TextContentBlock>();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be("DATAVERSE_USER_CONTEXT_REQUIRED");
        body.GetProperty("status").GetInt32().Should().Be(401);
    }

    private Task<JsonElement> CallAsync(string variant, string size) =>
        RpcAsync("tools/call", new { name = SpikeReturnFileTool.ToolName, arguments = new { variant, size } });

    private async Task<HttpResponseMessage> PostAsync(string method, object parameters, string? token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    /// <summary>One JSON-RPC call as the caller; the <c>result</c> (an <c>isError</c> result included). A JSON-RPC error throws.</summary>
    private async Task<JsonElement> RpcAsync(string method, object parameters)
    {
        using var response = await PostAsync(method, parameters, McpProgramHostFactory.MintToken(Caller));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body.Length > 2000 ? body[..2000] : body);

        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC error: {error}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }
}
