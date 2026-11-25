using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// GitHub #1571 review F1 (spaarkeai-word-add-in-r1 task 136): <see cref="DataverseUserClient"/> tells a failed
/// On-Behalf-Of exchange the CALLER can fix (<see cref="MsalUiRequiredException"/>: invalid_grant, interaction or consent
/// required) from one that is the server's fault (every other <see cref="MsalException"/>: the BFF's own credential
/// rejected, an Entra timeout, temporarily_unavailable, throttling). Both keep the
/// <see cref="DataverseUserClientErrorCodes.OboExchangeFailed"/> code; only the first is
/// <see cref="DataverseUserResponse.IsCallerFixable"/>.
/// </summary>
/// <remarks>
/// The MSAL exception is raised by the credential the provider asks for, inside the same guarded block as the exchange,
/// so no token request and no network call is made (the HTTP handler fails the test if anything is sent).
/// </remarks>
public class DataverseUserClientOboFailureKindSeamTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string AppId = "22222222-2222-2222-2222-222222222222";
    private const string DataverseUrl = "https://example.crm.dynamics.com";

    [Fact]
    public async Task AnExchangeTheCallerMustSignInAgainFor_IsCallerFixable()
    {
        var response = await ReadWith(new MsalUiRequiredException("invalid_grant", "AADSTS50173: the grant has expired."));

        response.IsSuccess.Should().BeFalse();
        response.ErrorCode.Should().Be(DataverseUserClientErrorCodes.OboExchangeFailed);
        response.IsCallerFixable.Should().BeTrue("a fresh sign-in is what fixes invalid_grant");
    }

    [Theory]
    [InlineData("invalid_client")]
    [InlineData("temporarily_unavailable")]
    [InlineData("request_timeout")]
    public async Task AnExchangeThatFailedForTheServersOwnReasons_IsNotCallerFixable(string msalErrorCode)
    {
        var response = await ReadWith(new MsalServiceException(msalErrorCode, "test stub"));

        response.IsSuccess.Should().BeFalse();
        response.ErrorCode.Should().Be(DataverseUserClientErrorCodes.OboExchangeFailed,
            "the code stays the same, so consumers that do not read the flag are unchanged");
        response.IsCallerFixable.Should().BeFalse("signing in again cannot fix the server's own credential or Entra being down");
    }

    private static async Task<DataverseUserResponse> ReadWith(MsalException credentialFailure)
    {
        var credentials = new OrderedCredentialClientProvider(
            Options.Create(new CredentialSelectionOptions { Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) } }),
            Config(),
            NullLogger<OrderedCredentialClientProvider>.Instance,
            new ThrowingAssertionProvider(credentialFailure));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer caller-token";

        var sut = new DataverseUserClient(
            new HttpClient(new NoNetworkHandler()),
            Options.Create(new DataverseOptions { EnvironmentUrl = DataverseUrl }),
            Config(),
            new HttpContextAccessor { HttpContext = context },
            NullLogger<DataverseUserClient>.Instance,
            credentials);

        return await sut.GetAsync("sprk_communications?$top=1", CancellationToken.None);
    }

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = DataverseUrl,
            ["TENANT_ID"] = Tenant,
            ["API_APP_ID"] = AppId,
        }).Build();

    /// <summary>Fails every credential mint with the given MSAL exception.</summary>
    private sealed class ThrowingAssertionProvider(MsalException failure) : IClientAssertionProvider
    {
        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
            => Task.FromException<string>(failure);
    }

    /// <summary>Fails the test if anything reaches the network.</summary>
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request may be sent by this test.");
    }
}
