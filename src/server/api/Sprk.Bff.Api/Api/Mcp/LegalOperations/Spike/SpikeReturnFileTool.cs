using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

/// <summary>
/// <c>SpaarkeMCP_spike_return_file</c>: THROWAWAY SPIKE (issue #3257, gap 1; branch <c>spike/spe-file-probe</c>, never merged to master).
/// Tests whether a Spaarke MCP tool result can reach Microsoft 365 Copilot Cowork's sandbox as a file. It returns one fixed sample .docx
/// (<see cref="SpikeFileProbeFiles"/>) in one of three shapes, chosen by <c>variant</c>: <c>a</c> a text block carrying the name, MIME type
/// and base64; <c>b</c> an embedded resource block with the bytes as a blob; <c>c</c> a resource link, whose target the same route serves
/// through <c>resources/read</c> (<see cref="SpikeFileProbeSmallWidgetResource"/>, <see cref="SpikeFileProbeLargeWidgetResource"/>).
/// </summary>
/// <remarks>
/// The tool name is fixed by the requester (accelerator-r1; Cowork's test script uses it) and is kept as given, not in the usual
/// <c>spaarke_{verb}_{object}</c> form. No data access: the only gate is the caller's, the same as the other tools (a null context or an
/// unauthenticated caller is <c>UserContextRequired</c>, via <see cref="DecisionCapability.HasSignedInCaller"/>).
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SpikeReturnFileTool : McpToolDefinition
{
    public const string ToolName = "SpaarkeMCP_spike_return_file";

    public const string VariantArgument = "variant";

    public const string SizeArgument = "size";

    public const string Description = "Spike for a file-delivery test only; returns a fixed sample .docx. Do not use for real documents.";

    /// <summary>The <c>reasonCode</c> of an <c>isError</c> result for a variant or size outside the closed sets.</summary>
    public const string InvalidArgumentReasonCode = "spike.invalid_argument";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [VariantArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("a", "b", "c"),
                ["description"] = "a: text block with base64; b: embedded resource block; c: resource link.",
            },
            [SizeArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("small", "large"),
                ["description"] = "Which fixed sample file to return.",
            },
        },
        ["required"] = new JsonArray(VariantArgument, SizeArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ReturnFile,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Spike: return a sample file",
                Description = Description,
                ReadOnly = true,
                Destructive = false,
                Idempotent = true,
                OpenWorld = false,
            });

        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>
    /// One call. The caller gate runs first (nothing is read either way); then <paramref name="variant"/> and <paramref name="size"/> are
    /// checked against their closed sets, and anything else is an <c>isError</c> result.
    /// </summary>
    internal static CallToolResult ReturnFile(IHttpContextAccessor accessor, string? variant = null, string? size = null)
    {
        var http = accessor.HttpContext;
        if (http == null || !DecisionCapability.HasSignedInCaller(http.User))
        {
            return McpToolResults.FromError(
                new DecisionToolError(DataverseUserClientErrorCodes.UserContextRequired, "The request has no signed-in caller.", StatusCodes.Status401Unauthorized),
                http?.TraceIdentifier);
        }

        var file = SpikeFileProbeFiles.ForSize(size);
        var shape = variant ?? string.Empty;
        if (file == null || (shape != "a" && shape != "b" && shape != "c"))
        {
            return McpToolResults.FromError(
                new DecisionToolError(InvalidArgumentReasonCode, "variant must be one of a, b, c and size must be small or large.", StatusCodes.Status400BadRequest),
                http.TraceIdentifier);
        }

        ContentBlock block = shape switch
        {
            "a" => new TextContentBlock
            {
                Text = $"name={file.FileName} mimeType={SpikeFileProbeFiles.DocxMimeType} base64={file.Base64}",
            },
            "b" => new EmbeddedResourceBlock
            {
                Resource = BlobResourceContents.FromBytes(file.Bytes, file.Uri, SpikeFileProbeFiles.DocxMimeType),
            },
            _ => new ResourceLinkBlock
            {
                Uri = file.Uri,
                Name = file.FileName,
                MimeType = SpikeFileProbeFiles.DocxMimeType,
                Size = file.Bytes.Length,
            },
        };

        return new CallToolResult { Content = [block] };
    }
}
