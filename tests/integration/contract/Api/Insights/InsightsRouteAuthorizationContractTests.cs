using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Insights;

/// <summary>
/// <b>Insights route authorization</b> — unified-access-control-r2 task 163 (sweep findings #17, #40, #41).
/// Drives the REAL Insights route maps through <see cref="RouteSweepAuthorizationFixture"/>: the subject
/// record is authorized for Read AS THE CALLER before <see cref="IInsightsAi"/> or the playbook cache is
/// reached; an unreadable and an absent subject get the identical uniform 404. On /ask the handler also
/// refuses raw playbook GUIDs that are not bound as insights-ask, and identifier parameters other than the
/// subject's own matterId.
/// </summary>
[Trait("category", "authorization")]
public sealed class InsightsRouteAuthorizationContractTests : IClassFixture<RouteSweepAuthorizationFixture>
{
    private readonly RouteSweepAuthorizationFixture _fixture;

    private static readonly Guid Readable = Guid.Parse("16300000-0000-0000-0000-0000000c0001");
    private static readonly Guid Unreadable = Guid.Parse("16300000-0000-0000-0000-0000000c0002");
    private static readonly Guid NonExistent = Guid.Parse("16300000-0000-0000-0000-0000000cffff");
    private static readonly Guid MatterHealthPlaybook = Guid.Parse("16300000-0000-0000-0000-0000000d0001");

    public InsightsRouteAuthorizationContractTests(RouteSweepAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
        _fixture.Access.Grant(Readable, AccessRights.Read);

        _fixture.Routing
            .Setup(r => r.ResolveBindingAsync(ConsumerTypes.InsightsAsk, "matter-health-single",
                It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.InsightsAsk,
                ConsumerCode = "matter-health-single", PlaybookId = MatterHealthPlaybook,
            });

        _fixture.InsightsAi
            .Setup(i => i.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsightsAgentResult.Declined(Decline(), cacheHit: false, processingTimeMs: 3));
        _fixture.InsightsAi
            .Setup(i => i.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsSearchFacadeResult { Query = "q" });
        _fixture.InsightsAi
            .Setup(i => i.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantQueryFacadeResult
            {
                Path = "rag", Answer = "answer", StructuredKind = "rag", IntentSource = "forceMode",
            });
        _fixture.InsightsAi
            .Setup(i => i.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(OneChunk());
    }

    // =====================================================================================================
    // POST /api/insights/ask — finding #17
    // =====================================================================================================

    [Theory]
    [InlineData("matter:not-a-guid")]
    [InlineData("matter:00000000-0000-0000-0000-000000000000")]
    public async Task Ask_SubjectIdThatIsNotAGuid_Is400_WithNoRightsQuery(string subject)
    {
        var response = await AskAsync(new { question = "matter-health-single", subject, parameters = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Access.RecordChecks.Should().BeEmpty();
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_UnreadableAndAbsentMatters_AreTheIdenticalUniform404_AndTheFacadeIsNeverReached()
    {
        var unreadable = await AskAsync(LiveShape(Unreadable));
        var absent = await AskAsync(LiveShape(NonExistent));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(unreadable);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        (await unreadable.Content.ReadAsStringAsync()).Should().NotContain(Unreadable.ToString());
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_TheRightsQuestionIsReadOnTheMatter_AsTheCaller()
    {
        await AskAsync(LiveShape(Readable));

        _fixture.Access.RecordChecks.Should().ContainSingle()
            .Which.Should().Be((RouteSweepAuthorizationFixture.CallerObjectId, "sprk_matters", Readable, RouteSweepAuthorizationFixture.BearerToken));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("other-consumer-type")]
    public async Task Ask_RawPlaybookGuidNotBoundAsInsightsAsk_IsTheSame400AsAnUnregisteredName(string mode)
    {
        var rawGuid = Guid.NewGuid();
        _fixture.Routing
            .Setup(r => r.GetBindingByPlaybookIdAsync(rawGuid, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mode == "absent" ? null : new Binding { BindingId = Guid.NewGuid(), ConsumerType = "ai-summary", PlaybookId = rawGuid });

        var byGuid = await AskAsync(new { question = rawGuid.ToString(), subject = $"matter:{Readable}", parameters = new { } });
        var byUnknownName = await AskAsync(new { question = rawGuid.ToString().Replace('-', '_'), subject = $"matter:{Readable}", parameters = new { } });

        byGuid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        byUnknownName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var guidBody = await byGuid.Content.ReadFromJsonAsync<JsonElement>();
        guidBody.GetProperty("title").GetString().Should().Be("Bad Request");
        guidBody.GetProperty("detail").GetString().Should().Contain("registered as an enabled sprk_playbookconsumer row");
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_RawPlaybookGuidBoundAsInsightsAsk_IsAccepted()
    {
        _fixture.Routing
            .Setup(r => r.GetBindingByPlaybookIdAsync(MatterHealthPlaybook, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding { BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.InsightsAsk, PlaybookId = MatterHealthPlaybook });

        var response = await AskAsync(new { question = MatterHealthPlaybook.ToString(), subject = $"matter:{Readable}", parameters = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == MatterHealthPlaybook), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("matterId", "other-matter")]
    [InlineData("tenantId", "x")]
    [InlineData("documentId", "x")]
    [InlineData("PROJECTID", "x")]
    public async Task Ask_IdentifierParameterOtherThanTheSubjectsOwn_Is400_ParametersNotAccepted(string key, string value)
    {
        var parameterValue = value == "other-matter" ? Unreadable.ToString() : Guid.NewGuid().ToString();

        var response = await AskAsync(new
        {
            question = "matter-health-single",
            subject = $"matter:{Readable}",
            parameters = new Dictionary<string, string> { [key] = parameterValue },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be("insights.parameters.not_accepted");
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_SubjectsOwnMatterIdAndTuningParameters_ReachTheFacadeUnchanged()
    {
        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = Readable.ToString().ToUpperInvariant(),
            ["lookBackYears"] = "3",
            ["currency"] = "USD",
            ["matterType"] = "IP licensing",
        };

        var response = await AskAsync(new { question = "matter-health-single", subject = $"matter:{Readable}", parameters });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Parameters != null
                && r.Parameters.Count == 4
                && r.Parameters["lookBackYears"] == "3"
                && r.Parameters["currency"] == "USD"
                && r.Parameters["matterType"] == "IP licensing"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_TheMatterFormsLiveShape_FromAReader_Is200()
    {
        var response = await AskAsync(LiveShape(Readable));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == MatterHealthPlaybook && r.Subject == $"matter:{Readable}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task Ask_FailsClosed(string mode)
    {
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await AskAsync(LiveShape(Readable), _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_WithNoOid_Is401_AndTheFacadeIsNeverReached()
    {
        var response = await AskAsync(LiveShape(Readable), _fixture.CreateCallerClient(withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyAskNeverRan();
    }

    // =====================================================================================================
    // POST /api/insights/assistant/query — finding #40 (single-shot AND SSE; forceMode playbook AND rag)
    // =====================================================================================================

    [Theory]
    [InlineData("matter", null, false)]
    [InlineData("project", null, false)]
    [InlineData("invoice", null, false)]
    [InlineData("matter", "playbook", false)]
    [InlineData("matter", "rag", false)]
    [InlineData("matter", null, true)]
    [InlineData("project", "playbook", true)]
    [InlineData("invoice", "rag", true)]
    public async Task Assistant_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_NoFacade_NoSseFrame(
        string scheme, string? forceMode, bool sse)
    {
        var client = _fixture.CreateCallerClient(accept: sse ? "text/event-stream" : null);

        var unreadable = await client.PostAsJsonAsync("/api/insights/assistant/query",
            new { query = "q", subject = $"{scheme}:{Unreadable}", forceMode });
        var absent = await client.PostAsJsonAsync("/api/insights/assistant/query",
            new { query = "q", subject = $"{scheme}:{NonExistent}", forceMode });

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(unreadable);
        unreadable.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "a denial is a ProblemDetails, never an opened event stream");
        (await unreadable.Content.ReadAsStringAsync()).Should().NotContain("event:");
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));

        _fixture.InsightsAi.Verify(i => i.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _fixture.InsightsAi.Verify(i => i.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("project", "sprk_projects")]
    [InlineData("invoice", "sprk_invoices")]
    public async Task Assistant_TheRightsQuestionNamesTheSubjectsEntitySet(string scheme, string entitySet)
    {
        await _fixture.CreateCallerClient().PostAsJsonAsync("/api/insights/assistant/query",
            new { query = "q", subject = $"{scheme}:{Unreadable}" });

        _fixture.Access.RecordChecks.Should().ContainSingle().Which.EntitySetName.Should().Be(entitySet);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assistant_AReader_GetsTodaysResponse(bool sse)
    {
        var response = await _fixture.CreateCallerClient(accept: sse ? "text/event-stream" : null)
            .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Readable}", forceMode = "rag" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (sse)
        {
            (await response.Content.ReadAsStringAsync()).Should().Contain("data: [DONE]");
        }
    }

    // =====================================================================================================
    // POST /api/insights/search — finding #41
    // =====================================================================================================

    [Theory]
    [InlineData("matter")]
    [InlineData("project")]
    [InlineData("invoice")]
    public async Task Search_UnreadableAndAbsentSubjects_AreTheIdenticalUniform404_AndSearchNeverRuns(string scheme)
    {
        var client = _fixture.CreateCallerClient();

        var unreadable = await client.PostAsJsonAsync("/api/insights/search", new { query = "q", subject = $"{scheme}:{Unreadable}" });
        var absent = await client.PostAsJsonAsync("/api/insights/search", new { query = "q", subject = $"{scheme}:{NonExistent}" });

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(unreadable);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        _fixture.InsightsAi.Verify(i => i.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Search_AReader_Gets200()
    {
        var response = await _fixture.CreateCallerClient()
            .PostAsJsonAsync("/api/insights/search", new { query = "q", subject = $"matter:{Readable}" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.InsightsAi.Verify(i => i.SearchAsync(It.IsAny<InsightsSearchFacadeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("assistant", "no-bearer")]
    [InlineData("assistant", "seam-throws")]
    [InlineData("search", "no-bearer")]
    [InlineData("search", "seam-throws")]
    public async Task AssistantAndSearch_FailClosed(string route, string mode)
    {
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await _fixture.CreateCallerClient(withBearer: mode != "no-bearer").PostAsJsonAsync(
            route == "assistant" ? "/api/insights/assistant/query" : "/api/insights/search",
            new { query = "q", subject = $"matter:{Readable}" });

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
        _fixture.InsightsAi.VerifyNoOtherCalls();
    }

    // =====================================================================================================
    // Harness
    // =====================================================================================================

    private static object LiveShape(Guid matterId) =>
        new { question = "matter-health-single", subject = $"matter:{matterId}", parameters = new { } };

    private Task<HttpResponseMessage> AskAsync(object body, HttpClient? client = null) =>
        (client ?? _fixture.CreateCallerClient()).PostAsJsonAsync("/api/insights/ask", body);

    private void VerifyAskNeverRan() =>
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

    private static DeclineResponse Decline() => new()
    {
        Reason = "insufficient-evidence",
        Explanation = "not enough",
        MinimumEvidenceNeeded = new Dictionary<string, object>(),
        SuggestedActions = [],
        ConfidenceInDecline = 0.9,
    };

    private static async IAsyncEnumerable<AssistantQueryChunk> OneChunk([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return new AssistantQueryChunk { Type = "progress", Step = "started" };
    }
}
