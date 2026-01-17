using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Tests for SSE event types used in AI Playbook Builder streaming.
/// Verifies proper JSON serialization and event type constants.
/// </summary>
public class BuilderSseEventsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Event Type Constants

    [Fact]
    public void EventTypes_AreCorrectlyDefined()
    {
        BuilderSseEventTypes.Thinking.Should().Be("thinking");
        BuilderSseEventTypes.DataverseOperation.Should().Be("dataverse_operation");
        BuilderSseEventTypes.CanvasPatch.Should().Be("canvas_patch");
        BuilderSseEventTypes.Message.Should().Be("message");
        BuilderSseEventTypes.Done.Should().Be("done");
        BuilderSseEventTypes.Error.Should().Be("error");
        BuilderSseEventTypes.Clarification.Should().Be("clarification");
        BuilderSseEventTypes.PlanPreview.Should().Be("plan_preview");
    }

    #endregion

    #region ThinkingEvent Tests

    [Fact]
    public void ThinkingEvent_Create_SetsCorrectType()
    {
        var evt = ThinkingEvent.Create("Processing...", "Step 1");

        evt.Type.Should().Be(BuilderSseEventTypes.Thinking);
        evt.Content.Should().Be("Processing...");
        evt.Step.Should().Be("Step 1");
    }

    [Fact]
    public void ThinkingEvent_SerializesToCorrectJson()
    {
        var evt = ThinkingEvent.Create("Analyzing intent", "Classification");

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"thinking\"");
        json.Should().Contain("\"content\":\"Analyzing intent\"");
        json.Should().Contain("\"step\":\"Classification\"");
    }

    #endregion

    #region DataverseOperationEvent Tests

    [Fact]
    public void DataverseOperationEvent_Create_SetsCorrectProperties()
    {
        var recordId = Guid.NewGuid();
        var evt = DataverseOperationEvent.Create(
            "created", "sprk_aiaction", recordId, "Created summarization action");

        evt.Type.Should().Be(BuilderSseEventTypes.DataverseOperation);
        evt.Operation.Should().Be("created");
        evt.EntityType.Should().Be("sprk_aiaction");
        evt.RecordId.Should().Be(recordId);
        evt.Description.Should().Be("Created summarization action");
    }

    [Fact]
    public void DataverseOperationEvent_SerializesToCorrectJson()
    {
        var evt = DataverseOperationEvent.Create("created", "sprk_aiskill");

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"dataverse_operation\"");
        json.Should().Contain("\"operation\":\"created\"");
        json.Should().Contain("\"entityType\":\"sprk_aiskill\"");
    }

    #endregion

    #region CanvasPatchEvent Tests

    [Fact]
    public void CanvasPatchEvent_Create_SetsCorrectProperties()
    {
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = new CanvasNode { Id = "node1", Type = "aiAnalysis" }
        };

        var evt = CanvasPatchEvent.Create(patch, "Added analysis node");

        evt.Type.Should().Be(BuilderSseEventTypes.CanvasPatch);
        evt.Patch.Should().Be(patch);
        evt.Description.Should().Be("Added analysis node");
    }

    [Fact]
    public void CanvasPatchEvent_SerializesToCorrectJson()
    {
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = new CanvasNode { Id = "n1", Type = "action" }
        };
        var evt = CanvasPatchEvent.Create(patch);

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"canvas_patch\"");
        json.Should().Contain("\"patch\":");
        json.Should().Contain("\"operation\":");
    }

    #endregion

    #region MessageEvent Tests

    [Fact]
    public void MessageEvent_Create_SetsCorrectProperties()
    {
        var evt = MessageEvent.Create("I'll add a summarization node", isPartial: false);

        evt.Type.Should().Be(BuilderSseEventTypes.Message);
        evt.Content.Should().Be("I'll add a summarization node");
        evt.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void MessageEvent_SerializesToCorrectJson()
    {
        var evt = MessageEvent.Create("Hello", isPartial: true);

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"message\"");
        json.Should().Contain("\"content\":\"Hello\"");
        json.Should().Contain("\"isPartial\":true");
    }

    #endregion

    #region DoneEvent Tests

    [Fact]
    public void DoneEvent_Create_SetsCorrectProperties()
    {
        var evt = DoneEvent.Create(operationCount: 3, summary: "Added 3 nodes");

        evt.Type.Should().Be(BuilderSseEventTypes.Done);
        evt.OperationCount.Should().Be(3);
        evt.Summary.Should().Be("Added 3 nodes");
    }

    [Fact]
    public void DoneEvent_SerializesToCorrectJson()
    {
        var evt = DoneEvent.Create(operationCount: 5);

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"done\"");
        json.Should().Contain("\"operationCount\":5");
    }

    #endregion

    #region ErrorEvent Tests

    [Fact]
    public void ErrorEvent_Create_SetsCorrectProperties()
    {
        var evt = ErrorEvent.Create(
            "Request failed",
            code: "API_ERROR",
            isRecoverable: true,
            suggestedAction: "Try again");

        evt.Type.Should().Be(BuilderSseEventTypes.Error);
        evt.Message.Should().Be("Request failed");
        evt.Code.Should().Be("API_ERROR");
        evt.IsRecoverable.Should().BeTrue();
        evt.SuggestedAction.Should().Be("Try again");
    }

    [Fact]
    public void ErrorEvent_SerializesToCorrectJson()
    {
        var evt = ErrorEvent.Create("Failed", code: "ERR_001");

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"error\"");
        json.Should().Contain("\"message\":\"Failed\"");
        json.Should().Contain("\"code\":\"ERR_001\"");
    }

    #endregion

    #region ClarificationEvent Tests

    [Fact]
    public void ClarificationEvent_Create_SetsCorrectProperties()
    {
        var options = new[] { "Summarization", "Extraction", "Classification" };
        var evt = ClarificationEvent.Create("What type of analysis?", options);

        evt.Type.Should().Be(BuilderSseEventTypes.Clarification);
        evt.Question.Should().Be("What type of analysis?");
        evt.Options.Should().BeEquivalentTo(options);
    }

    [Fact]
    public void ClarificationEvent_SerializesToCorrectJson()
    {
        var evt = ClarificationEvent.Create("Which node?");

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"clarification\"");
        json.Should().Contain("\"question\":\"Which node?\"");
    }

    #endregion

    #region PlanPreviewEvent Tests

    [Fact]
    public void PlanPreviewEvent_Create_SetsCorrectProperties()
    {
        var plan = new BuildPlan
        {
            Id = Guid.NewGuid(),
            Summary = "Create document analysis playbook"
        };
        var evt = PlanPreviewEvent.Create(plan);

        evt.Type.Should().Be(BuilderSseEventTypes.PlanPreview);
        evt.Plan.Should().Be(plan);
    }

    [Fact]
    public void PlanPreviewEvent_SerializesToCorrectJson()
    {
        var plan = new BuildPlan
        {
            Id = Guid.NewGuid(),
            Summary = "Test plan"
        };
        var evt = PlanPreviewEvent.Create(plan);

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.Should().Contain("\"type\":\"plan_preview\"");
        json.Should().Contain("\"plan\":");
        json.Should().Contain("\"summary\":\"Test plan\"");
    }

    #endregion

    #region Timestamp Tests

    [Fact]
    public void AllEvents_HaveTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var thinkingEvent = ThinkingEvent.Create("test");
        var messageEvent = MessageEvent.Create("test");
        var doneEvent = DoneEvent.Create();

        var after = DateTimeOffset.UtcNow;

        thinkingEvent.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        messageEvent.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        doneEvent.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    #endregion
}
