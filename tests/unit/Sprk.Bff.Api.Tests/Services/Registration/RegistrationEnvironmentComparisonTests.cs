using FluentAssertions;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// ISS-005 (#1673 review F1) - <see cref="RegistrationDataverseService.IsSameEnvironment"/>, the ONE decision whether two Dataverse
/// URLs name the same environment. It selects the owner-aware binder for this BFF's own environment (registration approval) and
/// excludes the own environment from the reconciliation job's provisioning targets, so a miss means an ownerless contact.
/// </summary>
public class RegistrationEnvironmentComparisonTests
{
    private const string Own = "https://spaarkedev1.crm.dynamics.com";

    [Theory]
    [InlineData(Own)]                                                   // exact
    [InlineData("https://SpaarkeDev1.CRM.Dynamics.com")]                // host case
    [InlineData("HTTPS://spaarkedev1.crm.dynamics.com")]                // scheme case
    [InlineData(Own + "/")]                                             // trailing slash
    [InlineData(Own + "/api/data/v9.2/")]                               // path
    [InlineData(Own + "/main.aspx?appid=1#x")]                          // query and fragment
    [InlineData("  " + Own + "  ")]                                     // padding
    [InlineData("https://spaarkedev1.api.crm.dynamics.com")]            // .api. spelling
    [InlineData("https://SPAARKEDEV1.API.CRM.DYNAMICS.COM/")]           // .api. spelling, shouting, slash
    [InlineData("https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2")] // .api. spelling with a path
    [InlineData("https://spaarkedev1.crm.dynamics.com:443")]            // the default port, spelled out
    [InlineData("https://spaarkedev1.crm.dynamics.com.")]               // fully-qualified host (trailing dot)
    [InlineData("https://spaarkedev1.crm.dynamics.com./api/data/v9.2/")] // trailing dot with a path
    [InlineData("https://spaarkedev1.api.crm.dynamics.com.")]           // trailing dot and .api.
    [InlineData("HTTPS://SpaarkeDev1.API.CRM.Dynamics.com.:443/")]      // everything at once
    public void TheSameEnvironment_InAnySpelling_IsTheSame(string spelling)
    {
        RegistrationDataverseService.IsSameEnvironment(Own, spelling).Should().BeTrue();
        RegistrationDataverseService.IsSameEnvironment(spelling, Own).Should().BeTrue("the comparison is symmetric");
    }

    [Theory]
    [InlineData("https://spaarkedev1.api.crm4.dynamics.com", "https://spaarkedev1.crm4.dynamics.com")]   // a regional host
    [InlineData("https://x.api.crm.dynamics.com", "https://x.api.crm.dynamics.com")]                     // both .api.
    [InlineData("https://x.api.crm.microsoftdynamics.us", "https://x.crm.microsoftdynamics.us")]         // sovereign cloud
    public void TheApiSegment_IsFoldedOnADataverseHost(string first, string second)
        => RegistrationDataverseService.IsSameEnvironment(first, second).Should().BeTrue();

    [Theory]
    [InlineData("https://spaarkedev2.crm.dynamics.com")]                // another environment
    [InlineData("https://spaarkedev10.crm.dynamics.com")]               // a longer name sharing the prefix
    [InlineData("https://spaarkedev.crm.dynamics.com")]                 // a shorter name
    [InlineData("https://spaarkedev1.crm4.dynamics.com")]               // the same name in another region
    [InlineData("https://spaarkedev1.crm.dynamics.com.evil.example")]   // the environment's name as a prefix of another domain
    [InlineData("https://evil.example/spaarkedev1.crm.dynamics.com")]   // the environment's name in a path
    [InlineData("http://spaarkedev1.crm.dynamics.com")]                 // another scheme
    [InlineData("https://spaarkedev1.crm.dynamics.com:8443")]           // another port
    [InlineData("https://spaarke-demo.api.crm.dynamics.com")]           // .api. spelling of ANOTHER environment
    [InlineData("https://spaarkedev2.crm.dynamics.com.")]               // trailing dot on ANOTHER environment
    public void ADifferentEnvironment_IsNotTheSame(string other)
    {
        RegistrationDataverseService.IsSameEnvironment(Own, other).Should().BeFalse();
        RegistrationDataverseService.IsSameEnvironment(other, Own).Should().BeFalse();
    }

    [Fact]
    public void TheApiSegment_IsNotFoldedOffADataverseHost()
        => RegistrationDataverseService.IsSameEnvironment("https://foo.api.example.com", "https://foo.example.com").Should().BeFalse();

    /// <remarks>
    /// An unusable URL is "not the same", and that is the SAFE default: "same" selects the owner-aware binder over THIS
    /// environment, so reading an unresolvable URL as "same" would send a write meant for the URL to this environment; "not
    /// the same" sends it to the factory binder over that URL, whose request faults - nothing is written, and the fault is
    /// logged (registration link) or fails the run (reconciliation job). Two identical malformed strings are still not "the
    /// same": neither names an environment.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("spaarkedev1.crm.dynamics.com")]       // no scheme
    [InlineData("not a url")]
    [InlineData("https://")]
    [InlineData("file:///spaarkedev1.crm.dynamics.com")]
    [InlineData("ftp://spaarkedev1.crm.dynamics.com")]
    public void AMalformedUrl_IsNeverTheSame_EvenAsItself_OrAgainstTheOwnEnvironment(string? malformed)
    {
        RegistrationDataverseService.IsSameEnvironment(Own, malformed).Should().BeFalse();
        RegistrationDataverseService.IsSameEnvironment(malformed, Own).Should().BeFalse();
        RegistrationDataverseService.IsSameEnvironment(malformed, malformed).Should().BeFalse();
    }
}
