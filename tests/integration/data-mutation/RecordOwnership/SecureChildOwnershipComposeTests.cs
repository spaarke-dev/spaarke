using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r2 (verifier item 14: "the Compose promoter is not driven end to end") — the
/// Compose create-on-save PROMOTE, through the REAL <see cref="ComposeCreateOnSavePromoter"/> and the REAL
/// <see cref="RecordOwnershipResolver"/> over <see cref="Directory"/>: a Word copy of a document filed to a secure matter
/// INHERITS that filing and is upserted owned by the named Secure team; a flagged-but-not-isolated inherited project
/// refuses BEFORE the upsert; an unfiled draft is owned by the saving user's business-unit team (the owner's required
/// unfiled-save case). The upsert keeps its SPE alternate key — it is not relaxed.
/// </summary>
[Trait("status", "new")]
public sealed class SecureChildOwnershipComposeTests
{
    private static readonly Guid SecureMatter = Guid.Parse("c1460000-0000-4000-8000-000000000001");
    private static readonly Guid FlaggedProject = Guid.Parse("c1460000-0000-4000-8000-000000000002");
    private static readonly Guid SourcePdf = Guid.Parse("c1460000-0000-4000-8000-000000000003");

    private readonly Directory _world = Directory.Standard()
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam);

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Loose);
    private readonly List<Entity> _upserts = new();

    public SecureChildOwnershipComposeTests()
    {
        _dataverse
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _upserts.Add(e))
            .ReturnsAsync((Guid.NewGuid(), true));
    }

    [Fact]
    public async Task Promote_ACopyOfADocumentFiledToASecureMatter_IsUpsertedOwnedByTheNamedTeam()
    {
        SourceFiledUnder("sprk_matter", "sprk_matter", SecureMatter);

        await Promoter().PromoteIfEphemeralAsync(Request(SourcePdf), SavingUser(), CancellationToken.None);

        var row = _upserts.Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("team", Directory.SecureNamedTeam));
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(SecureMatter, "the copy inherits the filing");
        row.KeyAttributes.Should().ContainKey("sprk_graphitemid", "the SPE alternate key is not relaxed");
        // c1-r1 (owner round 13 item 9): the saving user asked for the app-upserted row.
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Directory.CallerUserId);
    }

    [Fact]
    public async Task Promote_InheritingAFlaggedButNotIsolatedProject_IsRefusedBeforeTheUpsert()
    {
        SourceFiledUnder("sprk_project", "sprk_project", FlaggedProject);

        var act = () => Promoter().PromoteIfEphemeralAsync(Request(SourcePdf), SavingUser(), CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _upserts.Should().BeEmpty("a refusal writes no row");
    }

    [Fact]
    public async Task Promote_AnUnfiledDraft_IsOwnedByTheSavingUsersBusinessUnitTeam()
    {
        await Promoter().PromoteIfEphemeralAsync(Request(sourceDocument: null), SavingUser(), CancellationToken.None);

        _upserts.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("team", Directory.GeneralTeam));
    }

    private ComposeCreateOnSavePromoter Promoter()
    {
        var sessions = new Mock<ChatSessionManager>(
            Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(), Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance, null!, null!);
        var resolution = new ComposeRecordResolution(sessions.Object, _dataverse.Object, NullLogger.Instance, dedupDetector: null);
        return new ComposeCreateOnSavePromoter(_dataverse.Object, NullLogger.Instance, dedupDetector: null, resolution, _world.Resolver());
    }

    private void SourceFiledUnder(string column, string table, Guid id) =>
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_document", SourcePdf, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", SourcePdf) { [column] = new EntityReference(table, id) });

    private static PromoteComposeDocumentRequest Request(Guid? sourceDocument) => new()
    {
        DocumentSpeId = "spe-item-146",
        SessionId = string.Empty, // no bound session — the rebind is skipped (task 110)
        TenantId = "tenant-146",
        DisplayName = "Settlement draft",
        SourceDocumentRecordId = sourceDocument,
    };

    /// <summary>The saving user — the directory's caller, so an unfiled draft resolves to their unit's team.</summary>
    private static HttpContext SavingUser() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", Directory.CallerOid.ToString()) }, authenticationType: "test")),
    };
}
