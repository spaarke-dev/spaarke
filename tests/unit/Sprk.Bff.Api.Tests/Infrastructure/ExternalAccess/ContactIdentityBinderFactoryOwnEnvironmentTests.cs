using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ISS-005 (#1673 review K2) - <see cref="ContactIdentityBinderFactory.CreateBinder"/> builds an OWNERLESS binder
/// (<c>ContactCreateOwner.NoneForForeignEnvironment</c>), which is right only for another environment. The refusal of this
/// BFF's own environment (<c>Dataverse:ServiceUrl</c>, compared as <c>RegistrationDataverseService.IsSameEnvironment</c>) is in
/// the factory itself, so no caller can reach it by forgetting to check first.
/// </summary>
public class ContactIdentityBinderFactoryOwnEnvironmentTests
{
    private const string Own = "https://spaarkedev1.crm.dynamics.com";

    private sealed class Factory(string? ownServiceUrl) : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, IdentityBindingTestKit.Tenants(IdentityBindingTestKit.CustomerTenant),
        new FakeTimeProvider(IdentityBindingTestKit.Now), IdentityBindingTestKit.FactoryConfiguration(ownServiceUrl))
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => new InMemoryContactIdentityStore();
    }

    private static Task<string> NoToken(CancellationToken ct) => Task.FromResult("token");

    [Theory]
    [InlineData(Own)]
    [InlineData(Own + "/")]
    [InlineData("https://SpaarkeDev1.CRM.Dynamics.com")]
    [InlineData("https://spaarkedev1.api.crm.dynamics.com")]            // the .api. form
    [InlineData("https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2")]
    [InlineData("https://spaarkedev1.crm.dynamics.com.")]               // fully-qualified host
    public void CreateBinder_ForTheOwnEnvironment_InAnySpelling_Throws(string url)
    {
        var act = () => new Factory(Own).CreateBinder(url, NoToken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*own*Dataverse:ServiceUrl*");
    }

    [Theory]
    [InlineData("https://spaarke-demo.crm.dynamics.com")]
    [InlineData("https://spaarke-demo.api.crm.dynamics.com")]
    [InlineData("https://spaarkedev1.crm4.dynamics.com")]               // same name, another region
    [InlineData("https://spaarkedev10.crm.dynamics.com")]
    public void CreateBinder_ForAForeignEnvironment_Works(string url)
        => new Factory(Own).CreateBinder(url, NoToken).Should().NotBeNull();

    [Fact]
    public void CreateBinder_WhenTheOwnEnvironmentIsNotConfigured_CannotTellAndWorks()
        => new Factory(ownServiceUrl: null).CreateBinder(Own, NoToken).Should().NotBeNull(
            "an unknown own environment names nothing to refuse (the same default as IsSameEnvironment: unusable is 'not the same')");
}
