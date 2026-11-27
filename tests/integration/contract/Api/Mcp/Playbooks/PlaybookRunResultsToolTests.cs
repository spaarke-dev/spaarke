// CONTRACT — spaarke_get_playbook_run_results, the app-only results tool of the playbook run widget (spaarkeai-mcp-bff-api-r2 task 037b;
// D-037-results, D-037-engine; spec FR-05, FR-16, NFR-02, NFR-05; ADR-015).
// KEEP path: contract.
//
// WHAT RUNS: the tool's RunAsync over the REAL DetachedPlaybookRunner (its run table and IPlaybookRunAi.ReadResultsAsCallerAsync), the REAL
// PlaybookRunRecordStore over its Dataverse seam, the REAL analysis-read rule (AnalysisAuthorizationFilter.IsAnalysisReadableAsync) over
// task 007's REAL authorization stack, in PlaybookRunWorld. SUBSTITUTED at the boundaries only: the engine, the caller's OBO Dataverse
// client (IDataverseUserClient), the run record store's and the rule's app-only seam (IGenericEntityService), the access-data source.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;

public sealed class PlaybookRunResultsToolTests
{
    /// <summary>The largest tool result the host may return (NFR-02).</summary>
    private const int MaxResultBytes = 64 * 1024;

    /// <summary>A marker in the analysis text, a document name and the playbook name: it must never reach a log line.</summary>
    private const string ContentMarker = "CONTENT-037b-7d1e";

    private static readonly HandoffUrlBuilder Links = new(PlaybookRunWorld.EnvironmentUrl);

    private static JsonElement Body(CallToolResult result) => result.StructuredContent!.Value;

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content.Single()).Text;

    private static string Serialized(CallToolResult result) => JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

    private static async Task<Guid> StartAndFinishAsync(PlaybookRunWorld world, Guid playbookId, Guid documentId)
    {
        var started = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = Guid.NewGuid().ToString() });
        started.IsError.Should().NotBe(true, started.StructuredContent?.ToString());
        var runId = Guid.Parse(Body(started).GetProperty("runId").GetString()!);
        world.Gate.TrySetResult();
        await world.RunFinished();
        return runId;
    }

    // ---- available: the analysis's working document, as the caller ----------------------------------------------------------------

    [Fact(DisplayName = "037b results: a finished Copilot run returns its analysis's working document in sections, read as the caller, with the analysis link and the readable documents")]
    public async Task FinishedRun_ReturnsTheAnalysisTextInSections()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId,
            "Lead paragraph.\n\n# Summary\nThe NDA is mutual.\n\n## Risks\n- Term is 5 years.\n```\n# not a heading in a fence\n```\n");
        world.CallerReadsDocuments((documentId, "NDA.docx"));
        world.CallerReadsPlaybookName(playbookId, "Summarize an NDA");
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("runId").GetString().Should().Be(runId.ToString("D"));
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("outcome").GetString().Should().Be("completed");
        body.GetProperty("results").GetString().Should().Be("available");
        body.GetProperty("playbookId").GetString().Should().Be(playbookId.ToString("D"));
        body.GetProperty("playbookName").GetString().Should().Be("Summarize an NDA");
        body.GetProperty("analysisId").GetString().Should().Be(PlaybookRunWorld.AnalysisId.ToString("D"));
        body.GetProperty("truncated").GetBoolean().Should().BeFalse();
        body.TryGetProperty("moreLink", out _).Should().BeFalse("nothing was cut");
        body.GetProperty("consoleLink").GetString().Should().Be(Links.BuildAnalysisWorkspaceUrl(PlaybookRunWorld.AnalysisId, documentId),
            "the Console opens an existing analysis by analysisId (launch-resolver entry case 2d)");

        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().HaveCount(3);
        sections[0].TryGetProperty("heading", out _).Should().BeFalse("text before the first heading has no heading");
        sections[0].GetProperty("text").GetString().Should().Be("Lead paragraph.");
        sections[1].GetProperty("heading").GetString().Should().Be("Summary");
        sections[1].GetProperty("text").GetString().Should().Be("The NDA is mutual.");
        sections[2].GetProperty("heading").GetString().Should().Be("Risks");
        sections[2].GetProperty("text").GetString().Should().Contain("# not a heading in a fence");
        sections.Should().OnlyContain(s => !s.GetProperty("truncated").GetBoolean());

        var document = body.GetProperty("documents").EnumerateArray().Single();
        document.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        document.GetProperty("name").GetString().Should().Be("NDA.docx");
        document.GetProperty("link").GetString().Should().Be(Links.BuildConsoleUrl(documentId));

        world.AnalysisReads.Should().ContainSingle().Which.Should().Contain("sprk_workingdocument", "the caller's own read carries the content");
        world.RunRecords.Verify(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.Is<string[]>(c => c.Contains("sprk_workingdocument")), It.IsAny<CancellationToken>()),
            Times.Never(), "no app-only read of output content");
        Text(result).Should().NotContain("NDA is mutual", "the text line carries no content");
    }

    [Fact(DisplayName = "037b results: a run read from its durable record finds its analysis through the record's regarding columns")]
    public async Task StoredRun_FindsItsAnalysisThroughTheRegardingColumns()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var runId = world.SeedStoredRun(playbookId, PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerReadsAnalysis(analysisId, documentId, "No headings here, just one paragraph.");
        world.CallerReadsDocuments((documentId, "Lease.pdf"));

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("results").GetString().Should().Be("available");
        body.GetProperty("analysisId").GetString().Should().Be(analysisId.ToString("D"));
        body.TryGetProperty("playbookName", out _).Should().BeFalse("a playbook name the caller cannot read is left out, not an error");
        var section = body.GetProperty("sections").EnumerateArray().Single();
        section.TryGetProperty("heading", out _).Should().BeFalse("a document with no headings is one section without one");
        section.GetProperty("text").GetString().Should().Be("No headings here, just one paragraph.");
        body.GetProperty("consoleLink").GetString().Should().Be(Links.BuildAnalysisWorkspaceUrl(analysisId, documentId));
    }

    // ---- pending: a run still in progress returns its state, no content -------------------------------------------------------------

    [Fact(DisplayName = "037b results: a run still running returns its state and progress with no content; the working document is not even read")]
    public async Task RunningRun_ReturnsItsStateWithNoContent()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, "SHOULD NOT BE READ");
        world.CallerReadsDocuments((documentId, "NDA.docx"));
        var started = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "pending" });
        var runId = Guid.Parse(Body(started).GetProperty("runId").GetString()!);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("status").GetString().Should().Be("running");
        body.GetProperty("results").GetString().Should().Be("pending");
        body.GetProperty("progress").GetInt32().Should().Be(0);
        body.GetProperty("sections").GetArrayLength().Should().Be(0);
        body.GetProperty("documents").GetArrayLength().Should().Be(0);
        Serialized(result).Should().NotContain("SHOULD NOT BE READ");
        world.AnalysisReads.Should().OnlyContain(p => !p.Contains("sprk_workingdocument"), "a running run's working document is not read");
    }

    // ---- none: the "no results to show" state (037-decisions §5) --------------------------------------------------------------------

    [Theory(DisplayName = "037b results: an analysis whose working document is empty is the plain 'none' state with the analysis link, not the not-found")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\n ")]
    public async Task EmptyWorkingDocument_IsNone(string? workingDocument)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, workingDocument);
        world.CallerReadsDocuments((documentId, "NDA.docx"));
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("results").GetString().Should().Be("none");
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("sections").GetArrayLength().Should().Be(0);
        body.GetProperty("analysisId").GetString().Should().Be(PlaybookRunWorld.AnalysisId.ToString("D"));
        body.GetProperty("consoleLink").GetString().Should().Be(Links.BuildAnalysisWorkspaceUrl(PlaybookRunWorld.AnalysisId, documentId));
        body.GetProperty("documents").GetArrayLength().Should().Be(1);
        Text(result).Should().Contain("no results to show");
    }

    [Fact(DisplayName = "037b results: an owned run with no linked analysis (started before 037a) is the plain 'none' state with the document's Console link")]
    public async Task RunWithNoAnalysis_IsNone()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, regarding: null, documentId);
        world.CallerReadsDocuments((documentId, "Old run.docx"));

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("results").GetString().Should().Be("none");
        body.TryGetProperty("analysisId", out _).Should().BeFalse();
        body.GetProperty("consoleLink").GetString().Should().Be(Links.BuildConsoleUrl(documentId));
        body.GetProperty("documents").EnumerateArray().Single().GetProperty("name").GetString().Should().Be("Old run.docx");
        world.AnalysisReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "037b results: a run whose record is regarding a matter (a record writer, no analysis) is the 'none' state too")]
    public async Task RunRegardingAMatter_IsNone()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_matter", Guid.NewGuid()), documentId);
        world.CallerReadsDocuments((documentId, "Matter doc.docx"));

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        Body(result).GetProperty("results").GetString().Should().Be("none");
        world.AnalysisReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "037b results: a durable record still Running past the cap is reported interrupted with 'none', as the status tool reports it")]
    public async Task InterruptedRun_IsNone()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Running, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerReadsAnalysis(analysisId, documentId, null);
        world.Time.Advance(TimeSpan.FromHours(2));

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        var body = Body(result);
        body.GetProperty("status").GetString().Should().Be("failed");
        body.GetProperty("outcome").GetString().Should().Be("interrupted");
        body.GetProperty("results").GetString().Should().Be("none");
        body.GetProperty("reasonCode").GetString().Should().Be(PlaybookRunReasonCodes.Interrupted);
    }

    // ---- task 037 review F-A: a completed run whose save failed is "unsaved", not "none" ---------------------------------------------

    [Fact(DisplayName = "037 review F-A: a completed run whose analysis has no text and was never finalized is 'unsaved' with its reason, not 'none'")]
    public async Task CompletedRun_UnfinalizedEmptyAnalysis_IsUnsaved()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, workingDocument: null, statusCode: 1);
        world.CallerReadsDocuments((documentId, "NDA.docx"));
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        var body = Body(result);
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("results").GetString().Should().Be("unsaved");
        body.GetProperty("resultsReason").GetString().Should().Be(GetPlaybookRunResultsTool.UnsavedReason);
        body.GetProperty("sections").GetArrayLength().Should().Be(0);
        body.GetProperty("consoleLink").GetString().Should().Be(Links.BuildAnalysisWorkspaceUrl(PlaybookRunWorld.AnalysisId, documentId));
        world.AnalysisReads.Should().ContainSingle().Which.Should().Contain("statuscode");
        Text(result).Should().Contain("could not be saved");
    }

    [Fact(DisplayName = "037 review F-A: a FAILED run's unfinalized, empty analysis stays 'none' (a failed run never finalizes; its status says it failed)")]
    public async Task FailedRun_UnfinalizedEmptyAnalysis_IsNone()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Failed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerReadsAnalysis(analysisId, documentId, workingDocument: null, statusCode: 1);
        world.CallerReadsDocuments((documentId, "NDA.docx"));

        var body = Body(await world.ResultsToolAsync(world.CallerContext(), runId));

        body.GetProperty("status").GetString().Should().Be("failed");
        body.GetProperty("results").GetString().Should().Be("none");
        body.TryGetProperty("resultsReason", out _).Should().BeFalse();
    }

    // ---- task 037 review F-C: no input can make the result building throw out of the tool --------------------------------------------

    [Fact(DisplayName = "037 review F-C: a heading followed by 50,000 spaces is split quickly and never throws (it timed out the old pattern)")]
    public async Task LongWhitespaceHeading_IsLinear_AndTheToolAnswers()
    {
        var hostile = "# a" + new string(' ', 50_000) + "x\nbody";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (sections, _) = GetPlaybookRunResultsTool.Sections(hostile);
        clock.Stop();

        clock.ElapsedMilliseconds.Should().BeLessThan(500, "the heading scan is linear");
        sections.Single().Heading.Should().StartWith("a ");
        sections.Single().Text.Should().Be("body");

        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, hostile);
        world.CallerReadsDocuments((documentId, "NDA.docx"));
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        Body(result).GetProperty("results").GetString().Should().Be("available");
    }

    [Theory(DisplayName = "037 review F-C: the heading scan matches what the old ATX heading pattern matched")]
    [InlineData("# Title")]
    [InlineData("###### Six")]
    [InlineData("####### Seven is not a heading")]
    [InlineData("#NoSpace")]
    [InlineData("   # Three spaces")]
    [InlineData("    # Four spaces is not a heading")]
    [InlineData("\t# A tab first is not a heading")]
    [InlineData("# Closing ##")]
    [InlineData("# Closing ##   ")]
    [InlineData("# Not closing#")]
    [InlineData("# a # b")]
    [InlineData("# ##")]
    [InlineData("#  ##")]
    [InlineData("# ")]
    [InlineData("#\tTabbed\t#\t")]
    [InlineData("## a ## b ##")]
    [InlineData("plain text")]
    [InlineData("")]
    public void HeadingText_MatchesTheOldPattern(string line)
    {
        var old = new System.Text.RegularExpressions.Regex(
            @"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Match(line);

        GetPlaybookRunResultsTool.HeadingText(line).Should().Be(old.Success ? old.Groups["text"].Value : null);
    }

    [Fact(DisplayName = "037 review F-C: a fault while building the result is the 'could not read' isError (503, the status tool's code), never a thrown error")]
    public async Task ResultBuildingFault_IsCouldNotRead()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var run = new PlaybookRunView(Guid.NewGuid(), Guid.NewGuid(), PlaybookRunPhase.Completed, 100, 1, 0, 0, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, FromDurableRecord: false);
        var runs = new Mock<IPlaybookRunAi>();
        runs.Setup(r => r.ReadResultsAsCallerAsync(It.IsAny<HttpContext>(), run.RunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookRunResults(run, PlaybookRunResultsState.None, null, null, null, new FaultingDocuments(), null));

        var result = await GetPlaybookRunResultsTool.RunAsync(
            PlaybookRunWorld.Args(new { runId = run.RunId }), world.CallerContext(), runs.Object, world.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        result.IsError.Should().BeTrue();
        Body(result).GetProperty("reasonCode").GetString().Should().Be(GetPlaybookRunTool.GetFailedReasonCode);
        Body(result).GetProperty("status").GetInt32().Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>A document list whose enumeration faults: any fault inside the result building, from the tool's point of view.</summary>
    private sealed class FaultingDocuments : IReadOnlyList<PlaybookRunSourceDocument>
    {
        public PlaybookRunSourceDocument this[int index] => throw new InvalidOperationException("fault (test)");

        public int Count => 1;

        public IEnumerator<PlaybookRunSourceDocument> GetEnumerator() => throw new InvalidOperationException("fault (test)");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ---- task 037 review F-D: the document list is capped before it is queried ------------------------------------------------------

    [Fact(DisplayName = "037 review F-D: a run with 60 documents queries and lists at most 20, reports the other 40, and stays under 64 KiB")]
    public async Task SixtyDocuments_AreCappedAtTwenty_UnderTheSizeBound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var anchor = world.Host.ReadableDocument();
        var others = Enumerable.Range(0, 59).Select(_ => Guid.NewGuid()).ToArray();
        var all = others.Prepend(anchor).ToArray();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), all);
        world.SeedAnalysisAnchor(analysisId, anchor);
        world.CallerReadsAnalysis(analysisId, anchor, string.Concat(Enumerable.Repeat("# Überschrift\n" + new string('é', 2_000) + "\n", 40)));
        world.CallerReadsDocuments(all.Select(id => (id, new string('文', 850))).ToArray());

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        Encoding.UTF8.GetByteCount(Serialized(result)).Should().BeLessThan(MaxResultBytes);
        var query = world.DocumentQueries.Should().ContainSingle().Subject;
        System.Text.RegularExpressions.Regex.Matches(query, "sprk_documentid eq ").Count.Should().BeLessThanOrEqualTo(PlaybookRunLimits.MaxDocuments);
        var body = Body(result);
        body.GetProperty("documents").GetArrayLength().Should().Be(PlaybookRunLimits.MaxDocuments);
        body.GetProperty("documents").EnumerateArray().First().GetProperty("documentId").GetString().Should().Be(anchor.ToString("D"));
        body.GetProperty("moreDocuments").GetInt32().Should().Be(40);
    }

    // ---- NFR-02: every result under 64 KiB ---------------------------------------------------------------------------------------

    [Fact(DisplayName = "037b size: a working document far past the cap, all non-ASCII, with 20 long-named documents, is cut under 64 KiB with a 'more in Spaarke' link")]
    public void HugeResult_IsTruncatedUnder64KiB_WithAMoreLink()
    {
        var analysisId = Guid.NewGuid();
        var link = Links.BuildAnalysisWorkspaceUrl(analysisId, Guid.NewGuid());
        var sb = new StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            sb.Append("# Überschrift \"<&>\" ").Append(i).Append(" 😀\n").Append(new string('é', 2_000)).Append('\n');
        }

        var documents = Enumerable.Range(0, 20)
            .Select(_ => new PlaybookRunSourceDocument(Guid.NewGuid(), new string('文', 850), Links.BuildConsoleUrl(Guid.NewGuid())))
            .ToArray();
        var run = new PlaybookRunView(Guid.NewGuid(), Guid.NewGuid(), PlaybookRunPhase.Completed, 100, 2, 0, 0, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, FromDurableRecord: false);
        var results = new PlaybookRunResults(run, PlaybookRunResultsState.Available, analysisId, new string('名', 5_000), sb.ToString(), documents, link);

        var result = GetPlaybookRunResultsTool.ResultOf(results, DateTimeOffset.UnixEpoch);

        Encoding.UTF8.GetByteCount(Serialized(result)).Should().BeLessThan(MaxResultBytes);
        var body = Body(result);
        body.GetProperty("truncated").GetBoolean().Should().BeTrue();
        body.GetProperty("moreLink").GetString().Should().Be(link, "a cut result opens the full analysis in Spaarke");
        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        sections.Should().NotBeEmpty();
        sections.Count.Should().BeLessThan(400);
        sections[^1].GetProperty("truncated").GetBoolean().Should().BeTrue("the section the cap fell in is marked");
        sections.SkipLast(1).Should().OnlyContain(s => !s.GetProperty("truncated").GetBoolean());
        body.GetProperty("documents").GetArrayLength().Should().Be(20);
        body.GetProperty("documents").EnumerateArray()
            .Should().OnlyContain(d => d.GetProperty("name").GetString()!.Length <= GetPlaybookRunResultsTool.DocumentNameMaxChars);
        body.GetProperty("playbookName").GetString()!.Length.Should().BeLessThanOrEqualTo(GetPlaybookRunResultsTool.HeadingMaxChars);
        Text(result).Should().NotContainAny("\r", "\n");
    }

    [Fact(DisplayName = "037b size: a document of many tiny sections stops at the section cap and is marked truncated")]
    public void ManySections_StopAtTheCap()
    {
        var document = string.Concat(Enumerable.Range(0, 200).Select(i => $"# H{i}\ntext {i}\n"));

        var (sections, truncated) = GetPlaybookRunResultsTool.Sections(document);

        sections.Should().HaveCount(GetPlaybookRunResultsTool.MaxSections);
        truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "037b size: the budget counts six bytes for every character the encoder may escape, and a cut never splits a surrogate pair")]
    public void JsonCost_IsWorstCase_AndTheCutKeepsSurrogatesWhole()
    {
        GetPlaybookRunResultsTool.JsonCost("abc").Should().Be(3);
        GetPlaybookRunResultsTool.JsonCost("é<\"\n").Should().Be(24);
        GetPlaybookRunResultsTool.JsonCost("😀").Should().Be(12);

        var (sections, truncated) = GetPlaybookRunResultsTool.Sections(string.Concat(Enumerable.Repeat("😀", 40_000)));

        truncated.Should().BeTrue();
        var text = sections.Single().Text;
        text.Length.Should().BePositive();
        char.IsHighSurrogate(text[^1]).Should().BeFalse("a cut never leaves half a surrogate pair");
        GetPlaybookRunResultsTool.JsonCost(text).Should().BeLessThanOrEqualTo(GetPlaybookRunResultsTool.SectionsBudgetBytes);
    }

    // ---- errors: isError results with the REST vocabulary -----------------------------------------------------------------------

    [Fact(DisplayName = "037b errors: a missing, extra or non-GUID runId is an isError result with the fix, before any read")]
    public async Task BadArguments_AreRefused()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var missing = await GetPlaybookRunResultsTool.RunAsync(PlaybookRunWorld.Args(new { }), world.CallerContext(), world.Runs, world.Time, logger, CancellationToken.None);
        var extra = await GetPlaybookRunResultsTool.RunAsync(
            PlaybookRunWorld.Args(new { runId = Guid.NewGuid(), analysisId = Guid.NewGuid() }), world.CallerContext(), world.Runs, world.Time, logger, CancellationToken.None);
        var notAGuid = await GetPlaybookRunResultsTool.RunAsync(PlaybookRunWorld.Args(new { runId = "run-1" }), world.CallerContext(), world.Runs, world.Time, logger, CancellationToken.None);

        Body(missing).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        Body(extra).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidArgumentReasonCode, "no caller argument names the analysis");
        Body(notAGuid).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidIdReasonCode);
        Body(notAGuid).GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        world.DataverseUser.Verify(d => d.GetAsync(It.Is<string>(p => p != "WhoAmI"), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact(DisplayName = "037b errors: a fault reading the analysis is 'could not read' (503, the status tool's code), never the not-found, and logs the type only")]
    public async Task Fault_IsCouldNotRead_NotTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerAnalysisReadRefused(PlaybookRunWorld.AnalysisId, status: 500);
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().BeTrue();
        Body(result).GetProperty("reasonCode").GetString().Should().Be(GetPlaybookRunTool.GetFailedReasonCode);
        Body(result).GetProperty("status").GetInt32().Should().Be(StatusCodes.Status503ServiceUnavailable);
        world.Logs.Entries.Should().Contain(e => e.Contains(GetPlaybookRunResultsTool.ToolName) && e.Contains("InvalidOperationException"));
    }

    // ---- logs: no content (ADR-015, NFR-05) ---------------------------------------------------------------------------------------

    [Fact(DisplayName = "037b logs: one line per call with the tool, outcome, size, sections and correlation id; no analysis text, name or document name")]
    public async Task Logs_CarrySizesNotContent()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, $"# {ContentMarker} heading\nThe text {ContentMarker}.");
        world.CallerReadsDocuments((documentId, $"{ContentMarker}.docx"));
        world.CallerReadsPlaybookName(playbookId, $"Playbook {ContentMarker}");
        var runId = await StartAndFinishAsync(world, playbookId, documentId);

        var result = await world.ResultsToolAsync(world.CallerContext(traceId: "trace-037b"), runId);

        Serialized(result).Should().Contain(ContentMarker, "control: the widget does receive the content");
        world.Logs.Entries.Should().NotContain(e => e.Contains(ContentMarker, StringComparison.Ordinal));
        world.Logs.Entries.Should().Contain(e => e.Contains(GetPlaybookRunResultsTool.ToolName) && e.Contains("bytes") && e.Contains("trace-037b"));
        world.Logs.Entries.Should().NotContain(e => e.Contains(PlaybookRunWorld.TokenSecret, StringComparison.Ordinal));
    }

    // ---- tools/list: app-only, read-only, the published shape ---------------------------------------------------------------------

    [Fact(DisplayName = "037b tools/list: the real BFF host publishes the tool app-only (out of the model's tool set), read-only, titled, with only runId")]
    public async Task RealHost_PublishesTheToolAppOnly()
    {
        using var factory = new McpProgramHostFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new("Bearer", McpProgramHostFactory.MintToken(Guid.Parse(PlaybookRunWorld.CallerOid)));

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var json = string.Join("\n", raw.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(raw.TrimStart().StartsWith('{') ? raw : json);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var tool = tools.Single(t => t.GetProperty("name").GetString() == GetPlaybookRunResultsTool.ToolName);

        var ui = tool.GetProperty("_meta").GetProperty("ui");
        ui.GetProperty("visibility").EnumerateArray().Select(v => v.GetString()).Should().Equal(["app"],
            "app-only: the host keeps it out of the model's tool set (ADR-015)");
        ui.TryGetProperty("resourceUri", out _).Should().BeFalse("the widget calls it; it renders no widget of its own");
        tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        var description = tool.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1_024);
        description.Should().Contain("Use for:").And.Contain("Not for:").And.Contain("JPS playbook");
        tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal(["runId"]);
        tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        // The two model-visible playbook tools point at the playbook run widget (part c, 037c) and carry nothing else in _meta: no visibility,
        // so they stay in the model's tool set. Their text and structuredContent are frozen in PlaybookRunToolsContractTests.
        foreach (var name in new[] { RunPlaybookTool.ToolName, GetPlaybookRunTool.ToolName })
        {
            var meta = tools.Single(t => t.GetProperty("name").GetString() == name).GetProperty("_meta");
            meta.EnumerateObject().Select(p => p.Name).Should().Equal(["ui"], name);
            meta.GetProperty("ui").EnumerateObject().Select(p => p.Name).Should().Equal(["resourceUri"], name);
            meta.GetProperty("ui").GetProperty("resourceUri").GetString().Should().Be(PlaybookRunWidgetResource.Uri, name);
        }
    }
}
