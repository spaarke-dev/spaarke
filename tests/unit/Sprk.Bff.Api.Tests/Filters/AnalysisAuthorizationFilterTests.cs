using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using AuthorizationResult = Sprk.Bff.Api.Services.Ai.AuthorizationResult;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// Unit tests for AnalysisAuthorizationFilter - authorization for Analysis endpoints.
/// </summary>
[Trait("status", "repaired")]
public class AnalysisAuthorizationFilterTests
{
    private readonly Mock<IAiAuthorizationService> _authServiceMock;
    private readonly Mock<ILogger<AnalysisAuthorizationFilter>> _loggerMock;

    public AnalysisAuthorizationFilterTests()
    {
        _authServiceMock = new Mock<IAiAuthorizationService>();
        _loggerMock = new Mock<ILogger<AnalysisAuthorizationFilter>>();
    }

    private AnalysisAuthorizationFilter CreateFilter(AuthorizationMode mode) =>
        new(_authServiceMock.Object, _loggerMock.Object, mode);

    private static AuthorizationResult AllowedResult(params Guid[] documentIds) =>
        AuthorizationResult.Authorized(documentIds.Length > 0 ? documentIds : new[] { Guid.NewGuid() });

    private static AuthorizationResult DeniedResult(string reason = "NO_ACCESS") =>
        AuthorizationResult.Denied(reason);

    /// <summary>
    /// A realistic authenticated caller: the supplied id is issued as the Entra <c>oid</c>, and a
    /// DIVERGENT, sub-shaped <see cref="ClaimTypes.NameIdentifier"/> is issued alongside it.
    /// </summary>
    /// <remarks>
    /// This helper used to mint <see cref="ClaimTypes.NameIdentifier"/> ONLY — a principal shape no
    /// Entra caller ever has, since a real token always carries <c>oid</c> and routes <c>sub</c> to
    /// NameIdentifier under inbound claim mapping. Because the filter read NameIdentifier, the tests
    /// passed; because the stub keyed on the same string, they could not tell a correct read from a
    /// broken one. The divergent value is load-bearing: if the resolver ever falls back to
    /// NameIdentifier again, it returns SubClaim, the stub does not match, and these tests fail.
    /// </remarks>
    private static ClaimsPrincipal CreateUser(string userId = "9d4f7a12-6c3b-4e58-b0d1-2a7f5e9c4813")
    {
        var claims = new List<Claim>
        {
            new("oid", userId),
            new(ClaimTypes.NameIdentifier, SubClaim)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Entra's pairwise <c>sub</c> — never a GUID, never a systemuser match.</summary>
    private const string SubClaim = "d12L59FRq8kZ0m2Xr7bTn4wPqLzYhVcJ8sNdEuRkjg";

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Mock<EndpointFilterInvocationContext> CreateContext(
        ClaimsPrincipal user,
        Dictionary<string, object?>? routeValues = null,
        params object[] arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };

        // Add route values if provided
        if (routeValues != null)
        {
            foreach (var kvp in routeValues)
            {
                httpContext.Request.RouteValues[kvp.Key] = kvp.Value;
            }
        }

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        contextMock.Setup(c => c.Arguments).Returns(arguments.ToList()!);

        return contextMock;
    }

    private static ValueTask<object?> NextDelegate(EndpointFilterInvocationContext context)
        => ValueTask.FromResult<object?>(Results.Ok("Success"));

    #region Authentication Tests

    [Fact]
    public async Task InvokeAsync_NoUserIdentity_Returns401()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var request = new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid()], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateAnonymousUser(), arguments: request);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_AnalysisMode_NoUserIdentity_Returns401()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = Guid.NewGuid().ToString() };
        var context = CreateContext(CreateAnonymousUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
    }

    #endregion

    #region DocumentAccess Mode Tests


    [Fact]
    public async Task DocumentAccess_UserWithoutAccess_Returns403()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task DocumentAccess_NoDocumentsInRequest_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var request = new AnalysisExecuteRequest { DocumentIds = [], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }


    [Fact]
    public async Task DocumentAccess_MultipleDocumentsPartialAccess_Returns403()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [docId1, docId2], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        // Partial authorization - only docId1 is authorized
        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Partial(new[] { docId1 }, "Access denied to some documents"));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task DocumentAccess_EmptyGuidArgument_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var context = CreateContext(CreateUser(), arguments: Guid.Empty);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region AnalysisAccess Mode Tests


    [Fact]
    public async Task AnalysisAccess_MissingAnalysisId_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var context = CreateContext(CreateUser()); // No route values

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AnalysisAccess_InvalidAnalysisIdFormat_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = "not-a-guid" };
        var context = CreateContext(CreateUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }


    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task DocumentAccess_AuthorizationThrows_Returns500()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Fail-closed mode switch (task 162)

    [Fact(DisplayName = "162: an AuthorizationMode with no case DENIES with 403 system_failure and never calls next")]
    public async Task UnhandledMode_Denies403_AndNeverCallsNext()
    {
        var filter = CreateFilter((AuthorizationMode)99);
        var context = CreateContext(CreateUser());
        var nextCalled = false;

        var result = await filter.InvokeAsync(context.Object, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("sdap.access.error.system_failure");
    }

    /// <summary>
    /// The request services for the evaluator-unavailable cases: the evaluator's <c>AuthorizationService</c> either
    /// absent, or registered with its <c>IAccessDataSource</c> dependency missing (so resolving it throws).
    /// </summary>
    private static IServiceProvider ServicesWithoutAnEvaluator(bool registeredButUnresolvable, Guid? profilePlaybookId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (registeredButUnresolvable)
        {
            services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
        }
        if (profilePlaybookId is { } playbookId)
        {
            var routing = new Mock<IConsumerRoutingService>();
            routing
                .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(playbookId);
            services.AddSingleton(routing.Object);
        }
        return services.BuildServiceProvider().CreateScope().ServiceProvider;
    }

    [Theory(DisplayName = "162: on /execute, an evaluator that cannot be resolved (not registered, or a dependency missing) DENIES 403 system_failure — not a 500, never next")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_EvaluatorUnavailable_Denies403SystemFailure(bool registeredButUnresolvable)
    {
        var playbookId = Guid.NewGuid();
        var filter = CreateFilter(AuthorizationMode.AnalysisRun);
        var request = new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid()], PlaybookId = playbookId };
        var context = CreateContext(CreateUser(), arguments: request);
        context.Object.HttpContext.RequestServices = ServicesWithoutAnEvaluator(registeredButUnresolvable, profilePlaybookId: playbookId);
        var nextCalled = false;

        var result = await filter.InvokeAsync(context.Object, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("sdap.access.error.system_failure");
    }

    [Theory(DisplayName = "162: on GET, an evaluator that cannot be resolved answers the uniform 404 — not a 500, never next")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_EvaluatorUnavailable_IsTheUniform404(bool registeredButUnresolvable)
    {
        var analysisId = Guid.NewGuid();
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var context = CreateContext(CreateUser(), new Dictionary<string, object?> { ["analysisId"] = analysisId.ToString() });
        context.Object.HttpContext.RequestServices = ServicesWithoutAnEvaluator(registeredButUnresolvable);
        var nextCalled = false;

        var result = await filter.InvokeAsync(context.Object, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("sdap.access.deny.record_unavailable");
        problem.ProblemDetails.Detail.Should().NotContain(analysisId.ToString());
    }

    #endregion

    #region AnalysisAccess anchor rule (task 162)

    [Theory(DisplayName = "162: an anchor whose type has no entity set REJECTS the whole analysis even when a readable document anchor sits beside it — it is never skipped")]
    [InlineData("sprk_regardingbudget", "sprk_budget")]
    [InlineData("sprk_regardingcommunication", "sprk_communication")]
    [InlineData("sprk_regardingservicerequest", "sprk_servicerequest")]
    public void BuildAnchorTargets_UnmappedAnchorBesideADocument_Rejects(string column, string target)
    {
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_analysis", Guid.NewGuid())
        {
            ["sprk_documentid"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", Guid.NewGuid()),
            [column] = new Microsoft.Xrm.Sdk.EntityReference(target, Guid.NewGuid()),
        };

        var targets = AnalysisAuthorizationFilter.BuildAnchorTargets(row, new DefaultHttpContext(), logger: null);

        targets.Rejection.Should().NotBeNull("a skipped unmapped anchor would leave only the document check, which a document reader passes");
        targets.Checks.Should().BeEmpty();
    }

    /// <summary>
    /// EVERY Lookup attribute the live metadata sweep returned for sprk_analysis (spaarkedev1,
    /// EntityDefinitions(LogicalName='sprk_analysis')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata,
    /// 2026-10-03), with its targets. Recorded in notes/task-162-ai-analysis-route-authorization.md section 1.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LiveLookupSnapshot = new Dictionary<string, string>
    {
        ["createdby"] = "systemuser",
        ["createdonbehalfby"] = "systemuser",
        ["modifiedby"] = "systemuser",
        ["modifiedonbehalfby"] = "systemuser",
        ["ownerid"] = "systemuser,team",
        ["owningbusinessunit"] = "businessunit",
        ["owningteam"] = "team",
        ["owninguser"] = "systemuser",
        ["sprk_actionid"] = "sprk_analysisaction",
        ["sprk_agreementtype"] = "sprk_agreementtype",
        ["sprk_assignedattorney1"] = "contact",
        ["sprk_assignedattorney2"] = "contact",
        ["sprk_assignedparalegal1"] = "contact",
        ["sprk_assignedparalegal2"] = "contact",
        ["sprk_documentid"] = "sprk_document",
        ["sprk_outputfileid"] = "sprk_document",
        ["sprk_playbook"] = "sprk_analysisplaybook",
        ["sprk_regardingbudget"] = "sprk_budget",
        ["sprk_regardingcommunication"] = "sprk_communication",
        ["sprk_regardingdocument"] = "sprk_document",
        ["sprk_regardinginvoice"] = "sprk_invoice",
        ["sprk_regardingmatter"] = "sprk_matter",
        ["sprk_regardingproject"] = "sprk_project",
        ["sprk_regardingrecordtype"] = "sprk_recordtype_ref",
        ["sprk_regardingservicerequest"] = "sprk_servicerequest",
        ["sprk_regardingworkassignment"] = "sprk_workassignment",
        ["sprk_reviewerby"] = "systemuser",
    };

    [Fact(DisplayName = "162: every live sprk_analysis Lookup is classified exactly once, with its live target; none is unclassified")]
    public void EveryLiveAnalysisLookup_IsClassifiedExactlyOnce()
    {
        var table = AnalysisAuthorizationFilter.LookupColumns;

        table.Select(c => c.Column).Should().OnlyHaveUniqueItems();
        table.Select(c => c.Column).Should().BeEquivalentTo(LiveLookupSnapshot.Keys,
            "a Lookup added to sprk_analysis must be classified as anchor or non-anchor before it can be trusted");
        foreach (var column in table)
        {
            column.TargetLogicalName.Should().Be(LiveLookupSnapshot[column.Column], column.Column);
            column.Reason.Should().NotBeNullOrWhiteSpace(column.Column);
        }
    }

    [Fact(DisplayName = "162: the anchor retrieve selects only anchor columns that exist live; every lookup to a sprk_document is a document anchor")]
    public void AnchorRetrieve_SelectsOnlyLiveAnchorColumns()
    {
        var anchors = AnalysisAuthorizationFilter.LookupColumns
            .Where(c => c.Role != AnalysisLookupRole.NotAnchor)
            .Select(c => c.Column);

        AnalysisAuthorizationFilter.AnchorColumns.Should().BeEquivalentTo(anchors);
        AnalysisAuthorizationFilter.AnchorColumns.Should().OnlyContain(c => LiveLookupSnapshot.ContainsKey(c));
        AnalysisAuthorizationFilter.AnchorColumns.Should().Contain(new[]
        {
            "sprk_documentid", "sprk_outputfileid", "sprk_regardingdocument", "sprk_regardingmatter",
            "sprk_regardingproject", "sprk_regardingworkassignment", "sprk_regardinginvoice",
        });
        AnalysisAuthorizationFilter.LookupColumns
            .Where(c => c.TargetLogicalName == "sprk_document")
            .Should().OnlyContain(c => c.Role == AnalysisLookupRole.DocumentAnchor);
    }

    [Fact(DisplayName = "162: the Create privilege on sprk_analysis is pinned to the name live privileges returned")]
    public void CreateAnalysisPrivilege_IsTheLiveName()
    {
        // spaarkedev1 privileges?$filter=startswith(name,'prvCreatesprk_analysis'), 2026-10-03.
        AnalysisAuthorizationFilter.CreateAnalysisPrivilege.Should().Be("prvCreatesprk_analysis");
        AnalysisAuthorizationFilter.AnalysisEntitySetLabel.Should().Be("sprk_analysises");
    }

    [Fact(DisplayName = "162 f1: the analysis-read rule's ONE retrieve selects the anchors plus createdby — every column live; the stamped person is never in it")]
    public void ReadRuleRetrieve_SelectsAnchorsPlusCreatedBy_AllLive()
    {
        AnalysisAuthorizationFilter.ReadRuleColumns.Should().BeEquivalentTo(
            AnalysisAuthorizationFilter.AnchorColumns.Append("createdby"));
        AnalysisAuthorizationFilter.ReadRuleColumns.Should().OnlyContain(c => LiveLookupSnapshot.ContainsKey(c),
            "a selected column that does not exist live faults every read and denies every honest caller");
        AnalysisAuthorizationFilter.ReadRuleColumns.Should().NotContain(Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName,
            "sprk_createdbyperson does not exist on sprk_analysis until task 146's schema script runs — it is read in its own query");
    }

    [Fact(DisplayName = "162 f1: the personal branch's Read privilege, the scope entity sets and the creator column are pinned to their live names")]
    public void PersonalAndCreateNames_AreTheLiveNames()
    {
        // spaarkedev1, 2026-10-04: privileges(name) and EntityDefinitions.EntitySetName.
        AnalysisAuthorizationFilter.ReadAnalysisPrivilege.Should().Be("prvReadsprk_analysis");
        AnalysisAuthorizationFilter.SkillEntitySet.Should().Be("sprk_analysisskills");
        AnalysisAuthorizationFilter.KnowledgeEntitySet.Should().Be("sprk_analysisknowledges");
        AnalysisAuthorizationFilter.ToolEntitySet.Should().Be("sprk_analysistools");
        Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName.Should().Be("sprk_createdbyperson");
    }

    #endregion

    #region Playbook-use decision (task 162; shared with task 164)

    [Fact(DisplayName = "162: a PUBLIC playbook needs no check")]
    public async Task PlaybookUse_Public_NeedsNoCheck()
    {
        var id = Guid.NewGuid();
        var playbooks = new Mock<IPlaybookService>();
        playbooks.Setup(p => p.GetPlaybookAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = id, IsPublic = true });

        var check = await PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(playbooks.Object, id, "body.playbookId", CancellationToken.None);

        check.Should().BeNull();
    }

    [Theory(DisplayName = "162: a non-public or unknown playbook gets a Record-path Read check on its own row; Dataverse decides and the owner id is never consulted")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlaybookUse_NonPublicOrUnknown_IsARecordReadCheck(bool exists)
    {
        var id = Guid.NewGuid();
        var playbooks = new Mock<IPlaybookService>();
        playbooks.Setup(p => p.GetPlaybookAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists ? new PlaybookResponse { Id = id, IsPublic = false, OwnerId = Guid.NewGuid() } : null);

        var check = await PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(playbooks.Object, id, "body.playbookId", CancellationToken.None);

        check.Should().BeEquivalentTo(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = "sprk_analysisplaybooks",
            RecordId = id,
            Operation = "read",
            Source = "body.playbookId",
        });
    }

    [Fact(DisplayName = "162: a playbook lookup fault propagates (every caller denies on it)")]
    public async Task PlaybookUse_LookupFault_Propagates()
    {
        var playbooks = new Mock<IPlaybookService>();
        playbooks.Setup(p => p.GetPlaybookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));

        var act = () => PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(playbooks.Object, Guid.NewGuid(), "s", CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    #endregion

    #region Run write rule (task 162)

    public static TheoryData<string, PlaybookNodeDto[], bool> RunShapes => new()
    {
        { "zero nodes (Legacy mode writes the profile)", Array.Empty<PlaybookNodeDto>(), true },
        { "a node with no executor type", new[] { new PlaybookNodeDto { SprkExecutortype = null } }, true },
        { "an inactive side-effecting node", new[] { new PlaybookNodeDto { SprkExecutortype = ExecutorType.AiAnalysis }, new PlaybookNodeDto { SprkExecutortype = ExecutorType.DeliverToIndex, IsActive = false } }, true },
        { "only read-only nodes", new[] { new PlaybookNodeDto { SprkExecutortype = ExecutorType.Start }, new PlaybookNodeDto { SprkExecutortype = ExecutorType.AiAnalysis } }, false },
    };

    [Theory(DisplayName = "162: a run can write the documents when it has no nodes, an unclassifiable node, or any side-effecting node")]
    [MemberData(nameof(RunShapes))]
    public void RunCanWriteDocuments_FollowsTheNodeList(string shape, PlaybookNodeDto[] nodes, bool expected)
    {
        AnalysisAuthorizationFilter.RunCanWriteDocuments(nodes).Should().Be(expected, shape);
    }

    #endregion
}

/// <summary>
/// Tests for AuthorizationMode enum.
/// </summary>
public class AuthorizationModeTests
{
    [Fact]
    public void AuthorizationMode_HasExpectedValues()
    {
        // Assert
        ((int)AuthorizationMode.DocumentAccess).Should().Be(0);
        ((int)AuthorizationMode.AnalysisAccess).Should().Be(1);
        ((int)AuthorizationMode.AnalysisPromote).Should().Be(2);
        ((int)AuthorizationMode.AnalysisRun).Should().Be(3);
        ((int)AuthorizationMode.AnalysisCreate).Should().Be(4);
    }
}
