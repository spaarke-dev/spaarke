using System;
using FluentAssertions;
using Sprk.Bff.Api.Api;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Registration;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for
/// <see cref="RegistrationEndpoints.BuildRegistrationRecordUrl"/>. The helper is the
/// URL-formation rule that the fire-and-forget admin notification (<c>SendAdminNotificationAsync</c>
/// helper in <see cref="RegistrationEndpoints"/>) uses to embed a deep link to the newly
/// created <c>sprk_registrationrequest</c> record in the notification email.
///
/// customer-provisioning-orchestration-r1 task 081 extracted this helper when migrating
/// <c>SubmitDemoRequest</c>'s admin-notification flow off the (now-removed)
/// <c>DemoProvisioningOptions.Environments</c> + <c>DefaultEnvironment</c> pair onto
/// <see cref="Sprk.Bff.Api.Services.Registration.DataverseEnvironmentService"/>. These tests
/// pin the URL contract so the shape the admin sees in email (which drives them into the
/// correct Dataverse environment) cannot silently regress under future refactors.
///
/// Behavior protected here (would silently regress admin-notification deep links if changed):
///  - Trailing slash on the environment URL is trimmed before concatenation.
///  - <c>appid=…&amp;</c> is prepended to <c>pagetype=</c> when appId is provided.
///  - <c>appid=</c> is absent when appId is null or empty (no stray parameter).
///  - When env URL is null or empty, the historical generic fallback URL is used so the
///    admin still receives a best-effort deep link if environment lookup fails.
///  - <c>etn=sprk_registrationrequest</c> and the record GUID are always present.
/// </summary>
public class BuildRegistrationRecordUrlTests
{
    private static readonly Guid FixedRecordId = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void BuildRegistrationRecordUrl_WithEnvUrlAndAppId_EmbedsBothAndTrimsTrailingSlash()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: "https://spaarke-demo.crm.dynamics.com/",
            envAppId: "9c1b5f00-1234-4567-89ab-cdef01234567",
            recordId: FixedRecordId);

        url.Should().Be(
            "https://spaarke-demo.crm.dynamics.com/main.aspx?appid=9c1b5f00-1234-4567-89ab-cdef01234567&pagetype=entityrecord&etn=sprk_registrationrequest&id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void BuildRegistrationRecordUrl_WithEnvUrlAndNoAppId_OmitsAppIdParameter()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: "https://spaarke-demo.crm.dynamics.com",
            envAppId: null,
            recordId: FixedRecordId);

        url.Should().Be(
            "https://spaarke-demo.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_registrationrequest&id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        url.Should().NotContain("appid=");
    }

    [Fact]
    public void BuildRegistrationRecordUrl_WithEnvUrlAndEmptyAppId_OmitsAppIdParameter()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: "https://spaarke-demo.crm.dynamics.com",
            envAppId: "",
            recordId: FixedRecordId);

        url.Should().NotContain("appid=");
    }

    [Fact]
    public void BuildRegistrationRecordUrl_WhenEnvUrlIsNull_FallsBackToGenericDataverseUrl()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: null,
            envAppId: null,
            recordId: FixedRecordId);

        url.Should().StartWith("https://spaarkedev1.crm.dynamics.com/main.aspx?");
        url.Should().Contain("etn=sprk_registrationrequest");
        url.Should().Contain($"id={FixedRecordId}");
    }

    [Fact]
    public void BuildRegistrationRecordUrl_WhenEnvUrlIsEmpty_FallsBackToGenericDataverseUrl()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: "",
            envAppId: "any-app-id",
            recordId: FixedRecordId);

        url.Should().StartWith("https://spaarkedev1.crm.dynamics.com/main.aspx?");
        // Fallback path still honors a supplied appId — matches historical behavior where
        // an explicit appId parameter is preserved regardless of URL fallback.
        url.Should().Contain("appid=any-app-id&");
    }

    [Fact]
    public void BuildRegistrationRecordUrl_AlwaysEmbedsRecordIdAndEntityName()
    {
        var url = RegistrationEndpoints.BuildRegistrationRecordUrl(
            envUrl: "https://any.crm.dynamics.com",
            envAppId: null,
            recordId: FixedRecordId);

        url.Should().Contain("etn=sprk_registrationrequest");
        url.Should().Contain($"id={FixedRecordId}");
        url.Should().Contain("pagetype=entityrecord");
    }
}
