using System;
using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-E2 (auth-v4 task 051) — the Service Bus credential seam.
/// </summary>
/// <remarks>
/// <para><b>What is worth asserting here.</b> Two things decide whether background job processing
/// survives the removal of the SAS connection string: which credential the factory picks, and
/// whether a rejected credential is <i>visible</i>. Both are deterministic and neither needs a live
/// namespace. Everything else about <c>ServiceBusClient</c> — that it can actually reach Azure, that
/// the RBAC grant is present — is only knowable against the real service and belongs to the
/// deployment verification, not here.</para>
/// <para><b>Not asserted: "the client resolves from DI as a singleton."</b> That is an ADR-038 ban
/// <b>B3</b> DI-registration test. What makes the single-registration property hold is the
/// <c>ServiceBusClientGuardTests</c> structural guard, which fails the build on a second
/// construction site — a stronger instrument than a resolution assertion, because it catches the
/// duplicate at the moment it is written rather than after it shadows something.</para>
/// </remarks>
public class ServiceBusCredentialSeamTests
{
    private const string Namespace = "spaarke-servicebus-dev.servicebus.windows.net";

    private const string SasConnectionString =
        "Endpoint=sb://spaarke-servicebus-dev.servicebus.windows.net/;" +
        "SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=" +
        "Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cXV4Zm9vYmFyYmF6cQ==";

    /// <summary>
    /// The namespace path is selected whenever a namespace is configured — including while the SAS
    /// string is still present. This is what makes the cutover reversible with one setting
    /// (NFR-06), rather than requiring the credential to be deleted before it can be stopped from
    /// being used.
    /// </summary>
    [Fact]
    public void UseManagedIdentity_WhenNamespaceSetAlongsideConnectionString_PrefersNamespace()
    {
        var options = new ServiceBusOptions
        {
            FullyQualifiedNamespace = Namespace,
            ConnectionString = SasConnectionString,
        };

        ServiceBusClientFactory.UseManagedIdentity(options).Should().BeTrue(
            "a configured namespace must win over a still-present SAS string, so removing the " +
            "namespace is the rollback and the SAS string need not be deleted to stop using it");
    }

    [Fact]
    public void UseManagedIdentity_WhenOnlyConnectionStringSet_UsesTheSasPath()
    {
        var options = new ServiceBusOptions { ConnectionString = SasConnectionString };

        ServiceBusClientFactory.UseManagedIdentity(options).Should().BeFalse();
    }

    /// <summary>
    /// Whitespace is not a namespace. A half-applied App Service setting (present but empty) must
    /// fall through to the SAS string rather than select a path it cannot complete.
    /// </summary>
    [Fact]
    public void UseManagedIdentity_WhenNamespaceIsWhitespace_FallsBackToTheSasPath()
    {
        var options = new ServiceBusOptions
        {
            FullyQualifiedNamespace = "   ",
            ConnectionString = SasConnectionString,
        };

        ServiceBusClientFactory.UseManagedIdentity(options).Should().BeFalse();
    }

    /// <summary>
    /// The negative case named by task 051's acceptance criteria: no credential at all must fail
    /// loudly, and the message must name what to set.
    /// </summary>
    /// <remarks>
    /// Before this task the equivalent failure was a startup <c>InvalidOperationException</c> thrown
    /// from a DI module at registration time — which is why the check has to survive here. The other
    /// half of the old shape was worse: <c>WorkersModule</c> skipped the registration entirely when
    /// the string was absent, so the symptom was an unresolvable <c>ServiceBusClient</c> dependency
    /// naming a type no operator had ever configured (ADR-032 / CLAUDE.md §10 F.1).
    /// </remarks>
    [Fact]
    public void Create_WhenNeitherNamespaceNorConnectionStringConfigured_ThrowsAnActionableError()
    {
        var options = new ServiceBusOptions();

        var act = () => ServiceBusClientFactory.Create(options, credential: null!);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("ServiceBus:FullyQualifiedNamespace",
                    "the message must name the setting that fixes it")
                .And.Contain("Azure Service Bus Data Sender",
                    "namespace auth fails without the data-plane roles, and Owner does not imply them");
    }

    /// <summary>
    /// A rotated SAS key does NOT surface as an auth-specific
    /// <c>ServiceBusFailureReason</c> — the enum has no such member in Azure.Messaging.ServiceBus
    /// 7.18.1 — it surfaces as a general error whose message says <c>InvalidSignature</c>. This is
    /// the exact shape of the 2026-08-23 dev outage, in which both slots logged it for ~40 minutes
    /// while <c>/healthz</c> returned 200.
    /// </summary>
    [Theory]
    [InlineData("InvalidSignature: The token has an invalid signature.")]
    [InlineData("Unauthorized access. 'Listen' claim(s) are required to perform this operation.")]
    [InlineData("The token has an invalid signature; claim is empty or token is invalid.")]
    public void IsAuthorizationFailure_ForRejectedCredentialMessages_ReturnsTrue(string message)
    {
        ServiceBusJobProcessor.IsAuthorizationFailure(new InvalidOperationException(message))
            .Should().BeTrue();
    }

    [Fact]
    public void IsAuthorizationFailure_ForUnauthorizedAccessException_ReturnsTrue()
    {
        ServiceBusJobProcessor.IsAuthorizationFailure(new UnauthorizedAccessException("denied"))
            .Should().BeTrue();
    }

    /// <summary>
    /// Ordinary transient faults must NOT be classified as authorization failures — otherwise a
    /// busy namespace would degrade <c>/healthz</c> and the signal this seam exists to create would
    /// be worth nothing.
    /// </summary>
    [Theory]
    [InlineData("The service was unable to process the request; please retry the operation.")]
    [InlineData("The operation did not complete within the allotted timeout of 00:01:00.")]
    [InlineData("The messaging entity 'sdap-jobs' could not be found.")]
    public void IsAuthorizationFailure_ForTransientFaults_ReturnsFalse(string message)
    {
        ServiceBusJobProcessor.IsAuthorizationFailure(new InvalidOperationException(message))
            .Should().BeFalse();
    }
}
