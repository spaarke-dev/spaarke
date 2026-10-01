using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// GitHub #1075 (ISS-014): the Outlook ribbon quick-save sent the target's LOGICAL name (<c>sprk_matter</c>) as
/// <c>targetEntity.entityType</c>, and <c>POST /api/office/save</c> accepts only the friendly names, so every
/// predicted quick-save was refused with 400 <c>OFFICE_002</c>.
/// </summary>
/// <remarks>
/// <para>The fix is in the client (<c>quickSaveHelpers.ts</c> now sends <c>target.entityType</c>), pinned by
/// <c>quickSaveHelpers.test.ts</c> and the ribbon's <c>commands.test.ts</c>. This test pins the SERVER contract the
/// client must meet, so a future change to either side that re-opens the mismatch is caught here:
/// the save's association type is the friendly name; the logical name is refused.</para>
///
/// <para>Not "accept both" on purpose: <c>ValidateSaveRequest</c> and the finalization worker both key on the
/// friendly name, and widening one without the other would let a logical-name save through the gate and then
/// file it unassociated.</para>
///
/// <para>The caller holds AppendTo, so the association gate itself lets both requests through; the refusal
/// asserted below is the handler's type validation, which is where the bug bit.</para>
/// </remarks>
[Trait("status", "new")]
public class Issue1075_QuickSaveLogicalNameTests : IClassFixture<OfficeFilingAccessTestFixture>
{
    private readonly OfficeFilingAccessTestFixture _fixture;

    public Issue1075_QuickSaveLogicalNameTests(OfficeFilingAccessTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task OfficeSave_WithTheLogicalNameAsTheTargetType_IsRefusedWithOffice002()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");

        var response = await client.PostAsJsonAsync(
            "/api/office/save", QuickSaveBody("sprk_matter", OfficeFilingAccessTestFixture.MatterId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_002",
            "this is the refusal every pre-fix ribbon quick-save received");
    }

    [Fact]
    public async Task OfficeSave_WithTheFriendlyNameAsTheTargetType_IsNotRefusedForItsType()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,AppendToAccess");

        var response = await client.PostAsJsonAsync(
            "/api/office/save", QuickSaveBody("Matter", OfficeFilingAccessTestFixture.MatterId));

        (await ErrorCodeOf(response)).Should().NotBe("OFFICE_002",
            "the friendly name is the type the save accepts, and what the fixed client sends");
    }

    /// <summary>The body shape <c>buildEmailSaveRequest</c> sends (an email save filed to the prediction).</summary>
    private static object QuickSaveBody(string entityType, Guid entityId) => new
    {
        contentType = 0, // SaveContentType.Email
        email = new { subject = "Re: Acme", senderEmail = "counsel@example.com", senderName = "Counsel" },
        targetEntity = new { entityType, entityId, displayName = "Acme Onboarding" }
    };

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("errorCode", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
