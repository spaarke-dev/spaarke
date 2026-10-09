using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// #1511 probe (task 176 step 1): a chunk of a document on a secure matter the caller cannot read must not
/// reach the chat tool result. On master c8a87d818 nothing trims the RAG rows, so these FAIL (leak confirmed).
/// </summary>
public sealed class Issue1511ProbeTests : TypedToolHandlerTestFixture
{
    private const string Secret = "SECRET-1511-merger-price-42m";

    [Theory]
    [InlineData("SearchDocuments")]
    [InlineData("SearchDiscovery")]
    public async Task SecureMatterChunk_DoesNotReachTheChatResult(string method)
    {
        var rag = new Mock<IRagService>();
        rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "price",
                Results = new[]
                {
                    new RagSearchResult
                    {
                        Id = "chunk-1", DocumentId = Guid.NewGuid().ToString(), DocumentName = "Secure merger memo.docx",
                        Content = Secret, ChunkIndex = 0, ChunkCount = 1, Score = 0.9
                    }
                },
                TotalCount = 1
            });

        var handler = new DocumentSearchHandler(rag.Object, CreateLogger<DocumentSearchHandler>());
        var tool = BuildAnalysisTool(nameof(DocumentSearchHandler), $"{{\"method\":\"{method}\"}}", toolType: ToolType.Custom);
        var ctx = new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(), TenantId = "tenant-1", UserId = "caller-without-read",
            ToolArgumentsJson = "{\"query\":\"price\"}"
        };

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        JsonSerializer.Serialize(result.Data).Should().NotContain(Secret);
        JsonSerializer.Serialize(result.Metadata).Should().NotContain(Secret);
    }
}
