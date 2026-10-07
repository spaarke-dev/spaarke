using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.ServiceModel;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;
using FinanceAuthz = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Real-host authorization contract for every route under <c>/api/ai/analysis</c> (unified-access-control-r2
/// task 162: route authorization sweep findings #2 GET, #22 promote, #52 execute; GitHub #233 item 1).
/// </summary>
/// <remarks>
/// <para><b>Reach.</b> Each test goes through the REAL <c>MapAnalysisEndpoints</c> in an in-process host, with the
/// REAL <see cref="AuthorizationService"/> + <see cref="OperationAccessRule"/> and the REAL
/// <see cref="AiAuthorizationService"/>, substituted only at the access-data boundary (task 130's
/// <see cref="FinanceAuthz.RecordingAccessDataSource"/>) and the privilege seam (<see cref="FinanceAuthz.RecordingPrivilegeProbe"/>),
/// plus module-boundary doubles for the handlers' services (ADR-038). RouteAuthorizationGuardTests credits these
/// routes by filter NAME, which is why it could not see that four of the seven decided nothing; this class is the
/// proof that they decide now, and <see cref="EveryMappedAnalysisRoute_HasADenyCase_AndDeniesACallerWithNoRights"/>
/// fails when a new route under the group has no deny case.</para>
/// <para>/fork, /{analysisId}/save and /{analysisId}/export were DELETED by task 162 (owner round 10 item 1: no
/// caller in the repo, not in any published API description).</para>
/// </remarks>
public class AnalysisEndpointsAuthorizationContractTests
{
    private const string Documents = "sprk_documents";   // live EntityDefinitions, 2026-10-03
    private const string Matters = "sprk_matters";       // live EntityDefinitions, 2026-10-03
    private const string Projects = "sprk_projects";     // live EntityDefinitions, 2026-10-03
    private const string WorkAssignments = "sprk_workassignments"; // live EntityDefinitions, 2026-10-03
    private const string Invoices = "sprk_invoices";     // live EntityDefinitions, 2026-10-03
    private const string Playbooks = "sprk_analysisplaybooks"; // live EntityDefinitions, 2026-10-03
    private const string CreateAnalysisPrivilege = "prvCreatesprk_analysis"; // live privileges(name), 2026-10-03

    private const AccessRights ReadAppendTo = AccessRights.Read | AccessRights.AppendTo;
    private const AccessRights FullRights = AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;

    // =============================================================================================
    // GET /api/ai/analysis/{analysisId} — finding #2
    // =============================================================================================

    [Fact(DisplayName = "162 GET: Read on EVERY populated anchor returns 200 with the service's payload; each anchor is asked on its own path and set")]
    public async Task Get_ReadOnEveryAnchor_Returns200AndAsksEachAnchorOnItsPath()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var anchors = new Dictionary<string, (string Target, Guid Id)>
        {
            ["sprk_documentid"] = ("sprk_document", Guid.NewGuid()),
            ["sprk_outputfileid"] = ("sprk_document", Guid.NewGuid()),
            ["sprk_regardingdocument"] = ("sprk_document", Guid.NewGuid()),
            ["sprk_regardingmatter"] = ("sprk_matter", Guid.NewGuid()),
            ["sprk_regardingproject"] = ("sprk_project", Guid.NewGuid()),
            ["sprk_regardingworkassignment"] = ("sprk_workassignment", Guid.NewGuid()),
            ["sprk_regardinginvoice"] = ("sprk_invoice", Guid.NewGuid()),
        };
        host.SeedAnalysis(analysisId, anchors);
        host.Access.Grant(Documents, anchors["sprk_documentid"].Id, AccessRights.Read);
        host.Access.Grant(Documents, anchors["sprk_outputfileid"].Id, AccessRights.Read);
        host.Access.Grant(Documents, anchors["sprk_regardingdocument"].Id, AccessRights.Read);
        host.Access.Grant(Matters, anchors["sprk_regardingmatter"].Id, AccessRights.Read);
        host.Access.Grant(Projects, anchors["sprk_regardingproject"].Id, AccessRights.Read);
        host.Access.Grant(WorkAssignments, anchors["sprk_regardingworkassignment"].Id, AccessRights.Read);
        host.Access.Grant(Invoices, anchors["sprk_regardinginvoice"].Id, AccessRights.Read);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed", WorkingDocument = "# draft" });

        var response = await host.SendAsync(Get(analysisId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["workingDocument"]!.GetValue<string>().Should().Be("# draft");
        host.Access.Calls.Should().BeEquivalentTo(new[]
        {
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, anchors["sprk_documentid"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, anchors["sprk_outputfileid"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, anchors["sprk_regardingdocument"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Matters, anchors["sprk_regardingmatter"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Projects, anchors["sprk_regardingproject"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, WorkAssignments, anchors["sprk_regardingworkassignment"].Id, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Invoices, anchors["sprk_regardinginvoice"].Id, true),
        });
    }

    [Fact(DisplayName = "162 GET: a readable document and an unreadable regarding matter is DENIED (AND, not OR) with the uniform 404")]
    public async Task Get_ReadableDocumentUnreadableMatter_IsUniform404()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new()
        {
            ["sprk_documentid"] = ("sprk_document", documentId),
            ["sprk_regardingmatter"] = ("sprk_matter", matterId),
        });
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Get(analysisId));

        await AssertUniform404Async(response, analysisId);
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Matters, matterId, true));
        host.Orchestration.VerifyNoOtherCalls();
    }

    public static TheoryData<string> GetDenyCases => new()
    {
        "unknown-id", "denied-document", "no-anchor", "polymorphic-pair-only", "anchor-read-faults",
        "anchor-type-without-entity-set", "no-bearer-token",
    };

    [Theory(DisplayName = "162 GET: unknown, denied, no-anchor, pair-only, faulting, unmapped-type and token-less requests are ONE uniform 404, never echoing the id, service never invoked")]
    [MemberData(nameof(GetDenyCases))]
    public async Task Get_DenyCases_AreTheSameUniform404(string denyCase)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var withToken = true;
        switch (denyCase)
        {
            case "unknown-id":
                host.EntityService
                    .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new FaultException<OrganizationServiceFault>(
                        new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")));
                break;
            case "denied-document":
                host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", Guid.NewGuid()) });
                break;
            case "no-anchor":
                host.SeedAnalysis(analysisId, new());
                break;
            case "polymorphic-pair-only":
                host.SeedAnalysis(analysisId, new(), entity =>
                {
                    entity["sprk_regardingrecordid"] = Guid.NewGuid().ToString();
                    entity["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid());
                });
                break;
            case "anchor-read-faults":
                host.EntityService
                    .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new TimeoutException("Dataverse unavailable"));
                break;
            case "anchor-type-without-entity-set":
                var budgetId = Guid.NewGuid();
                host.SeedAnalysis(analysisId, new() { ["sprk_regardingbudget"] = ("sprk_budget", budgetId) });
                host.Access.Grant("sprk_budgets", budgetId, FullRights);
                break;
            case "no-bearer-token":
                var documentId = Guid.NewGuid();
                host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
                host.Access.Grant(Documents, documentId, FullRights);
                withToken = false;
                break;
        }

        var response = await host.SendAsync(Get(analysisId, withToken));

        await AssertUniform404Async(response, analysisId);
        host.Orchestration.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "162 GET: a READABLE document anchor beside an anchor whose type has no entity set (budget, communication, service request) is still the uniform 404 — the unmapped anchor is rejected, never skipped")]
    [InlineData("sprk_regardingbudget", "sprk_budget", "sprk_budgets")]
    [InlineData("sprk_regardingcommunication", "sprk_communication", "sprk_communications")]
    [InlineData("sprk_regardingservicerequest", "sprk_servicerequest", "sprk_servicerequests")]
    public async Task Get_ReadableDocumentBesideAnAnchorWithoutEntitySet_IsUniform404(string column, string target, string guessedSet)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var unmappedId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new()
        {
            ["sprk_documentid"] = ("sprk_document", documentId),
            [column] = (target, unmappedId),
        });
        // The caller can read the document, and would pass even a guessed entity set for the other anchor: only the
        // reject-not-skip rule stands between this caller and the analysis.
        host.Access.Grant(Documents, documentId, FullRights);
        host.Access.Grant(guessedSet, unmappedId, FullRights);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed", WorkingDocument = "# draft" });

        var response = await host.SendAsync(Get(analysisId));

        await AssertUniform404Async(response, analysisId);
        host.Orchestration.Verify(o => o.GetAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "162 GET: an analysis deleted between the check and the read answers the SAME uniform 404 (handler KeyNotFoundException)")]
    public async Task Get_HandlerKeyNotFoundAfterTheCheck_IsTheSameUniform404()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
        host.Access.Grant(Documents, documentId, AccessRights.Read);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"Analysis {analysisId} not found"));

        var response = await host.SendAsync(Get(analysisId));

        await AssertUniform404Async(response, analysisId);
    }

    // =============================================================================================
    // GET — an analysis with NO anchor is PERSONAL (owner round 15 item 4; task 162 f1)
    // =============================================================================================

    private const string ReadAnalysisPrivilege = "prvReadsprk_analysis"; // live privileges(name), 2026-10-04
    private static readonly Guid CallerSystemUserId = Guid.Parse("d6f8f439-0000-4000-8000-000000000162");
    private static readonly Guid BffApplicationUserId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000162");

    [Fact(DisplayName = "162 f1 GET: a no-anchor analysis whose createdby IS the caller's systemuserid (WhoAmI) is 200 — the creator needs only the Read privilege, never a row Read")]
    public async Task Get_NoAnchor_CreatorByCreatedBy_Is200()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
        host.Probe.CallerSystemUserId = CallerSystemUserId;
        host.Probe.Hold(ReadAnalysisPrivilege);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed", WorkingDocument = "# mine" });

        var response = await host.SendAsync(Get(analysisId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["workingDocument"]!.GetValue<string>().Should().Be("# mine");
        host.Probe.Calls.Should().ContainSingle(c => c.Privilege == ReadAnalysisPrivilege && c.HasToken);
        host.Access.Calls.Should().BeEmpty("the personal branch never asks Dataverse for a row Read on the analysis");
    }

    [Fact(DisplayName = "162 f1 GET: a no-anchor analysis the BFF created as the application is the caller's when its server-stamped sprk_createdbyperson is the caller (task 146) — 200")]
    public async Task Get_NoAnchor_CreatorByStampedPerson_Is200()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new(), e =>
        {
            e["createdby"] = new EntityReference("systemuser", BffApplicationUserId);
            e["sprk_createdbyperson"] = new EntityReference("systemuser", CallerSystemUserId);
        });
        host.Probe.CallerSystemUserId = CallerSystemUserId;
        host.Probe.Hold(ReadAnalysisPrivilege);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed", WorkingDocument = "# asked for" });

        var response = await host.SendAsync(Get(analysisId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Access.Calls.Should().BeEmpty();
    }

    public static TheoryData<string> PersonalDenyCases => new()
    {
        "a-colleague-created-it", "app-created-no-person-recorded", "person-column-not-yet-deployed",
        "caller-systemuserid-unresolvable", "creator-without-the-read-privilege", "createdby-equals-the-callers-entra-oid",
        "creator-identity-read-faults",
    };

    [Theory(DisplayName = "162 f1 GET: a no-anchor analysis is the uniform 404 for anyone but its creator, and whenever the creator cannot be verified — never by the Entra oid, never by a row Read")]
    [MemberData(nameof(PersonalDenyCases))]
    public async Task Get_NoAnchor_NotTheVerifiedCreator_IsUniform404(string denyCase)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        host.Probe.CallerSystemUserId = CallerSystemUserId;
        host.Probe.Hold(ReadAnalysisPrivilege);
        switch (denyCase)
        {
            case "a-colleague-created-it":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", Guid.NewGuid()));
                break;
            case "app-created-no-person-recorded":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", BffApplicationUserId));
                break;
            case "person-column-not-yet-deployed":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", BffApplicationUserId));
                host.CreatorPersonColumnMissing(analysisId);
                break;
            case "caller-systemuserid-unresolvable":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
                host.Probe.CallerSystemUserId = null;
                break;
            case "creator-without-the-read-privilege":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
                host.Probe.Release(ReadAnalysisPrivilege);
                break;
            case "createdby-equals-the-callers-entra-oid":
                // The creator column holds the caller's Entra OID (a different GUID space). A rule that compared the oid
                // would admit this caller; the systemuserid rule must not.
                host.SeedAnalysis(analysisId, new(), e =>
                    e["createdby"] = new EntityReference("systemuser", Guid.Parse(AnalysisAuthzTestAuthHandler.CallerObjectId)));
                break;
            case "creator-identity-read-faults":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
                host.Probe.ThrowOnEveryCall = new TimeoutException("WhoAmI unavailable");
                break;
        }

        var response = await host.SendAsync(Get(analysisId));

        await AssertUniform404Async(response, analysisId);
        host.Orchestration.VerifyNoOtherCalls();
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_analysises", "never a row Read on the analysis");
    }

    [Fact(DisplayName = "162 f1 GET: an ANCHORED analysis is decided by its anchors alone — its creator gets no bypass")]
    public async Task Get_Anchored_CreatorGetsNoBypass()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", Guid.NewGuid()) },
            e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
        host.Probe.CallerSystemUserId = CallerSystemUserId;
        host.Probe.Hold(ReadAnalysisPrivilege);

        var response = await host.SendAsync(Get(analysisId));

        await AssertUniform404Async(response, analysisId);
        host.Probe.SystemUserIdCalls.Should().Be(0, "the personal branch applies only to an analysis with no anchor");
    }

    [Theory(DisplayName = "162 f1: the shared analysis-read evaluation (task 164's chat analysis host) decides exactly as GET — anchored, personal creator, colleague")]
    [InlineData("anchored-readable", true)]
    [InlineData("anchored-unreadable", false)]
    [InlineData("personal-creator", true)]
    [InlineData("personal-colleague", false)]
    public async Task IsAnalysisReadable_DecidesExactlyAsGet(string shape, bool expected)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        host.Probe.CallerSystemUserId = CallerSystemUserId;
        host.Probe.Hold(ReadAnalysisPrivilege);
        switch (shape)
        {
            case "anchored-readable":
                host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
                host.Access.Grant(Documents, documentId, AccessRights.Read);
                break;
            case "anchored-unreadable":
                host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
                break;
            case "personal-creator":
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", CallerSystemUserId));
                break;
            default:
                host.SeedAnalysis(analysisId, new(), e => e["createdby"] = new EntityReference("systemuser", Guid.NewGuid()));
                break;
        }

        var readable = await host.WithCallerContextAsync(ctx => AnalysisAuthorizationFilter.IsAnalysisReadableAsync(ctx, analysisId, logger: null));

        readable.Should().Be(expected, shape);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed" });
        var viaGet = await host.SendAsync(Get(analysisId));
        (viaGet.StatusCode == HttpStatusCode.OK).Should().Be(expected, "the chat host and GET share ONE rule");
    }

    [Fact(DisplayName = "162 f1: the shared evaluation denies when the evaluator cannot be resolved — never an allow")]
    public async Task IsAnalysisReadable_EvaluatorUnavailable_IsFalse()
    {
        await using var host = await AnalysisAuthHost.StartAsync(services =>
            services.AddScoped<AuthorizationService>(_ => throw new InvalidOperationException("evaluator dependency missing")));
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
        host.Access.Grant(Documents, documentId, AccessRights.Read);

        var readable = await host.WithCallerContextAsync(ctx => AnalysisAuthorizationFilter.IsAnalysisReadableAsync(ctx, analysisId, logger: null));

        readable.Should().BeFalse();
        host.Access.Calls.Should().BeEmpty();
    }

    // =============================================================================================
    // POST /api/ai/analysis/create — G5, matching promote (owner/main-session round 25 item 2; task 162 f1)
    // =============================================================================================

    private const string Skills = "sprk_analysisskills";         // live EntityDefinitions, 2026-10-04
    private const string Knowledge = "sprk_analysisknowledges";  // live EntityDefinitions, 2026-10-04
    private const string Tools = "sprk_analysistools";           // live EntityDefinitions, 2026-10-04

    private static HttpRequestMessage Create(object body) => Json(HttpMethod.Post, "/api/ai/analysis/create", body);

    [Fact(DisplayName = "162 f1 create: with the Create privilege, Read+AppendTo on the document and Read on every scope row is 201, and the new row is anchored to the document")]
    public async Task Create_WithTheG5Rights_Is201AndAnchorsTheDocument()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var skill = Guid.NewGuid();
        var knowledge = Guid.NewGuid();
        var tool = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Access.Grant(Skills, skill, AccessRights.Read);
        host.Access.Grant(Knowledge, knowledge, AccessRights.Read);
        host.Access.Grant(Tools, tool, AccessRights.Read);
        var playbookId = host.PublicPlaybook();

        var response = await host.SendAsync(Create(new
        {
            name = "Analysis - NDA.pdf", documentId, playbookId,
            skillIds = new[] { skill }, knowledgeIds = new[] { knowledge }, toolIds = new[] { tool },
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Analysis.Verify(a => a.CreateAnalysisAsync(documentId, "Analysis - NDA.pdf", playbookId, null, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once, "the analysis created in a document's context records that document as its anchor");
        host.Analysis.Verify(a => a.AssociateScopesAsync(It.IsAny<Guid>(),
            It.Is<IEnumerable<Guid>>(s => s.Single() == skill), It.Is<IEnumerable<Guid>>(k => k.Single() == knowledge),
            It.Is<IEnumerable<Guid>>(t => t.Single() == tool), It.IsAny<CancellationToken>()), Times.Once);
        host.Probe.Calls.Should().ContainSingle(c => c.Privilege == CreateAnalysisPrivilege && c.HasToken);
        host.Access.Calls.Should().Contain(new[]
        {
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, documentId, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Skills, skill, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Knowledge, knowledge, true),
            new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Tools, tool, true),
        });
        host.Access.Calls.Should().NotContain(c => c.Set == Playbooks, "a public playbook needs no Read on its row");
    }

    [Fact(DisplayName = "162 f1 create: without the Create privilege on sprk_analysis is 403 insufficient_privilege and creates nothing")]
    public async Task Create_WithoutCreatePrivilege_Is403()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Probe.Release(CreateAnalysisPrivilege);

        var response = await host.SendAsync(Create(new { name = "A", documentId }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_privilege");
        host.VerifyCreateWroteNothing();
    }

    [Theory(DisplayName = "162 f1 create: a document the caller cannot Read AND AppendTo (or that does not exist) is 403 insufficient_rights and creates nothing")]
    [MemberData(nameof(InsufficientAttachRights))]
    public async Task Create_DocumentWithoutAttachRights_Is403(string _, AccessRights rights)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, rights);

        var response = await host.SendAsync(Create(new { name = "A", documentId }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, documentId, true));
        host.VerifyCreateWroteNothing();
    }

    [Theory(DisplayName = "162 f1 create: a body playbook that is not public and not readable, or does not exist, is the same 403")]
    [InlineData("private-unreadable")]
    [InlineData("nonexistent")]
    public async Task Create_UnusablePlaybook_Is403(string playbookCase)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        var playbookId = Guid.NewGuid();
        host.SetPlaybook(playbookId, playbookCase == "nonexistent" ? null : new PlaybookResponse { Id = playbookId, IsPublic = false });

        var response = await host.SendAsync(Create(new { name = "A", documentId, playbookId }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Playbooks, playbookId, true));
        host.VerifyCreateWroteNothing();
    }

    [Theory(DisplayName = "162 f1 create: a skill, knowledge or tool row the caller cannot Read (or that does not exist) is 403, asked of its own entity set, and creates nothing")]
    [InlineData("skillIds", Skills)]
    [InlineData("knowledgeIds", Knowledge)]
    [InlineData("toolIds", Tools)]
    public async Task Create_UnreadableScopeRow_Is403(string property, string expectedSet)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        var scopeId = Guid.NewGuid();
        var body = new Dictionary<string, object> { ["name"] = "A", ["documentId"] = documentId, [property] = new[] { scopeId } };

        var response = await host.SendAsync(Create(body));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, expectedSet, scopeId, true));
        host.VerifyCreateWroteNothing();
    }

    public static TheoryData<string, object, string> MalformedCreateBodies => new()
    {
        { "no-document", new { name = "A" }, "No document identifier found in request" },
        { "empty-document", new { name = "A", documentId = Guid.Empty }, "No document identifier found in request" },
        { "empty-name", new { name = " ", documentId = Guid.NewGuid() }, "Analysis name is required." },
        { "both-empty", new { name = "", documentId = Guid.Empty }, "No document identifier found in request" },
    };

    [Theory(DisplayName = "162 f1 create: a malformed body gets exactly the 400 it got before, with no rights query at all")]
    [MemberData(nameof(MalformedCreateBodies))]
    public async Task Create_MalformedBody_IsTheExisting400WithNoRightsQuery(string _, object body, string detail)
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var response = await host.SendAsync(Create(body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>().Should().Be(detail);
        host.Access.Calls.Should().BeEmpty();
        host.Probe.Calls.Should().BeEmpty();
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "162 f1 create: a fault while declaring the checks (the body playbook's lookup throws) denies 403 system_failure and creates nothing")]
    public async Task Create_CheckDeclarationFault_Denies()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        var playbookId = Guid.NewGuid();
        host.PlaybookService
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("playbooks unavailable"));

        var response = await host.SendAsync(Create(new { name = "A", documentId, playbookId }));

        await AssertForbiddenAsync(response, "sdap.access.error.system_failure");
        host.VerifyCreateWroteNothing();
    }

    // =============================================================================================
    // POST /api/ai/analysis/promote — finding #22 (G5)
    // =============================================================================================

    [Fact(DisplayName = "162 promote: without the Create privilege on sprk_analysis is 403 insufficient_privilege and writes nothing")]
    public async Task Promote_WithoutCreatePrivilege_Is403AndWritesNothing()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(Guid.NewGuid());
        host.Probe.Release(CreateAnalysisPrivilege);

        var response = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A" }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_privilege");
        host.VerifyPromoteWroteNothing();
    }

    public static TheoryData<string, AccessRights> InsufficientAttachRights => new()
    {
        { "read-only", AccessRights.Read },
        { "appendto-only", AccessRights.AppendTo },
        { "none-or-nonexistent", AccessRights.None },
    };

    [Theory(DisplayName = "162 promote: a body document the caller cannot Read AND AppendTo (or that does not exist) is 403 insufficient_rights, nothing written")]
    [MemberData(nameof(InsufficientAttachRights))]
    public async Task Promote_BodyDocumentWithoutAttachRights_Is403(string _, AccessRights rights)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(null);
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, rights);

        var response = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A", documentId }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, documentId, true));
        host.VerifyPromoteWroteNothing();
    }

    [Theory(DisplayName = "162 promote: a regarding matter/project the caller cannot Read AND AppendTo (or that does not exist) is 403, asked of the right entity set, nothing written")]
    [InlineData("sprk_matter", Matters, false)]
    [InlineData("sprk_project", Projects, false)]
    [InlineData("SPRK_MATTER ", Matters, true)]
    public async Task Promote_RegardingWithoutAttachRights_Is403(string regardingType, string expectedSet, bool grantReadOnly)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(null);
        var regardingId = Guid.NewGuid();
        if (grantReadOnly)
        {
            host.Access.Grant(expectedSet, regardingId, AccessRights.Read);
        }

        var response = await host.SendAsync(Promote(new
        {
            sessionId = session.SessionId, name = "A", regardingEntityType = regardingType, regardingEntityId = regardingId,
        }));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, expectedSet, regardingId, true));
        host.VerifyPromoteWroteNothing();
    }

    [Fact(DisplayName = "162 promote: with no body document, a SESSION document the caller cannot attach to is 403 with the SAME body as a body-document deny, nothing written")]
    public async Task Promote_SessionDocumentWithoutAttachRights_Is403WithTheBodyDocumentDenyBody()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var sessionDocumentId = Guid.NewGuid();
        var session = await host.SeedOwnSessionAsync(sessionDocumentId);
        host.Access.Grant(Documents, sessionDocumentId, AccessRights.Read);

        var sessionDeny = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A" }));

        var bodyDocumentId = Guid.NewGuid();
        var bodyDeny = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A", documentId = bodyDocumentId }));

        await AssertForbiddenAsync(sessionDeny, "sdap.access.deny.insufficient_rights");
        (await NormalizedBodyAsync(sessionDeny)).Should().Be(await NormalizedBodyAsync(bodyDeny));
        host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Document, Documents, sessionDocumentId, true));
        host.VerifyPromoteWroteNothing();
    }

    [Fact(DisplayName = "Round 34 item 2: a session document whose row does not exist (the rights query answers no access, not a fault) is insufficient_rights, never system_failure — the same body as a body-document deny, nothing written")]
    public async Task Promote_SessionDocumentMissingRow_IsInsufficientRights_NotSystemFailure()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(Guid.NewGuid()); // no grant at all: the row is absent for this caller

        var sessionDeny = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A" }));
        var bodyDeny = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A", documentId = Guid.NewGuid() }));

        await AssertForbiddenAsync(sessionDeny, "sdap.access.deny.insufficient_rights");
        (await sessionDeny.Content.ReadAsStringAsync()).Should().NotContain("system_failure",
            "a missing row is a deny, not a fault (round 34 item 2; ADR-003 still fails closed)");
        (await NormalizedBodyAsync(sessionDeny)).Should().Be(await NormalizedBodyAsync(bodyDeny));
        host.VerifyPromoteWroteNothing();
    }

    [Theory(DisplayName = "162 promote: another user's session, or one with no owner, is the 404 a missing session gets — byte-identical apart from correlationId — and nothing is written")]
    [InlineData("another-owner")]
    [InlineData("no-owner")]
    public async Task Promote_SessionNotTheCallers_IsTheMissingSession404(string ownership)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        var foreign = ownership == "another-owner"
            ? await host.Sessions.CreateSessionAsync(AnalysisAuthHost.TenantId, "11111111-2222-4333-8444-555555555555", documentId.ToString())
            : host.SeedOwnerlessSession(documentId);

        var notYours = await host.SendAsync(Promote(new { sessionId = foreign.SessionId, name = "A" }));
        var missing = await host.SendAsync(Promote(new { sessionId = Guid.NewGuid().ToString("N"), name = "A" }));

        notYours.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NormalizedBodyAsync(notYours)).Should().Be(await NormalizedBodyAsync(missing));
        host.VerifyPromoteWroteNothing();
    }

    [Fact(DisplayName = "162 promote: another user's ALREADY-BOUND session is the same 404, not the already-bound 400")]
    public async Task Promote_AnotherUsersBoundSession_Is404NotTheAlreadyBound400()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var bound = await host.Sessions.CreateSessionAsync(
            AnalysisAuthHost.TenantId, "11111111-2222-4333-8444-555555555555", Guid.NewGuid().ToString(),
            hostContext: new ChatHostContext("sprk_analysisoutput", Guid.NewGuid().ToString()));

        var response = await host.SendAsync(Promote(new { sessionId = bound.SessionId, name = "A" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("already bound");
        host.VerifyPromoteWroteNothing();
    }

    public static TheoryData<string, object> MalformedPromoteBodies => new()
    {
        { "empty-sessionId", new { sessionId = "", name = "A" } },
        { "empty-name", new { sessionId = "s", name = " " } },
        { "regarding-wrong-type", new { sessionId = "s", name = "A", regardingEntityType = "account", regardingEntityId = Guid.NewGuid() } },
        { "regarding-missing-id", new { sessionId = "s", name = "A", regardingEntityType = "sprk_matter" } },
    };

    [Theory(DisplayName = "162 promote: a malformed body gets the existing 400 with no rights query at all")]
    [MemberData(nameof(MalformedPromoteBodies))]
    public async Task Promote_MalformedBody_Is400WithNoRightsQuery(string _, object body)
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var response = await host.SendAsync(Promote(body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.Probe.Calls.Should().BeEmpty();
        host.VerifyPromoteWroteNothing();
    }

    [Fact(DisplayName = "162 promote: the caller's own session with the rights is 201 for the {sessionId, name} shape (session document checked as the caller)")]
    public async Task Promote_OwnSessionReviewedDocumentShape_Is201()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var session = await host.SeedOwnSessionAsync(documentId);
        host.Access.Grant(Documents, documentId, ReadAppendTo);

        var response = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "Reviewed" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Analysis.Verify(a => a.CreateAnalysisAsync(documentId, "Reviewed", It.IsAny<Guid?>(), null, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        host.ChatRepo.Bound.Should().ContainSingle();
        host.Probe.Calls.Should().ContainSingle(c => c.Privilege == CreateAnalysisPrivilege && c.HasToken);
    }

    [Fact(DisplayName = "162 promote: the caller's own session with a regarding matter (HistoryOverlay shape) is 201")]
    public async Task Promote_OwnSessionWithRegardingMatter_Is201()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(null);
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, ReadAppendTo);

        var response = await host.SendAsync(Promote(new
        {
            sessionId = session.SessionId, name = "Related", regardingEntityType = "sprk_matter", regardingEntityId = matterId,
            regardingEntityName = "Matter A",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.ChatRepo.Bound.Should().ContainSingle();
        host.Analysis.Verify(a => a.CreateAnalysisAsync(null, "Related", It.IsAny<Guid?>(),
            It.Is<AnalysisRegardingTarget?>(r => r != null && r.EntityLogicalName == "sprk_matter" && r.RecordId == matterId),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once,
            "an analysis promoted in a matter's context records that matter as its anchor (task 162 f1, owner round 15 item 2)");
    }

    [Fact(DisplayName = "162 f1 promote: a rights-query FAULT on the session document is the same 403 (system_failure) as the same fault on a body document — one evaluator, one body — and nothing is written")]
    public async Task Promote_SessionDocumentFault_IsTheBodyDocumentFault403()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(Guid.NewGuid());
        host.Access.ThrowOnEveryCall = new TimeoutException("RetrievePrincipalAccess unavailable");

        var sessionFault = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A" }));
        var bodyFault = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A", documentId = Guid.NewGuid() }));

        await AssertForbiddenAsync(sessionFault, "sdap.access.error.system_failure");
        (await NormalizedBodyAsync(sessionFault)).Should().Be(await NormalizedBodyAsync(bodyFault));
        host.VerifyPromoteWroteNothing();
    }

    [Fact(DisplayName = "162 f1 promote: an exception that ESCAPES the authorization service during the session-document check denies 403 and writes nothing (verifier item 7)")]
    public async Task Promote_SessionDocumentCheckException_Denies()
    {
        var failing = new FinanceAuthz.RecordingAccessDataSource { ThrowOnEveryCall = new TimeoutException("data source down") };
        await using var host = await AnalysisAuthHost.StartAsync(services =>
            // AuthorizationService catches a data-source fault and logs it; a logger that throws on that error is the one way
            // an exception leaves AuthorizeAsync — the branch the handler must still deny on.
            services.AddScoped(sp => new AuthorizationService(failing, sp.GetServices<IAuthorizationRule>(), new ThrowingOnErrorLogger())));
        var session = await host.SeedOwnSessionAsync(Guid.NewGuid());

        var response = await host.SendAsync(Promote(new { sessionId = session.SessionId, name = "A" }));

        await AssertForbiddenAsync(response, "sdap.access.error.system_failure");
        failing.Calls.Should().ContainSingle(c => c.Path == FinanceAuthz.AccessPath.Document);
        host.VerifyPromoteWroteNothing();
    }

    /// <summary>An <see cref="ILogger{T}"/> that throws when asked to log an error — so an exception escapes the service.</summary>
    private sealed class ThrowingOnErrorLogger : ILogger<AuthorizationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                throw new InvalidOperationException("logging sink unavailable");
            }
        }
    }

    [Theory(DisplayName = "162 promote: a body playbook that is not public and not readable, or does not exist, is the same 403; a public one needs no Read on its row")]
    [InlineData("private-unreadable", false)]
    [InlineData("nonexistent", false)]
    [InlineData("public", true)]
    public async Task Promote_BodyPlaybook_IsDecidedByTheUseDecision(string playbookCase, bool allowed)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(null);
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, ReadAppendTo);
        var playbookId = Guid.NewGuid();
        host.SetPlaybook(playbookId, playbookCase switch
        {
            "public" => new PlaybookResponse { Id = playbookId, IsPublic = true },
            "private-unreadable" => new PlaybookResponse { Id = playbookId, IsPublic = false },
            _ => null,
        });

        var response = await host.SendAsync(Promote(new
        {
            sessionId = session.SessionId, name = "A", regardingEntityType = "sprk_matter", regardingEntityId = matterId, playbookId,
        }));

        if (allowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            host.Access.Calls.Should().NotContain(c => c.Set == Playbooks, "a public playbook needs no Read on its row");
        }
        else
        {
            await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
            host.Access.Calls.Should().Contain(new FinanceAuthz.AccessCall(FinanceAuthz.AccessPath.Record, Playbooks, playbookId, true));
            host.VerifyPromoteWroteNothing();
        }
    }

    [Fact(DisplayName = "162 promote: a fault while declaring the checks (the body playbook's lookup throws) denies 403 system_failure and writes nothing")]
    public async Task Promote_CheckDeclarationFault_Denies()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var session = await host.SeedOwnSessionAsync(null);
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, ReadAppendTo);
        var playbookId = Guid.NewGuid();
        host.PlaybookService
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("playbooks unavailable"));

        var response = await host.SendAsync(Promote(new
        {
            sessionId = session.SessionId, name = "A", regardingEntityType = "sprk_matter", regardingEntityId = matterId, playbookId,
        }));

        await AssertForbiddenAsync(response, "sdap.access.error.system_failure");
        host.VerifyPromoteWroteNothing();
    }

    // =============================================================================================
    // POST /api/ai/analysis/execute — finding #52
    // =============================================================================================

    [Fact(DisplayName = "162 execute: a document the caller cannot Read keeps the existing 403 (unchanged first filter)")]
    public async Task Execute_UnreadableDocument_KeepsTheExisting403()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var response = await host.SendAsync(Execute(Guid.NewGuid(), AnalysisAuthHost.ProfilePlaybookId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Routing.VerifyNoOtherCalls();
        host.PlaybookOrchestration.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "162 execute: no PlaybookId is the existing 400 body, with no routing, playbook or node lookup")]
    public async Task Execute_NoPlaybookId_IsTheExisting400WithNoLookup()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, FullRights);

        var response = await host.SendAsync(Execute(documentId, playbookId: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["error"]!.GetValue<string>().Should().Be(AnalysisEndpoints.PlaybookIdRequiredMessage);
        host.Routing.VerifyNoOtherCalls();
        host.PlaybookService.VerifyNoOtherCalls();
        host.Nodes.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "162 execute: document-profile branch with Read but not Write is 403 before any field write, indexing enqueue or AI call")]
    public async Task Execute_ProfileBranchWithoutWrite_Is403BeforeAnyWrite()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read);

        var response = await host.SendAsync(Execute(documentId, AnalysisAuthHost.ProfilePlaybookId));

        await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
        host.VerifyNoRunSideEffect();
    }

    [Fact(DisplayName = "162 execute: document-profile branch with Write runs the profile pipeline as today")]
    public async Task Execute_ProfileBranchWithWrite_RunsTheProfile()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Execute(documentId, AnalysisAuthHost.ProfilePlaybookId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.ActionResolver.Verify(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "162 execute: a consumer-routing fault denies (fail closed)")]
    public async Task Execute_RoutingFault_Denies()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, FullRights);
        host.Routing
            .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("routing unavailable"));

        var response = await host.SendAsync(Execute(documentId, AnalysisAuthHost.ProfilePlaybookId));

        await AssertForbiddenAsync(response, "sdap.access.error.system_failure");
        host.VerifyNoRunSideEffect();
    }

    [Theory(DisplayName = "162 execute: engine branch — a playbook not public and not readable, or nonexistent, is the same 403; a public one needs no Read on its row")]
    [InlineData("private-unreadable", false)]
    [InlineData("nonexistent", false)]
    [InlineData("private-readable", true)]
    [InlineData("public", true)]
    public async Task Execute_EnginePlaybook_IsDecidedByTheUseDecision(string playbookCase, bool allowed)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read);
        var playbookId = Guid.NewGuid();
        host.SetPlaybook(playbookId, playbookCase == "nonexistent"
            ? null
            : new PlaybookResponse { Id = playbookId, IsPublic = playbookCase == "public" });
        if (playbookCase == "private-readable")
        {
            host.Access.Grant(Playbooks, playbookId, AccessRights.Read);
        }
        host.SetNodes(playbookId, Node(ExecutorType.AiAnalysis));

        var response = await host.SendAsync(Execute(documentId, playbookId));

        if (allowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            host.VerifyEngineRan(Times.Once());
            if (playbookCase == "public")
            {
                host.Access.Calls.Should().NotContain(c => c.Set == Playbooks);
            }
        }
        else
        {
            await AssertForbiddenAsync(response, "sdap.access.deny.insufficient_rights");
            host.VerifyEngineRan(Times.Never());
        }
    }

    public static TheoryData<string> WriteRequiredNodeShapes => new() { "side-effecting-node", "null-executor-type", "zero-nodes" };

    [Theory(DisplayName = "162 execute: engine branch with a side-effecting node, a node with no executor type, or zero nodes needs Write on every document")]
    [MemberData(nameof(WriteRequiredNodeShapes))]
    public async Task Execute_EngineThatCanWrite_NeedsWriteOnEveryDocument(string shape)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var writable = Guid.NewGuid();
        var readOnly = Guid.NewGuid();
        host.Access.Grant(Documents, writable, AccessRights.Read | AccessRights.Write);
        host.Access.Grant(Documents, readOnly, AccessRights.Read);
        var playbookId = host.PublicPlaybook();
        switch (shape)
        {
            case "side-effecting-node":
                host.SetNodes(playbookId, Node(ExecutorType.AiAnalysis), Node(ExecutorType.UpdateRecord, active: false));
                break;
            case "null-executor-type":
                host.SetNodes(playbookId, Node(ExecutorType.AiAnalysis), new PlaybookNodeDto { Id = Guid.NewGuid(), SprkExecutortype = null });
                break;
            default:
                host.SetNodes(playbookId);
                break;
        }

        var denied = await host.SendAsync(Execute(new[] { writable, readOnly }, playbookId));
        await AssertForbiddenAsync(denied, "sdap.access.deny.insufficient_rights");
        host.VerifyEngineRan(Times.Never());

        var allowed = await host.SendAsync(Execute(new[] { writable }, playbookId));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        host.VerifyEngineRan(Times.Once());
    }

    [Fact(DisplayName = "162 execute: engine branch with only read-only nodes and Read on every document streams as today")]
    public async Task Execute_ReadOnlyEngine_RunsWithRead()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read);
        var playbookId = host.PublicPlaybook();
        host.SetNodes(playbookId, Node(ExecutorType.Start), Node(ExecutorType.AiAnalysis));

        var response = await host.SendAsync(Execute(documentId, playbookId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("[DONE]");
        host.VerifyEngineRan(Times.Once());
    }

    [Theory(DisplayName = "162 execute: a node-list fault or a null node list denies (fail closed)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execute_NodeLookupFaultOrNull_Denies(bool throws)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, FullRights);
        var playbookId = host.PublicPlaybook();
        if (throws)
        {
            host.Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("nodes"));
        }
        else
        {
            host.Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync((PlaybookNodeDto[])null!);
        }

        var response = await host.SendAsync(Execute(documentId, playbookId));

        await AssertForbiddenAsync(response, "sdap.access.error.system_failure");
        host.VerifyEngineRan(Times.Never());
    }

    // =============================================================================================
    // Completeness — every route the group maps has a deny case, and each one denies a caller with no rights
    // =============================================================================================

    private static readonly IReadOnlyDictionary<string, Func<AnalysisAuthHost, Task<HttpRequestMessage>>> DenyCaseByRoute =
        new Dictionary<string, Func<AnalysisAuthHost, Task<HttpRequestMessage>>>(StringComparer.Ordinal)
        {
            ["POST /api/ai/analysis/create"] = _ => Task.FromResult(Json(HttpMethod.Post, "/api/ai/analysis/create",
                new { name = "A", documentId = Guid.NewGuid() })),
            ["POST /api/ai/analysis/promote"] = async h =>
            {
                var session = await h.SeedOwnSessionAsync(Guid.NewGuid());
                return Promote(new { sessionId = session.SessionId, name = "A" });
            },
            ["POST /api/ai/analysis/execute"] = _ => Task.FromResult(Execute(Guid.NewGuid(), AnalysisAuthHost.ProfilePlaybookId)),
            ["GET /api/ai/analysis/{analysisId:guid}"] = h =>
            {
                var analysisId = Guid.NewGuid();
                h.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", Guid.NewGuid()) });
                return Task.FromResult(Get(analysisId));
            },
        };

    [Fact(DisplayName = "162 completeness: every route mapped under /api/ai/analysis has a deny case, and each denies a caller the seams report as None")]
    public async Task EveryMappedAnalysisRoute_HasADenyCase_AndDeniesACallerWithNoRights()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        host.Probe.Release(CreateAnalysisPrivilege);

        var mapped = host.MappedAnalysisRoutes();

        mapped.Should().NotBeEmpty();
        mapped.Should().BeEquivalentTo(DenyCaseByRoute.Keys,
            "every route under /api/ai/analysis needs a deny case here — a route with none is unproven, which is how "
            + "four of these seven routes decided nothing while the structural guard credited them by filter name");

        foreach (var route in mapped)
        {
            var response = await host.SendAsync(await DenyCaseByRoute[route](host));
            ((int)response.StatusCode).Should().BeOneOf(new[] { 403, 404 }, $"{route} must deny a caller with no rights");
        }

        host.Analysis.Verify(a => a.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Orchestration.VerifyNoOtherCalls();
        host.VerifyNoRunSideEffect();
    }

    // =============================================================================================
    // Helpers
    // =============================================================================================

    private static PlaybookNodeDto Node(ExecutorType executorType, bool active = true) =>
        new() { Id = Guid.NewGuid(), SprkExecutortype = executorType, IsActive = active };

    private static HttpRequestMessage Get(Guid analysisId, bool withToken = true) =>
        Request(HttpMethod.Get, $"/api/ai/analysis/{analysisId}", body: null, withToken);

    private static HttpRequestMessage Promote(object body) => Json(HttpMethod.Post, "/api/ai/analysis/promote", body);

    private static HttpRequestMessage Execute(Guid documentId, Guid? playbookId) =>
        Execute(new[] { documentId }, playbookId);

    private static HttpRequestMessage Execute(Guid[] documentIds, Guid? playbookId) =>
        Json(HttpMethod.Post, "/api/ai/analysis/execute", new { documentIds, playbookId });

    private static HttpRequestMessage Json(HttpMethod method, string path, object body) => Request(method, path, body, withToken: true);

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body, bool withToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(AnalysisAuthzTestAuthHandler.CallerHeader, "1");
        if (withToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response, string reasonCode)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["reasonCode"]!.GetValue<string>().Should().Be(reasonCode);
        body["detail"]!.GetValue<string>().Should().Be("Access denied", "the 403 detail is one constant that names no record");
    }

    private static async Task AssertUniform404Async(HttpResponseMessage response, Guid analysisId)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(analysisId.ToString(), "the uniform 404 never echoes the requested id");
        raw.Should().NotContain(analysisId.ToString("N"));
        var body = JsonNode.Parse(raw)!;
        body["title"]!.GetValue<string>().Should().Be("Not Found");
        body["detail"]!.GetValue<string>().Should().Be("The requested record was not found.");
        body["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        (await NormalizedBodyAsync(response)).Should().Be(UniformNotFoundBody, "every deny case is byte-identical apart from correlation ids");
    }

    /// <summary>The uniform 404 body with its correlation ids removed — what every deny case must equal.</summary>
    private static readonly string UniformNotFoundBody =
        """{"type":"https://tools.ietf.org/html/rfc7231#section-6.5.4","title":"Not Found","status":404,"detail":"The requested record was not found.","reasonCode":"sdap.access.deny.record_unavailable"}""";

    private static async Task<string> NormalizedBodyAsync(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return node.ToJsonString();
    }
}

/// <summary>
/// A minimal host over the REAL <c>MapAnalysisEndpoints</c>, with the REAL authorization stack substituted only at
/// the access-data boundary and the privilege seam (task 130's recording fakes).
/// </summary>
internal sealed class AnalysisAuthHost : IAsyncDisposable
{
    public const string TenantId = "00000000-0000-0000-0000-000000000162";

    /// <summary>Mirrors the seeded spaarkedev1 document-profile Binding row's sprk_playbook.</summary>
    public static readonly Guid ProfilePlaybookId = Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");

    private WebApplication? _app;
    private HttpClient? _client;
    private readonly InMemoryTenantCache _cache = new();

    public FinanceAuthz.RecordingAccessDataSource Access { get; } = new();
    public FinanceAuthz.RecordingPrivilegeProbe Probe { get; } = new();
    public Mock<IGenericEntityService> EntityService { get; } = new();
    public Mock<IAnalysisOrchestrationService> Orchestration { get; } = new(MockBehavior.Strict);
    public Mock<IAnalysisDataverseService> Analysis { get; } = new();
    public Mock<IConsumerRoutingService> Routing { get; } = new();
    public Mock<IPlaybookService> PlaybookService { get; } = new();
    public Mock<INodeService> Nodes { get; } = new();
    public Mock<IPlaybookOrchestrationService> PlaybookOrchestration { get; } = new();
    public Mock<IActionResolver> ActionResolver { get; } = new();
    public Mock<IActionRunner> ActionRunner { get; } = new();
    public Mock<IDocumentDataverseService> DocumentDataverse { get; } = new();
    public Mock<IPostUploadIndexingEnqueuer> Indexing { get; } = new();
    public CapturingChatDataverseRepository ChatRepo { get; } = new();

    /// <summary>
    /// Task 146's ONE owner resolver at its module boundary (sweep integration): /create and /promote resolve the new
    /// analysis's owner after this task's gate. Ownership is not this suite's subject (146's tests drive the real resolver).
    /// </summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble Ownership { get; } = new();
    public ChatSessionManager Sessions { get; private set; } = null!;

    /// <param name="configure">Registrations added LAST (so they win) — e.g. an AuthorizationService built with a fault.</param>
    public static async Task<AnalysisAuthHost> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var host = new AnalysisAuthHost();
        await host.InitializeAsync(configure);
        return host;
    }

    private async Task InitializeAsync(Action<IServiceCollection>? configure)
    {
        Probe.Hold("prvCreatesprk_analysis");

        Routing
            .Setup(r => r.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfilePlaybookId);
        ActionResolver
            .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test host: no Action configured"));
        PlaybookOrchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyEventsAsync);
        Analysis
            .Setup(a => a.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = AnalysisAuthzTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = AnalysisAuthzTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AnalysisAuthzTestAuthHandler>(AnalysisAuthzTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-batch", _ => RateLimitPartition.GetNoLimiter("ai-batch-test"));
            opt.AddPolicy("ai-stream", _ => RateLimitPartition.GetNoLimiter("ai-stream-test"));
        });

        // The REAL authorization stack, substituted only at the access-data boundary and the privilege seam.
        builder.Services.AddSingleton<IAccessDataSource>(Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
        builder.Services.AddSingleton<IAiAuthorizationService>(sp =>
            new AiAuthorizationService(sp.GetRequiredService<IAccessDataSource>(), NullLogger<AiAuthorizationService>.Instance));

        // Module-boundary doubles for the filters' declarations and the handlers.
        builder.Services.AddSingleton(EntityService.Object);
        builder.Services.AddSingleton(Orchestration.Object);
        builder.Services.AddSingleton(Analysis.Object);
        builder.Services.AddSingleton(Routing.Object);
        builder.Services.AddSingleton(PlaybookService.Object);
        builder.Services.AddSingleton(Nodes.Object);
        builder.Services.AddSingleton(PlaybookOrchestration.Object);
        builder.Services.AddSingleton(ActionResolver.Object);
        builder.Services.AddSingleton(ActionRunner.Object);
        builder.Services.AddSingleton(Mock.Of<IDocumentTextSource>());
        builder.Services.AddSingleton(DocumentDataverse.Object);
        builder.Services.AddSingleton(Indexing.Object);
        builder.Services.AddSingleton(Options.Create(new AnalysisOptions { Enabled = true, MultiDocumentEnabled = true }));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Mock.Of<ISpeFileOperations>());
        builder.Services.AddSingleton(Mock.Of<ITextExtractor>());
        builder.Services.AddSingleton<ITenantCache>(_cache);
        // Task 171: document text is read app-only after the row's pointer check — a permissive pointer world here, so
        // these tests stay about the route's Dataverse decisions.
        builder.Services.AddSingleton(TestRecordContainerResolver.ForBusinessUnitContainers(c => true));
        builder.Services.AddSingleton<AnalysisDocumentLoader>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<IChatDataverseRepository>(ChatRepo);
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(Ownership);
        Sessions = new ChatSessionManager(
            cache: _cache,
            dataverseRepository: ChatRepo,
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null);
        builder.Services.AddSingleton(Sessions);
        configure?.Invoke(builder.Services);

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapAnalysisEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

    /// <summary>Every route the REAL mapper registered under /api/ai/analysis, as "VERB /pattern".</summary>
    public IReadOnlyList<string> MappedAnalysisRoutes() =>
        _app!.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/ai/analysis", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>())
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToList();

    /// <summary>Seeds the app-only anchor read of an analysis: one EntityReference per populated anchor column.</summary>
    public void SeedAnalysis(Guid analysisId, Dictionary<string, (string Target, Guid Id)> anchors, Action<Entity>? extra = null)
    {
        var entity = new Entity("sprk_analysis", analysisId);
        foreach (var (column, (target, id)) in anchors)
        {
            entity[column] = new EntityReference(target, id);
        }
        extra?.Invoke(entity);

        // Like Dataverse, answer ONLY the columns the caller selected — so a filter that stops selecting an anchor
        // column stops seeing that anchor, exactly as it would live.
        EntityService
            .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) =>
            {
                var selected = new Entity("sprk_analysis", analysisId);
                foreach (var column in columns.Where(entity.Attributes.ContainsKey))
                {
                    selected[column] = entity[column];
                }
                return selected;
            });
    }

    /// <summary>
    /// Like Dataverse BEFORE task 146's schema script: a select naming <c>sprk_createdbyperson</c> faults, because the
    /// column does not exist yet (live spaarkedev1, 2026-10-04).
    /// </summary>
    public void CreatorPersonColumnMissing(Guid analysisId) =>
        EntityService
            .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId,
                It.Is<string[]>(c => c.Contains("sprk_createdbyperson")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147217149 },
                new FaultReason("'sprk_analysis' entity doesn't contain attribute with Name = 'sprk_createdbyperson'")));

    /// <summary>
    /// Runs <paramref name="action"/> with an HttpContext shaped like an authenticated request to this host (the caller's
    /// oid, tid and bearer token, the host's request services) — for a shared decision a non-route caller makes.
    /// </summary>
    public async Task<T> WithCallerContextAsync<T>(Func<HttpContext, Task<T>> action)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", AnalysisAuthzTestAuthHandler.CallerObjectId), new Claim("tid", TenantId) },
                AnalysisAuthzTestAuthHandler.SchemeName)),
            TraceIdentifier = "shared-decision",
        };
        httpContext.Request.Headers.Authorization = "Bearer caller-token";
        return await action(httpContext);
    }

    public void VerifyCreateWroteNothing()
    {
        Analysis.Verify(a => a.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        Analysis.Verify(a => a.AssociateScopesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<IEnumerable<Guid>>(),
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public Task<ChatSession> SeedOwnSessionAsync(Guid? documentId) =>
        Sessions.CreateSessionAsync(TenantId, AnalysisAuthzTestAuthHandler.CallerObjectId, documentId?.ToString());

    /// <summary>A session served from the cold read path with NO recorded owner.</summary>
    public ChatSession SeedOwnerlessSession(Guid documentId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new ChatSession(sessionId, TenantId, documentId.ToString(), null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<ChatMessage>()) { OwnerOid = null };
        ChatRepo.SessionsById[sessionId] = session;
        return session;
    }

    public void SetPlaybook(Guid playbookId, PlaybookResponse? playbook) =>
        PlaybookService.Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(playbook);

    public Guid PublicPlaybook()
    {
        var playbookId = Guid.NewGuid();
        SetPlaybook(playbookId, new PlaybookResponse { Id = playbookId, IsPublic = true });
        return playbookId;
    }

    public void SetNodes(Guid playbookId, params PlaybookNodeDto[] nodes) =>
        Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>())).ReturnsAsync(nodes);

    public void VerifyPromoteWroteNothing()
    {
        Analysis.Verify(a => a.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        ChatRepo.Bound.Should().BeEmpty("no session may be bound when promote is denied");
    }

    public void VerifyEngineRan(Times times) =>
        PlaybookOrchestration.Verify(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), times);

    /// <summary>No profile field write, no indexing enqueue, no AI call and no engine run happened.</summary>
    public void VerifyNoRunSideEffect()
    {
        DocumentDataverse.VerifyNoOtherCalls();
        Indexing.VerifyNoOtherCalls();
        ActionResolver.Verify(a => a.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        ActionRunner.VerifyNoOtherCalls();
        VerifyEngineRan(Times.Never());
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyEventsAsync(
        PlaybookRunRequest _, HttpContext __, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ___)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

/// <summary>
/// The analysis authorization stack with an EXPLICIT ALLOW at every seam, for the contract fixtures that pin what the
/// analysis handlers DO once authorized (promote composition, execute dispatch). The REAL
/// <see cref="AuthorizationService"/> + <see cref="OperationAccessRule"/> evaluate; the access-data boundary answers
/// every record with full rights, the privilege seam holds the Create privilege on sprk_analysis, every playbook is
/// public and every playbook has one read-only node. The deny cases live in
/// <see cref="AnalysisEndpointsAuthorizationContractTests"/> (task 162).
/// </summary>
internal static class AnalysisAuthorizationTestRegistrations
{
    public static IServiceCollection AddAllowAllAnalysisAuthorization(this IServiceCollection services)
    {
        const AccessRights full = AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;
        var access = new Mock<IAccessDataSource>();
        access
            .Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) =>
                new AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = full });
        access
            .Setup(a => a.GetRecordAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, string _, Guid recordId, string? _, CancellationToken _) =>
                new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = full });
        services.AddSingleton(access.Object);
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        services.AddScoped<AuthorizationService>();

        var probe = new FinanceAuthz.RecordingPrivilegeProbe();
        probe.Hold(AnalysisAuthorizationFilter.CreateAnalysisPrivilege);
        services.AddSingleton<CallerRecordAccessProbe>(probe);

        var playbooks = new Mock<IPlaybookService>();
        playbooks
            .Setup(p => p.GetPlaybookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PlaybookResponse { Id = id, IsPublic = true });
        services.AddSingleton(playbooks.Object);

        var nodes = new Mock<INodeService>();
        nodes
            .Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PlaybookNodeDto { Id = Guid.NewGuid(), SprkExecutortype = ExecutorType.AiAnalysis } });
        services.AddSingleton(nodes.Object);

        return services;
    }
}

/// <summary>
/// Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra <c>oid</c> and a <c>tid</c>,
/// independent of the Authorization header — so a test can present an authenticated caller whose bearer token is
/// absent (the "token unreadable" fail-closed case).
/// </summary>
public sealed class AnalysisAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AnalysisAuthzTest";
    public const string CallerHeader = "X-Test-Caller";
    public const string CallerObjectId = "6f0c1a52-0000-4000-8000-000000000162";

    public AnalysisAuthzTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim("oid", CallerObjectId), new Claim("tid", AnalysisAuthHost.TenantId) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
