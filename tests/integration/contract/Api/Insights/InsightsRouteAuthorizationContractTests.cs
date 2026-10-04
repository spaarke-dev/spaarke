using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Insights;

/// <summary>
/// <b>Insights route authorization</b> — unified-access-control-r2 task 163 (sweep findings #17, #40, #41; owner round 16
/// items 1 and 3, round 25 item 3). Drives the REAL Insights route maps through <see cref="RouteSweepAuthorizationFixture"/>:
/// the subject record is authorized AS THE CALLER before <see cref="IInsightsAi"/> or the playbook cache is reached; an
/// unreadable and an absent subject get the identical uniform 404. On /ask the route runs only a playbook registered as
/// an insights-ask Binding, applies task 164's SHARED playbook-parameter policy, and asks for Write on the subject when a
/// node that can write reaches it ("Read suffices only for non-persisting playbooks"). On /assistant/query the caller's
/// Write on the subject is asked when a playbook may run, and the run carries the answer.
/// </summary>
[Trait("category", "authorization")]
public sealed class InsightsRouteAuthorizationContractTests : IClassFixture<RouteSweepAuthorizationFixture>
{
    private readonly RouteSweepAuthorizationFixture _fixture;

    private static readonly Guid Readable = Guid.Parse("16300000-0000-0000-0000-0000000c0001");
    private static readonly Guid Unreadable = Guid.Parse("16300000-0000-0000-0000-0000000c0002");
    private static readonly Guid Writable = Guid.Parse("16300000-0000-0000-0000-0000000c0003");
    private static readonly Guid OtherReadableMatter = Guid.Parse("16300000-0000-0000-0000-0000000c0004");
    private static readonly Guid OtherWritableMatter = Guid.Parse("16300000-0000-0000-0000-0000000c0005");
    private static readonly Guid ReadableProject = Guid.Parse("16300000-0000-0000-0000-0000000c0006");
    private static readonly Guid NonExistent = Guid.Parse("16300000-0000-0000-0000-0000000cffff");

    /// <summary>Bound as insights-ask "matter-health-single": PERSISTS onto the subject (its UpdateRecord recordId is {{matterId}}).</summary>
    private static readonly Guid MatterHealthPlaybook = Guid.Parse("16300000-0000-0000-0000-0000000d0001");

    /// <summary>Bound as insights-ask "predict-matter-cost": writes nothing (no writing node references the subject).</summary>
    private static readonly Guid PredictCostPlaybook = Guid.Parse("16300000-0000-0000-0000-0000000d0002");

    public InsightsRouteAuthorizationContractTests(RouteSweepAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
        _fixture.Access
            .Grant(Readable, AccessRights.Read)
            .Grant(Writable, AccessRights.Read | AccessRights.Write)
            .Grant(OtherReadableMatter, AccessRights.Read)
            .Grant(OtherWritableMatter, AccessRights.Read | AccessRights.Write)
            .Grant(ReadableProject, AccessRights.Read);

        BindInsightsAsk("matter-health-single", MatterHealthPlaybook);
        BindInsightsAsk("predict-matter-cost", PredictCostPlaybook);
        ArrangeNodes(MatterHealthPlaybook, MatterHealthSingleNodes());
        ArrangeNodes(PredictCostPlaybook, PredictMatterCostNodes());

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
    // POST /api/insights/ask — finding #17; owner round 16 items 1 and 3
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
        var unreadable = await AskAsync(Shape("predict-matter-cost", Unreadable));
        var absent = await AskAsync(Shape("predict-matter-cost", NonExistent));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(unreadable);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        (await unreadable.Content.ReadAsStringAsync()).Should().NotContain(Unreadable.ToString());
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_NonPersistingPlaybook_TheRightsQuestionIsReadOnTheMatter_AsTheCaller_AndTheRunCarriesNoWrite()
    {
        var response = await AskAsync(Shape("predict-matter-cost", Readable));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Read suffices for a playbook that cannot write to its subject");
        _fixture.Access.RecordChecks.Should().ContainSingle()
            .Which.Should().Be((RouteSweepAuthorizationFixture.CallerObjectId, "sprk_matters", Readable, RouteSweepAuthorizationFixture.BearerToken));
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == PredictCostPlaybook && !r.SubjectWriteAuthorized),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_PersistingPlaybook_AReaderWithoutWrite_GetsTheSameUniform404_AndNothingRuns()
    {
        var readerOnly = await AskAsync(Shape("matter-health-single", Readable));
        var absent = await AskAsync(Shape("matter-health-single", NonExistent));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(readerOnly);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(readerOnly))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent));
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_PersistingPlaybook_ACallerWithWrite_Runs_AndTheRunCarriesTheEstablishedWrite()
    {
        var response = await AskAsync(Shape("matter-health-single", Writable));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Access.RecordChecks.Should().ContainSingle().Which.RecordId.Should().Be(Writable);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == MatterHealthPlaybook && r.SubjectWriteAuthorized),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("no nodes (the Legacy run mode, which writes)")]
    [InlineData("an unclassifiable node that names the subject")]
    public async Task Ask_ANodeListThatCannotBeShownNotToWriteTheSubject_NeedsWrite(string shape)
    {
        ArrangeNodes(PredictCostPlaybook, shape.StartsWith("no nodes", StringComparison.Ordinal)
            ? []
            : [Node(null, "{\"recordId\":\"{{matterId}}\"}")]);

        var reader = await AskAsync(Shape("predict-matter-cost", Readable));
        var writer = await AskAsync(Shape("predict-matter-cost", Writable));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(reader);
        writer.StatusCode.Should().Be(HttpStatusCode.OK, shape);
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("null")]
    public async Task Ask_ANodeListThatCannotBeRead_IsTheUniform404_EvenForAWriter(string mode)
    {
        var setup = _fixture.Nodes.Setup(n => n.GetNodesAsync(PredictCostPlaybook, It.IsAny<CancellationToken>()));
        if (mode == "throws")
        {
            setup.ThrowsAsync(new InvalidOperationException("simulated Dataverse fault"));
        }
        else
        {
            setup.ReturnsAsync((PlaybookNodeDto[])null!);
        }

        var response = await AskAsync(Shape("predict-matter-cost", Writable));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
        _fixture.Access.RecordChecks.Should().BeEmpty("the rights cannot be decided, so none is asked");
        VerifyAskNeverRan();
    }

    [Theory]
    [InlineData("tenantId", "guid")]
    [InlineData("USERID", "guid")]
    [InlineData("run.userId", "guid")]
    [InlineData("userPreferences.locale", "en")]
    [InlineData("lookBackYears", "3")]
    [InlineData("timeWindowHours", "abc")]
    [InlineData("matterId", "not-a-guid")]
    [InlineData("matterDescription", "guid")]
    public async Task Ask_AParameterTheSharedPolicyRefuses_Is400_ItsBody_BeforeAnyLookupOrRightsQuery(string key, string value)
    {
        var parameterValue = value == "guid" ? Guid.NewGuid().ToString() : value;

        var response = await AskAsync(new
        {
            question = "predict-matter-cost",
            subject = $"matter:{Writable}",
            parameters = new Dictionary<string, string> { [key] = parameterValue },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("playbook.parameter-rejected",
            "/ask answers with the SAME body /execute and /run-playbook do — the shared policy, not a fork");
        body.GetProperty("detail").GetString().Should().Contain($"'{key}'");
        if (value == "guid")
        {
            body.GetRawText().Should().NotContain(parameterValue, "a refused record id is never echoed");
        }

        _fixture.Access.RecordChecks.Should().BeEmpty();
        _fixture.Routing.Verify(r => r.ResolveBindingAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAskNeverRan();
    }

    [Theory]
    [InlineData("predict-matter-cost", "projectId", "unreadable-project", false)]
    [InlineData("predict-matter-cost", "projectId", "readable-project", true)]
    [InlineData("predict-matter-cost", "matterId", "other-readable-matter", true)]
    [InlineData("matter-health-single", "matterId", "other-readable-matter", false)]
    [InlineData("matter-health-single", "matterId", "other-writable-matter", true)]
    public async Task Ask_ARecordParameterIsAuthorizedAsTheCaller_ByTheSharedRule(
        string playbook, string key, string record, bool allowed)
    {
        var recordId = record switch
        {
            "unreadable-project" => Unreadable,
            "readable-project" => ReadableProject,
            "other-readable-matter" => OtherReadableMatter,
            _ => OtherWritableMatter,
        };

        var response = await AskAsync(new
        {
            question = playbook,
            subject = $"matter:{Writable}",
            parameters = new Dictionary<string, string> { [key] = recordId.ToString() },
        });

        if (allowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
                It.Is<InsightsAgentRequest>(r => r.Parameters != null && r.Parameters[key] == recordId.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
            VerifyAskNeverRan();
        }

        _fixture.Access.RecordChecks.Select(c => (c.EntitySetName, c.RecordId))
            .Should().Contain((key == "projectId" ? "sprk_projects" : "sprk_matters", recordId), "the parameter's record is asked about, as the caller");
    }

    [Fact]
    public async Task Ask_TheSubjectsOwnMatterIdAndDeclaredParameters_ReachTheFacadeUnchanged_AndTheMatterIsAskedOnce()
    {
        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = Readable.ToString().ToUpperInvariant(),
            ["matterDescription"] = "IP licensing dispute",
            ["todayUtc"] = "2026-10-04",
        };

        var response = await AskAsync(new { question = "predict-matter-cost", subject = $"matter:{Readable}", parameters });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Access.RecordChecks.Should().ContainSingle("the subject's own matterId folds into the subject's check");
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Parameters != null
                && r.Parameters.Count == 3
                && r.Parameters["matterDescription"] == "IP licensing dispute"
                && r.Parameters["todayUtc"] == "2026-10-04"),
            It.IsAny<CancellationToken>()), Times.Once);
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

        var byGuid = await AskAsync(new { question = rawGuid.ToString(), subject = $"matter:{Writable}", parameters = new { } });
        var byUnknownName = await AskAsync(new { question = rawGuid.ToString().Replace('-', '_'), subject = $"matter:{Writable}", parameters = new { } });

        byGuid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        byUnknownName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var guidBody = await byGuid.Content.ReadFromJsonAsync<JsonElement>();
        guidBody.GetProperty("title").GetString().Should().Be("Bad Request");
        guidBody.GetProperty("detail").GetString().Should().Contain("registered as an enabled sprk_playbookconsumer row");
        _fixture.Access.RecordChecks.Should().BeEmpty();
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_RawPlaybookGuidBoundAsInsightsAsk_IsAccepted_AndThatPlaybookIsTheOneRun()
    {
        _fixture.Routing
            .Setup(r => r.GetBindingByPlaybookIdAsync(PredictCostPlaybook, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding { BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.InsightsAsk, PlaybookId = PredictCostPlaybook });

        var response = await AskAsync(new { question = PredictCostPlaybook.ToString(), subject = $"matter:{Readable}", parameters = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Nodes.Verify(n => n.GetNodesAsync(PredictCostPlaybook, It.IsAny<CancellationToken>()), Times.Once);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == PredictCostPlaybook), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_TheMatterFormsLiveShape_RunsForACallerWithWrite_AndIsTheUniform404ForAReader()
    {
        var writer = await AskAsync(Shape("matter-health-single", Writable));
        var reader = await AskAsync(Shape("matter-health-single", Readable));

        writer.StatusCode.Should().Be(HttpStatusCode.OK);
        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(reader);
        _fixture.InsightsAi.Verify(i => i.AnswerQuestionAsync(
            It.Is<InsightsAgentRequest>(r => r.Question == MatterHealthPlaybook && r.Subject == $"matter:{Writable}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_TheFacadesRunGuardRefusal_IsTheSameUniform404()
    {
        _fixture.InsightsAi
            .Setup(i => i.AnswerQuestionAsync(It.IsAny<InsightsAgentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SdapProblemException(
                InsightsAgentRequest.SubjectWriteRequiredCode, "Write on the subject is required", "x", 404));

        var refused = await AskAsync(Shape("predict-matter-cost", Readable));
        var absent = await AskAsync(Shape("predict-matter-cost", NonExistent));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(refused);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(refused))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent));
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task Ask_FailsClosed(string mode)
    {
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await AskAsync(Shape("matter-health-single", Writable), _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
        VerifyAskNeverRan();
    }

    [Fact]
    public async Task Ask_WithNoOid_Is401_AndNothingIsLookedUpOrRun()
    {
        var response = await AskAsync(Shape("matter-health-single", Writable), _fixture.CreateCallerClient(withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.Nodes.Verify(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Theory]
    [InlineData(null, "writer", true)]
    [InlineData(null, "reader", false)]
    [InlineData("playbook", "writer", true)]
    [InlineData("playbook", "reader", false)]
    public async Task Assistant_WhenAPlaybookMayRun_TheCallersWriteOnTheSubjectIsAskedAsTheCaller_AndCarried(
        string? forceMode, string caller, bool expected)
    {
        var subject = caller == "writer" ? Writable : Readable;

        var response = await _fixture.CreateCallerClient()
            .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{subject}", forceMode });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Read is the route's gate; Write only decides which playbooks may run");
        _fixture.Access.RecordChecks.Should().HaveCount(2, "Read, then the Write question — both as the caller")
            .And.AllSatisfy(c => c.Should().Be((RouteSweepAuthorizationFixture.CallerObjectId, "sprk_matters", subject, RouteSweepAuthorizationFixture.BearerToken)));
        _fixture.InsightsAi.Verify(i => i.AssistantQueryAsync(
            It.Is<AssistantQueryFacadeRequest>(r => r.SubjectWriteAuthorized == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("seam-throws-on-write")]
    [InlineData("no-bearer")]
    public async Task Assistant_TheWriteQuestion_FailsClosedToNoWrite(string mode)
    {
        if (mode == "no-bearer")
        {
            // Without a forwarded token the Read gate itself denies — no playbook question is reached.
            var denied = await _fixture.CreateCallerClient(withBearer: false)
                .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Writable}" });
            await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(denied);
            _fixture.InsightsAi.VerifyNoOtherCalls();
            return;
        }

        // The Read gate's question (check 1) is answered; the Write question (check 2) faults.
        _fixture.Access.ThrowFromCheckNumber = 2;
        var response = await _fixture.CreateCallerClient()
            .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Writable}" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a faulting Write question answers 'no Write', never an error or an allow");
        _fixture.Access.RecordChecks.Should().HaveCount(2);
        _fixture.InsightsAi.Verify(i => i.AssistantQueryAsync(
            It.Is<AssistantQueryFacadeRequest>(r => !r.SubjectWriteAuthorized), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assistant_ForceModeRag_AsksNoWriteQuestion_AndTheRunCarriesNoWrite()
    {
        var response = await _fixture.CreateCallerClient()
            .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Writable}", forceMode = "rag" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Access.RecordChecks.Should().ContainSingle("no playbook can run on the RAG path, so Write is never asked");
        _fixture.InsightsAi.Verify(i => i.AssistantQueryAsync(
            It.Is<AssistantQueryFacadeRequest>(r => !r.SubjectWriteAuthorized), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assistant_TheFacadesRunGuardRefusal_BeforeAnyFrame_IsTheUniform404(bool sse)
    {
        var refusal = new SdapProblemException(InsightsAgentRequest.SubjectWriteRequiredCode, "Write on the subject is required", "x", 404);
        _fixture.InsightsAi
            .Setup(i => i.AssistantQueryAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(refusal);
        _fixture.InsightsAi
            .Setup(i => i.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingStream(refusal, chunksFirst: 0));

        var client = _fixture.CreateCallerClient(accept: sse ? "text/event-stream" : null);
        var refused = await client.PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Readable}", forceMode = "playbook" });
        var absent = await client.PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{NonExistent}", forceMode = "playbook" });

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(refused);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(refused))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent));
    }

    [Fact]
    public async Task Assistant_TheFacadesRunGuardRefusal_MidStream_IsAnErrorFrame_WithItsCode_AndNoRecordId()
    {
        var refusal = new SdapProblemException(InsightsAgentRequest.SubjectWriteRequiredCode, "Write on the subject is required", "x", 404);
        _fixture.InsightsAi
            .Setup(i => i.AssistantQueryStreamAsync(It.IsAny<AssistantQueryFacadeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingStream(refusal, chunksFirst: 1));

        var response = await _fixture.CreateCallerClient(accept: "text/event-stream")
            .PostAsJsonAsync("/api/insights/assistant/query", new { query = "q", subject = $"matter:{Readable}" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("event: error").And.Contain(InsightsAgentRequest.SubjectWriteRequiredCode).And.Contain("data: [DONE]");
        body.Should().NotContain(Readable.ToString());
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

    private void BindInsightsAsk(string canonicalName, Guid playbookId) =>
        _fixture.Routing
            .Setup(r => r.ResolveBindingAsync(ConsumerTypes.InsightsAsk, canonicalName,
                It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.InsightsAsk,
                ConsumerCode = canonicalName, PlaybookId = playbookId,
            });

    private void ArrangeNodes(Guid playbookId, PlaybookNodeDto[] nodes) =>
        _fixture.Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(nodes);

    private static PlaybookNodeDto Node(ExecutorType? type, string configJson) =>
        new() { Id = Guid.NewGuid(), SprkExecutortype = type, ConfigJson = configJson, Name = type?.ToString() ?? "unclassified" };

    /// <summary>The executor shape of the repo's matter-health-single.playbook.json (the node kinds and the {{matterId}} uses).</summary>
    private static PlaybookNodeDto[] MatterHealthSingleNodes() =>
    [
        Node(ExecutorType.LiveFact, "{\"subject\":\"matter:{{matterId}}\"}"),
        Node(ExecutorType.QueryDataverse, "{\"fetchXml\":\"<condition attribute='sprk_matter' operator='eq' value='{{matterId}}' />\"}"),
        Node(ExecutorType.IndexRetrieve, "{\"filter\":\"matterId eq '{{matterId}}'\"}"),
        Node(ExecutorType.AgentService, "{\"tenantId\":\"{{tenantId}}\",\"templateParameters\":{\"matterId\":\"{{matterId}}\"}}"),
        Node(ExecutorType.ReturnInsightArtifact, "{\"subject\":\"matter:{{matterId}}\"}"),
        Node(ExecutorType.UpdateRecord, "{\"entityLogicalName\":\"sprk_matter\",\"recordId\":\"{{matterId}}\"}"),
    ];

    /// <summary>The executor shape of the repo's predict-matter-cost.playbook.json: no node that can write references {{matterId}}.</summary>
    private static PlaybookNodeDto[] PredictMatterCostNodes() =>
    [
        Node(ExecutorType.LiveFact, "{\"subject\":\"matter:{{matterId}}\"}"),
        Node(ExecutorType.IndexRetrieve, "{\"filter\":\"matterId eq '{{matterId}}'\"}"),
        Node(ExecutorType.EvidenceSufficiency, "{}"),
        Node(ExecutorType.AgentService, "{\"tenantId\":\"{{tenantId}}\"}"),
        Node(ExecutorType.GroundingVerify, "{}"),
        Node(ExecutorType.ReturnInsightArtifact, "{\"subject\":\"matter:{{matterId}}\"}"),
    ];

    private static object Shape(string question, Guid matterId) =>
        new { question, subject = $"matter:{matterId}", parameters = new { } };

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

    private static async IAsyncEnumerable<AssistantQueryChunk> ThrowingStream(
        Exception refusal, int chunksFirst, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        for (var i = 0; i < chunksFirst; i++)
        {
            yield return new AssistantQueryChunk { Type = "progress", Step = "classifier_started" };
        }

        throw refusal;
    }
}
