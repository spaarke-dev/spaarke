using System.Text;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Files;

/// <summary>
/// The owner of a stored file created through <see cref="StoredFileService"/>'s own create path (the caller upload and the app create;
/// uac-r2 Q2): <c>sprk_storedfile</c> is a hard-deny table (UAC 194b R4b-2), so an ordinary file is owned by the restricted-content
/// owner team and never by a business unit's default team, and a Secure parent's file by the Secure team. The REAL resolver answers,
/// over <see cref="OwnershipDirectory"/>.
/// </summary>
public sealed class StoredFileServiceOwnershipTests
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 test file");
    private static readonly Guid ParentId = Guid.Parse("1a000000-0000-4000-8000-0000000000f2");
    private static readonly Guid AppRequester = Guid.Parse("a9900000-0000-4000-8000-0000000000f2");
    private static readonly Guid AppRequesterOid = Guid.Parse("a9900000-0000-4000-8000-0000000000f3");

    /// <summary>Every default team of the standard directory: an ordinary stored file is owned by none of them.</summary>
    private static readonly Guid[] DefaultTeams =
    [
        OwnershipDirectory.GeneralTeam, OwnershipDirectory.ChildTeam, OwnershipDirectory.CustomerChildTeam, OwnershipDirectory.SecureDefaultTeam,
    ];

    public enum CreatePath { Upload, App }

    private static StoredFilePurpose PurposeOf(StoredFileParentKind kind) => kind switch
    {
        StoredFileParentKind.Invoice => StoredFilePurpose.InvoiceSupportingFile,
        StoredFileParentKind.ProcedureVersion => StoredFilePurpose.ProcedureSource,
        StoredFileParentKind.BillingExport => StoredFilePurpose.ExportFile,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The standard directory with both acting users in a business unit below the customer's top unit, and the parent record in that
    /// unit too, owned by its default team (an ordinary parent), or in the Secure Record unit owned by the Secure team.
    /// </summary>
    private static OwnershipDirectory World(StoredFileParent parent, bool secureParent = false) =>
        OwnershipDirectory.Standard()
            .WithUser(StatedCaller.SystemUserId, Guid.Parse(Sprk.Bff.Api.Tests.Api.Finance.FinanceAuthzTestAuthHandler.CallerObjectId), OwnershipDirectory.CustomerChildBu)
            .WithUser(AppRequester, AppRequesterOid, OwnershipDirectory.CustomerChildBu)
            .WithRecord(
                parent.Table, parent.Id,
                secureParent ? OwnershipDirectory.SecureBu : OwnershipDirectory.CustomerChildBu,
                owningTeam: secureParent ? OwnershipDirectory.SecureNamedTeam : OwnershipDirectory.CustomerChildTeam);

    private static async Task<StoredFileCreateResult> CreateAsync(StoredFileRig rig, CreatePath path, StoredFileParent parent, Guid? requestedBy = null)
    {
        var purpose = PurposeOf(parent.Kind);
        if (path == CreatePath.Upload)
        {
            rig.Probe.Grant(parent.EntitySet, parent.Id, Spaarke.Dataverse.AccessRights.Write);
            return await rig.Service.UploadForCallerAsync(
                StoredFileRig.Caller(), "token", purpose, parent, "a.pdf", "application/pdf", new MemoryStream(Pdf), CancellationToken.None);
        }

        var spec = new StoredFileRowSpec
        {
            FileId = Guid.NewGuid(),
            Purpose = purpose,
            Parent = parent,
            Name = "a.pdf",
            ContentType = "application/pdf",
        };
        return await rig.Service.CreateForAppAsync(spec, new MemoryStream(Pdf), requestedBy, CancellationToken.None);
    }

    private static Guid OwnerOf(StoredFileRig rig, StoredFileCreateResult result)
    {
        result.IsCreated.Should().BeTrue(result.RefusalCode);
        var owner = rig.Dataverse.Rows[result.FileId!.Value].GetAttributeValue<EntityReference>("ownerid");
        owner.Should().NotBeNull("the row is written with its resolved owner");
        owner.LogicalName.Should().Be("team");
        return owner.Id;
    }

    public static TheoryData<StoredFileParentKind, CreatePath> EveryParentKindAndPath()
    {
        var data = new TheoryData<StoredFileParentKind, CreatePath>();
        foreach (var kind in Enum.GetValues<StoredFileParentKind>())
        {
            data.Add(kind, CreatePath.Upload);
            data.Add(kind, CreatePath.App);
        }

        return data;
    }

    [Theory(DisplayName = "uac-r2 Q2 must-fire: an ordinary stored file, under every parent kind and on both create paths, is owned by the restricted-content owner team, never a business unit's default team")]
    [MemberData(nameof(EveryParentKindAndPath))]
    public async Task OrdinaryStoredFile_IsOwnedByTheRestrictedContentTeam(StoredFileParentKind kind, CreatePath path)
    {
        var parent = new StoredFileParent(kind, ParentId);
        var rig = new StoredFileRig { Ownership = World(parent) };

        var owner = OwnerOf(rig, await CreateAsync(rig, path, parent, requestedBy: AppRequester));

        owner.Should().Be(OwnershipDirectory.RestrictedContentTeam);
        DefaultTeams.Should().NotContain(owner);
        rig.Ownership!.IsTeamOfTheSecureRecordBusinessUnit(owner).Should().BeFalse();

        // The question is the written row's own: its table, and the parents ForChild reads off the row as written.
        var asked = rig.OwnerContexts.Should().ContainSingle().Subject;
        asked.ChildEntityLogicalName.Should().Be(StoredFileSchema.Entity);
        var written = RecordOwnershipContext.ForChild(rig.Dataverse.Rows[rig.Dataverse.Rows.Keys.Single()]);
        asked.Parents.Should().Equal(written.Parents);
        asked.SecureOnlyParents.Should().Equal(written.SecureOnlyParents);
    }

    [Theory(DisplayName = "uac-r2 Q2: a stored file under a Secure invoice is owned by the Secure Record owner team, on both create paths")]
    [InlineData(CreatePath.Upload)]
    [InlineData(CreatePath.App)]
    public async Task StoredFile_UnderASecureInvoice_IsOwnedByTheSecureTeam(CreatePath path)
    {
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, ParentId);
        var rig = new StoredFileRig { Ownership = World(parent, secureParent: true) };

        var owner = OwnerOf(rig, await CreateAsync(rig, path, parent, requestedBy: AppRequester));

        owner.Should().Be(OwnershipDirectory.SecureNamedTeam);
        rig.Ownership!.IsTeamOfTheSecureRecordBusinessUnit(owner).Should().BeTrue();
        owner.Should().NotBe(OwnershipDirectory.RestrictedContentTeam);
    }

    [Theory(DisplayName = "uac-r2 Q2: a parent that files nothing (a billing export, a procedure version) leaves the file unfiled - the acting user's business unit decides, through the restricted-content team of that user's customer top unit")]
    [InlineData(StoredFileParentKind.BillingExport, CreatePath.Upload)]
    [InlineData(StoredFileParentKind.BillingExport, CreatePath.App)]
    [InlineData(StoredFileParentKind.ProcedureVersion, CreatePath.Upload)]
    [InlineData(StoredFileParentKind.ProcedureVersion, CreatePath.App)]
    public async Task NoCoreParent_FallsBackToTheActingUser_ThenTheRestrictedContentTeam(StoredFileParentKind kind, CreatePath path)
    {
        var parent = new StoredFileParent(kind, ParentId);
        // The parent sits in the Secure Record unit: it is not an ownership parent, so it is never read and decides nothing.
        var rig = new StoredFileRig { Ownership = World(parent, secureParent: true) };

        var owner = OwnerOf(rig, await CreateAsync(rig, path, parent, requestedBy: AppRequester));

        var asked = rig.OwnerContexts.Should().ContainSingle().Subject;
        asked.Parents.Should().BeEmpty("a billing export and a procedure version are not ownership parents");
        asked.CallerSystemUserId.Should().Be(path == CreatePath.Upload ? StatedCaller.SystemUserId : AppRequester);
        owner.Should().Be(OwnershipDirectory.RestrictedContentTeam);
        var answer = await rig.Ownership!.Resolver().ResolveOwnerAsync(asked, CancellationToken.None);
        answer.InPlaceOfDefaultTeamId.Should().Be(OwnershipDirectory.CustomerChildTeam, "the acting user's business-unit default team is what it replaces");
    }

    [Theory(DisplayName = "uac-r2 Q2 (fail closed): an app create under a parent that files nothing, with no acting user, is refused and writes nothing")]
    [InlineData(StoredFileParentKind.BillingExport)]
    [InlineData(StoredFileParentKind.ProcedureVersion)]
    public async Task NoCoreParent_NoActingUser_IsRefused(StoredFileParentKind kind)
    {
        var parent = new StoredFileParent(kind, ParentId);
        var rig = new StoredFileRig { Ownership = World(parent) };

        var result = await CreateAsync(rig, CreatePath.App, parent, requestedBy: null);

        result.RefusalCode.Should().Be(StoredFileRefusal.OwnerUnresolved);
        rig.Gateway.StorageCalls.Should().Be(0);
        rig.Dataverse.Writes.Should().Be(0);
    }
}
