// AUTH — spaarke_start_analysis and spaarke_get_analysis refuse what the REST routes refuse, with the routes' reasonCode, and write or
// leak nothing (spaarkeai-mcp-bff-api-r2 task 031, spec FR-06, FR-15, NFR-07; ADR-028).
// KEEP path: auth.
//
// WHAT RUNS: the tools' RunAsync with an HttpContext of the REAL AnalysisAuthHost (the REAL create decision and read rule; doubles only at
// the access-data boundary, the privilege seam, the owner resolver and the app-only writer). Each refusal's reasonCode is the one the
// REST baseline (AnalysisCreateContractBaselineTests) freezes for the same case.

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Protocol;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Analysis;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Api.Mcp.Analysis;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Mcp.Analysis;

public sealed class AnalysisToolsAuthTests
{
    private const string Documents = "sprk_documents";
    private const AccessRights ReadAppendTo = AccessRights.Read | AccessRights.AppendTo;

    private static string ReasonOf(CallToolResult result) => result.StructuredContent!.Value.GetProperty("reasonCode").GetString()!;

    private static int StatusOf(CallToolResult result) => result.StructuredContent!.Value.GetProperty("status").GetInt32();

    private static object Start(Guid documentId, Guid? playbookId = null) =>
        playbookId is { } playbook
            ? new { documentId = documentId.ToString(), name = "A", playbookId = playbook.ToString(), idempotencyKey = Guid.NewGuid().ToString() }
            : new { documentId = documentId.ToString(), name = "A", idempotencyKey = Guid.NewGuid().ToString() };

    // ---- start: the route's refusals ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 start auth: no Create privilege on sprk_analysis is the route's insufficient_privilege 403, nothing written")]
    public async Task Start_NoCreatePrivilege_IsTheRouteRefusal()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Probe.Release("prvCreatesprk_analysis");

        var result = await AnalysisToolsContractTests.StartAsync(host, Start(documentId));

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("sdap.access.deny.insufficient_privilege");
        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "031 start auth: a document without Read and AppendTo (analysis.attach) is the route's insufficient_rights 403, nothing written")]
    public async Task Start_NoAttachRights_IsTheRouteRefusal()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read);

        var result = await AnalysisToolsContractTests.StartAsync(host, Start(documentId));

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("sdap.access.deny.insufficient_rights");
        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        ((TextContentBlock)result.Content.Single()).Text.Should().Contain("may not start an analysis", "a real refusal keeps the refusal text (control)");
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "031 start auth: a private playbook the caller cannot read is the route's insufficient_rights 403, nothing written")]
    public async Task Start_UnreadablePrivatePlaybook_IsTheRouteRefusal()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.SetPlaybook(playbookId, new PlaybookResponse { Id = playbookId, IsPublic = false });

        var result = await AnalysisToolsContractTests.StartAsync(host, Start(documentId, playbookId));

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("sdap.access.deny.insufficient_rights");
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "031 start auth: a fault while deciding is the route's system_failure 403 with no exception text")]
    public async Task Start_DecisionFault_IsSystemFailure_NoExceptionText()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.PlaybookService
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed: secret detail"));

        var result = await AnalysisToolsContractTests.StartAsync(host, Start(documentId, playbookId));

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("sdap.access.error.system_failure");
        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        // A system fault is not a refusal of the caller: its own text, never "may not start".
        result.StructuredContent!.Value.GetProperty("message").GetString().Should().Be("Spaarke could not check access right now. Try again shortly.");
        ((TextContentBlock)result.Content.Single()).Text.Should().Be("Spaarke could not check access right now. Try again shortly.").And.NotContain("may not");
        JsonSerializer.Serialize(result).Should().NotContain("secret detail");
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "031 start auth: no owner could be resolved is the route's 409 reasonCode, nothing written")]
    public async Task Start_OwnerUnresolved_IsTheRoute409()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Ownership.TeamId = null;

        var result = await AnalysisToolsContractTests.StartAsync(host, Start(documentId));

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("record_owner_secure_parent_not_isolated");
        StatusOf(result).Should().Be(StatusCodes.Status409Conflict);
        JsonSerializer.Serialize(result).Should().NotContain("double: refused", "the route's detail text is not copied into a model-visible result");
        host.AppOnlyCreates.Should().BeEmpty();
    }

    [Fact(DisplayName = "031 start auth: no signed-in caller is UserContextRequired before any read, rights question or write")]
    public async Task Start_NoCaller_IsUserContextRequired()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);

        var result = await host.WithCallerContextAsync(ctx =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            return StartAnalysisTool.RunAsync(
                AnalysisToolsContractTests.Args(Start(documentId)), ctx, ctx.RequestServices.GetRequiredService<ITenantCache>(),
                AnalysisToolsContractTests.EnvironmentUrl, NullLogger.Instance, CancellationToken.None);
        });

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyCreateWroteNothing();
    }

    [Fact(DisplayName = "031 start auth: an argument outside the closed set is refused before any rights question")]
    public async Task Start_UnknownArgument_IsRefused()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var result = await AnalysisToolsContractTests.StartAsync(host, new
        {
            documentId = Guid.NewGuid().ToString(),
            name = "A",
            idempotencyKey = "k",
            routeAuthorizedLookups = "x",
        });

        result.IsError.Should().BeTrue();
        ReasonOf(result).Should().Be("mcp.invalid_argument");
        host.Access.Calls.Should().BeEmpty();
        host.VerifyCreateWroteNothing();
    }

    // ---- get: uniform not-found, nothing leaks ----------------------------------------------------------------------------------

    [Fact(DisplayName = "031 get auth: an unreadable analysis and an absent one are the byte-identical uniform not-found, with no record text")]
    public async Task Get_UnreadableAndAbsent_AreByteIdentical()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var unreadable = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        host.SeedAnalysis(unreadable, new() { ["sprk_documentid"] = ("sprk_document", documentId) });
        var absent = Guid.NewGuid();

        var denied = await AnalysisToolsContractTests.GetAsync(host, unreadable);
        var missing = await AnalysisToolsContractTests.GetAsync(host, absent);

        denied.IsError.Should().BeTrue();
        ReasonOf(denied).Should().Be("sdap.access.deny.record_unavailable");
        StatusOf(denied).Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(denied).Should().Be(JsonSerializer.Serialize(missing), "NFR-07: no existence oracle");
        JsonSerializer.Serialize(denied).Should().NotContain(unreadable.ToString()).And.NotContain(documentId.ToString());
        host.Orchestration.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "031 get auth: a readable document beside an unreadable regarding matter is the same not-found (AND, as the route)")]
    public async Task Get_ReadableDocumentUnreadableMatter_IsNotFound()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new()
        {
            ["sprk_documentid"] = ("sprk_document", documentId),
            ["sprk_regardingmatter"] = ("sprk_matter", Guid.NewGuid()),
        });
        host.Access.Grant(Documents, documentId, AccessRights.Read);

        var result = await AnalysisToolsContractTests.GetAsync(host, analysisId);

        ReasonOf(result).Should().Be("sdap.access.deny.record_unavailable");
        host.Orchestration.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "031 get auth: no signed-in caller is UserContextRequired before any read")]
    public async Task Get_NoCaller_IsUserContextRequired()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var result = await host.WithCallerContextAsync(ctx =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            return GetAnalysisTool.RunAsync(
                AnalysisToolsContractTests.Args(new { analysisId = Guid.NewGuid() }), ctx,
                AnalysisToolsContractTests.EnvironmentUrl, NullLogger.Instance, CancellationToken.None);
        });

        ReasonOf(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Access.Calls.Should().BeEmpty();
        host.Orchestration.VerifyNoOtherCalls();
    }
}
