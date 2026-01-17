using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Streaming;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Tests for ServerSentEventWriter utility.
/// Verifies proper SSE format with event: and data: lines.
/// </summary>
public class ServerSentEventWriterTests
{
    private readonly DefaultHttpContext _httpContext;
    private readonly MemoryStream _responseStream;

    public ServerSentEventWriterTests()
    {
        _responseStream = new MemoryStream();
        _httpContext = new DefaultHttpContext();
        _httpContext.Response.Body = _responseStream;
    }

    private HttpResponse Response => _httpContext.Response;

    private string GetWrittenContent()
    {
        _responseStream.Position = 0;
        using var reader = new StreamReader(_responseStream, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    #region SetSseHeaders Tests

    [Fact]
    public void SetSseHeaders_SetsCorrectHeaders()
    {
        ServerSentEventWriter.SetSseHeaders(Response);

        Response.ContentType.Should().Be("text/event-stream");
        Response.Headers["Cache-Control"].ToString().Should().Be("no-cache");
        Response.Headers["Connection"].ToString().Should().Be("keep-alive");
    }

    #endregion

    #region WriteEventAsync Tests

    [Fact]
    public async Task WriteEventAsync_WritesCorrectSseFormat()
    {
        var evt = ThinkingEvent.Create("Processing...");

        await ServerSentEventWriter.WriteEventAsync(Response, evt, CancellationToken.None);

        var content = GetWrittenContent();
        content.Should().StartWith("event: thinking\ndata: ");
        content.Should().EndWith("\n\n");
        content.Should().Contain("\"type\":\"thinking\"");
        content.Should().Contain("\"content\":\"Processing...\"");
    }

    #endregion

    #region WriteThinkingAsync Tests

    [Fact]
    public async Task WriteThinkingAsync_WritesThinkingEvent()
    {
        await ServerSentEventWriter.WriteThinkingAsync(
            Response, "Analyzing...", "Step 1");

        var content = GetWrittenContent();
        content.Should().Contain("event: thinking");
        content.Should().Contain("\"content\":\"Analyzing...\"");
        content.Should().Contain("\"step\":\"Step 1\"");
    }

    #endregion

    #region WriteDataverseOperationAsync Tests

    [Fact]
    public async Task WriteDataverseOperationAsync_WritesDataverseOperationEvent()
    {
        var recordId = Guid.NewGuid();

        await ServerSentEventWriter.WriteDataverseOperationAsync(
            Response, "created", "sprk_aiaction", recordId, "Created action");

        var content = GetWrittenContent();
        content.Should().Contain("event: dataverse_operation");
        content.Should().Contain("\"operation\":\"created\"");
        content.Should().Contain("\"entityType\":\"sprk_aiaction\"");
        content.Should().Contain(recordId.ToString());
    }

    #endregion

    #region WriteCanvasPatchAsync Tests

    [Fact]
    public async Task WriteCanvasPatchAsync_WritesCanvasPatchEvent()
    {
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = new CanvasNode { Id = "n1", Type = "action" }
        };

        await ServerSentEventWriter.WriteCanvasPatchAsync(
            Response, patch, "Added node");

        var content = GetWrittenContent();
        content.Should().Contain("event: canvas_patch");
        content.Should().Contain("\"patch\":");
        content.Should().Contain("\"description\":\"Added node\"");
    }

    #endregion

    #region WriteMessageAsync Tests

    [Fact]
    public async Task WriteMessageAsync_WritesMessageEvent()
    {
        await ServerSentEventWriter.WriteMessageAsync(
            Response, "Hello, I can help!");

        var content = GetWrittenContent();
        content.Should().Contain("event: message");
        content.Should().Contain("\"content\":\"Hello, I can help!\"");
    }

    [Fact]
    public async Task WriteMessageAsync_IncludesIsPartialFlag()
    {
        await ServerSentEventWriter.WriteMessageAsync(
            Response, "Part 1...", isPartial: true);

        var content = GetWrittenContent();
        content.Should().Contain("\"isPartial\":true");
    }

    #endregion

    #region WriteDoneAsync Tests

    [Fact]
    public async Task WriteDoneAsync_WritesDoneEvent()
    {
        await ServerSentEventWriter.WriteDoneAsync(
            Response, operationCount: 5, summary: "Added 5 nodes");

        var content = GetWrittenContent();
        content.Should().Contain("event: done");
        content.Should().Contain("\"operationCount\":5");
        content.Should().Contain("\"summary\":\"Added 5 nodes\"");
    }

    #endregion

    #region WriteErrorAsync Tests

    [Fact]
    public async Task WriteErrorAsync_WritesErrorEvent()
    {
        await ServerSentEventWriter.WriteErrorAsync(
            Response,
            "Something went wrong",
            code: "ERR_500",
            isRecoverable: true,
            suggestedAction: "Try again");

        var content = GetWrittenContent();
        content.Should().Contain("event: error");
        content.Should().Contain("\"message\":\"Something went wrong\"");
        content.Should().Contain("\"code\":\"ERR_500\"");
        content.Should().Contain("\"suggestedAction\":\"Try again\"");
    }

    #endregion

    #region WriteClarificationAsync Tests

    [Fact]
    public async Task WriteClarificationAsync_WritesClarificationEvent()
    {
        var options = new[] { "Option A", "Option B" };

        await ServerSentEventWriter.WriteClarificationAsync(
            Response, "Which option?", options);

        var content = GetWrittenContent();
        content.Should().Contain("event: clarification");
        content.Should().Contain("\"question\":\"Which option?\"");
        content.Should().Contain("\"options\":[\"Option A\",\"Option B\"]");
    }

    #endregion

    #region WritePlanPreviewAsync Tests

    [Fact]
    public async Task WritePlanPreviewAsync_WritesPlanPreviewEvent()
    {
        var plan = new BuildPlan
        {
            Summary = "Build a playbook",
            Steps = []
        };

        await ServerSentEventWriter.WritePlanPreviewAsync(Response, plan);

        var content = GetWrittenContent();
        content.Should().Contain("event: plan_preview");
        content.Should().Contain("\"plan\":");
        content.Should().Contain("\"summary\":\"Build a playbook\"");
    }

    #endregion

    #region SSE Format Compliance Tests

    [Fact]
    public async Task AllEventTypes_FollowSseFormat()
    {
        // Write multiple events
        await ServerSentEventWriter.WriteThinkingAsync(Response, "Thinking...");
        await ServerSentEventWriter.WriteMessageAsync(Response, "Message");
        await ServerSentEventWriter.WriteDoneAsync(Response);

        var content = GetWrittenContent();

        // Each event should have event: and data: lines, ending with \n\n
        var events = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        events.Length.Should().Be(3);

        foreach (var evt in events)
        {
            evt.Should().StartWith("event: ");
            evt.Should().Contain("\ndata: ");
        }
    }

    #endregion
}
