using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// The resolution filter on <c>POST /api/documents/resolve-identity</c> (spaarkeai-word-add-in-r1 task 012). It
/// must hand <see cref="DocumentAuthorizationFilter"/> a canonical <c>documentId</c> when — and only when — there is
/// something to authorize, and must never itself put document metadata in a response. The authorization decision
/// (403 with no metadata) belongs to <see cref="DocumentAuthorizationFilter"/> and is exercised end-to-end by the
/// route's contract test (task 016); <c>AuthorizationService</c> is concrete and non-virtual, so it cannot be
/// substituted here.
/// </summary>
public class DocumentUrlIdentityFilterTests
{
    private const string SpeUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private bool _nextCalled;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public async Task MissingOrMalformedUrl_Is400_AndNothingDownstreamRuns(string? url)
    {
        var http = Context(DocumentUrlIdentityResolutionTests.Spe(Outcome(SpeSharedItemOutcome.NotFound)));
        var request = url is null ? null : new ResolveDocumentIdentityRequest(url);

        var act = async () => await Invoke(http, request);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LocalFile_ShortCircuits_200NoIdentity_WithoutReachingAuthorization()
    {
        var http = Context(DocumentUrlIdentityResolutionTests.Spe(Outcome(SpeSharedItemOutcome.NotFound)));

        var result = await Invoke(http, new ResolveDocumentIdentityRequest("file:///C:/Users/me/brief.docx"));

        AssertNoIdentity(result, DocumentUrlIdentityResolution.ReasonNotCloudDocument);
        _nextCalled.Should().BeFalse("with nothing resolved there is nothing for the authorization filter to decide");
        http.Request.RouteValues.Should().NotContainKey("documentId");
    }

    [Fact]
    public async Task UnresolvableUrl_ShortCircuits_200NoIdentity_CarryingNoMetadata()
    {
        var http = Context(DocumentUrlIdentityResolutionTests.Spe(Outcome(SpeSharedItemOutcome.NotFound)));

        var result = await Invoke(http, new ResolveDocumentIdentityRequest(SpeUrl));

        AssertNoIdentity(result, DocumentUrlIdentityResolution.ReasonNotResolvable);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ResolvedDocument_WritesTheCanonicalIdForTheAuthorizationFilter_AndDefersTheResponseToIt()
    {
        var documentId = Guid.Parse("3D3E6CBF-1111-4222-8333-944455556666");
        _dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", documentId) { ["sprk_graphdriveid"] = DriveId });
        var http = Context(DocumentUrlIdentityResolutionTests.Spe(
            DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId)));

        var result = await Invoke(http, new ResolveDocumentIdentityRequest(SpeUrl));

        // The id is written under the key DocumentAuthorizationFilter reads, in the ADR-044 canonical form.
        http.Request.RouteValues["documentId"].Should().Be("3d3e6cbf-1111-4222-8333-944455556666");
        http.Items[DocumentUrlIdentityFilter.ResolutionItemKey]
            .Should().BeOfType<DocumentUrlIdentityResolution.Resolution>()
            .Which.DocumentId.Should().Be(documentId);

        // Whatever is downstream (authorization, then the handler) owns the response; this filter adds nothing.
        _nextCalled.Should().BeTrue();
        result.Should().Be(DownstreamResult);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("documentId")]
    public async Task ARouteThatAlreadyBindsADocumentKey_IsRefused_BeforeAnythingIsResolved(string key)
    {
        // DocumentAuthorizationFilter reads `id` before `documentId`: a pre-bound key would be authorized instead of
        // the resolved id. The filter must refuse such a mapping rather than authorize the wrong document.
        var spe = DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor(DriveId, ItemId));
        var http = Context(spe);
        http.Request.RouteValues[key] = "11111111-1111-1111-1111-111111111111";

        var act = async () => await Invoke(http, new ResolveDocumentIdentityRequest(SpeUrl));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _nextCalled.Should().BeFalse();
        spe.Verify(s => s.ResolveSharedItemAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private const string DownstreamResult = "downstream-result";

    private DefaultHttpContext Context(Mock<SpeFileStore> spe)
    {
        var services = new ServiceCollection()
            .AddSingleton<SpeFileStore>(spe.Object)
            .AddSingleton<IGenericEntityService>(_dataverse.Object)
            .AddSingleton<ILogger<DocumentUrlIdentityFilter>>(NullLogger<DocumentUrlIdentityFilter>.Instance)
            .BuildServiceProvider();

        return new DefaultHttpContext { RequestServices = services };
    }

    private ValueTask<object?> Invoke(HttpContext http, ResolveDocumentIdentityRequest? request)
    {
        var context = new DefaultEndpointFilterInvocationContext(http, request);
        return new DocumentUrlIdentityFilter().InvokeAsync(context, _ =>
        {
            _nextCalled = true;
            return ValueTask.FromResult<object?>(DownstreamResult);
        });
    }

    private static void AssertNoIdentity(object? result, string reason)
    {
        var ok = result.Should().BeOfType<Ok<DocumentIdentityResponse>>().Subject;
        ok.Value!.Resolved.Should().BeFalse();
        ok.Value.Reason.Should().Be(reason);
        ok.Value.DocumentId.Should().BeNull();
        ok.Value.DocumentName.Should().BeNull();
        ok.Value.FileName.Should().BeNull();
        ok.Value.RelatedRecord.Should().BeNull();
    }

    private static SpeSharedItemResolution Outcome(SpeSharedItemOutcome outcome)
        => new(outcome, null, null, null, Array.Empty<SpeSharedItemAttempt>());
}
