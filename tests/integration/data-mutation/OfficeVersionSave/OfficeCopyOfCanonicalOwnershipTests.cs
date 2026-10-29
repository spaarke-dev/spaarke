using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// UAC task 194b V-fix 3 (coordinator ruling, option a): an editable Office save byte-identical to an existing canonical is
/// created LINKED to it (<c>sprk_canonicaldocument</c>), and the canonical is a Secure-only parent of the copy's owner question
/// (<see cref="RecordOwnershipResolver.IsSecureOnlyParent"/>): a copy of Secure content is created Secure, and an ordinary
/// canonical never decides the copy's owner. The identity is read from the uploaded item, so the canonical-aware question is
/// asked after the upload and before the create; the create is owned as that answer says and never re-owned afterwards.
/// </summary>
/// <remarks>
/// <para><b>Why plain-text bytes.</b> A Word package is stamped with its own record id before it is stored, so two Word saves
/// never hash-equal and no link arises (OfficeSaveAsNewDocumentLinkGraduateTests). Bytes the stamper passes through unchanged
/// (not a Word package) keep the canonical's hash, which is the case a link — and so this rule — still applies to.</para>
/// <para>Module-boundary doubles only (ADR-038 §4); the owner resolver is the factory's
/// <see cref="Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble"/>, whose questions are read back.</para>
/// </remarks>
[Trait("status", "task-194b-uac-r2")]
public class OfficeCopyOfCanonicalOwnershipTests
{
    private const string DocumentDrive = "b!doc-drive";
    private const string FileName = "Notes.txt";
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("Plain notes that the stamper passes through unchanged.");
    private static readonly Guid SecureTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-0000000194b3");

    [Fact]
    public async Task ACopyOfACanonical_IsAskedAgainWithTheCanonicalAsASecureOnlyParent_TheMatterStaysThePrimary()
    {
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, _) = world.SeedDocument(DocumentDrive, FileName, Content);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var save = OfficeVersionSaveWorld.NewDocumentSave(FileName, Content);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", save);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var copy = world.Documents.Values.Single(r => r.Id != canonicalId);
        copy.CanonicalDocumentId.Should().Be(canonicalId, "the bytes pass through unstamped, so the copy links to its canonical");

        var link = new RecordOwnershipParent("sprk_document", canonicalId);
        var matter = new RecordOwnershipParent("sprk_matter", save.TargetEntity!.EntityId);
        var asked = factory.Ownership.Requests.ToArray();
        asked.Should().HaveCount(2, "once before anything is written, once with the canonical the upload identified");
        asked[0].SecureOnlyParents.Should().BeEmpty();
        asked[1].SecureOnlyParents.Should().Equal(new[] { link }, "a copy of Secure content is Secure");
        asked[1].Parents.Should().Equal(asked[0].Parents, "the canonical is never the copy's primary");
        asked[1].Parents.Should().Contain(matter);
        asked[1].Parents.Should().NotContain(link);
        world.CreatedDocumentOwningTeams.Should().Equal(
            new Guid?[] { Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId },
            "an ordinary canonical leaves the matter's answer as it is");
    }

    [Fact]
    public async Task ACopyOfASecureCanonical_IsCreatedOwnedByTheSecureTeam_AndLinked()
    {
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, _) = world.SeedDocument(DocumentDrive, FileName, Content);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        factory.Ownership.AnswerFor = context =>
            context.SecureOnlyParents.Any(p => p.RecordId == canonicalId)
                ? RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true }
                : null;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave(FileName, Content));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        world.CreatedDocumentOwningTeams.Should().Equal(new Guid?[] { SecureTeam },
            "created Secure on the create itself, never re-owned afterwards");
        world.Documents.Values.Single(r => r.Id != canonicalId).CanonicalDocumentId.Should().Be(canonicalId);
    }

    [Fact]
    public async Task ACopyWhoseCanonicalAwareOwnerIsRefused_IsNotSaved_AndItsUploadIsRemoved()
    {
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, canonicalItemId) = world.SeedDocument(DocumentDrive, FileName, Content);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        factory.Ownership.AnswerFor = context =>
            context.SecureOnlyParents.Count > 0
                ? RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "the canonical could not be read")
                : null;

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave(FileName, Content));

        (await response.Content.ReadAsStringAsync()).Should().Contain(OfficeErrorCodes.RecordOwnerUnresolved);
        world.DocumentCreates.Should().Be(0, "no document is written when the copy's owner cannot be decided");
        world.Documents.Should().ContainSingle().Which.Key.Should().Be(canonicalId);
        world.DeletedItemIds.Should().ContainSingle().Which.Should().NotBe(canonicalItemId,
            "the upload this save made is removed; the canonical's file is untouched");
    }

    [Fact]
    public void DocumentOwnershipOf_HoldsTheCanonicalAsASecureOnlyParent_AsTheReconcilerReadsTheWrittenCopy()
    {
        var matter = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        var written = new Microsoft.Xrm.Sdk.Entity("sprk_document", Guid.NewGuid())
        {
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
            ["sprk_canonicaldocument"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", canonical),
        };

        var asked = OfficeDocumentPersistence.DocumentOwnershipOf("sprk_matter", matter, null, null, canonicalDocumentId: canonical);

        asked.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(written));
        asked.SecureOnlyParents.Should().Equal(RecordOwnershipContext.SecureOnlyParentsOf(written));
        asked.SecureOnlyParents.Should().Equal(new RecordOwnershipParent("sprk_document", canonical));
        OfficeDocumentPersistence.DocumentOwnershipOf("sprk_matter", matter, null, null).SecureOnlyParents.Should().BeEmpty();
    }
}
