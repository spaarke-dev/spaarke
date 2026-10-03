using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <see cref="RecordShareLevels.MaskForRightsCsv"/> / <see cref="RecordShareLevels.RightsCsvForMask"/> — the mapping
/// between a Web API <c>AccessMask</c> literal and the <c>accessrightsmask</c> Dataverse stores on a POA row
/// (unified-access-control-r2 task 133; pinned by its verifier round 1).
/// </summary>
/// <remarks>
/// <para><b>Why these are pinned on their own.</b> Secure provisioning confirms the creator's share by comparing the
/// stored mask with <c>MaskForRightsCsv(CreatorAccessRights)</c>, and the provisioning test fixture stores masks with
/// the SAME function. A wrong bit (say <c>ShareAccess</c>) would therefore be invisible to every provisioning test,
/// while live Dataverse would read every creator share as "not exact" and fail every provision. Only an independent
/// statement of Dataverse's numbers catches that.</para>
///
/// <para>The expected values are LITERALS in Dataverse's own <c>AccessRights</c> values (Microsoft.Crm.Sdk.Proxy):
/// Read 1, Write 2, Append 4, AppendTo 16, Create 32, Delete 65536, Share 262144, Assign 524288 — the same literals
/// <see cref="InternalUserShareTests"/> asserts. The expected creator mask is computed from THESE literals over the
/// names in <see cref="ProvisionProjectEndpoint.CreatorAccessRights"/>, so the test follows task 139 if that changes
/// which rights the creator holds, but never trusts the production bit table.</para>
///
/// Placement: <c>tests/integration/auth/**</c> — the ADR-038 security-auth KEEP path, beside the share endpoint tests.
/// </remarks>
public class RecordShareRightsMaskTests
{
    private static readonly IReadOnlyDictionary<string, int> DataverseBits = new Dictionary<string, int>
    {
        ["ReadAccess"] = 1,
        ["WriteAccess"] = 2,
        ["AppendAccess"] = 4,
        ["AppendToAccess"] = 16,
        ["CreateAccess"] = 32,
        ["DeleteAccess"] = 65536,
        ["ShareAccess"] = 262144,
        ["AssignAccess"] = 524288,
    };

    [Theory]
    [InlineData("ReadAccess", 1)]
    [InlineData("WriteAccess", 2)]
    [InlineData("AppendAccess", 4)]
    [InlineData("AppendToAccess", 16)]
    [InlineData("CreateAccess", 32)]
    [InlineData("DeleteAccess", 65536)]
    [InlineData("ShareAccess", 262144)]
    [InlineData("AssignAccess", 524288)]
    public void EachRight_MapsToDataversesOwnBit_AndBack(string right, int bit)
    {
        RecordShareLevels.MaskForRightsCsv(right).Should().Be(bit);
        RecordShareLevels.RightsCsvForMask(bit).Should().Be(right);
    }

    [Fact]
    public void TheProvisioningCreatorAndColleagueRights_MapToTheMasksDataverseStores()
    {
        static int Expected(string csv) => csv.Split(',').Sum(name => DataverseBits[name.Trim()]);

        RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights)
            .Should().Be(Expected(ProvisionProjectEndpoint.CreatorAccessRights));
        ProvisionProjectEndpoint.CreatorAccessMask.Should().Be(Expected(ProvisionProjectEndpoint.CreatorAccessRights));
        RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights)
            .Should().Be(Expected(ProvisionProjectEndpoint.CollaboratorAccessRights));
    }

    [Fact]
    public void AMask_RoundTripsThroughItsRightsLiteral()
    {
        // Collaborate + Share: today's provisioning creator mask, written as Dataverse's numbers.
        const int creatorShape = 1 + 2 + 4 + 16 + 262144;

        RecordShareLevels.RightsCsvForMask(creatorShape)
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess");
        RecordShareLevels.MaskForRightsCsv(RecordShareLevels.RightsCsvForMask(creatorShape)).Should().Be(creatorShape);
    }

    [Fact]
    public void AnUnknownRightName_IsRefused_NeverGuessed()
    {
        var act = () => RecordShareLevels.MaskForRightsCsv("ReadAccess,ShareAcess");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]   // a zero mask is a revoke, not a modify
    [InlineData(8)]   // no Dataverse access right is bit 8
    [InlineData(1 | 8)]
    public void AMaskNoRightsLiteralCanExpress_IsRefused(int mask)
    {
        var act = () => RecordShareLevels.RightsCsvForMask(mask);

        act.Should().Throw<ArgumentException>();
    }
}
