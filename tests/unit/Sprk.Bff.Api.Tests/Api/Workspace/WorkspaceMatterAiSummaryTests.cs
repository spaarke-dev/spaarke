using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using static Sprk.Bff.Api.Api.Workspace.WorkspaceMatterEndpoints;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// POST /api/workspace/matters/ai-summary — task 239 extended the route with an optional
/// <c>recordKind</c> so the Create Project wizard can draft a Project Description. The matter
/// request must keep producing exactly the same prompt and answers it did before.
/// </summary>
public class WorkspaceMatterAiSummaryTests
{
    // The matter prompt exactly as it was before task 239 (copied from the pre-change handler).
    private const string ExpectedMatterPrompt =
        "You are a legal assistant helping draft professional matter summaries for a law firm.\n" +
        "Generate a concise, professional summary (2-3 paragraphs) for a legal matter with the following details:\n" +
        "- Matter Name: Acme v. Beta\n" +
        "- Matter Type: Litigation\n" +
        "- Practice Area: Employment\n" +
        "\n" +
        "The summary should:\n" +
        "1. Open with the nature and purpose of the matter\n" +
        "2. Describe the key legal work and objectives\n" +
        "3. Note any important considerations or typical next steps for this type of matter\n" +
        "\n" +
        "Write in clear, professional language suitable for internal distribution to legal staff and partners.\n" +
        "Do not use placeholder text, hypothetical examples, or state that you are an AI.\n" +
        "Respond with only the summary text — no headings, labels, or JSON.";

    // The raw-string prompt takes the source file's line endings; the repo attributes force CRLF for *.cs on every OS,
    // so the expected text is compared with line endings normalised.
    private static string Norm(string? s) => (s ?? string.Empty).Replace("\r\n", "\n");

    /// <summary>The summary carried by the SSE "result" chunk, or null when the stream has none.</summary>
    private static string? ResultSummary(string sseBody)
    {
        foreach (var line in sseBody.Split('\n'))
        {
            if (!line.StartsWith("data: {", StringComparison.Ordinal)) continue;
            using var chunk = JsonDocument.Parse(line["data: ".Length..]);
            if (chunk.RootElement.GetProperty("type").GetString() != "result") continue;
            using var content = JsonDocument.Parse(chunk.RootElement.GetProperty("content").GetString()!);
            return content.RootElement.GetProperty("summary").GetString();
        }
        return null;
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<(int Status, string Body, string? Prompt)> RunAsync(AiSummaryRequest request, bool aiEnabled = true, string aiReply = "  Drafted text.  ")
    {
        string? capturedPrompt = null;
        var ai = new Mock<IBriefingAi>();
        ai.Setup(a => a.GenerateNarrativeAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<string, int?, CancellationToken>((p, _, _) => capturedPrompt = p)
            .ReturnsAsync(aiReply);

        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-239" };
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await HandleAiSummary(request, ctx, NullLogger<Program>.Instance, CancellationToken.None, aiEnabled ? ai.Object : null);

        return (ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()), capturedPrompt);
    }

    // ---- must-not-fire: the matter request is unchanged ----

    [Fact]
    public void BuildAiSummaryPrompt_Matter_IsByteForByteTheOriginalPrompt()
    {
        var request = new AiSummaryRequest("Acme v. Beta", "Litigation", "Employment");

        var prompt = BuildAiSummaryPrompt(request, AiSummaryKind.Matter);

        Norm(prompt).Should().Be(ExpectedMatterPrompt);
    }

    [Fact]
    public void BuildAiSummaryPrompt_Matter_IgnoresProjectFields()
    {
        var request = new AiSummaryRequest("Acme v. Beta", "Litigation", "Employment", ProjectName: "Ignored", ProjectType: "Ignored");

        Norm(BuildAiSummaryPrompt(request, AiSummaryKind.Matter)).Should().Be(ExpectedMatterPrompt);
    }

    [Fact]
    public async Task Handle_MatterRequestWithNoRecordKind_UsesTheMatterPromptAndStreamsTheTrimmedResult()
    {
        var (status, body, prompt) = await RunAsync(new AiSummaryRequest("Acme v. Beta", "Litigation", "Employment"));

        status.Should().Be(200);
        Norm(prompt).Should().Be(ExpectedMatterPrompt);
        body.Should().Contain("Loading matter details...").And.Contain("Reading matter information...");
        ResultSummary(body).Should().Be("Drafted text.");
        body.Should().EndWith("data: [DONE]\n\n");
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("MATTER")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Handle_ExplicitMatterKind_IsTheSameAsNoKind(string kind)
    {
        var (_, _, prompt) = await RunAsync(new AiSummaryRequest("Acme v. Beta", "Litigation", "Employment", RecordKind: kind));

        Norm(prompt).Should().Be(ExpectedMatterPrompt);
    }

    [Fact]
    public async Task Handle_MatterWithoutMatterName_Is400MatterNameRequired_EvenWithAProjectName()
    {
        var (status, body, prompt) = await RunAsync(new AiSummaryRequest("  ", "Litigation", "Employment", ProjectName: "A project"));

        status.Should().Be(400);
        body.Should().Contain("matterName is required.");
        prompt.Should().BeNull("a bad request never reaches the model");
    }

    [Fact]
    public void Deserialize_TheOriginalMatterBody_StillBinds()
    {
        var request = JsonSerializer.Deserialize<AiSummaryRequest>(
            "{\"matterName\":\"Acme v. Beta\",\"matterType\":\"Litigation\",\"practiceArea\":\"Employment\"}", Web)!;

        request.RecordKind.Should().BeNull();
        ResolveAiSummaryKind(request.RecordKind).Should().Be(AiSummaryKind.Matter);
        Norm(BuildAiSummaryPrompt(request, AiSummaryKind.Matter)).Should().Be(ExpectedMatterPrompt);
    }

    // ---- must-fire: a project request builds a project prompt ----

    [Fact]
    public void BuildAiSummaryPrompt_Project_NamesTheProjectAndItsTypeAndArea()
    {
        var request = new AiSummaryRequest("", "", "Employment", RecordKind: "project", ProjectName: "Data Room Migration", ProjectType: "Transaction");

        var prompt = BuildAiSummaryPrompt(request, AiSummaryKind.Project);

        prompt.Should().Contain("- Project Name: Data Room Migration")
            .And.Contain("- Project Type: Transaction")
            .And.Contain("- Practice Area: Employment")
            .And.Contain("project descriptions")
            .And.NotContain("Matter");
    }

    [Fact]
    public void BuildAiSummaryPrompt_Project_OmittedTypeAndAreaSayNotSpecified()
    {
        var request = new AiSummaryRequest("", null!, null!, RecordKind: "project", ProjectName: "Data Room Migration");

        var prompt = BuildAiSummaryPrompt(request, AiSummaryKind.Project);

        prompt.Should().Contain("- Project Type: Not specified").And.Contain("- Practice Area: Not specified");
    }

    [Fact]
    public async Task Handle_ProjectRequest_SendsTheProjectPromptAndStreamsTheResult()
    {
        var request = JsonSerializer.Deserialize<AiSummaryRequest>(
            "{\"recordKind\":\"Project\",\"projectName\":\"Data Room Migration\",\"projectType\":\"Transaction\",\"practiceArea\":\"Employment\"}", Web)!;

        var (status, body, prompt) = await RunAsync(request);

        status.Should().Be(200);
        prompt.Should().Contain("- Project Name: Data Room Migration").And.NotContain("Matter Name");
        body.Should().Contain("Loading project details...");
        ResultSummary(body).Should().Be("Drafted text.");
        body.Should().EndWith("data: [DONE]\n\n");
    }

    // ---- bad input is refused ----

    [Fact]
    public async Task Handle_ProjectWithoutProjectName_Is400_EvenWithAMatterName()
    {
        var (status, body, prompt) = await RunAsync(new AiSummaryRequest("Acme v. Beta", "", "", RecordKind: "project", ProjectName: " "));

        status.Should().Be(400);
        body.Should().Contain("projectName is required.");
        prompt.Should().BeNull();
    }

    [Theory]
    [InlineData("document")]
    [InlineData("proj")]
    [InlineData("matter; drop")]
    public async Task Handle_UnknownRecordKind_Is400(string kind)
    {
        var (status, body, prompt) = await RunAsync(new AiSummaryRequest("Acme v. Beta", "", "", RecordKind: kind, ProjectName: "P"));

        status.Should().Be(400);
        body.Should().Contain("recordKind must be");
        prompt.Should().BeNull();
    }

    // ---- over-length prompt fields are refused ----

    private static string Long(int extra = 1) => new('x', WorkspaceMatterEndpoints.MaxAiSummaryFieldLength + extra);

    [Theory]
    [InlineData("name", "matterName")]
    [InlineData("type", "matterType")]
    [InlineData("area", "practiceArea")]
    public async Task Handle_MatterFieldOverTheCap_Is400_AndNothingIsSentToTheAi(string field, string expectedName)
    {
        var request = new AiSummaryRequest(
            field == "name" ? Long() : "Acme v. Beta",
            field == "type" ? Long() : "Litigation",
            field == "area" ? Long() : "Employment");

        var (status, body, prompt) = await RunAsync(request);

        status.Should().Be(400);
        body.Should().Contain($"{expectedName} must be 500 characters or fewer.");
        prompt.Should().BeNull();
    }

    [Theory]
    [InlineData("name", "projectName")]
    [InlineData("type", "projectType")]
    [InlineData("area", "practiceArea")]
    public async Task Handle_ProjectFieldOverTheCap_Is400_AndNothingIsSentToTheAi(string field, string expectedName)
    {
        var request = new AiSummaryRequest(
            "", "",
            field == "area" ? Long() : "Employment",
            RecordKind: "project",
            ProjectName: field == "name" ? Long() : "Data Room Migration",
            ProjectType: field == "type" ? Long() : "Transaction");

        var (status, body, prompt) = await RunAsync(request);

        status.Should().Be(400);
        body.Should().Contain($"{expectedName} must be 500 characters or fewer.");
        prompt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FieldsExactlyAtTheCap_AreAccepted_ForBothKinds()
    {
        var atCap = Long(extra: 0);

        var matter = await RunAsync(new AiSummaryRequest(atCap, atCap, atCap));
        var project = await RunAsync(new AiSummaryRequest("", "", atCap, RecordKind: "project", ProjectName: atCap, ProjectType: atCap));

        matter.Status.Should().Be(200);
        matter.Prompt.Should().NotBeNull();
        project.Status.Should().Be(200);
        project.Prompt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_AiDisabled_Is503_AndNamesTheKind()
    {
        var matter = await RunAsync(new AiSummaryRequest("A", "", ""), aiEnabled: false);
        var project = await RunAsync(new AiSummaryRequest("", "", "", RecordKind: "project", ProjectName: "P"), aiEnabled: false);

        matter.Status.Should().Be(503);
        matter.Body.Should().Contain("Matter AI summary requires AI features");
        project.Status.Should().Be(503);
        project.Body.Should().Contain("Project AI summary requires AI features");
    }
}
