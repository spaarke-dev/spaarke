// -----------------------------------------------------------------------------
// OperationAccessPolicyDescriptionTests.cs
//
// OperationAccessPolicy.GetAccessRightsDescription is the one AccessRights → text formatter.
// PermissionsEndpoints used to keep its own copy (#2866); these pin the text the endpoint returns
// in its AccessRights field and that GetRequirementDescription returns.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Auth;

public sealed class OperationAccessPolicyDescriptionTests
{
    [Fact]
    public void GetAccessRightsDescription_WhenNone_ReturnsNone()
    {
        OperationAccessPolicy.GetAccessRightsDescription(AccessRights.None).Should().Be("None");
    }

    [Fact]
    public void GetAccessRightsDescription_WithFlagsCombinedOutOfOrder_ListsThemInFixedOrder()
    {
        var rights = AccessRights.Share | AccessRights.Read | AccessRights.Write;

        OperationAccessPolicy.GetAccessRightsDescription(rights).Should().Be("Read, Write, Share");
    }

    [Fact]
    public void GetAccessRightsDescription_WithEveryNamedFlag_ListsAllSeven()
    {
        var rights = AccessRights.Read | AccessRights.Write | AccessRights.Delete | AccessRights.Create
            | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

        OperationAccessPolicy.GetAccessRightsDescription(rights)
            .Should().Be("Read, Write, Delete, Create, Append, AppendTo, Share");
    }
}
