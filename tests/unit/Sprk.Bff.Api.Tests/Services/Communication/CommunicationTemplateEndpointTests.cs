using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior tests for <see cref="CommunicationTemplateEndpoints.RenderTemplateAsync"/> — the
/// email composer's "insert template" handler. Verifies the mapping from
/// <see cref="IEmailTemplateService"/> results to HTTP results, that a request with no regarding
/// still renders (with an empty merge-variable set), and (task 161) that the regarding record is read AS THE
/// CALLER through <see cref="IImpersonatedCommunicationQuery"/> with the merge text unchanged. The template
/// fetch/render is mocked at the <see cref="IEmailTemplateService"/> module boundary and the read at the
/// impersonated-query seam (allowed per ADR-038 — neither is an HttpMessageHandler mock).
/// </summary>
public class CommunicationTemplateEndpointTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("61616161-6161-6161-6161-616161616161");

    private readonly Mock<IEmailTemplateService> _emailTemplateServiceMock = new();
    private readonly Mock<IImpersonatedCommunicationQuery> _queryMock = new(MockBehavior.Strict);
    private readonly Mock<ICallerSystemUserResolver> _callerResolverMock = new();
    private readonly Mock<ILogger<CommunicationTemplateRenderResponse>> _loggerMock = new();
    private readonly IOptions<DataverseOptions> _dataverseOptions =
        Options.Create(new DataverseOptions { EnvironmentUrl = "https://test.crm.dynamics.com" });

    public CommunicationTemplateEndpointTests()
    {
        _callerResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
    }

    private Task<IResult> InvokeAsync(CommunicationTemplateRenderRequest request) =>
        CommunicationTemplateEndpoints.RenderTemplateAsync(
            request,
            _emailTemplateServiceMock.Object,
            _queryMock.Object,
            _callerResolverMock.Object,
            new FakeTokenCredential(),
            _dataverseOptions,
            _loggerMock.Object,
            new DefaultHttpContext(),
            CancellationToken.None);

    [Fact]
    public async Task RenderTemplate_WhenRenderSucceeds_ReturnsOkWithSubjectBodyIsHtml()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                templateId,
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Ok("Rendered Subject", "<p>Rendered Body</p>", isHtml: true));

        // Act
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        // Assert
        var ok = result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>().Subject;
        ok.Value!.Subject.Should().Be("Rendered Subject");
        ok.Value.Body.Should().Be("<p>Rendered Body</p>");
        ok.Value.IsHtml.Should().BeTrue();
    }

    [Fact]
    public async Task RenderTemplate_WhenServiceReturnsError_ReturnsProblem400()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Fail("Template rendering failed: bad placeholder"));

        // Act
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Detail.Should().Contain("bad placeholder");
    }

    [Fact]
    public async Task RenderTemplate_WhenTemplateNotFound_ReturnsProblem404()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Fail($"Email template not found: {templateId}"));

        // Act
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RenderTemplate_WhenTheServiceReportsNotFoundInOtherWords_AnswersTheSameBodyTheRecordFilterDeniesWith()
    {
        // Unknown equals denied (task 161): the record filter answers an UNREADABLE template with
        // TemplateNotFound(templateId); a MISSING one must get that same body even if the service rewords its error.
        var templateId = Guid.NewGuid();
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailTemplateResult.Fail("The requested template was not found in this environment."));

        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var filterDeny = CommunicationTemplateEndpoints.TemplateNotFound(templateId).Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Title.Should().Be(filterDeny.ProblemDetails.Title).And.Be("Template Not Found");
        problem.ProblemDetails.Detail.Should().Be(filterDeny.ProblemDetails.Detail).And.Be($"Email template not found: {templateId}");
    }

    [Fact]
    public async Task RenderTemplate_WhenNoRegarding_CallsRenderWithEmptyVariablesAndDoesNotReadDataverse()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        Dictionary<string, object?>? capturedVariables = null;
        string? capturedType = "unset";
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Dictionary<string, object?>, string?, string, string, CancellationToken>(
                (_, vars, type, _, _, _) => { capturedVariables = vars; capturedType = type; })
            .ReturnsAsync(EmailTemplateResult.Ok("s", "b", isHtml: true));

        // Act — no RegardingEntityType / RegardingRecordId
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        // Assert — the strict query mock fails the test on any Dataverse read
        result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>();
        capturedVariables.Should().NotBeNull();
        capturedVariables!.Should().BeEmpty("no regarding record was supplied");
        capturedType.Should().BeNull("with no regarding record no placeholder may be filled");
    }

    [Fact]
    public async Task RenderTemplate_WhenRegardingProvided_ReadsTheRecordAsTheCallerIntoMergeVariables()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        string? capturedSet = null;
        string? capturedQuery = null;
        Guid capturedCaller = Guid.Empty;
        _queryMock
            .Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, Guid, CancellationToken>((set, odata, caller, _) =>
            {
                capturedSet = set;
                capturedQuery = odata;
                capturedCaller = caller;
            })
            .ReturnsAsync(Rows($$"""
                { "sprk_mattername": "Acme v. Widgets", "sprk_status": 2,
                  "sprk_status@OData.Community.Display.V1.FormattedValue": "Open" }
                """));

        Dictionary<string, object?>? capturedVariables = null;
        string? capturedType = "unset";
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Dictionary<string, object?>, string?, string, string, CancellationToken>(
                (_, vars, type, _, _, _) => { capturedVariables = vars; capturedType = type; })
            .ReturnsAsync(EmailTemplateResult.Ok("s", "b", isHtml: true));

        // Act — a mixed-case type is read as its logical name
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest
        {
            TemplateId = templateId,
            RegardingEntityType = "sprk_Matter",
            RegardingRecordId = recordId,
        });

        // Assert — impersonated as the resolved caller, on the live-verified set, keyed by the primary id
        result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>();
        capturedSet.Should().Be("sprk_matters");
        capturedQuery.Should().Be($"$filter=sprk_matterid eq {recordId}&$top=1");
        capturedCaller.Should().Be(CallerSystemUserId);
        capturedType.Should().Be("sprk_matter", "the renderer is told the regarding type so it fills only that entity's placeholders");
        capturedVariables!["sprk_mattername"].Should().Be("Acme v. Widgets");
        capturedVariables["sprk_status"].Should().Be("Open", "an option set merges as the caller's formatted label, not its integer");
        capturedVariables.Keys.Should().NotContain(k => k.Contains('@'), "annotations are not merge variables");
    }

    [Fact]
    public async Task RenderTemplate_WhenTheCallerReadFaults_RefusesWithANon2xxAndDoesNotRender()
    {
        _queryMock
            .Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse said 500"));

        var act = () => InvokeAsync(new CommunicationTemplateRenderRequest
        {
            TemplateId = Guid.NewGuid(),
            RegardingEntityType = "sprk_matter",
            RegardingRecordId = Guid.NewGuid(),
        });

        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(502);
        ex.Which.Code.Should().Be("TEMPLATE_MERGE_READ_FAILED");
        _emailTemplateServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RenderTemplate_WhenTheCallerIsUnresolved_RefusesWithANon2xxAndDoesNotRead()
    {
        _callerResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var act = () => InvokeAsync(new CommunicationTemplateRenderRequest
        {
            TemplateId = Guid.NewGuid(),
            RegardingEntityType = "sprk_matter",
            RegardingRecordId = Guid.NewGuid(),
        });

        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(403);
        _emailTemplateServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RenderTemplate_WhenTemplateIdEmpty_ThrowsValidationProblem()
    {
        // Act
        var act = () => InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = Guid.Empty });

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
    }

    // ── What the recipient sees (task 195 fix round) ────────────────────────────────────────────────────
    //
    // The merge values are the CALLER's own formatted text: the impersonated read annotates every typed column with
    // @OData.Community.Display.V1.FormattedValue in the caller's language, currency and time zone, and the route hands
    // that string to the renderer verbatim. The shapes below are the live JSON shapes for those columns (statuscode
    // option set, money, createdon in UTC with a local-zone annotation, a two-option column with a custom label).
    // The render is the REAL EmailTemplateService over a Dataverse-shaped safehtml row; the earlier test ran the
    // variables through RenderFromContent (Handlebars), a path no Dataverse template takes, and pinned strings no
    // user sees.
    private const string MergeSubjectSafeHtml =
        "Matter <span class=\"dataslug\" data-value=\"{!sprk_matter:sprk_mattername;}\">x</span>"
        + " (<span class=\"dataslug\" data-value=\"{!sprk_matter:sprk_invoicecount;}\">x</span> invoices)";

    private const string MergeBodySafeHtml =
        "<p>Status <span data-value=\"{!sprk_matter:statuscode;}\">x</span>;"
        + " budget <span data-value=\"{!sprk_matter:sprk_totalbudget;}\">x</span>;"
        + " owner <span data-value=\"{!sprk_matter:ownerid/@name;}\">x</span>;"
        + " opened <span data-value=\"{!sprk_matter:createdon;}\">x</span>;"
        + " secure <span data-value=\"{!sprk_matter:sprk_issecure;}\">x</span></p>";

    [Fact]
    public async Task RenderTemplate_EachValueKindRendersAsTheCallersFormattedText()
    {
        var recordId = Guid.NewGuid();
        _queryMock
            .Setup(q => q.QueryAsync("sprk_matters", It.IsAny<string?>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows($$"""
                {
                  "@odata.etag": "W/\"25731381\"",
                  "sprk_matterid": "{{recordId}}",
                  "sprk_mattername": "Acme v. Widgets",
                  "sprk_invoicecount": 5,
                  "sprk_invoicecount@OData.Community.Display.V1.FormattedValue": "5",
                  "statuscode": 1,
                  "statuscode@OData.Community.Display.V1.FormattedValue": "Draft",
                  "sprk_totalbudget": 25000.5000000000,
                  "sprk_totalbudget@OData.Community.Display.V1.FormattedValue": "$25,000.50",
                  "_ownerid_value": "1d02f31c-1872-f011-b4cb-7c1e52671ad0",
                  "_ownerid_value@OData.Community.Display.V1.FormattedValue": "Ralph Schroeder",
                  "createdon": "2026-06-08T02:59:06Z",
                  "createdon@OData.Community.Display.V1.FormattedValue": "6/7/2026 10:59 PM",
                  "sprk_issecure": false,
                  "sprk_issecure@OData.Community.Display.V1.FormattedValue": "Not Secure"
                }
                """));

        var service = CreateRealRenderer(MergeSubjectSafeHtml, MergeBodySafeHtml);

        var result = await CommunicationTemplateEndpoints.RenderTemplateAsync(
            new CommunicationTemplateRenderRequest
            {
                TemplateId = Guid.NewGuid(),
                RegardingEntityType = "sprk_matter",
                RegardingRecordId = recordId,
            },
            service,
            _queryMock.Object,
            _callerResolverMock.Object,
            new FakeTokenCredential(),
            _dataverseOptions,
            _loggerMock.Object,
            new DefaultHttpContext(),
            CancellationToken.None);

        var ok = result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>().Subject;
        ok.Value!.Subject.Should().Be("Matter Acme v. Widgets (5 invoices)");
        ok.Value.Body.Should().Be(
            "<p>Status Draft; budget $25,000.50; owner Ralph Schroeder; opened 6/7/2026 10:59 PM; secure Not Secure</p>");
    }

    [Fact]
    public void ToMergeVariables_AnnotatedValuesAreTheFormattedText_UnannotatedKeepTheirRawValues()
    {
        var variables = CommunicationTemplateEndpoints.ToMergeVariables(Rows("""
            {
              "sprk_duedate": "2026-03-30",
              "sprk_duedate@OData.Community.Display.V1.FormattedValue": "3/30/2026",
              "statuscode": 2,
              "statuscode@OData.Community.Display.V1.FormattedValue": "Open",
              "sprk_budget": 25000.5000000000,
              "sprk_budget@OData.Community.Display.V1.FormattedValue": "$25,000.50",
              "sprk_reference": "2026-03-30",
              "sprk_count": 7,
              "sprk_flag": true,
              "_sprk_client_value": "11111111-2222-3333-4444-555555555555"
            }
            """)[0]);

        variables["sprk_duedate"].Should().Be("3/30/2026");
        variables["statuscode"].Should().Be("Open");
        variables["sprk_budget"].Should().Be("$25,000.50");
        variables["sprk_reference"].Should().Be("2026-03-30", "a column with no annotation keeps its raw string");
        variables["sprk_count"].Should().Be(7, "a column with no annotation keeps its raw number");
        variables["sprk_flag"].Should().Be(true, "a column with no annotation keeps its raw boolean");
        variables["sprk_client"].Should().Be("11111111-2222-3333-4444-555555555555",
            "a lookup with no name renders its id");
    }

    /// <summary>
    /// The REAL <see cref="EmailTemplateService"/>, fetching a Dataverse-shaped <c>template</c> row (only the HTTP
    /// exchange is substituted, with a hand-written handler) so the test pins the text a user sees.
    /// </summary>
    private static EmailTemplateService CreateRealRenderer(string subjectSafeHtml, string safeHtml)
    {
        var row = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["templateid"] = Guid.NewGuid(),
            ["title"] = "Matter summary",
            ["ispersonal"] = false,
            ["templatetypecode"] = "sprk_matter",
            ["subjectsafehtml"] = subjectSafeHtml,
            ["safehtml"] = safeHtml,
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Dataverse"))
            .Returns(() => new HttpClient(new StaticJsonHandler(row), disposeHandler: false));

        return new EmailTemplateService(
            Mock.Of<Sprk.Bff.Api.Services.Ai.ITemplateEngine>(),
            factory.Object,
            Mock.Of<ILogger<EmailTemplateService>>());
    }

    private sealed class StaticJsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static IReadOnlyList<Dictionary<string, JsonElement>> Rows(string json) =>
        new[] { JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)! };

    /// <summary>Minimal <see cref="TokenCredential"/> test double returning a static token (no I/O).</summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
