using System.Threading;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior tests for <see cref="CommunicationTemplateEndpoints.RenderTemplateAsync"/> — the
/// email composer's "insert template" handler. Verifies the mapping from
/// <see cref="IEmailTemplateService"/> results to HTTP results and that a request with no regarding
/// still renders (with an empty merge-variable set). The template fetch/render itself is mocked at the
/// <see cref="IEmailTemplateService"/> module boundary (allowed per ADR-038 — not an HttpMessageHandler mock).
/// </summary>
public class CommunicationTemplateEndpointTests
{
    private readonly Mock<IEmailTemplateService> _emailTemplateServiceMock = new();
    private readonly Mock<IGenericEntityService> _genericEntityServiceMock = new();
    private readonly Mock<ILogger<CommunicationTemplateRenderResponse>> _loggerMock = new();
    private readonly IOptions<DataverseOptions> _dataverseOptions =
        Options.Create(new DataverseOptions { EnvironmentUrl = "https://test.crm.dynamics.com" });

    private Task<IResult> InvokeAsync(CommunicationTemplateRenderRequest request) =>
        CommunicationTemplateEndpoints.RenderTemplateAsync(
            request,
            _emailTemplateServiceMock.Object,
            _genericEntityServiceMock.Object,
            new FakeTokenCredential(),
            _dataverseOptions,
            _loggerMock.Object,
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
    public async Task RenderTemplate_WhenNoRegarding_CallsRenderWithEmptyVariablesAndDoesNotReadDataverse()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        Dictionary<string, object?>? capturedVariables = null;
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Dictionary<string, object?>, string, string, CancellationToken>(
                (_, vars, _, _, _) => capturedVariables = vars)
            .ReturnsAsync(EmailTemplateResult.Ok("s", "b", isHtml: true));

        // Act — no RegardingEntityType / RegardingRecordId
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest { TemplateId = templateId });

        // Assert
        result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>();
        capturedVariables.Should().NotBeNull();
        capturedVariables!.Should().BeEmpty("no regarding record was supplied");
        _genericEntityServiceMock.Verify(
            g => g.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the Dataverse read must be skipped when no regarding record is provided");
    }

    [Fact]
    public async Task RenderTemplate_WhenRegardingProvided_ProjectsRecordAttributesIntoMergeVariables()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var record = new Entity("sprk_matter") { Id = recordId };
        record["sprk_name"] = "Acme v. Widgets";
        record["sprk_status"] = new OptionSetValue(2);

        _genericEntityServiceMock
            .Setup(g => g.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { record }));

        Dictionary<string, object?>? capturedVariables = null;
        _emailTemplateServiceMock
            .Setup(s => s.FetchAndRenderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Dictionary<string, object?>, string, string, CancellationToken>(
                (_, vars, _, _, _) => capturedVariables = vars)
            .ReturnsAsync(EmailTemplateResult.Ok("s", "b", isHtml: true));

        // Act
        var result = await InvokeAsync(new CommunicationTemplateRenderRequest
        {
            TemplateId = templateId,
            RegardingEntityType = "sprk_matter",
            RegardingRecordId = recordId,
        });

        // Assert
        result.Should().BeOfType<Ok<CommunicationTemplateRenderResponse>>();
        capturedVariables.Should().NotBeNull();
        capturedVariables!["sprk_name"].Should().Be("Acme v. Widgets");
        capturedVariables["sprk_status"].Should().Be(2, "OptionSetValue is unwrapped to its integer value");
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

    /// <summary>Minimal <see cref="TokenCredential"/> test double returning a static token (no I/O).</summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-dataverse-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
