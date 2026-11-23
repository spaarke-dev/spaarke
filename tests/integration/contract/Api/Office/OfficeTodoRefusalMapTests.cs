using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 030a (fix round 2): <see cref="OfficeService.MapTodoRefusal"/> keeps the To Do route's code and status
/// (403 OFFICE_010) for a TRANSIENT refusal but tells the user to try again, instead of the generic "Failed to create the To Do."
/// A refusal a retry would not clear stays on the route's generic path (<see langword="null"/>).
/// </summary>
public class OfficeTodoRefusalMapTests
{
    private const string TryAgainDetail =
        "The To Do could not be created right now, and nothing was saved. Try again in a moment. "
        + "If it keeps failing, contact your administrator.";

    /// <summary>The kind is carried by name: the enum is internal, and a public theory cannot expose it.</summary>
    private static RecordCreationRefusal Refusal(string kindName, string code, int? upstreamStatus = null) =>
        new(Enum.Parse<RecordCreationRefusalKind>(kindName), code, "service detail that must never reach the user")
        {
            ClientFailure = upstreamStatus is { } status
                ? DataverseUserResponse.Fail(status, DataverseUserClientErrorCodes.ServiceError, "dataverse text that must never reach the user")
                : null,
        };

    [Fact]
    public void TheTryAgainDetail_IsPinned()
    {
        OfficeService.TransientDetail.Should().Be(TryAgainDetail);
    }

    [Theory]
    [InlineData("Unavailable", RecordCreationRefusal.CheckFailedCode, null)]
    [InlineData("Unavailable", RecordCreationRefusal.OwnerUnavailableCode, null)]
    [InlineData("Unavailable", RecordCreationRefusal.StampUnavailableCode, null)]
    [InlineData("Undecided", RecordCreationRefusal.NoDecisionCode, null)]
    [InlineData("Undecided", RecordOwnerRefusal.SecureOwnerTeamUnresolved, null)]
    [InlineData("Undecided", "sdap.provision.creator_share_failed", null)]
    [InlineData("Upstream", RecordCreationRefusal.UpstreamCode, 429)]
    [InlineData("Upstream", RecordCreationRefusal.UpstreamCode, 503)]
    [InlineData("Upstream", RecordCreationRefusal.UpstreamCode, 504)]
    public void ATransientRefusal_IsOffice010_403_WithTheTryAgainDetail(string kind, string code, int? upstreamStatus)
    {
        var problem = OfficeService.MapTodoRefusal(Refusal(kind, code, upstreamStatus), "sprk_matter");

        problem.Should().NotBeNull();
        problem!.Code.Should().Be(OfficeErrorCodes.CannotCreateEntity).And.Be("OFFICE_010");
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.Title.Should().Be(OfficeErrorCodes.GetTitle(OfficeErrorCodes.CannotCreateEntity));
        problem.Detail.Should().Be(TryAgainDetail);
        problem.Detail.Should().NotContain("service detail").And.NotContain("dataverse text").And.NotContain(code);
    }

    [Theory]
    [InlineData("Upstream", 400)]
    [InlineData("Upstream", 403)]
    [InlineData("Upstream", 404)]
    [InlineData("Forbidden", null)]
    [InlineData("BlockedColumn", null)]
    [InlineData("Invalid", null)]
    [InlineData("PrincipalUnresolved", null)]
    [InlineData("Conflict", null)]
    public void ARefusalARetryWouldNotClear_StaysOnTheRoutesGenericPath(string kind, int? upstreamStatus)
    {
        OfficeService.MapTodoRefusal(Refusal(kind, "record_creation.any", upstreamStatus), "sprk_matter")
            .Should().BeNull("the route answers its generic 403 OFFICE_010 \"Failed to create the To Do.\"");
    }

    [Fact]
    public void TheOwnerAndAccessMappings_AreUnchanged()
    {
        OfficeService.MapTodoRefusal(Refusal("OwnerUnresolved", RecordOwnerRefusal.NoDefaultOwnerTeam), null)!
            .Code.Should().Be(OfficeErrorCodes.RecordOwnerUnresolved);
        OfficeService.MapTodoRefusal(Refusal("NotFoundOrDenied", RecordCreationRefusal.NotFoundCode), "sprk_matter")!
            .Code.Should().Be(OfficeErrorCodes.AccessDenied);
    }
}
