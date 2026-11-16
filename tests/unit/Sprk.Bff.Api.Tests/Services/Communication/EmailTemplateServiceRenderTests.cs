// Ontology task 195 - applying a Dataverse email template fills Subject and body with FINISHED text.
//
// Before: EmailTemplateService read the template row's `subject` / `body` columns, which Dataverse stores as XSL
// stylesheets with CDATA sections, and only rewrote `{!x}` to `{{x}}`; the composer got `<?xml ... xsl:stylesheet ...` in
// Subject and `]]>` debris in the body ("Dear ]]>Valued Customer"), and an email carrying that debris was sent.
// After: it renders from the row's `subjectsafehtml` / `safehtml` columns, where every merge field is a
// `dataslug` span carrying `{!entity:attribute;fallback}`.
//
// The fixture rows have the shape of the dev environment's system templates (columns and placeholder syntax read from a
// downloaded sample; no ids). Only the Dataverse HTTP exchange is substituted (a hand-written handler, never
// Mock<HttpMessageHandler>: testing.md B1); the request building, JSON parsing and rendering are the real code.

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public sealed class EmailTemplateServiceRenderTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("61616161-6161-6161-6161-616161616161");

    // The generated XSL columns exactly as Dataverse stores them: never to be rendered.
    private const string SubjectXsl =
        "<?xml version=\"1.0\" ?><xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\">"
        + "<xsl:output method=\"text\" indent=\"no\" /><xsl:template match=\"/data\">"
        + "<![CDATA[We haven't heard from you...]]></xsl:template></xsl:stylesheet>";

    private const string BodyXsl =
        "<?xml version=\"1.0\" ?><xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\">"
        + "<xsl:output method=\"text\" indent=\"no\" /><xsl:template match=\"/data\"><![CDATA[<div><p>Dear ]]>"
        + "<xsl:choose><xsl:when test=\"account/name\"><xsl:value-of select=\"account/name\" /></xsl:when>"
        + "<xsl:otherwise>Valued Customer</xsl:otherwise></xsl:choose><![CDATA[,</p></div>]]></xsl:template></xsl:stylesheet>";

    private const string AccountBodyHtml =
        "<div>\n<p>Dear <span class=\"dataslug\" id=\"TemplateEditor0\" data-value=\"{!account:name;Valued Customer}\" "
        + "contenteditable=\"false\" style=\"background-color: #E7EFFF;\">{!Account:Account Name;Valued Customer}</span>,</p>\n"
        + "<p>We have asked <span class=\"dataslug\" id=\"TemplateEditor1\" data-value=\"{!account:ownerid/@name;a customer service representative}\" "
        + "contenteditable=\"false\" style=\"background-color: #E7EFFF;\">{!Account:Owner;a customer service representative}</span> "
        + "to contact you next week.</p>\n</div>";

    private static string Row(string? subjectSafeHtml, string? safeHtml, string? subjectXsl = SubjectXsl, string? bodyXsl = BodyXsl) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["templateid"] = Guid.NewGuid(),
            ["title"] = "Reconnect",
            ["ispersonal"] = false,
            ["templatetypecode"] = "account",
            ["subject"] = subjectXsl,
            ["body"] = bodyXsl,
            ["subjectsafehtml"] = subjectSafeHtml,
            ["safehtml"] = safeHtml,
        });

    // -- the service ---------------------------------------------------------------------------------

    [Fact]
    public async Task ASubjectColumnThatIsXsl_RendersAPlainSubject_WithNoXslOrCdata()
    {
        var (service, _) = CreateService(Row("We haven't heard from you...", AccountBodyHtml));

        var result = await Render(service, new Dictionary<string, object?>(), regardingEntityType: null);

        result.Success.Should().BeTrue();
        result.Subject.Should().Be("We haven't heard from you...");
        result.Subject.Should().NotContain("<?xml").And.NotContain("xsl:").And.NotContain("]]>");
    }

    [Fact]
    public async Task TheRenderReadsTheSafeHtmlColumns_AndNotTheXslOnes()
    {
        var (service, handler) = CreateService(Row("Subject", AccountBodyHtml));

        await Render(service, new Dictionary<string, object?>(), null);

        var query = handler.LastRequestUri!.Query;
        query.Should().Contain("subjectsafehtml").And.Contain("safehtml");
        query.Should().NotContain("subject,").And.NotContain(",body");
    }

    [Fact]
    public async Task APlaceholder_RendersTheRegardingRecordsValue_WhenTheTypeMatchesAndTheValueIsReadable()
    {
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));
        var values = new Dictionary<string, object?> { ["name"] = "Contoso Ltd", ["ownerid"] = "Alex Morgan" };

        var result = await Render(service, values, regardingEntityType: "account");

        result.Body.Should().Contain("<p>Dear Contoso Ltd,</p>");
        result.Body.Should().Contain("We have asked Alex Morgan to contact you next week.");
        result.Body.Should().NotContain("]]>").And.NotContain("<![CDATA[").And.NotContain("{!").And.NotContain("dataslug");
        result.IsHtml.Should().BeTrue();
    }

    [Fact]
    public async Task APlaceholder_RendersItsFallback_WhenNoRegardingRecordIsGiven()
    {
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));

        var result = await Render(service, new Dictionary<string, object?>(), regardingEntityType: null);

        result.Body.Should().Contain("<p>Dear Valued Customer,</p>");
        result.Body.Should().Contain("We have asked a customer service representative to contact you");
        result.Body.Should().NotContain("]]>").And.NotContain("<![CDATA[");
    }

    [Fact]
    public async Task APlaceholderWithNoValueAndNoFallback_RendersNothing()
    {
        var html = "<p>Hello <span class=\"dataslug\" data-value=\"{!contact:salutation;}\">{!Person:Salutation;}</span>!</p>";
        var (service, _) = CreateService(Row("Hi", html));

        var result = await Render(service, new Dictionary<string, object?>(), regardingEntityType: "contact");

        result.Body.Should().Be("<p>Hello !</p>");
    }

    [Fact]
    public async Task AnUnreadableAttribute_RendersItsFallback_WithoutAnError()
    {
        // Field-level security: the caller's read of the regarding record simply has no `name` column.
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));
        var values = new Dictionary<string, object?> { ["accountnumber"] = "A-1" };

        var result = await Render(service, values, regardingEntityType: "account");

        result.Success.Should().BeTrue();
        result.Body.Should().Contain("<p>Dear Valued Customer,</p>");
    }

    [Fact]
    public async Task APlaceholderForAnotherEntity_IsNeverFilledFromTheRegardingRecord()
    {
        // Must-not-fire control: a matter's `name`-like attribute must not fill an `account:name` placeholder.
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));
        var values = new Dictionary<string, object?> { ["name"] = "Acme v. Widgets" };

        var result = await Render(service, values, regardingEntityType: "sprk_matter");

        result.Body.Should().Contain("<p>Dear Valued Customer,</p>").And.NotContain("Acme v. Widgets");
    }

    [Fact]
    public async Task ARecordValue_IsHtmlEncodedInTheBody_AndPlainInTheSubject()
    {
        var subjectHtml = "Re: <span class=\"dataslug\" data-value=\"{!account:name;your account}\">{!Account:Account Name;your account}</span>";
        var (service, _) = CreateService(Row(subjectHtml, AccountBodyHtml));
        var values = new Dictionary<string, object?> { ["name"] = "Smith & <b>Sons</b>" };

        var result = await Render(service, values, regardingEntityType: "account");

        result.Body.Should().Contain("Dear Smith &amp; &lt;b&gt;Sons&lt;/b&gt;,");
        result.Subject.Should().Be("Re: Smith & <b>Sons</b>", "the subject is plain text, so the value is not markup and not escaped");
    }

    [Fact]
    public async Task ASubjectWithMarkupAndLineBreaks_BecomesOneLineOfPlainText()
    {
        var subjectHtml = "Microsoft  <b>Dynamics</b>:\n<span class=\"dataslug\" data-value=\"{!account:name;Bulk task}\">{!System Job:Name;Bulk task}</span>&nbsp;is complete";
        var (service, _) = CreateService(Row(subjectHtml, AccountBodyHtml));

        var result = await Render(service, new Dictionary<string, object?>(), regardingEntityType: null);

        result.Subject.Should().Be("Microsoft Dynamics: Bulk task is complete");
    }

    [Fact]
    public async Task AMultiAttributeToken_JoinsTheReadableValues_AndFallsBackWhenNoneIsReadable()
    {
        var html = "<p><span class=\"dataslug\" data-value=\"{!contact:address1_line1;contact:address1_line2;contact:address1_line3;No Address Provided}\">x</span></p>";
        var (service, _) = CreateService(Row("s", html));

        var some = await Render(service, new Dictionary<string, object?> { ["address1_line1"] = "1 Main St", ["address1_line3"] = "Suite 4" }, "contact");
        var none = await Render(service, new Dictionary<string, object?>(), "contact");

        some.Body.Should().Be("<p>1 Main St, Suite 4</p>");
        none.Body.Should().Be("<p>No Address Provided</p>");
    }

    [Fact]
    public async Task ABareTokenLeftInTheText_IsResolvedToo_AndNoTokenDebrisRemains()
    {
        var (service, _) = CreateService(Row("s", "<p>Hi {!contact:fullname;friend}, welcome {!contact:nickname;}.</p>"));

        var result = await Render(service, new Dictionary<string, object?>(), "contact");

        result.Body.Should().Be("<p>Hi friend, welcome .</p>");
    }

    [Fact]
    public async Task DataverseShapedValues_RenderAsEmailText()
    {
        var html = "<p><span data-value=\"{!sprk_matter:sprk_budget;}\">x</span> | <span data-value=\"{!sprk_matter:sprk_opened;}\">x</span> | <span data-value=\"{!sprk_matter:sprk_secure;}\">x</span> | <span data-value=\"{!sprk_matter:sprk_count;}\">x</span></p>";
        var (service, _) = CreateService(Row("s", html));
        var values = new Dictionary<string, object?>
        {
            ["sprk_budget"] = 25000.0000000000m,
            ["sprk_opened"] = new DateTime(2026, 6, 8, 2, 59, 6, DateTimeKind.Utc),
            ["sprk_secure"] = false,
            ["sprk_count"] = 5,
        };

        var result = await Render(service, values, "sprk_matter");

        result.Body.Should().Be("<p>25,000 | June 8, 2026 2:59 AM UTC | No | 5</p>");
    }

    [Fact]
    public async Task ASpanThatIsNotAMergeField_IsLeftAlone()
    {
        var (service, _) = CreateService(Row("s", "<p><span data-value=\"layout\" style=\"color:red\">keep</span></p>"));

        var result = await Render(service, new Dictionary<string, object?>(), null);

        result.Body.Should().Be("<p><span data-value=\"layout\" style=\"color:red\">keep</span></p>");
    }

    [Fact]
    public async Task ATemplateWithNoSafeHtmlBody_IsRefused_NotRenderedFromTheXsl()
    {
        var (service, _) = CreateService(Row("Subject", safeHtml: null));

        var result = await Render(service, new Dictionary<string, object?>(), null);

        result.Success.Should().BeFalse();
        result.Subject.Should().BeNull();
        result.Body.Should().BeNull();
        result.Error.Should().NotContain("not found", "that wording is the route's 404");
    }

    // -- through the route (the refusal shape and the caller-scoped read are the route's) ----------------

    [Fact]
    public async Task Route_RendersFinishedText_FromTheCallersRead()
    {
        var (service, _) = CreateService(Row("Re: <span data-value=\"{!account:name;your account}\">x</span>", AccountBodyHtml));
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        query.Setup(q => q.QueryAsync("accounts", It.IsAny<string?>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"name\":\"Contoso Ltd\"}")! });

        var result = await InvokeRoute(service, query.Object, new CommunicationTemplateRenderRequest
        {
            TemplateId = Guid.NewGuid(),
            RegardingEntityType = "Account",
            RegardingRecordId = Guid.NewGuid(),
        });

        var ok = result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>().Subject;
        ok.Value!.Subject.Should().Be("Re: Contoso Ltd");
        ok.Value.Body.Should().Contain("Dear Contoso Ltd,").And.NotContain("]]>").And.NotContain("<?xml");
    }

    [Fact]
    public async Task Route_WhenTheCallerCannotReadTheAttribute_StillReturns200WithTheFallback()
    {
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        // Dataverse omits a column the caller's field security hides, and the row itself is still returned.
        query.Setup(q => q.QueryAsync("accounts", It.IsAny<string?>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"accountnumber\":\"A-1\"}")! });

        var result = await InvokeRoute(service, query.Object, new CommunicationTemplateRenderRequest
        {
            TemplateId = Guid.NewGuid(),
            RegardingEntityType = "account",
            RegardingRecordId = Guid.NewGuid(),
        });

        var ok = result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>().Subject;
        ok.Value!.Body.Should().Contain("Dear Valued Customer,");
    }

    [Fact]
    public async Task Route_WhenTheCallerCannotSeeTheRecordRow_RendersFallbacks()
    {
        var (service, _) = CreateService(Row("Hello", AccountBodyHtml));
        var query = new Mock<IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        query.Setup(q => q.QueryAsync("accounts", It.IsAny<string?>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        var result = await InvokeRoute(service, query.Object, new CommunicationTemplateRenderRequest
        {
            TemplateId = Guid.NewGuid(),
            RegardingEntityType = "account",
            RegardingRecordId = Guid.NewGuid(),
        });

        var ok = result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>().Subject;
        ok.Value!.Body.Should().Contain("Dear Valued Customer,");
    }

    [Fact]
    public async Task Route_AnUnrenderableTemplate_ReturnsTheExistingRefusalShape_NotA500()
    {
        var (service, _) = CreateService(Row("Subject", safeHtml: null));

        var result = await InvokeRoute(service, Mock.Of<IImpersonatedCommunicationQuery>(MockBehavior.Strict),
            new CommunicationTemplateRenderRequest { TemplateId = Guid.NewGuid() });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Title.Should().Be("Template Render Failed");
    }

    [Fact]
    public async Task Route_AMissingTemplate_StillAnswersTheNotFoundBody()
    {
        var templateId = Guid.NewGuid();
        var (service, _) = CreateService("{}", HttpStatusCode.NotFound);

        var result = await InvokeRoute(service, Mock.Of<IImpersonatedCommunicationQuery>(MockBehavior.Strict),
            new CommunicationTemplateRenderRequest { TemplateId = templateId });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Detail.Should().Be($"Email template not found: {templateId}");
    }

    // -- support ---------------------------------------------------------------------------------------

    private static Task<EmailTemplateResult> Render(
        EmailTemplateService service, Dictionary<string, object?> values, string? regardingEntityType) =>
        service.FetchAndRenderAsync(
            Guid.NewGuid(), values, regardingEntityType, "https://test.crm.dynamics.com", "token", CancellationToken.None);

    private static Task<IResult> InvokeRoute(
        EmailTemplateService service, IImpersonatedCommunicationQuery query, CommunicationTemplateRenderRequest request)
    {
        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        return CommunicationTemplateEndpoints.RenderTemplateAsync(
            request,
            service,
            query,
            resolver.Object,
            new FakeTokenCredential(),
            Options.Create(new DataverseOptions { EnvironmentUrl = "https://test.crm.dynamics.com" }),
            Mock.Of<ILogger<CommunicationTemplateRenderResponse>>(),
            new DefaultHttpContext(),
            CancellationToken.None);
    }

    private static (EmailTemplateService Service, TemplateHandler Handler) CreateService(
        string templateJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new TemplateHandler(status, templateJson);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Dataverse")).Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new EmailTemplateService(
            Mock.Of<ITemplateEngine>(),
            factory.Object,
            Mock.Of<ILogger<EmailTemplateService>>());
        return (service, handler);
    }

    /// <summary>Answers every request with the configured status and JSON body, remembering the last request URI.</summary>
    private sealed class TemplateHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
