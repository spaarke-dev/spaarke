using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 177 (D-217, owner 2026-11-10): every outgoing email whose created <c>sprk_communication</c> is filed to a record
/// (any typed regarding lookup written on the row) is archived as an .eml Document, whatever <c>ArchiveToSpe</c> says, on BOTH
/// send paths (shared mailbox / app-only and send-as-user) and per recipient of a bulk send. A record-less send keeps the
/// opt-in flag; the sender account's <c>ArchiveOutgoingOptIn</c> still skips it; an archive failure is still non-fatal.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4), as <see cref="OutboundAttachmentArchiveTests"/>: the channel sender seam, the
/// <see cref="SpeFileStore"/> facade's virtual upload, the Dataverse writer (<see cref="IGenericEntityService"/>), the account
/// cache and the job queue. The assertions read the entities the service handed to the writer: the created communication (what
/// was filed) and the created archive document (what was archived).
/// </remarks>
public class OutgoingFiledEmailArchiveTests
{
    private const string ArchiveContainerId = "drive-archive";
    private const string SharedMailbox = "noreply@contoso.com";
    private const string UserMailbox = "author@contoso.com";
    private const int EmailArchiveSourceType = 659490003;

    private static readonly Guid MatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid OtherMatterId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");
    private static readonly Guid ServiceRequestId = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000001");

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required List<(Entity Entity, Guid Id)> Created { get; init; }
        public required List<string> UploadPaths { get; init; }
        public required List<JobContract> Jobs { get; init; }

        public List<(Entity Entity, Guid Id)> Communications =>
            Created.Where(c => c.Entity.LogicalName == "sprk_communication").ToList();

        public List<(Entity Entity, Guid Id)> ArchivedEmls => Created
            .Where(c => c.Entity.LogicalName == "sprk_document"
                        && c.Entity.GetAttributeValue<OptionSetValue>("sprk_sourcetype")?.Value == EmailArchiveSourceType)
            .ToList();
    }

    // ── criterion 1: a filed send with ArchiveToSpe = false archives, on both paths ─────────────────────────────

    [Fact]
    public async Task SharedMailboxPath_AssignTaskNoticeShape_FiledToAMatter_IsArchived_ThoughArchiveToSpeIsFalse()
    {
        // The Assign Task notice: shared mailbox, internal-only, filed under its record, no ArchiveToSpe.
        var h = Build();
        var request = Request(SendMode.SharedMailbox) with
        {
            IsInternalOnly = true,
            Associations = [Matter(MatterId)],
        };

        var response = await h.Sut.SendAsync(request);

        var communication = h.Communications.Should().ContainSingle().Subject;
        communication.Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        var eml = h.ArchivedEmls.Should().ContainSingle("a communication filed to a record is archived (D-217)").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(communication.Id);
        response.ArchivedDocumentId.Should().Be(eml.Id);
        response.ArchivalWarning.Should().BeNull();
        h.Jobs.Select(j => j.SubjectId).Should().Contain(eml.Id.ToString(), "the archived .eml is profiled, as a requested archive is");
    }

    [Fact]
    public async Task UserPath_FiledToAMatter_IsArchived_ThoughArchiveToSpeIsFalse()
    {
        var h = Build();

        var response = await h.Sut.SendAsync(
            Request(SendMode.User) with { Associations = [Matter(MatterId)] }, UserContext());

        var communication = h.Communications.Should().ContainSingle().Subject;
        var eml = h.ArchivedEmls.Should().ContainSingle("a communication filed to a record is archived (D-217)").Subject;
        eml.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(communication.Id);
        response.ArchivedDocumentId.Should().Be(eml.Id);
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task BudgetInquiryShape_TwoLookups_IsArchivedOnce(SendMode mode)
    {
        // Task 176: filed under the service request AND the matter. One .eml per communication, never one per lookup.
        var h = Build();
        var request = Request(mode) with
        {
            IsInternalOnly = true,
            Associations =
            [
                new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId },
                Matter(MatterId) with { AlsoRegarding = true },
            ],
        };

        await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        var communication = h.Communications.Should().ContainSingle().Subject.Entity;
        communication.GetAttributeValue<EntityReference>("sprk_regardingservicerequest").Id.Should().Be(ServiceRequestId);
        communication.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        h.ArchivedEmls.Should().ContainSingle("one document per communication");
    }

    // ── C1: the archive document names its communication IN its create, on both paths ───────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task ArchivedEml_CarriesItsCommunication_InTheSameCreate(SendMode mode)
    {
        var h = Build();

        await h.Sut.SendAsync(Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        // The entity handed to CreateAsync is the whole row: the link is not a later update.
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject.Entity;
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Should().NotBeNull();
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").LogicalName.Should().Be("sprk_communication");
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.Communications.Single().Id);
    }

    // ── criterion 2: reply inheritance; record-less sends keep the opt-in ───────────────────────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task Reply_WithNoAssociations_InheritingRegardingFromItsSource_IsArchived(SendMode mode)
    {
        var source = Guid.NewGuid();
        var h = Build(sourceCommunication: new Entity("sprk_communication", source)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_isinternalonly"] = false,
        });

        await h.Sut.SendAsync(
            Request(mode) with { InheritRegardingFromCommunicationId = source },
            mode == SendMode.User ? UserContext() : null);

        h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId,
            "the reply inherited its source's regarding");
        h.ArchivedEmls.Should().ContainSingle("the filing is decided from the created row, not from Associations");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task PostIntoAThread_WithNoAssociations_InheritingTheThreadsRegarding_IsArchived(SendMode mode)
    {
        // Review F3 (must fire): an explicit ThreadId send with no Associations and ArchiveToSpe = false takes the thread's
        // regarding (task 065 fix round 2) and so is filed — the decision reads the created row, not Associations.
        var threadId = Guid.NewGuid();
        var h = Build(targetThread: new Entity("sprk_communicationthread", threadId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
        });

        await h.Sut.SendAsync(Request(mode) with { ThreadId = threadId }, mode == SendMode.User ? UserContext() : null);

        h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId,
            "the post inherited its thread's regarding");
        h.ArchivedEmls.Should().ContainSingle("a post filed through its thread is archived (D-217)");
        h.UploadPaths.Should().ContainSingle();
    }

    [Fact]
    public async Task SystemMailSend_FiledToAMatter_IsNeverArchived()
    {
        // Review F3 (must not fire): system mail keeps its secret out of storage, so it is never archived, filed or not.
        var h = Build();
        var request = Request(SendMode.SharedMailbox) with
        {
            Associations = [Matter(MatterId)],
            SystemMail = new SystemMailOptions { StoredBody = "<p>[redacted]</p>" },
        };

        var response = await h.Sut.SendAsync(request);

        h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId,
            "the row is filed, so only the system-mail exclusion keeps it out of the archive");
        h.ArchivedEmls.Should().BeEmpty();
        h.UploadPaths.Should().BeEmpty("no .eml is uploaded for system mail");
        response.ArchivedDocumentId.Should().BeNull();
    }

    [Fact]
    public async Task ServerSend_FiledToAMatter_IsNeverArchived()
    {
        // Review F3 (must not fire): a server-only send never copies its content into storage, filed or not.
        var h = Build();
        var request = Request(SendMode.User) with
        {
            Associations = [Matter(MatterId)],
            ServerSend = new ServerSendOptions(),
        };

        var response = await h.Sut.SendAsync(request, UserContext());

        h.Communications.Single().Entity.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId,
            "the row is filed, so only the server-send exclusion keeps it out of the archive");
        h.ArchivedEmls.Should().BeEmpty();
        h.UploadPaths.Should().BeEmpty("no .eml is uploaded for a server-only send");
        response.ArchivedDocumentId.Should().BeNull();
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task RecordLessSend_ArchiveToSpeFalse_IsNotArchived(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(Request(mode), mode == SendMode.User ? UserContext() : null);

        h.Communications.Should().ContainSingle();
        h.ArchivedEmls.Should().BeEmpty("a record-less send keeps the opt-in flag");
        h.UploadPaths.Should().BeEmpty();
        response.ArchivedDocumentId.Should().BeNull();
        response.ArchivalWarning.Should().BeNull();
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task RecordLessSend_ArchiveToSpeTrue_IsArchived_AsToday(SendMode mode)
    {
        var h = Build();

        var response = await h.Sut.SendAsync(Request(mode) with { ArchiveToSpe = true }, mode == SendMode.User ? UserContext() : null);

        h.ArchivedEmls.Should().ContainSingle();
        response.ArchivedDocumentId.Should().Be(h.ArchivedEmls.Single().Id);
    }

    // ── criterion 3: the sender account's switch still skips; a failure is still non-fatal ──────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FiledSend_SenderAccountOptedOut_IsNotArchived_WithTodaysWarning(SendMode mode)
    {
        var h = Build(optedOutMailbox: mode == SendMode.User ? UserMailbox : SharedMailbox);
        var request = Request(mode) with
        {
            Associations = [Matter(MatterId)],
            // The user path reads the switch from the FromMailbox account, as today.
            FromMailbox = mode == SendMode.User ? UserMailbox : null,
        };

        var response = await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        h.ArchivedEmls.Should().BeEmpty();
        h.UploadPaths.Should().BeEmpty();
        response.ArchivalWarning.Should().Be("Archival skipped: ArchiveOutgoingOptIn is disabled for this sender account.");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task FiledSend_ArchiveFails_SendStillSucceeds_WithTodaysWarning(SendMode mode)
    {
        var h = Build(uploadFails: true);

        var response = await h.Sut.SendAsync(
            Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        response.CommunicationId.Should().Be(h.Communications.Single().Id, "the send and its record succeed");
        response.ArchivedDocumentId.Should().BeNull();
        response.ArchivalWarning.Should().StartWith("Email sent successfully but archival failed:");
        h.ArchivedEmls.Should().BeEmpty();
    }

    // ── criterion 4: bulk send archives each recipient's filed communication ───────────────────────────────────

    [Fact]
    public async Task BulkSend_FiledToAMatter_ArchivesEachRecipientsCommunication()
    {
        var h = Build();
        var bulk = new BulkSendRequest
        {
            Subject = "Status",
            Body = "<p>Update.</p>",
            Associations = [Matter(MatterId)],
            Recipients =
            [
                new BulkRecipient { To = "one@example.com" },
                new BulkRecipient { To = "two@example.com" },
            ],
        };

        var handler = typeof(CommunicationEndpoints).GetMethod(
            "SendBulkCommunicationAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task<IResult>)handler.Invoke(null, new object?[]
        {
            bulk, h.Sut, Mock.Of<ILogger<CommunicationService>>(), new DefaultHttpContext(), CancellationToken.None,
        })!;

        h.Communications.Should().HaveCount(2, "one communication per recipient");
        h.ArchivedEmls.Should().HaveCount(2, "each recipient's filed communication is archived");
        h.ArchivedEmls.Select(e => e.Entity.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id)
            .Should().BeEquivalentTo(h.Communications.Select(c => c.Id));
    }

    // ── A1/A2 + C2: the archived .eml through the REAL external document narrowing ─────────────────────────────

    [Theory]
    [InlineData(true, ThreadPrivacyState.Open, false)]   // an internal-only message's .eml: hidden
    [InlineData(false, ThreadPrivacyState.Private, false)] // a Private-thread message's .eml: hidden
    [InlineData(false, ThreadPrivacyState.Open, true)]   // control: an Open, not internal-only message's .eml: shown
    public async Task ArchivedEml_ThroughTheRealExternalNarrowing_FollowsItsMessage(
        bool internalOnly, ThreadPrivacyState threadPrivacy, bool visible)
    {
        var h = Build();
        await h.Sut.SendAsync(Request(SendMode.SharedMailbox) with { Associations = [Matter(MatterId)] });
        var (emlEntity, emlId) = h.ArchivedEmls.Single();
        var communicationId = h.Communications.Single().Id;

        // A3: the document exactly as the send created it, WITH the record link the send wrote (no stand-in): a contact with a
        // grant on that matter reaches it through this link, and the communication leg alone decides whether it is shown.
        var document = ArchivedEmlNarrowing.AsStored(emlEntity, emlId);
        document.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId, "the send links the .eml to its matter");

        // The communication and its thread as Dataverse would answer the narrowing's app-only reads. A contact and a systemuser
        // flagged external are both "not an internal systemuser": the narrowing is the same decision for both.
        var world = new ArchivedEmlNarrowing.World();
        world.AddCommunication(communicationId, internalOnly, (int)threadPrivacy);

        var (shown, reason) = await ArchivedEmlNarrowing.ExternalAsync(document, world);
        var shownOnTheInternalPlane = await ArchivedEmlNarrowing.InternalPlaneFlaggedExternalAsync(document, world);

        shown.Should().Be(visible, "the external plane (a contact with a grant on the linked matter)");
        shownOnTheInternalPlane.Should().Be(visible, "the internal plane, for a systemuser flagged external");
        if (!visible)
            reason.Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task InternalOnlyBudgetInquiryEml_LinkedToTheMatter_IsHiddenOnBothPlanes()
    {
        // Must not fire: the internal-only inquiry's .eml, linked by the send to the matter, never reaches an external caller.
        var h = Build();
        await h.Sut.SendAsync(Request(SendMode.SharedMailbox) with
        {
            IsInternalOnly = true,
            Associations =
            [
                new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId },
                Matter(MatterId) with { AlsoRegarding = true },
            ],
        });
        var (emlEntity, emlId) = h.ArchivedEmls.Single();
        var document = ArchivedEmlNarrowing.AsStored(emlEntity, emlId);
        document.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);

        var world = new ArchivedEmlNarrowing.World();
        world.AddCommunication(h.Communications.Single().Id, internalOnly: true, (int)ThreadPrivacyState.Open);

        var (shown, reason) = await ArchivedEmlNarrowing.ExternalAsync(document, world);
        shown.Should().BeFalse("a contact with a grant on the matter does not see an internal-only message's .eml");
        reason.Should().Be(DocumentNarrowingReason.CommunicationHidden);
        (await ArchivedEmlNarrowing.InternalPlaneFlaggedExternalAsync(document, world)).Should().BeFalse(
            "nor does a systemuser flagged external");
    }

    // ── A3 (D-218, uac-r2 Option A): the archived .eml is linked to its record in its own create ──────────────────────

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task ArchivedEml_IsLinkedToItsMatter_InTheSameCreateAsItsCommunicationLink(SendMode mode)
    {
        var h = Build();

        await h.Sut.SendAsync(Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        // One create carries both: the entity handed to CreateAsync holds the record link and the communication link.
        var eml = h.ArchivedEmls.Should().ContainSingle().Subject.Entity;
        var link = eml.GetAttributeValue<EntityReference>("sprk_matter");
        link.Should().NotBeNull("an archived email shows in its record's Documents (D-218)");
        link.LogicalName.Should().Be("sprk_matter");
        link.Id.Should().Be(MatterId);
        eml.GetAttributeValue<EntityReference>("sprk_relatedcommunication").Id.Should().Be(h.Communications.Single().Id);
        LinkColumnsOn(eml).Should().Equal(["sprk_matter"], "exactly one record link is written");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task BudgetInquiryShape_ArchivedEml_IsLinkedToTheMatter(SendMode mode)
    {
        var h = Build();
        var request = Request(mode) with
        {
            IsInternalOnly = true,
            Associations =
            [
                new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId },
                Matter(MatterId) with { AlsoRegarding = true },
            ],
        };

        await h.Sut.SendAsync(request, mode == SendMode.User ? UserContext() : null);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject.Entity;
        eml.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId,
            "a service request has no document column, so the first regarding the map maps (the matter) is the link");
        LinkColumnsOn(eml).Should().Equal(["sprk_matter"], "exactly one record link is written");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task ServiceRequestOnly_ArchivedEml_HasNoRecordLink_AndIsNotRefreshed(SendMode mode)
    {
        var refresh = new DocumentRefreshProbe();
        var h = Build(coreAncestors: refresh.Resolver);

        await h.Sut.SendAsync(
            Request(mode) with
            {
                Associations = [new CommunicationAssociation { EntityType = "sprk_servicerequest", EntityId = ServiceRequestId }],
            },
            mode == SendMode.User ? UserContext() : null);

        var eml = h.ArchivedEmls.Should().ContainSingle("it is filed, so it is archived (D-217)").Subject;
        LinkColumnsOn(eml.Entity).Should().BeEmpty("no regarding the document map maps means no link");
        refresh.Documents.Should().NotContain(eml.Id, "a document with no record link has no parent to inherit from");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task LinkedEml_ItsAccessPermissionIsRefreshedAfterTheCreate(SendMode mode)
    {
        var refresh = new DocumentRefreshProbe();
        var h = Build(coreAncestors: refresh.Resolver);

        await h.Sut.SendAsync(Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        refresh.Documents.Should().Contain(eml.Id, "a linked document takes its record's Access Permission at once (I-16)");
    }

    [Theory]
    [InlineData(SendMode.SharedMailbox)]
    [InlineData(SendMode.User)]
    public async Task LinkedEml_ARefreshThatFails_NeverFailsTheArchive(SendMode mode)
    {
        var refresh = new DocumentRefreshProbe(fails: true);
        var h = Build(coreAncestors: refresh.Resolver);

        var response = await h.Sut.SendAsync(
            Request(mode) with { Associations = [Matter(MatterId)] }, mode == SendMode.User ? UserContext() : null);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        refresh.Documents.Should().Contain(eml.Id);
        response.ArchivedDocumentId.Should().Be(eml.Id, "the reconciliation job corrects the value within its cycle");
        response.ArchivalWarning.Should().BeNull();
    }

    [Theory]
    [InlineData(CommunicationDirection.Outgoing, true)]
    [InlineData(CommunicationDirection.Incoming, false)]
    public async Task OnDemandArchive_LinksAnOutgoingEmail_NeverAnIncomingOne(CommunicationDirection direction, bool linked)
    {
        var communicationId = Guid.NewGuid();
        var h = Build(sourceCommunication: new Entity("sprk_communication", communicationId)
        {
            ["sprk_subject"] = "Filed earlier",
            ["sprk_to"] = "recipient@example.com",
            ["sprk_direction"] = new OptionSetValue((int)direction),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingrecordid"] = MatterId.ToString(),
        });

        var result = await h.Sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject;
        result.ArchiveDocumentId.Should().Be(eml.Id);
        if (linked)
        {
            LinkColumnsOn(eml.Entity).Should().Equal(["sprk_matter"],
                "the on-demand archive of an outgoing email is linked from the communication's regarding");
            eml.Entity.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        }
        else
        {
            LinkColumnsOn(eml.Entity).Should().BeEmpty("an inbound email's archive is not linked by this path");
        }
    }

    [Theory]
    [InlineData(CommunicationDirection.Outgoing)]
    [InlineData(CommunicationDirection.Incoming)]
    public async Task OnDemandArchive_WithNoEmbeddedAttachments_WritesNoAccessNarrowing(CommunicationDirection direction)
    {
        // Must not fire (uac-r2 F-177-R2-1): nothing is embedded, so nothing narrows the .eml and no source is read for it.
        var communicationId = Guid.NewGuid();
        var h = Build(sourceCommunication: new Entity("sprk_communication", communicationId)
        {
            ["sprk_subject"] = "Filed earlier",
            ["sprk_to"] = "recipient@example.com",
            ["sprk_direction"] = new OptionSetValue((int)direction),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingrecordid"] = MatterId.ToString(),
        });

        await h.Sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        var eml = h.ArchivedEmls.Should().ContainSingle().Subject.Entity;
        eml.Attributes.Should().NotContainKey("sprk_accesspermission");
        eml.Attributes.Should().NotContainKey("sprk_accessinheritance");
    }

    [Fact]
    public void StageArchiveRecordLink_PrimaryWins_WhenTheMapMapsIt()
    {
        // The primary (a project) is not first in RegardingFieldMap order (the matter is), and still wins.
        var projectId = Guid.NewGuid();
        var communication = new Entity("sprk_communication", Guid.NewGuid())
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", projectId),
            ["sprk_regardingrecordid"] = projectId.ToString(),
        };
        var document = new Entity("sprk_document");

        CommunicationService.StageArchiveRecordLink(document, communication).Should().BeTrue();

        LinkColumnsOn(document).Should().Equal(["sprk_project"]);
        document.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(projectId);
    }

    /// <summary>The record-association columns a document create may write (DocumentAssociationMap's columns), as written.</summary>
    private static List<string> LinkColumnsOn(Entity document) =>
        new[] { "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment", "sprk_relatedevent", "sprk_relatedtodo", "sprk_relatedcontact" }
            .Where(document.Attributes.ContainsKey).ToList();

    /// <summary>
    /// A real <c>CoreAncestorResolver</c> whose reads record every <c>sprk_document</c> its Access Permission refresh reads (the
    /// refresh reads the document before it decides), and can fail those reads.
    /// </summary>
    private sealed class DocumentRefreshProbe
    {
        public List<Guid> Documents { get; } = new();
        public Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver Resolver { get; }

        public DocumentRefreshProbe(bool fails = false)
        {
            var reads = new Mock<IGenericEntityService>(MockBehavior.Loose);
            reads.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                {
                    if (table == "sprk_document")
                    {
                        Documents.Add(id);
                        if (fails)
                            throw new InvalidOperationException("Dataverse unavailable");
                    }

                    return new Entity(table, id);
                });
            Resolver = new Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver(
                reads.Object,
                CoreAncestorResolverFixtures.ProbeReturning(
                [
                    .. CoreAncestorResolverFixtures.AllRootColumns,
                    Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.Column,
                    Sprk.Bff.Api.Services.Access.AccessInheritance.Column,
                ]),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance);
        }
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static CommunicationAssociation Matter(Guid id) => new() { EntityType = "sprk_matter", EntityId = id };

    private static SendCommunicationRequest Request(SendMode mode) => new()
    {
        To = ["recipient@example.com"],
        Subject = "Status update",
        Body = "<p>Hello.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        SendMode = mode,
        ArchiveToSpe = false,
    };

    private static HttpContext UserContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserMailbox),
            new Claim("oid", Guid.NewGuid().ToString()),
        }, "Test")),
    };

    private static Harness Build(
        Entity? sourceCommunication = null, string? optedOutMailbox = null, bool uploadFails = false, Entity? targetThread = null,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver? coreAncestors = null)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = SharedMailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = SharedMailbox,
            ArchiveContainerId = ArchiveContainerId,
        };

        // The Dataverse writer: every create recorded with the id it was given; the reply source answers its read.
        var created = new List<(Entity Entity, Guid Id)>();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                sourceCommunication is not null && sourceCommunication.Id == id ? sourceCommunication : null!);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                targetThread is not null && targetThread.Id == id ? targetThread : null!);
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                created.Add((e, id));
                return id;
            });

        var sender = new Mock<ICommunicationChannelSender>();
        sender.SetupGet(s => s.SupportedType).Returns(CommunicationType.Email);
        sender
            .Setup(s => s.SendAsync(It.IsAny<ChannelSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSendRequest r, CancellationToken _) => new ChannelSendResult { FromAddress = r.FromAddress });
        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender.Object },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        // The send-enabled accounts (the cache answers them): the opted-out mailbox has ArchiveOutgoingOptIn = false.
        var accounts = new[] { SharedMailbox, UserMailbox }.Select(address => new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = address,
            EmailAddress = address,
            SendEnabled = true,
            ArchiveOutgoingOptIn = !string.Equals(address, optedOutMailbox, StringComparison.OrdinalIgnoreCase),
        }).ToArray();
        var accountCache = new Mock<IDistributedCache>();
        accountCache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts)));
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), accountCache.Object, Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var uploadPaths = new List<string>();
        var spe = BuildSpeFileStore(uploadPaths, uploadFails);

        var jobs = new List<JobContract>();
        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            // Issue #2033: a send as user needs the caller's enabled systemuser.
            Mock.Of<ICommunicationDataverseService>(c =>
                c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<Guid?>(Guid.NewGuid())),
            entityService.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs),
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            coreAncestors ?? CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: SpeScopeFactoryStub.Create(spe.Object, SpeScopeFactoryStub.NonSecureContainerResolver()));

        return new Harness { Sut = sut, Created = created, UploadPaths = uploadPaths, Jobs = jobs };
    }

    private static Mock<SpeFileStore> BuildSpeFileStore(List<string> uploadPaths, bool uploadFails)
    {
        var metadataCache = new GraphMetadataCache(
            new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Mock.Of<ILogger<GraphMetadataCache>>());
        var gcf = Mock.Of<IGraphClientFactory>();
        var speMock = new Mock<SpeFileStore>(
            MockBehavior.Loose,
            new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>(), metadataCache),
            new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string path, Stream _, CancellationToken _) =>
            {
                if (uploadFails)
                    throw new InvalidOperationException("SharePoint Embedded upload failed");
                uploadPaths.Add(path);
                return new FileHandleDto(
                    Id: $"item-{uploadPaths.Count}", Name: path, ParentId: null, Size: 1,
                    CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                    ETag: null, IsFolder: false, WebUrl: null, DriveId: driveId);
            });

        return speMock;
    }

    private static JobSubmissionService BuildJobQueue(List<JobContract> submitted)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose, sbOptions.Object, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((JobContract job, CancellationToken _) => submitted.Add(job))
            .Returns(Task.CompletedTask);
        return jobs.Object;
    }
}
