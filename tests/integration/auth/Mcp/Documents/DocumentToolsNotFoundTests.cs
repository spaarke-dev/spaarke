// AUTH — the refusals of spaarke_list_documents and spaarke_get_document (spaarkeai-mcp-bff-api-r2 task 023, FR-03, NFR-07, ADR-028 A10).
// KEEP path: endpoint-auth.
//
// WHAT RUNS: the two handlers' RunAsync, called directly with a request context, over the module-boundary doubles of DocumentToolsWorld.
// Direct, not over the MCP route, because the claims asserted here are byte-level (the request's correlation id is fixed, so two refusals
// can be compared whole) and the route's own token shapes are task 021's.
//
// PINNED: a document the caller may not read, one the external narrowing hides, one that does not exist and a narrowing that faults answer
// ONE result, byte for byte; a matter the caller may not read and one that does not exist the same; no caller reads nothing; a list never
// shows a document the narrowing hides.

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;
using IAuthorizationService = Spaarke.Core.Auth.IAuthorizationService;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Documents;

public sealed class DocumentToolsNotFoundTests
{
    private static string Arg(string documentId) => $$"""{"documentId":"{{documentId}}"}""";

    private static IDictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static IDictionary<string, JsonElement> ListArgs(Guid matterId) =>
        Args($$"""{"regardingEntityType":"sprk_matter","regardingId":"{{matterId}}"}""");

    private sealed class Run
    {
        public Run(DocumentToolsWorld world)
        {
            World = world;
            var services = new ServiceCollection();
            world.Register(services);
            Services = services.BuildServiceProvider();
        }

        public DocumentToolsWorld World { get; }

        public IServiceProvider Services { get; }

        public HttpContext Http(bool authenticated = true) => DocumentToolsWorld.RequestFor(Services, authenticated: authenticated);

        public Task<CallToolResult> GetAsync(string documentId, HttpContext? http, CancellationToken cancellationToken = default) =>
            GetDocumentTool.RunAsync(
                Args(Arg(documentId)), http, World.Documents.Object, Services.GetRequiredService<IAuthorizationService>(),
                Services.GetRequiredService<SpeFileStore>(), Services.GetRequiredService<RecordContainerResolver>(),
                NullLogger.Instance, cancellationToken);

        public Task<CallToolResult> ListAsync(Guid matterId, HttpContext? http, CancellationToken cancellationToken = default) =>
            ListDocumentsTool.RunAsync(
                ListArgs(matterId), http, World.Documents.Object, World.Probe, NullLogger.Instance, cancellationToken);
    }

    /// <summary>The whole result as bytes: the error flag, the text the model reads and the structured body.</summary>
    private static string Bytes(CallToolResult result) =>
        $"{result.IsError}|{string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text))}|{result.StructuredContent?.GetRawText()}";

    private static string Reason(CallToolResult result) => result.StructuredContent!.Value.GetProperty("reasonCode").GetString()!;

    // ---- spaarke_get_document --------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_AForbiddenAndANonExistentId_ReturnByteIdenticalNotFoundResults()
    {
        var run = new Run(new DocumentToolsWorld());

        var forbidden = await run.GetAsync(DocumentToolsWorld.ForbiddenDocumentId, run.Http());
        var missing = await run.GetAsync(DocumentToolsWorld.MissingDocumentId, run.Http());

        forbidden.IsError.Should().BeTrue();
        Reason(forbidden).Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        Bytes(forbidden).Should().Be(Bytes(missing), "a caller cannot tell a record they may not read from one that does not exist");
        Bytes(forbidden).Should().NotContain(DocumentToolsWorld.ForbiddenDocumentId, "the not-found names no id");
        run.World.SpeCalls.Should().BeEmpty("a refused document's storage is never followed");
    }

    [Fact]
    public async Task Get_ADocumentTheExternalNarrowingHides_IsTheSameNotFound()
    {
        var world = new DocumentToolsWorld { CallerIsExternal = true };
        world.HiddenByNarrowing.Add(Guid.Parse(DocumentToolsWorld.ReadableDocumentId));
        var run = new Run(world);

        var hidden = await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http());
        var missing = await run.GetAsync(DocumentToolsWorld.MissingDocumentId, run.Http());

        hidden.IsError.Should().BeTrue();
        Bytes(hidden).Should().Be(Bytes(missing), "a document the narrowing hides is not told apart from one that does not exist");
        world.Documents.Invocations.Should().BeEmpty("a hidden document is not read");
    }

    [Fact]
    public async Task Get_WhenTheNarrowingFaults_FailsClosedWithTheSameNotFound()
    {
        var world = new DocumentToolsWorld { CallerIsExternal = true, NarrowingUnavailable = true };
        var run = new Run(world);

        var faulted = await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http());
        var missing = await run.GetAsync(DocumentToolsWorld.MissingDocumentId, run.Http());

        faulted.IsError.Should().BeTrue("a decision that cannot be made does not release the document");
        Bytes(faulted).Should().Be(Bytes(missing));
        world.Documents.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ForAnInternalCallerWhoMayRead_ReturnsTheDocument_WithTheBearerTokenGivenOnlyToTheAuthorizationService()
    {
        var world = new DocumentToolsWorld();
        var run = new Run(world);

        var result = await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http());

        result.IsError.Should().NotBe(true, Bytes(result));
        world.Authorization.Asked.Should().ContainSingle().Which.Should().Be(
            $"read:{DocumentToolsWorld.ReadableDocumentId}:{DocumentToolsWorld.CallerOid:D}",
            "the caller's own Read on this document, asked as the caller");
        Bytes(result).Should().NotContain("opaque-test-token", "ADR-028: the token is never in a result");
    }

    [Fact]
    public async Task Get_WithNoCaller_IsUserContextRequired_AndNothingIsReadOrDecided()
    {
        var world = new DocumentToolsWorld();
        var run = new Run(world);

        var noContext = await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, null);
        var unauthenticated = await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http(authenticated: false));

        foreach (var result in new[] { noContext, unauthenticated })
        {
            result.IsError.Should().BeTrue();
            Reason(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        }

        world.Authorization.Asked.Should().BeEmpty("no caller, no decision");
        world.Documents.Invocations.Should().BeEmpty("no caller, no read");
        world.SpeCalls.Should().BeEmpty();
    }

    // ---- spaarke_list_documents ------------------------------------------------------------------------------------

    [Fact]
    public async Task List_AForbiddenAndANonExistentMatter_ReturnByteIdenticalNotFoundResults_AndListNothing()
    {
        var run = new Run(new DocumentToolsWorld());

        var forbidden = await run.ListAsync(DocumentToolsWorld.RestrictedMatterId, run.Http());
        var missing = await run.ListAsync(DocumentToolsWorld.MissingMatterId, run.Http());

        forbidden.IsError.Should().BeTrue();
        Reason(forbidden).Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        Bytes(forbidden).Should().Be(Bytes(missing), "a matter the caller may not read is not told apart from one that does not exist");
        run.World.Documents.Invocations.Should().BeEmpty("the documents of a matter the caller may not read are never read");
    }

    [Fact]
    public async Task List_ForAnExternalCaller_NeverShowsADocumentTheNarrowingHides()
    {
        var world = new DocumentToolsWorld { CallerIsExternal = true };
        world.HiddenByNarrowing.Add(Guid.Parse(DocumentToolsWorld.SecondReadableDocumentId));
        var run = new Run(world);

        var result = await run.ListAsync(DocumentToolsWorld.MatterId, run.Http());

        result.IsError.Should().NotBe(true, Bytes(result));
        var ids = result.StructuredContent!.Value.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
        ids.Should().Equal([DocumentToolsWorld.ReadableDocumentId], "only the document the narrowing shows is listed");
        Bytes(result).Should().NotContain(DocumentToolsWorld.SecondReadableDocumentId).And.NotContain("Disclosure schedule");
    }

    [Fact]
    public async Task List_WhenTheNarrowingFaults_ShowsNoDocuments()
    {
        var world = new DocumentToolsWorld { CallerIsExternal = true, NarrowingUnavailable = true };
        var run = new Run(world);

        var result = await run.ListAsync(DocumentToolsWorld.MatterId, run.Http());

        result.IsError.Should().NotBe(true);
        result.StructuredContent!.Value.GetProperty("count").GetInt32().Should().Be(0, "fail closed: a narrowing that cannot decide shows nothing");
        Bytes(result).Should().NotContain("Master services agreement").And.NotContain(DocumentToolsWorld.ReadableDocumentId);
        Text(result).Should().Be(
            "No documents you can see are filed under this matter.",
            "rows came back and none could be shown, so the matter is not reported as empty");
    }

    [Fact]
    public async Task List_WhenRowsCameBackButTheNarrowingHidesEveryOne_NeverSaysTheMatterHasNoDocuments()
    {
        var world = new DocumentToolsWorld { CallerIsExternal = true };
        world.HiddenByNarrowing.Add(Guid.Parse(DocumentToolsWorld.ReadableDocumentId));
        world.HiddenByNarrowing.Add(Guid.Parse(DocumentToolsWorld.SecondReadableDocumentId));
        var run = new Run(world);

        var result = await run.ListAsync(DocumentToolsWorld.MatterId, run.Http());

        result.IsError.Should().NotBe(true);
        result.StructuredContent!.Value.GetProperty("count").GetInt32().Should().Be(0);
        Text(result).Should().Be("No documents you can see are filed under this matter.");
        Text(result).Should().NotContain("No documents are filed", "the matter has documents; the caller may not see them");
    }

    [Fact]
    public async Task List_ForAMatterTheSourceHoldsNoDocumentsFor_StillSaysNoDocumentsAreFiled()
    {
        var world = new DocumentToolsWorld();
        var empty = Guid.Parse("23000000-0000-4000-8000-0000000000c2");
        world.ReadableMatters.Add(empty);
        var run = new Run(world);

        var result = await run.ListAsync(empty, run.Http());

        Text(result).Should().Be("No documents are filed under this matter.");
    }

    // ---- unexpected faults (F1): isError document.unavailable 503, fixed text; the caller's own cancellation propagates --------------

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static void ShouldBeUnavailable(CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        Reason(result).Should().Be(DocumentToolSupport.UnavailableReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(503);
        result.StructuredContent!.Value.GetProperty("correlationId").GetString().Should().Be(DocumentToolsWorld.TraceId);
        Bytes(result).Should().NotContain("secret", "fixed text: no exception text reaches the model");
    }

    [Fact]
    public async Task Get_WhenTheDocumentReadThrows_IsUnavailable503_WithTheCorrelationId_AndNoExceptionText()
    {
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new InvalidOperationException("secret dataverse detail") });

        ShouldBeUnavailable(await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http()));
    }

    [Fact]
    public async Task Get_WhenTheAuthorizationReadThrows_IsUnavailable503_NotTheNotFound()
    {
        var run = new Run(new DocumentToolsWorld { AuthorizationFault = new InvalidOperationException("secret authorization detail") });

        ShouldBeUnavailable(await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http()));
    }

    [Fact]
    public async Task Get_WhenTheReadTimesOutWithATaskCanceledNotFromTheCaller_IsUnavailable503()
    {
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new TaskCanceledException("secret HttpClient timeout") });

        ShouldBeUnavailable(await run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_WhenTheCallerCancels_TheCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new OperationCanceledException(cts.Token) });

        var act = () => run.GetAsync(DocumentToolsWorld.ReadableDocumentId, run.Http(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task List_WhenTheDocumentReadThrows_IsUnavailable503_NotAnEmptyList()
    {
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new InvalidOperationException("secret dataverse detail") });

        ShouldBeUnavailable(await run.ListAsync(DocumentToolsWorld.MatterId, run.Http()));
    }

    [Fact]
    public async Task List_WhenTheReadTimesOutWithATaskCanceledNotFromTheCaller_IsUnavailable503()
    {
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new TaskCanceledException("secret HttpClient timeout") });

        ShouldBeUnavailable(await run.ListAsync(DocumentToolsWorld.MatterId, run.Http(), CancellationToken.None));
    }

    [Fact]
    public async Task List_WhenTheCallerCancels_TheCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var run = new Run(new DocumentToolsWorld { DocumentReadFault = new OperationCanceledException(cts.Token) });

        var act = () => run.ListAsync(DocumentToolsWorld.MatterId, run.Http(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task List_WithNoCaller_IsUserContextRequired_AndNothingIsReadOrDecided()
    {
        var world = new DocumentToolsWorld();
        var run = new Run(world);

        var noContext = await run.ListAsync(DocumentToolsWorld.MatterId, null);
        var unauthenticated = await run.ListAsync(DocumentToolsWorld.MatterId, run.Http(authenticated: false));

        foreach (var result in new[] { noContext, unauthenticated })
        {
            result.IsError.Should().BeTrue();
            Reason(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        }

        world.Probe.Asked.Should().BeEmpty("no caller, no rights read");
        world.Documents.Invocations.Should().BeEmpty("no caller, no read");
    }
}
