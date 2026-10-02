using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Task 075: an exception the save did not expect is a server fault. <c>POST /api/office/save</c> used to turn it into
/// <c>OFFICE_INTERNAL</c> → HTTP <b>400</b> (the default arm of <c>MapSaveErrorToProblem</c>), with
/// <c>"Save failed: " + ex.Message</c> as the detail — blaming the request and putting server internals on the wire.
/// </summary>
/// <remarks>
/// The fault is <see cref="OfficeVersionSaveWorld.FailNextDocumentCreate"/>: the <c>sprk_document</c> create throws
/// after the job row and the SPE upload exist, which no refusal code describes.
/// </remarks>
public class OfficeSaveUnexpectedErrorContractTests
{
    private static readonly byte[] Docx = MinimalDocx.Create("unexpected error");

    [Fact]
    public async Task Post_Save_WhenTheServerThrowsUnexpectedly_Returns500ProblemDetails_WithoutTheExceptionText()
    {
        var world = new OfficeVersionSaveWorld { FailNextDocumentCreate = true };
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Docx));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
        problem.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();
        problem.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
        body.Should().NotContain("Dataverse refused", "the exception's message is the server's business, not the caller's")
            .And.NotContain("InvalidOperationException")
            .And.NotContain(" at ", "no stack trace");
    }
}
