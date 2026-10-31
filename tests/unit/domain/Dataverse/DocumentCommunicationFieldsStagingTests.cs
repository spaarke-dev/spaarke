using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126): the two <c>sprk_document</c> columns an email-derived document is CREATED
/// with — <c>sprk_relatedcommunication</c> (the email's communication) and its own <c>sprk_accesspermission</c>
/// (Restricted, when no communication could be identified) — as <see cref="DataverseServiceClientImpl.CreateDocumentAsync"/>
/// stages them onto the create entity.
/// </summary>
/// <remarks>
/// Every Office test above this layer mocks <c>CreateDocumentAsync</c>, so a wrong logical name, a wrong lookup target or
/// a plain <c>int</c> where the SDK needs an <see cref="OptionSetValue"/> would pass all of them and fail only against
/// live Dataverse. The stager is pure (no ServiceClient), so it is asserted directly — the
/// <c>AnalysisRegardingWriteTests</c> precedent. KEEP path: tests/unit/domain/** (ADR-038 §2 — pure mapping logic).
/// </remarks>
[Trait("status", "task-130-word-add-in-r1")]
public class DocumentCommunicationFieldsStagingTests
{
    private static readonly Guid CommunicationId = Guid.Parse("13013013-0130-4130-8130-000000000c0c");

    [Fact]
    public void StageDocumentCommunicationFields_WithACommunication_WritesTheLookupToSprkCommunication_AndNoAccessPermission()
    {
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentCommunicationFields(
            document, new CreateDocumentRequest { Name = "Status.eml", ContainerId = "b!c", RelatedCommunicationId = CommunicationId });

        var link = document["sprk_relatedcommunication"].Should().BeOfType<EntityReference>().Subject;
        link.LogicalName.Should().Be("sprk_communication");
        link.Id.Should().Be(CommunicationId);
        document.Contains("sprk_accesspermission").Should().BeFalse("a linked document keeps the default (Standard) value");
    }

    [Fact]
    public void StageDocumentCommunicationFields_Restricted_WritesTheOptionSetValue_AndItsOwnNarrowingRecord_AndNoLink()
    {
        // The request as the save-time writer leaves it when no communication could be identified (I-19).
        var request = new CreateDocumentRequest { Name = "Status.eml", ContainerId = "b!c" };
        Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(request, relatedCommunicationId: null)
            .Should().BeTrue();
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentCommunicationFields(document, request);

        document["sprk_accesspermission"].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000002, "Restricted");
        var staged = document["sprk_accessinheritance"].Should().BeOfType<string>().Subject;
        staged.Should().Be(request.AccessInheritanceRecord, "task 189: the value and its record in one write");
        var (record, unreadable) = Sprk.Bff.Api.Services.Access.AccessInheritance.Parse(staged);
        unreadable.Should().BeFalse();
        record!.OwnPermission.Should().Be(100000002);
        record.OwnProvisional.Should().BeTrue(
            "owner round 122: the save-time Restricted is provisional, so a later communication link releases it");
        document.Contains("sprk_relatedcommunication").Should().BeFalse();
    }

    [Fact]
    public void StageDocumentCommunicationFields_AnAccessPermissionWithoutItsRecord_IsRefused_AndStagesNoValue()
    {
        var document = new Entity("sprk_document");

        var act = () => DataverseServiceClientImpl.StageDocumentCommunicationFields(
            document, new CreateDocumentRequest { Name = "Status.eml", ContainerId = "b!c", AccessPermission = 100000002 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*sprk_accessinheritance*");
        document.Contains("sprk_accesspermission").Should().BeFalse("a bare value would read as inherited and be widened");
    }

    [Fact]
    public void StageDocumentCommunicationFields_ForADocumentSave_WritesNeitherColumn()
    {
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentCommunicationFields(
            document, new CreateDocumentRequest { Name = "Brief.docx", ContainerId = "b!c", RelatedCommunicationId = Guid.Empty });

        document.Attributes.Should().BeEmpty("a Document save is created exactly as before task 130");
    }

    // ── ebilling-r1 task 095: the from-email submission's .eml (own non-provisional Restricted + the marker) ──────────

    [Fact]
    [Trait("status", "task-095-ebilling-r1")]
    public void SubmissionNarrowing_StagesTheLink_Restricted_ANonProvisionalRecord_AndTheMarker_InTheOneCreate()
    {
        var request = new CreateDocumentRequest { Name = "Invoice email.eml", ContainerId = "b!c" };
        Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence
            .ApplyEmailCommunicationOutcome(request, CommunicationId, submissionNarrowing: true)
            .Should().BeFalse("this Restricted is chosen; it is not the unidentified-email fallback logged as an Error");
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentCommunicationFields(document, request);

        document["sprk_relatedcommunication"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(CommunicationId);
        document["sprk_accesspermission"].Should().BeOfType<OptionSetValue>().Which.Value.Should().Be(100000002, "Restricted");
        var (record, unreadable) = Sprk.Bff.Api.Services.Access.AccessInheritance.Parse((string)document["sprk_accessinheritance"]);
        unreadable.Should().BeFalse();
        record!.OwnPermission.Should().Be(100000002);
        record.OwnProvisional.Should().BeFalse("model 9.1: linking the communication must never release the submission's narrowing");
        document["sprk_restrictedbysubmission"].Should().Be(true);
    }

    [Fact]
    [Trait("status", "task-095-ebilling-r1")]
    public void SubmissionNarrowing_WithoutACommunication_IsRefused_AndSetsNothing()
    {
        foreach (var missing in new Guid?[] { null, Guid.Empty })
        {
            var request = new CreateDocumentRequest { Name = "Invoice email.eml", ContainerId = "b!c" };

            var act = () => Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence
                .ApplyEmailCommunicationOutcome(request, missing, submissionNarrowing: true);

            act.Should().Throw<InvalidOperationException>().WithMessage("*communication*");
            request.RestrictedBySubmission.Should().BeFalse();
            request.AccessPermission.Should().BeNull();
            request.AccessInheritanceRecord.Should().BeNull();
        }
    }

    [Theory]
    [Trait("status", "task-095-ebilling-r1")]
    [InlineData(null, null)]                    // no narrowing at all
    [InlineData(100000002, null)]               // Restricted without its record (refused by task 189's rule first)
    [InlineData(100000001, "{\"v\":1}")]        // a narrowing that is not Restricted
    public void TheMarker_WithoutItsOwnRestrictedAndRecord_IsRefused_AndNotStaged(int? permission, string? record)
    {
        var document = new Entity("sprk_document");
        var request = new CreateDocumentRequest
        {
            Name = "Invoice email.eml", ContainerId = "b!c", RelatedCommunicationId = CommunicationId,
            AccessPermission = permission, AccessInheritanceRecord = record, RestrictedBySubmission = true,
        };

        var act = () => DataverseServiceClientImpl.StageDocumentCommunicationFields(document, request);

        act.Should().Throw<InvalidOperationException>();
        document.Contains("sprk_restrictedbysubmission").Should().BeFalse();
    }

    [Theory]
    [Trait("status", "task-095-ebilling-r1")]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOrdinaryEmailSave_NeverCarriesTheMarker(bool identified)
    {
        var request = new CreateDocumentRequest { Name = "Status.eml", ContainerId = "b!c" };
        Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence
            .ApplyEmailCommunicationOutcome(request, identified ? CommunicationId : null);
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentCommunicationFields(document, request);

        request.RestrictedBySubmission.Should().BeFalse();
        document.Contains("sprk_restrictedbysubmission").Should().BeFalse();
    }
}
