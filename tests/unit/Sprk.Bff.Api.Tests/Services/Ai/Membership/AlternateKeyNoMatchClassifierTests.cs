using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

/// <summary>
/// Pins <see cref="DataverseServiceClientImpl.IsAlternateKeyNoMatch"/> (#2292): the typed classifier for
/// "an alternate-key retrieve matched no row" (<c>OrganizationServiceFault.ErrorCode</c> <c>0x80060891</c>).
/// Probe-then-create callers (<c>MembershipJunctionUpdater</c>, <c>DocumentUrlIdentityResolution</c>) read a
/// true answer as "row absent"; a false positive would turn a real fault into a create, a false negative
/// dead-letters every first-time event. Pure logic, tested through the public surface (ADR-038).
/// </summary>
public class AlternateKeyNoMatchClassifierTests
{
    private const int NoMatchCode = unchecked((int)0x80060891);
    private const string NoMatchMessage =
        "A record with the specified key values does not exist in sprk_userentityassociation entity";

    private static FaultException<OrganizationServiceFault> TypedFault(int errorCode, string message)
        => new(new OrganizationServiceFault { ErrorCode = errorCode, Message = message }, new FaultReason(message));

    [Fact]
    public void IsAlternateKeyNoMatch_DirectFault_ReturnsTrue()
    {
        DataverseServiceClientImpl.IsAlternateKeyNoMatch(TypedFault(NoMatchCode, NoMatchMessage))
            .Should().BeTrue();
    }

    [Fact]
    public void IsAlternateKeyNoMatch_FaultWrappedOnce_ReturnsTrue()
    {
        // The shape RetrieveByAlternateKeyAsync throws.
        var wrapped = new InvalidOperationException(
            "Failed to retrieve sprk_userentityassociation by alternate key: " + NoMatchMessage,
            TypedFault(NoMatchCode, NoMatchMessage));

        DataverseServiceClientImpl.IsAlternateKeyNoMatch(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsAlternateKeyNoMatch_FaultWrappedTwice_ReturnsTrue()
    {
        var wrapped = new InvalidOperationException(
            "outer",
            new InvalidOperationException("middle", TypedFault(NoMatchCode, NoMatchMessage)));

        DataverseServiceClientImpl.IsAlternateKeyNoMatch(wrapped).Should().BeTrue();
    }

    [Theory]
    [InlineData(unchecked((int)0x80060892))] // duplicate / not-Active key — one integer away, must not match
    [InlineData(unchecked((int)0x80040217))] // by-id ObjectDoesNotExist — a different answer
    [InlineData(unchecked((int)0x80040220))] // privilege denied
    public void IsAlternateKeyNoMatch_DifferentCode_ReturnsFalse(int errorCode)
    {
        // Same "does not exist" wording on purpose: the classifier is typed on the code, never the message.
        var wrapped = new InvalidOperationException("outer", TypedFault(errorCode, NoMatchMessage));

        DataverseServiceClientImpl.IsAlternateKeyNoMatch(wrapped).Should().BeFalse();
    }

    [Fact]
    public void IsAlternateKeyNoMatch_FaultWithNullDetail_ReturnsFalse()
    {
        var fault = new FaultException<OrganizationServiceFault>(null!, new FaultReason(NoMatchMessage));

        DataverseServiceClientImpl.IsAlternateKeyNoMatch(fault).Should().BeFalse();
        DataverseServiceClientImpl.IsAlternateKeyNoMatch(new InvalidOperationException("outer", fault))
            .Should().BeFalse();
    }

    [Fact]
    public void IsAlternateKeyNoMatch_UnrelatedException_ReturnsFalse()
    {
        DataverseServiceClientImpl.IsAlternateKeyNoMatch(
                new InvalidOperationException("Entity sprk_userentityassociation not found with provided alternate key values"))
            .Should().BeFalse("the client's own null-response literal is not the Dataverse fault");
        DataverseServiceClientImpl.IsAlternateKeyNoMatch(new TimeoutException("The request channel timed out."))
            .Should().BeFalse();
        DataverseServiceClientImpl.IsAlternateKeyNoMatch(null).Should().BeFalse();
    }
}
