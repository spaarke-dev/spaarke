using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 098 (procedure-and-file-model 9.1, (a′)): THE invoice predicate, driven through the REAL
/// <see cref="ExternalDataService.HideInvoicesForAsync"/> / <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/> over an
/// app-only Web API double (<see cref="InvoiceWorld"/>) that answers every query the predicate and task 188's narrowing send from
/// seeded rows, applying the <c>$filter</c> and projecting through <c>$select</c>. A query that drops a column the rule needs
/// reads it as "not set", so a test fails. Each rule leg is proven for a CIAM contact and for a flagged-external systemuser.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceVisibilityTests
{
    private const int Restricted = 100000002;
    private const int Standard = 100000000;
    private const int Open = 100000000;
    private const int Private = 100000001;

    private static readonly Guid FlaggedUser = Guid.Parse("09800000-0000-4000-8000-0000000000f1");
    private static readonly Guid InternalUser = Guid.Parse("09800000-0000-4000-8000-0000000000e1");
    private static readonly Guid FailingUser = Guid.Parse("09800000-0000-4000-8000-0000000000d1");

    private readonly InvoiceWorld _world = new();
    private readonly Mock<ISystemUserIdentityResolver> _identity = new(MockBehavior.Strict);

    public ExternalInvoiceVisibilityTests()
    {
        _identity.Setup(i => i.IsExternalAsync(FlaggedUser, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _identity.Setup(i => i.IsExternalAsync(InternalUser, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _identity.Setup(i => i.IsExternalAsync(FailingUser, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The isexternal read failed (test)."));
    }

    public static TheoryData<string> ExternalCallers => new() { "contact", "flagged-systemuser" };

    private static CallerPrincipal Caller(string kind) => kind switch
    {
        "contact" => new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            Email = "partner@firm.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        },
        "flagged-systemuser" => SystemUser(FlaggedUser),
        "internal-systemuser" => SystemUser(InternalUser),
        "lookup-fails" => SystemUser(FailingUser),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CallerPrincipal SystemUser(Guid id) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = id,
        Email = "user@spaarke.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private Task<InvoiceVisibilityResult> Decide(string caller, params Guid[] invoices) =>
        _world.NewService(_identity.Object).HideInvoicesForAsync(Caller(caller), invoices, _world.RootFlags);

    private async Task ShouldBeHidden(string caller, Guid invoice, InvoiceHiddenReason reason)
    {
        var decision = await Decide(caller, invoice);
        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Contain(InvoiceVisibilityResult.CodeOf(reason));
    }

    private async Task ShouldBeVisible(string caller, Guid invoice)
    {
        var decision = await Decide(caller, invoice);
        decision.IsVisible(invoice).Should().BeTrue(decision.HiddenSummary());
        decision.Faulted.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must fire (hidden), for a CIAM contact and for a flagged-external systemuser
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule2_ASourceThatIsOwnRestricted_AndNotAnEml_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule2_AnOwnRestrictedSource_WithTheMarkerButNotAnEml_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted, eml: false, marker: true);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule2_AnOwnRestrictedEml_WithoutTheMarker_HidesTheInvoice(string caller)
    {
        var unmarked = Guid.NewGuid();
        _world.Source(unmarked, permission: Restricted, eml: true, marker: false);
        var blank = Guid.NewGuid();
        _world.Source(blank, permission: Restricted, eml: true, marker: null);

        await ShouldBeHidden(caller, unmarked, InvoiceHiddenReason.SourceHidden);
        await ShouldBeHidden(caller, blank, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule3_ARestrictedAncestor_HidesTheInvoice_EvenUnderAMarkedEml(string caller)
    {
        var ancestor = _world.Document(Guid.NewGuid(), permission: Restricted);
        var plain = Guid.NewGuid();
        _world.Source(plain, parent: ancestor);
        var markedEml = Guid.NewGuid();
        _world.Source(markedEml, permission: Restricted, eml: true, marker: true, parent: ancestor);

        await ShouldBeHidden(caller, plain, InvoiceHiddenReason.SourceHidden);
        await ShouldBeHidden(caller, markedEml, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule4_AnInternalOnlyCommunication_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var thread = _world.Thread(Open);
        _world.Source(invoice, communication: _world.Communication(internalOnly: true, thread));

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule4_ACommunicationOnANonOpenThread_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, communication: _world.Communication(internalOnly: false, _world.Thread(Private)));

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule4_TheMarkedEmlsOwnCommunicationStillDecides(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted, eml: true, marker: true,
            communication: _world.Communication(internalOnly: true, _world.Thread(Open)));

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Roots_ASourceFiledUnderARestrictedRoot_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        _world.Source(invoice, matter: matter);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.RootRestricted);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_ASourceThatDoesNotComeBack_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.StoredFile(invoice, source: Guid.NewGuid()); // no such document

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceNotFound);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_ASourceLinkThatIsNotAGuid_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.StoredFileRaw(invoice, rawSource: "not-a-guid");

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_AnOwnValueThatIsNotAKnownLevel_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: 424242);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_AMarkerThatIsNotExactlyTrue_HidesTheInvoice(string caller)
    {
        var stringMarker = Guid.NewGuid();
        _world.Source(stringMarker, permission: Restricted, eml: true, rawMarker: "true");
        var stringEml = Guid.NewGuid();
        _world.Source(stringEml, permission: Restricted, rawEml: "true", marker: true);

        await ShouldBeHidden(caller, stringMarker, InvoiceHiddenReason.SourceHidden);
        await ShouldBeHidden(caller, stringEml, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_ARootWhoseFlagsCannotBeRead_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.Unreadable;
        _world.Source(invoice, matter: matter);
        var missingFlag = Guid.NewGuid();
        _world.Source(missingFlag, project: Guid.NewGuid()); // the flag reader does not return it

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.RootUnresolved);
        await ShouldBeHidden(caller, missingFlag, InvoiceHiddenReason.RootUnresolved);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Unreadable_ARootLinkThatIsNotAGuid_OrALiveParentThatCannotBeWalked_HidesTheInvoice(string caller)
    {
        var malformed = Guid.NewGuid();
        _world.Source(malformed, rawMatter: 42);
        var liveParent = Guid.NewGuid();
        _world.Source(liveParent, extra: ("_sprk_invoice_value", Guid.NewGuid().ToString())); // not in the filing: unresolved

        await ShouldBeHidden(caller, malformed, InvoiceHiddenReason.RootUnresolved);
        await ShouldBeHidden(caller, liveParent, InvoiceHiddenReason.RootUnresolved);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Roots_ALiveParentFiledUnderARestrictedRoot_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var otherInvoice = _world.Filed("sprk_invoice", ("sprk_matter", "sprk_matter", matter));
        _world.Source(invoice, extra: ("_sprk_invoice_value", otherInvoice.ToString()));

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.RootRestricted);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task AFilingWalkThatFaults_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, communication: _world.Communication(internalOnly: false, _world.Thread(Open)));
        _world.FilingReadThrows = true;

        var decision = await Decide(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
    }

    [Theory]
    [InlineData("contact", "sprk_storedfiles")]
    [InlineData("contact", "sprk_documents")]
    [InlineData("contact", "sprk_communications")]
    [InlineData("flagged-systemuser", "sprk_storedfiles")]
    [InlineData("flagged-systemuser", "sprk_documents")]
    [InlineData("flagged-systemuser", "sprk_communicationthreads")]
    public async Task AReadFault_HidesEveryAskedInvoice_AndSaysFaulted(string caller, string faultingSet)
    {
        var invoice = Guid.NewGuid();
        var clean = Guid.NewGuid();
        _world.Source(invoice, communication: _world.Communication(internalOnly: false, _world.Thread(Open)));
        _world.Source(clean);
        _world.Faults[faultingSet] = HttpStatusCode.ServiceUnavailable;

        var decision = await Decide(caller, invoice, clean);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.IsVisible(clean).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ARootFlagReadThatThrows_HidesEveryAskedInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, matter: Guid.NewGuid());
        _world.FlagReadThrows = true;

        var decision = await Decide(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task MoreSourceFilesThanOneReadMayReturn_HidesTheWholeChunk(string caller)
    {
        var busy = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var clean = _world.Document(Guid.NewGuid());
        for (var i = 0; i <= ExternalInvoiceVisibility.MaxSourceFilesPerChunk; i++)
            _world.StoredFile(busy, source: clean);
        _world.Source(neighbour);

        var decision = await Decide(caller, busy, neighbour);

        decision.IsVisible(busy).Should().BeFalse();
        decision.IsVisible(neighbour).Should().BeFalse("the read of its chunk may have been cut short");
        decision.HiddenSummary().Should().Contain("sources_truncated");
    }

    // Review round 1, F1: the confirm route (InvoiceSubmissionService.SubmitExistingDocumentAsync) records its source only on
    // sprk_invoicesubmission.sprk_document, with no Stored File.

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_AnInvoiceWhoseOnlySourceIsASubmissionsRestrictedDocument_IsHidden(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.SubmissionSource(invoice, permission: Restricted);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_ASubmissionsDocumentUnderARestrictedRoot_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = new RootRecordFlags(IsSecure: true, IsRestricted: true);
        _world.SubmissionSource(invoice, matter: matter);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.RootRestricted);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_ASubmissionsReadableDocument_LeavesTheInvoiceVisible(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.SubmissionSource(invoice, permission: Standard, matter: matter);

        await ShouldBeVisible(caller, invoice);
    }

    // Review round 2: a submission whose document link is empty hides the invoice (a submission with no document is refused
    // before any invoice exists, so the shape is never a legitimate one).

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_ASubmissionWhoseDocumentLinkIsEmpty_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.SubmissionSource(invoice, permission: Standard, matter: matter);
        _world.Submission(invoice, document: null);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_ADocumentLinkEmptiedAfterTheInvoiceWasCreated_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.SubmissionSource(invoice, permission: Restricted, matter: matter);
        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);

        _world.SetSubmissions(invoice, "_sprk_document_value", null);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    // Review round 3: the submission's channel (the shared InvoiceSubmissionChannel enum) decides what an empty document link
    // means. Spa is never a document source; Internal with an empty link is hidden unless the invoice has a sourceless
    // InvoicePdf (the internal upload's own PDF); an unknown channel is hidden.

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task SpaUpload_NoSubmissionDocument_AndASourcelessInvoicePdf_IsVisible(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.SpaUpload(invoice);

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task InternalUpload_NoSubmissionDocument_AndASourcelessInvoicePdf_IsVisible(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.InternalUpload(invoice);

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Internal_AnEmptyDocumentLink_AndNoStoredFile_IsHidden(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Submission(invoice, document: null, channel: InvoiceWorld.InternalChannel);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Internal_AnEmptyDocumentLink_AndOnlyASourcelessSupportingFile_IsHidden(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Submission(invoice, document: null, channel: InvoiceWorld.InternalChannel);
        _world.StoredFileRaw(invoice, rawSource: null, purpose: InvoiceWorld.InvoiceSupportingFile);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory]
    [InlineData("contact", 999)]
    [InlineData("contact", null)]
    [InlineData("contact", "internal")]
    [InlineData("flagged-systemuser", 999)]
    [InlineData("flagged-systemuser", null)]
    [InlineData("flagged-systemuser", "internal")]
    public async Task AnUnknownOrUnreadableChannel_HidesTheInvoice(string caller, object? channel)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.Submission(invoice, _world.Document(Guid.NewGuid(), permission: Standard, matter: matter), channel);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceKindUnknown);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task AnEmptySubmissionLink_PlusAStoredFileWithARestrictedSource_IsHidden_DecidedByTheFile(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.Submission(invoice, document: null, channel: InvoiceWorld.SpaChannel);
        _world.Source(invoice, permission: Restricted);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    /// <summary>
    /// Review round 4a, rule 3: an Internal submission with an empty document link is decided by the invoice's InvoicePdf.
    /// Sourceless: the upload's own PDF, visible. With a source document: decided by that document (readable visible,
    /// Restricted hidden), even when the row also carries a communication and part identity. With a communication or an
    /// attachment identity only: hidden. (Replaces round 3's case that hid an InvoicePdf with a readable source document.)
    /// </summary>
    public static TheoryData<string, string, string?> Rule3InvoicePdfShapes()
    {
        var data = new TheoryData<string, string, string?>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "sourceless", null);
            data.Add(caller, "readable-document", null);
            data.Add(caller, "readable-document-with-communication-and-part", null);
            data.Add(caller, "restricted-document", "source_hidden");
            data.Add(caller, "communication-only", "source_link_unreadable");
            data.Add(caller, "attachment-only", "source_link_unreadable");
            data.Add(caller, "communication-and-attachment-only", "source_link_unreadable");
        }

        return data;
    }

    [Theory, MemberData(nameof(Rule3InvoicePdfShapes))]
    public async Task Rule3_AnInternalEmptyLink_IsDecidedByTheInvoicePdf(string caller, string shape, string? hiddenCode)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.Submission(invoice, document: null, channel: InvoiceWorld.InternalChannel);
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open));
        _ = shape switch
        {
            "sourceless" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf),
            "readable-document" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf,
                sourceDocument: _world.Document(Guid.NewGuid(), permission: Standard, matter: matter)),
            "readable-document-with-communication-and-part" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf,
                _world.Document(Guid.NewGuid(), permission: Standard, matter: matter), communication, "eml-part-1"),
            "restricted-document" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf,
                sourceDocument: _world.Document(Guid.NewGuid(), permission: Restricted, matter: matter)),
            "communication-only" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf, sourceCommunication: communication),
            "attachment-only" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf, attachmentIdentity: "eml-part-1"),
            "communication-and-attachment-only" => _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf,
                sourceCommunication: communication, attachmentIdentity: "eml-part-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        if (hiddenCode is null)
        {
            await ShouldBeVisible(caller, invoice);
            return;
        }

        var decision = await Decide(caller, invoice);
        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Contain(hiddenCode);
    }

    // Review round 4a: writer-shaped fixtures (the 094 head and 095's contract). Each carries its route's real submission row
    // and its real Stored File rows.

    private Guid CleanMatter()
    {
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        return matter;
    }

    private Guid FirmCommunication() => _world.Communication(internalOnly: false, _world.Thread(Open));

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromDocument_AReadableDocument_IsVisible(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.FromDocument(invoice, _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter()));

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_AMarkedEml_IsVisibleToItsFirm(string caller)
    {
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        _world.FromEmail(invoice, _world.SubmissionEml(marked: true, communication, CleanMatter()), communication, supportingParts: 2);

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_AnUnmarkedRestrictedEml_IsHidden(string caller)
    {
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        _world.FromEmail(invoice, _world.SubmissionEml(marked: false, communication, CleanMatter()), communication);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_NoInvoicePdf_OnlySupportingFiles_IsHidden(string caller)
    {
        // A supporting file never counts as the upload's own PDF, even with a source the firm may see.
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        var eml = _world.SubmissionEml(marked: true, communication, CleanMatter());
        _world.Submission(invoice, document: null, channel: InvoiceWorld.InternalChannel);
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoiceSupportingFile, eml, communication, "eml-part-2");

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_ASupportingFileWithARestrictedSource_HidesTheInvoice(string caller)
    {
        // A supporting file with a source is still decided by that source.
        var invoice = Guid.NewGuid();
        var matter = CleanMatter();
        var communication = FirmCommunication();
        _world.FromEmail(invoice, _world.SubmissionEml(marked: true, communication, matter), communication, supportingParts: 0);
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoiceSupportingFile,
            _world.Document(Guid.NewGuid(), permission: Restricted, matter: matter), communication, "eml-part-2");

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_AStoredFileWhosePurposeReadsNull_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        _world.FromDocument(invoice, _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter()));
        _world.SetStoredFile(_world.StoredFilesOf(invoice).Single(), "sprk_purpose", null);

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_AFieldSecurityMaskedRow_HidesTheInvoice(string caller)
    {
        // The invoice is visible to its firm; one of its rows then reads as a reader without the profile reads it.
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        _world.FromEmail(invoice, _world.SubmissionEml(marked: true, communication, CleanMatter()), communication, supportingParts: 1);
        await ShouldBeVisible(caller, invoice);

        _world.MaskStoredFile(_world.StoredFilesOf(invoice).Last());

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task WriterShape_FromEmail_AFailedStoredFileRead_HidesTheInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        _world.FromEmail(invoice, _world.SubmissionEml(marked: true, communication, CleanMatter()), communication);
        _world.Faults["sprk_storedfiles"] = HttpStatusCode.ServiceUnavailable;

        var decision = await Decide(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Fact]
    public async Task WriterShape_FromEmail_AFailedInvoiceRead_HidesItsRows()
    {
        // The rows' invoice links are read first (taint closure): a failed read gives no answer at all; a link that is not a
        // GUID hides the row. The same invoice's rows are visible when the read succeeds.
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        var submission = _world.FromEmail(invoice, _world.SubmissionEml(marked: true, communication, CleanMatter()), communication);
        var unreadable = _world.FamilyRow("sprk_invoicesubmission", invoice: null);
        var service = _world.NewService(_identity.Object);

        (await service.HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission }, _world.RootFlags))
            .Should().BeEmpty("the from-email invoice is visible to its firm");

        _world.SetRow("sprk_invoicesubmissions", "sprk_invoicesubmissionid", unreadable, "_sprk_invoice_value", "not-a-guid");
        (await service.HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission, unreadable }, _world.RootFlags))
            .Should().BeEquivalentTo(new[] { unreadable });

        _world.Faults["sprk_invoicesubmissions"] = HttpStatusCode.ServiceUnavailable;
        var act = () => service.HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission }, _world.RootFlags);
        await act.Should().ThrowAsync<Exception>("a failed invoice read gives the caller no data");
        (await Decide("contact", invoice)).IsVisible(invoice).Should().BeFalse("the predicate's own submission read failed");
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task Rule2_ASubmissionsOwnDocument_HonoursTheMarker(string caller)
    {
        // The legacy confirm shape (no Stored File): the submission's own document is the only source.
        var marked = Guid.NewGuid();
        _world.Submission(marked, _world.SubmissionEml(marked: true, FirmCommunication(), CleanMatter()));
        var unmarked = Guid.NewGuid();
        _world.Submission(unmarked, _world.SubmissionEml(marked: false, FirmCommunication(), CleanMatter()));

        await ShouldBeVisible(caller, marked);
        await ShouldBeHidden(caller, unmarked, InvoiceHiddenReason.SourceHidden);
    }

    [Fact]
    public async Task DecideSourceDocuments_DecidesEachSource_AndHidesOneItWasNotAskedOrCouldNotRead()
    {
        var visible = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var restricted = _world.Document(Guid.NewGuid(), permission: Restricted);
        var missing = Guid.NewGuid();
        var service = _world.NewService(_identity.Object);
        var http = new HttpClient(_world);
        CommunicationOpenThreadGate.RowQuery query = async (set, odata, ct) =>
        {
            var response = await http.GetAsync($"https://org.test/api/data/v9.2/{set}?{odata}", ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("value").EnumerateArray()
                .Select(row => row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()))
                .ToList();
        };
        var readers = new InvoiceVisibilityReaders(
            query,
            (rows, ct) => ExternalDocumentNarrowing.DecideAsync(rows, query, ct, honourSubmissionMarker: true),
            service.GetLiveParentRootsAsync,
            _world.RootFlags,
            NullLogger.Instance);

        var decision = await ExternalInvoiceVisibility.DecideSourceDocumentsAsync(
            new[] { visible, restricted, missing, Guid.Empty }, readers, cost: null, CancellationToken.None);

        decision.IsVisible(visible).Should().BeTrue();
        decision.HiddenReason(restricted).Should().Be(InvoiceHiddenReason.SourceHidden);
        decision.HiddenReason(missing).Should().Be(InvoiceHiddenReason.SourceNotFound);
        decision.IsVisible(Guid.Empty).Should().BeFalse();
        decision.IsVisible(Guid.NewGuid()).Should().BeFalse("a source it was not asked about is hidden");
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ConfirmRoute_ASubmissionReadThatFaults_HidesEveryAskedInvoice(string caller)
    {
        var invoice = Guid.NewGuid();
        var clean = Guid.NewGuid();
        _world.SubmissionSource(invoice, permission: Standard);
        _world.Source(clean);
        _world.Faults["sprk_invoicesubmissions"] = HttpStatusCode.ServiceUnavailable;

        var decision = await Decide(caller, invoice, clean);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.IsVisible(clean).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmRoute_MoreSubmissionsThanOneReadMayReturn_HidesTheWholeChunk()
    {
        var busy = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var clean = _world.Document(Guid.NewGuid());
        for (var i = 0; i <= ExternalInvoiceVisibility.MaxSourceFilesPerChunk; i++)
            _world.Submission(busy, clean);
        _world.Source(neighbour);

        var decision = await Decide("contact", busy, neighbour);

        decision.IsVisible(busy).Should().BeFalse();
        decision.IsVisible(neighbour).Should().BeFalse("the read of its chunk may have been cut short");
        decision.HiddenSummary().Should().Contain("sources_truncated");
    }

    // Review round 1, K1: a Stored File whose source is a communication or an attachment but no document has a source this
    // rule cannot check (090's file route refuses the same shape).

    [Theory]
    [InlineData("contact", "communication")]
    [InlineData("contact", "attachment")]
    [InlineData("flagged-systemuser", "communication")]
    [InlineData("flagged-systemuser", "attachment")]
    public async Task AStoredFileWithASourceCommunicationOrAttachment_ButNoDocument_HidesTheInvoice(string caller, string kind)
    {
        var invoice = Guid.NewGuid();
        if (kind == "communication")
            _world.StoredFileRaw(invoice, rawSource: null, sourceCommunication: Guid.NewGuid());
        else
            _world.StoredFileRaw(invoice, rawSource: null, attachmentIdentity: "AAMkAD-attachment-1");

        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceLinkUnreadable);
    }

    [Fact]
    public async Task UnknownIsNotInternal_ASystemuserWhoseLookupFails_IsDecidedAsExternal()
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted);

        await ShouldBeHidden("lookup-fails", invoice, InvoiceHiddenReason.SourceHidden);
    }

    [Fact]
    public async Task UnknownIsNotInternal_NoIdentityResolver_DecidesEveryCallerAsExternal()
    {
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted);

        var decision = await _world.NewService(identity: null)
            .HideInvoicesForAsync(Caller("internal-systemuser"), new[] { invoice }, _world.RootFlags);

        decision.IsVisible(invoice).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must not fire (visible)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task AMarkedEml_OwnRestricted_AndOtherwiseClean_LeavesTheInvoiceVisible(string caller)
    {
        var invoice = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = RootRecordFlags.None;
        _world.Source(invoice, permission: Restricted, eml: true, marker: true, matter: matter,
            communication: _world.Communication(internalOnly: false, _world.Thread(Open)));

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task AnInvoiceWithNoSource_IsNotHiddenByThisRule(string caller)
    {
        var noFile = Guid.NewGuid();
        var fileWithoutSource = Guid.NewGuid();
        _world.StoredFile(fileWithoutSource, source: null);

        await ShouldBeVisible(caller, noFile);
        await ShouldBeVisible(caller, fileWithoutSource);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ASourceRefiledToARootTheCallerHoldsNothingOn_LeavesTheInvoiceVisible_Rule1IsNotUsed(string caller)
    {
        var invoice = Guid.NewGuid();
        var otherMatter = Guid.NewGuid(); // the caller holds no grant on it (no access maps at all)
        _world.Flags[otherMatter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _world.Source(invoice, matter: otherMatter);

        await ShouldBeVisible(caller, invoice);
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task ASourceLoweredAfterHiding_IsVisibleOnTheNextRead_NothingIsCached(string caller)
    {
        var invoice = Guid.NewGuid();
        var source = _world.Source(invoice, permission: Restricted);
        await ShouldBeHidden(caller, invoice, InvoiceHiddenReason.SourceHidden);

        _world.SetDocument(source, "sprk_accesspermission", Standard);

        await ShouldBeVisible(caller, invoice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AnInternalSystemuser_IsNeverHiddenByThisRule_AndNothingIsRead(bool? flag)
    {
        // Review round 1, F4: the REAL resolver reads systemuser.sprk_isexternal as false, or finds it blank (owner round 67).
        var user = Guid.NewGuid();
        var row = new Entity("systemuser", user);
        if (flag is { } stored)
            row["sprk_isexternal"] = stored;
        var flagReads = new List<QueryExpression>();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => flagReads.Add(q))
            .ReturnsAsync(new EntityCollection(new List<Entity> { row }));
        var resolver = new SystemUserIdentityResolver(
            dataverse.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<SystemUserIdentityResolver>.Instance);
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted);

        var decision = await _world.NewService(resolver)
            .HideInvoicesForAsync(SystemUser(user), new[] { invoice }, _world.RootFlags);

        decision.IsVisible(invoice).Should().BeTrue($"an internal systemuser (flag {flag?.ToString() ?? "blank"}) is never hidden");
        flagReads.Should().ContainSingle(q => q.EntityName == "systemuser" && q.ColumnSet.Columns.Contains("sprk_isexternal"),
            "the flag itself decides, read by the real resolver");
        _world.Urls.Should().BeEmpty("an internal caller costs no read");
    }

    [Fact]
    public async Task AnInvoiceThePredicateWasNotAskedAbout_IsNeverVisible()
    {
        var asked = Guid.NewGuid();

        var decision = await Decide("contact", asked);

        decision.IsVisible(Guid.NewGuid()).Should().BeFalse();
        decision.IsVisible(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public async Task OneSourceHiddenAmongSeveral_HidesTheInvoice_AndOnlyThatInvoice()
    {
        var invoice = Guid.NewGuid();
        var other = Guid.NewGuid();
        _world.Source(invoice);
        _world.Source(invoice, permission: Restricted);
        _world.Source(other);

        var decision = await Decide("contact", invoice, other);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.IsVisible(other).Should().BeTrue();
    }

    [Fact]
    public async Task ThePageIsDecidedInBatchedReads_NotOnePerInvoice()
    {
        var invoices = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var invoice in invoices)
            _world.Source(invoice, communication: _world.Communication(internalOnly: false, _world.Thread(Open)));

        var decision = await Decide("contact", invoices);

        invoices.Should().OnlyContain(i => decision.IsVisible(i));
        _world.Reads("sprk_storedfiles").Should().HaveCount(1);
        _world.Reads("sprk_invoicesubmissions").Should().HaveCount(1);
        _world.Reads("sprk_communications").Should().HaveCount(1);
        _world.Reads("sprk_communicationthreads").Should().HaveCount(1);
        decision.Cost.StoredFileReads.Should().Be(1);
        decision.Cost.SubmissionReads.Should().Be(1);
        decision.Cost.SourceReads.Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Review round 4b: the external submission routes' decision (ExternalDataService.HideSubmissionsForAsync). A submission with
    // an invoice is decided by the invoice predicate; one without an invoice by its own source. Writer-shaped rows throughout.
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    private Task<InvoiceVisibilityResult> DecideSubmissions(string caller, params InvoiceSubmissionView[] submissions) =>
        _world.NewService(_identity.Object).HideSubmissionsForAsync(Caller(caller), submissions, _world.RootFlags);

    /// <summary>The submission row as <c>InvoiceSubmissionService.ToView</c> reads it (channel, document link, invoice link).</summary>
    private static InvoiceSubmissionView SubmissionView(
        Guid id, int? channel, Guid? document = null, Guid? invoice = null, InvoiceSubmissionStatus status = InvoiceSubmissionStatus.Received) =>
        new()
        {
            SubmissionId = id,
            Status = status.ToString(),
            ChannelValue = channel,
            DocumentId = document,
            InvoiceId = invoice,
        };

    /// <summary>
    /// Per source: the decision before the invoice exists (the submission row only, as its route writes it at Received) and after
    /// the persister's commit (the invoice link and the invoice's Stored Files). Only an Internal submission with an empty
    /// document link (internal upload, from-email) may differ, and only from withheld to visible.
    /// </summary>
    public static TheoryData<string, string, bool, bool> SubmissionSourcesBeforeAndAfter()
    {
        var data = new TheoryData<string, string, bool, bool>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "spa-upload", true, true);
            data.Add(caller, "internal-upload", false, true);
            data.Add(caller, "confirm-legacy-readable-document", true, true);
            data.Add(caller, "from-document-readable-document", true, true);
            data.Add(caller, "confirm-legacy-isolated-document", false, false);
            data.Add(caller, "from-document-isolated-document", false, false);
            data.Add(caller, "from-email-marked-eml", false, true);
            data.Add(caller, "from-email-unmarked-restricted-eml", false, false);
        }

        return data;
    }

    [Theory, MemberData(nameof(SubmissionSourcesBeforeAndAfter))]
    public async Task Submission_DecidedBeforeItsInvoiceExists_AndAfter_DiffersOnlyForAnInternalEmptyLink(
        string caller, string source, bool visibleBefore, bool visibleAfter)
    {
        var submission = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var communication = FirmCommunication();
        var readable = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var isolated = _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter());
        var markedEml = _world.SubmissionEml(marked: true, communication, CleanMatter());
        var unmarkedEml = _world.SubmissionEml(marked: false, communication, CleanMatter());
        var (channel, document) = source switch
        {
            "spa-upload" => (InvoiceWorld.SpaChannel, (Guid?)null),
            "internal-upload" or "from-email-marked-eml" or "from-email-unmarked-restricted-eml" => (InvoiceWorld.InternalChannel, null),
            "confirm-legacy-readable-document" or "from-document-readable-document" => (InvoiceWorld.InternalChannel, readable),
            "confirm-legacy-isolated-document" or "from-document-isolated-document" => (InvoiceWorld.InternalChannel, isolated),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        var before = await DecideSubmissions(caller, SubmissionView(submission, channel, document));

        // The persister's commit: the submission's invoice link and the invoice's Stored Files, in the writer's shape.
        switch (source)
        {
            case "spa-upload":
                _world.SpaUpload(invoice, submission);
                break;
            case "internal-upload":
                _world.InternalUpload(invoice, submission);
                break;
            case "confirm-legacy-readable-document" or "confirm-legacy-isolated-document":
                _world.Submission(invoice, document, InvoiceWorld.InternalChannel, submission);
                break;
            case "from-document-readable-document" or "from-document-isolated-document":
                _world.FromDocument(invoice, document!.Value, submission);
                break;
            case "from-email-marked-eml":
                _world.FromEmail(invoice, markedEml, communication, submissionId: submission);
                break;
            case "from-email-unmarked-restricted-eml":
                _world.FromEmail(invoice, unmarkedEml, communication, submissionId: submission);
                break;
        }

        var after = await DecideSubmissions(
            caller, SubmissionView(submission, channel, document, invoice, InvoiceSubmissionStatus.Accepted));

        before.IsVisible(submission).Should().Be(visibleBefore, $"before the invoice ({before.HiddenSummary()})");
        after.IsVisible(submission).Should().Be(visibleAfter, $"after the invoice ({after.HiddenSummary()})");
        before.Faulted.Should().BeFalse();
        after.Faulted.Should().BeFalse();
        var internalEmptyLink = channel == InvoiceWorld.InternalChannel && document is null;
        if (internalEmptyLink)
            (before.IsVisible(submission) && !after.IsVisible(submission)).Should().BeFalse("an empty link may only go from withheld to visible");
        else
            after.IsVisible(submission).Should().Be(before.IsVisible(submission), "the same source decides before and after the invoice");
    }

    public static TheoryData<string, InvoiceSubmissionStatus> ExternalCallersAtEveryStatusWithoutAnInvoice()
    {
        var data = new TheoryData<string, InvoiceSubmissionStatus>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            foreach (var status in new[]
                     {
                         InvoiceSubmissionStatus.Received, InvoiceSubmissionStatus.Processing, InvoiceSubmissionStatus.Failed,
                         InvoiceSubmissionStatus.Rejected,
                     })
                data.Add(caller, status);
        }

        return data;
    }

    [Theory, MemberData(nameof(ExternalCallersAtEveryStatusWithoutAnInvoice))]
    public async Task NoInvoice_AnIsolatedSubmissionDocument_IsWithheld_AtEveryStatus(string caller, InvoiceSubmissionStatus status)
    {
        var submission = Guid.NewGuid();
        var isolated = _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter());

        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.InternalChannel, isolated, status: status));

        decision.IsVisible(submission).Should().BeFalse();
        decision.HiddenSummary().Should().Contain(InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceHidden));
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task NoInvoice_ASubmissionDocumentUnderARestrictedRoot_IsWithheld(string caller)
    {
        var submission = Guid.NewGuid();
        var matter = Guid.NewGuid();
        _world.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: matter);

        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.InternalChannel, document));

        decision.IsVisible(submission).Should().BeFalse();
        decision.HiddenSummary().Should().Contain(InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.RootRestricted));
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task NoInvoice_Spa_IsVisible(string caller)
    {
        var submission = Guid.NewGuid();

        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.SpaChannel));

        decision.IsVisible(submission).Should().BeTrue(decision.HiddenSummary());
        _world.Urls.Should().BeEmpty("a Spa submission is decided without a read");
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task NoInvoice_Internal_AReadableSubmissionDocument_IsVisible(string caller)
    {
        var submission = Guid.NewGuid();
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());

        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.InternalChannel, document));

        decision.IsVisible(submission).Should().BeTrue(decision.HiddenSummary());
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task NoInvoice_Internal_ASubmissionsOwnEml_HonoursTheMarker(string caller)
    {
        var marked = Guid.NewGuid();
        var unmarked = Guid.NewGuid();

        var decision = await DecideSubmissions(caller,
            SubmissionView(marked, InvoiceWorld.InternalChannel, _world.SubmissionEml(marked: true, FirmCommunication(), CleanMatter())),
            SubmissionView(unmarked, InvoiceWorld.InternalChannel, _world.SubmissionEml(marked: false, FirmCommunication(), CleanMatter())));

        decision.IsVisible(marked).Should().BeTrue(decision.HiddenSummary());
        decision.IsVisible(unmarked).Should().BeFalse();
    }

    [Theory, MemberData(nameof(ExternalCallers))]
    public async Task NoInvoice_Internal_AnEmptyDocumentLink_IsWithheld(string caller)
    {
        var submission = Guid.NewGuid();

        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.InternalChannel));

        decision.IsVisible(submission).Should().BeFalse();
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceLinkUnreadable)}=1");
    }

    [Theory]
    [InlineData("contact", null)]
    [InlineData("contact", 999)]
    [InlineData("contact", 100000002)] // Mailbox: a reserved option value with no writer
    [InlineData("flagged-systemuser", null)]
    [InlineData("flagged-systemuser", 999)]
    [InlineData("flagged-systemuser", 100000002)]
    public async Task NoInvoice_AnUnknownOrUnreadableChannel_IsWithheld(string caller, int? channel)
    {
        var submission = Guid.NewGuid();
        var readable = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());

        var decision = await DecideSubmissions(caller, SubmissionView(submission, channel, readable));

        decision.IsVisible(submission).Should().BeFalse();
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceKindUnknown)}=1");
    }

    [Theory]
    [InlineData("contact", "sprk_documents")]
    [InlineData("flagged-systemuser", "sprk_documents")]
    [InlineData("contact", "sprk_storedfiles")]
    [InlineData("flagged-systemuser", "sprk_storedfiles")]
    public async Task AFailedRead_WithholdsEveryAskedSubmission_AndSaysFaulted(string caller, string faultingSet)
    {
        var spa = Guid.NewGuid();
        var internalWithDocument = Guid.NewGuid();
        var withInvoice = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        _world.SpaUpload(invoice, withInvoice);
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        _world.Faults[faultingSet] = HttpStatusCode.ServiceUnavailable;

        var decision = await DecideSubmissions(caller,
            SubmissionView(spa, InvoiceWorld.SpaChannel),
            SubmissionView(internalWithDocument, InvoiceWorld.InternalChannel, document),
            SubmissionView(withInvoice, InvoiceWorld.SpaChannel, invoice: invoice, status: InvoiceSubmissionStatus.Accepted));

        decision.Faulted.Should().BeTrue();
        new[] { spa, internalWithDocument, withInvoice }.Should().OnlyContain(id => !decision.IsVisible(id));
    }

    [Fact]
    public async Task AnInternalSystemuser_SeesEverySubmission_AndNothingIsRead()
    {
        var isolated = _world.Document(Guid.NewGuid(), permission: Restricted);
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();

        var decision = await DecideSubmissions("internal-systemuser",
            SubmissionView(ids[0], InvoiceWorld.InternalChannel, isolated),
            SubmissionView(ids[1], InvoiceWorld.InternalChannel),
            SubmissionView(ids[2], channel: null));

        ids.Should().OnlyContain(id => decision.IsVisible(id), "an internal systemuser is never narrowed (as on the invoice path)");
        _world.Urls.Should().BeEmpty("an internal caller costs no read");
    }

    [Fact]
    public async Task ASubmissionTheDecisionWasNotAskedAbout_IsNeverVisible()
    {
        var decision = await DecideSubmissions("contact", SubmissionView(Guid.NewGuid(), InvoiceWorld.SpaChannel));

        decision.IsVisible(Guid.NewGuid()).Should().BeFalse();
        decision.IsVisible(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public async Task ThePageOfSubmissions_IsDecidedInBatchedReads_NotOnePerSubmission()
    {
        var page = new List<InvoiceSubmissionView>();
        for (var i = 0; i < 30; i++)
        {
            var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
            page.Add(SubmissionView(Guid.NewGuid(), InvoiceWorld.InternalChannel, document));
        }

        for (var i = 0; i < 10; i++)
        {
            var invoice = Guid.NewGuid();
            var submission = _world.SpaUpload(invoice);
            page.Add(SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: invoice, status: InvoiceSubmissionStatus.Accepted));
        }

        var decision = await DecideSubmissions("contact", page.ToArray());

        page.Should().OnlyContain(s => decision.IsVisible(s.SubmissionId), decision.HiddenSummary());
        _world.Reads("sprk_documents").Should().HaveCount(1, "one source read for the page's submission documents");
        _world.Reads("sprk_storedfiles").Should().HaveCount(1);
        _world.Reads("sprk_invoicesubmissions").Should().HaveCount(1);
        decision.Cost.SourceReads.Should().Be(1);
    }

    /// <summary>
    /// Review round 5 (F3): the production wiring. <c>AddExternalAccess</c> (the real module, not a test registration) registers
    /// <see cref="ExternalInvoiceSubmissionHiding"/> bound to <see cref="ExternalDataService.HideSubmissionsForAsync"/>: the
    /// page is decided over the module's app-only <see cref="ExternalDataService"/> (whose transport is the double here).
    /// </summary>
    [Fact]
    public async Task TheSubmissionSeam_IsRegisteredByAddExternalAccess_AndDelegatesToHideSubmissionsForAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build());
        services.AddSingleton<Azure.Core.TokenCredential>(new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
        services.AddSingleton<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
        services.AddHttpContextAccessor();
        // Production registers the filing reader elsewhere; a parentless source makes no filing read (strict: any call fails).
        services.AddSingleton(new Mock<IGenericEntityService>(MockBehavior.Strict).Object);
        services.AddExternalAccess();
        services.AddHttpClient<ExternalDataService>().ConfigurePrimaryHttpMessageHandler(() => _world);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var withheld = Guid.NewGuid();
        var spa = Guid.NewGuid();
        var isolated = _world.Document(Guid.NewGuid(), permission: Restricted);

        var seam = scope.ServiceProvider.GetRequiredService<ExternalInvoiceSubmissionHiding>();
        var decision = await seam(Caller("contact"),
            new[] { SubmissionView(withheld, InvoiceWorld.InternalChannel, isolated), SubmissionView(spa, InvoiceWorld.SpaChannel) },
            CancellationToken.None);

        decision.Faulted.Should().BeFalse(decision.HiddenSummary());
        decision.IsVisible(spa).Should().BeTrue();
        decision.HiddenReason(withheld).Should().Be(InvoiceHiddenReason.SourceHidden);
        _world.Reads("sprk_documents").Should().ContainSingle("the submission's own document, read app-only through the module's service");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 188 unchanged at the option's default
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TheMarkerOption_IsOff_ByDefault_So188StillHidesAMarkedEml()
    {
        var id = Guid.NewGuid();
        var row = ExternalDocumentNarrowing.FromWebApi(id, Row(
            ("sprk_documentid", id.ToString()), ("sprk_accesspermission", Restricted),
            ("sprk_isemailarchive", true), ("sprk_restrictedbysubmission", true)), readSubmissionMarker: true);
        CommunicationOpenThreadGate.RowQuery noReads = (_, _, _) => throw new InvalidOperationException("no read expected");

        var defaultDecision = await ExternalDocumentNarrowing.DecideAsync(new[] { row }, noReads, CancellationToken.None);
        var optIn = await ExternalDocumentNarrowing.DecideAsync(new[] { row }, noReads, CancellationToken.None, honourSubmissionMarker: true);

        defaultDecision.IsVisible(row).Should().BeFalse("the 188 decision never looks at the marker");
        optIn.IsVisible(row).Should().BeTrue();
    }

    [Fact]
    public void TheMarker_IsReadOnlyByTheOptInOverload()
    {
        var id = Guid.NewGuid();
        var json = Row(("sprk_isemailarchive", true), ("sprk_restrictedbysubmission", true));

        ExternalDocumentNarrowing.FromWebApi(id, json).SubmissionMarked.Should().BeFalse();
        ExternalDocumentNarrowing.FromWebApi(id, json, readSubmissionMarker: false).SubmissionMarked.Should().BeFalse();
        ExternalDocumentNarrowing.FromWebApi(id, json, readSubmissionMarker: true).SubmissionMarked.Should().BeTrue();
    }

    [Fact]
    public void TheSourceRead_SelectsEveryColumnTheRuleReads()
    {
        ExternalInvoiceVisibility.SourceSelectColumns.Should().Contain(ExternalDocumentNarrowing.SelectColumns)
            .And.Contain(ExternalDocumentNarrowing.SubmissionMarkerColumns)
            .And.Contain(ExternalDataService.DocumentRootLinks.SelectColumns)
            .And.Contain(ExternalMultiRootRule.LiveParentSelect("sprk_document"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Taint closure (model 9.1): every derived row follows its invoice
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> FamilyTables =>
        new(ExternalInvoiceVisibility.FamilyTables.Keys.Where(t => t != "sprk_invoice"));

    [Theory, MemberData(nameof(FamilyTables))]
    public async Task TaintClosure_ARowOfAHiddenInvoice_IsHidden_AndOneOfAVisibleInvoiceIsNot(string table)
    {
        var hiddenInvoice = Guid.NewGuid();
        _world.Source(hiddenInvoice, permission: Restricted);
        var visibleInvoice = Guid.NewGuid();
        _world.Source(visibleInvoice);
        var ofHidden = _world.FamilyRow(table, invoice: hiddenInvoice);
        var ofVisible = _world.FamilyRow(table, invoice: visibleInvoice);
        var ofNone = _world.FamilyRow(table, invoice: null);
        var missing = Guid.NewGuid();

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync(table, new[] { ofHidden, ofVisible, ofNone, missing }, _world.RootFlags);

        if (table == "sprk_invoicesubmission")
        {
            // Review round 5 (F1): a submission with no invoice is decided by its own source, as on the routes. This row has no
            // channel, so it is withheld (source_kind_unknown).
            hidden.Should().BeEquivalentTo(new[] { ofHidden, ofNone, missing },
                "a submission of a hidden invoice, one with no invoice and no channel, and one that does not come back are hidden");
            return;
        }

        hidden.Should().BeEquivalentTo(new[] { ofHidden, missing },
            "a row of a hidden invoice and a row that does not come back are hidden; a row of a visible invoice or of none is not");
    }

    // ── Review round 5 (F1): the assistant tools decide a submission row with the routes' function ──────────────────────

    public static TheoryData<string, bool> SubmissionSourcesWithoutAnInvoice => new()
    {
        { "spa", false },
        { "internal-readable-document", false },
        { "internal-marked-eml", false },
        { "internal-isolated-document", true },
        { "internal-unmarked-restricted-eml", true },
        { "internal-empty-link", true },
        { "unknown-channel", true },
        { "no-channel", true },
    };

    [Theory, MemberData(nameof(SubmissionSourcesWithoutAnInvoice))]
    public async Task TaintClosure_ASubmissionWithoutAnInvoice_IsDecidedAsOnTheRoutes(string source, bool hiddenExpected)
    {
        (int? Channel, Guid? Document) shape = source switch
        {
            "spa" => (InvoiceWorld.SpaChannel, null),
            "internal-readable-document" =>
                (InvoiceWorld.InternalChannel, _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter())),
            "internal-marked-eml" => (InvoiceWorld.InternalChannel, _world.SubmissionEml(marked: true, FirmCommunication(), CleanMatter())),
            "internal-isolated-document" =>
                (InvoiceWorld.InternalChannel, _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter())),
            "internal-unmarked-restricted-eml" =>
                (InvoiceWorld.InternalChannel, _world.SubmissionEml(marked: false, FirmCommunication(), CleanMatter())),
            "internal-empty-link" => (InvoiceWorld.InternalChannel, null),
            "unknown-channel" => (999, null),
            "no-channel" => (null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        var (channel, document) = shape;
        var submission = _world.PendingSubmission(document, channel);

        var tool = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission }, _world.RootFlags);
        var route = await DecideSubmissions("contact", SubmissionView(submission, channel, document));

        tool.Contains(submission).Should().Be(hiddenExpected, route.HiddenSummary());
        tool.Contains(submission).Should().Be(!route.IsVisible(submission), "the tools and the routes decide a submission alike");
    }

    [Fact]
    public async Task TaintClosure_ASubmissionWhoseDocumentLinkIsNotAGuid_IsHidden()
    {
        var submission = _world.PendingSubmission(null, InvoiceWorld.InternalChannel);
        _world.SetRow("sprk_invoicesubmissions", "sprk_invoicesubmissionid", submission, "_sprk_document_value", "not-a-guid");

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission }, _world.RootFlags);

        hidden.Should().Contain(submission);
    }

    [Fact]
    public async Task TaintClosure_SubmissionRows_AreReadOnce_WithTheirChannelAndDocument()
    {
        var spa = _world.PendingSubmission(null, InvoiceWorld.SpaChannel);
        var isolated = _world.PendingSubmission(
            _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter()), InvoiceWorld.InternalChannel);
        var invoice = Guid.NewGuid();
        var withInvoice = _world.SpaUpload(invoice);

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { spa, isolated, withInvoice }, _world.RootFlags);

        hidden.Should().BeEquivalentTo(new[] { isolated });
        var byId = _world.Reads("sprk_invoicesubmissions").Where(u => u.Contains("sprk_invoicesubmissionid eq", StringComparison.Ordinal)).ToList();
        byId.Should().ContainSingle("one app-only read of the rows by id for the page");
        byId[0].Should().Contain("sprk_channel").And.Contain("_sprk_document_value").And.Contain("_sprk_invoice_value");
    }

    [Fact]
    public async Task TaintClosure_ASubmissionDecisionFault_Throws_SoTheToolReturnsNoData()
    {
        var submission = _world.PendingSubmission(
            _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter()), InvoiceWorld.InternalChannel);
        _world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;

        var act = () => _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { submission }, _world.RootFlags);

        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("sprk_invoicecorrection")]
    [InlineData("sprk_decisionrecord")]
    [InlineData("sprk_signal")]
    public async Task TaintClosure_ARowOfALineOfAHiddenInvoice_IsHidden(string table)
    {
        var hiddenInvoice = Guid.NewGuid();
        _world.Source(hiddenInvoice, permission: Restricted);
        var visibleInvoice = Guid.NewGuid();
        _world.Source(visibleInvoice);
        var hiddenLine = _world.FamilyRow("sprk_billingevent", invoice: hiddenInvoice);
        var visibleLine = _world.FamilyRow("sprk_billingevent", invoice: visibleInvoice);
        var ofHiddenLine = _world.FamilyRow(table, invoice: null, line: hiddenLine);
        var ofVisibleLine = _world.FamilyRow(table, invoice: null, line: visibleLine);
        var ofMissingLine = _world.FamilyRow(table, invoice: null, line: Guid.NewGuid());

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync(table, new[] { ofHiddenLine, ofVisibleLine, ofMissingLine }, _world.RootFlags);

        hidden.Should().BeEquivalentTo(new[] { ofHiddenLine, ofMissingLine });
    }

    [Fact]
    public async Task TaintClosure_AnInvoiceRow_IsItsOwnInvoice()
    {
        var hiddenInvoice = Guid.NewGuid();
        _world.Source(hiddenInvoice, permission: Restricted);
        var visibleInvoice = Guid.NewGuid();

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoice", new[] { hiddenInvoice, visibleInvoice }, _world.RootFlags);

        hidden.Should().BeEquivalentTo(new[] { hiddenInvoice });
    }

    [Fact]
    public async Task TaintClosure_AFault_Throws_SoTheToolReturnsNoData()
    {
        var row = _world.FamilyRow("sprk_billingevent", invoice: Guid.NewGuid());
        _world.Faults["sprk_billingevents"] = HttpStatusCode.ServiceUnavailable;

        var act = () => _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_billingevent", new[] { row }, _world.RootFlags);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void TaintClosure_IsTheClosedListOfTheModel()
    {
        ExternalInvoiceVisibility.FamilyTables.Keys.Should().BeEquivalentTo(new[]
        {
            "sprk_invoice", "sprk_billingevent", "sprk_invoiceapproval", "sprk_invoicesubmission", "sprk_invoicecorrection",
            "sprk_externalidentifier", "sprk_storedfile", "sprk_signal", "sprk_decisionrecord",
        });
    }

    private static Dictionary<string, JsonElement> Row(params (string Column, object? Value)[] columns) =>
        columns.ToDictionary(c => c.Column, c => JsonSerializer.SerializeToElement(c.Value));
}

/// <summary>
/// Task 098: the app-only Web API the invoice predicate reads, as a double. Every GET is answered from the seeded rows of its
/// entity set: <c>col eq {guid}</c> conditions are OR-ed (the predicate's and 188's by-key and by-link reads), <c>col ne null</c>
/// conditions AND-ed, <c>$top</c> honoured, and each row projected through <c>$select</c> (an unselected column is absent). A
/// set in <see cref="Faults"/> answers that status (a read fault). Root flags come from <see cref="Flags"/>.
/// </summary>
internal sealed class InvoiceWorld : HttpMessageHandler
{
    private readonly Dictionary<string, List<Dictionary<string, object?>>> _sets = new();

    public ConcurrentQueue<string> Urls { get; } = new();

    public Dictionary<string, HttpStatusCode> Faults { get; } = new();

    public Dictionary<Guid, RootRecordFlags> Flags { get; } = new();

    public bool FlagReadThrows { get; set; }

    /// <summary>When true, every filing-walk read throws (the live-parent walk faults).</summary>
    public bool FilingReadThrows { get; set; }

    private readonly Dictionary<(string Table, Guid Id), Entity> _filing = new();
    private readonly Mock<IGenericEntityService> _filingReader = new(MockBehavior.Strict);

    public InvoiceWorld()
    {
        // The live-parent walk's app-only reads (ParentLineageWalk), answered from _filing by the id condition each carries,
        // projected through the query's ColumnSet (as ExternalRootEventReadTests.FakeDataverse does).
        _filingReader.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                if (FilingReadThrows)
                    throw new TimeoutException("The filing read failed (test).");
                var ids = query.Criteria.Conditions
                    .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(c => c.Values.Select(v => (Guid)v))
                    .ToHashSet();
                lock (_filing)
                {
                    return new EntityCollection(_filing
                        .Where(kv => string.Equals(kv.Key.Table, query.EntityName, StringComparison.OrdinalIgnoreCase) && ids.Contains(kv.Key.Id))
                        .Select(kv => Projected(kv.Value, query.ColumnSet))
                        .ToList());
                }
            });
    }

    private static Entity Projected(Entity stored, ColumnSet? columns)
    {
        if (columns is null || columns.AllColumns)
            return stored;
        var row = new Entity(stored.LogicalName, stored.Id);
        foreach (var column in columns.Columns)
        {
            if (stored.Attributes.TryGetValue(column, out var value))
                row[column] = value;
        }

        return row;
    }

    /// <summary>A record of the filing world (what the live-parent walk climbs), with its filing lookups; returns its id.</summary>
    public Guid Filed(string table, params (string Column, string Target, Guid Id)[] lookups) => Filed(table, Guid.NewGuid(), lookups);

    private Guid Filed(string table, Guid id, params (string Column, string Target, Guid Id)[] lookups)
    {
        var entity = new Entity(table, id);
        foreach (var (column, target, targetId) in lookups)
            entity[column] = new EntityReference(target, targetId);
        lock (_filing) _filing[(table, id)] = entity;
        return id;
    }

    public IReadOnlyList<string> Reads(string set) => Urls.Where(u => u.Contains($"/{set}?", StringComparison.Ordinal)).ToList();

    public EffectiveRootFlagReader RootFlags => (_, ids, _) =>
    {
        if (FlagReadThrows)
            throw new TimeoutException("The root flag read failed (test).");
        return Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(
            ids.Where(Flags.ContainsKey).ToDictionary(id => id, id => Flags[id]));
    };

    public ExternalDataService NewService(ISystemUserIdentityResolver? identity) => new(
        new HttpClient(this),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
        NullLogger<ExternalDataService>.Instance,
        filing: _filingReader.Object,
        columns: null,
        identity: identity);

    /// <summary>A document with its own Access Permission, links and marker; returns its id.</summary>
    public Guid Document(
        Guid id, object? permission = null, bool? eml = null, bool? marker = null, Guid? communication = null, Guid? parent = null,
        Guid? matter = null, Guid? project = null, object? rawMarker = null, object? rawEml = null, object? rawMatter = null,
        (string Column, object? Value)? extra = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_documentid"] = id.ToString(),
            ["sprk_documentname"] = $"document {id}",
            ["sprk_accesspermission"] = permission,
            ["_sprk_relatedcommunication_value"] = communication?.ToString(),
            ["_sprk_parentdocument_value"] = parent?.ToString(),
            ["sprk_isemailarchive"] = rawEml ?? eml,
            ["sprk_restrictedbysubmission"] = rawMarker ?? marker,
            ["_sprk_matter_value"] = rawMatter ?? matter?.ToString(),
            ["_sprk_project_value"] = project?.ToString(),
        };
        if (extra is { } e) row[e.Column] = e.Value;
        Seed("sprk_documents", row);
        Filed("sprk_document", id, parent is { } p ? new[] { ("sprk_parentdocument", "sprk_document", p) } : Array.Empty<(string, string, Guid)>());
        return id;
    }

    /// <summary>A source document of <paramref name="invoice"/> (a Stored File pointing at it); returns the document id.</summary>
    public Guid Source(
        Guid invoice, object? permission = null, bool? eml = null, bool? marker = null, Guid? communication = null,
        Guid? parent = null, Guid? matter = null, Guid? project = null, object? rawMarker = null, object? rawEml = null,
        object? rawMatter = null, (string Column, object? Value)? extra = null)
    {
        var document = Document(Guid.NewGuid(), permission, eml, marker, communication, parent, matter, project, rawMarker, rawEml,
            rawMatter, extra);
        StoredFile(invoice, document);
        return document;
    }

    public void StoredFile(Guid invoice, Guid? source) => StoredFileRaw(invoice, source?.ToString());

    public const int InvoicePdf = (int)Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoicePdf;
    public const int InvoiceSupportingFile = (int)Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoiceSupportingFile;
    public const int SpaChannel = (int)Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Spa;
    public const int InternalChannel = (int)Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Internal;

    /// <summary>A Stored File of <paramref name="invoice"/>, purpose InvoicePdf.</summary>
    public void StoredFileRaw(Guid invoice, object? rawSource, Guid? sourceCommunication = null, string? attachmentIdentity = null) =>
        StoredFileRaw(invoice, rawSource, InvoicePdf, sourceCommunication, attachmentIdentity);

    /// <summary>A Stored File of <paramref name="invoice"/> with an explicit <paramref name="purpose"/> (raw; null allowed).</summary>
    public void StoredFileRaw(
        Guid invoice, object? rawSource, object? purpose, Guid? sourceCommunication = null, string? attachmentIdentity = null) =>
        Seed("sprk_storedfiles", new Dictionary<string, object?>
        {
            ["sprk_storedfileid"] = Guid.NewGuid().ToString(),
            ["_sprk_invoice_value"] = invoice.ToString(),
            ["_sprk_sourcedocument_value"] = rawSource,
            ["_sprk_sourcecommunication_value"] = sourceCommunication?.ToString(),
            ["sprk_sourceattachmentidentity"] = attachmentIdentity,
            ["sprk_purpose"] = purpose,
            ["sprk_name"] = "file.pdf",
        });

    /// <summary>A submission of <paramref name="invoice"/> naming <paramref name="document"/> on <paramref name="channel"/>
    /// (Internal by default: the confirm route's only record of its source is <c>sprk_invoicesubmission.sprk_document</c>).</summary>
    public Guid Submission(Guid invoice, Guid? document) => Submission(invoice, document, InternalChannel);

    /// <summary>A submission with an explicit raw <paramref name="channel"/> (null and unknown values allowed); returns its id
    /// (<paramref name="id"/> when given: the row a submission decided before its invoice existed gets on the commit).</summary>
    public Guid Submission(Guid invoice, Guid? document, object? channel, Guid? id = null)
    {
        var submission = id ?? Guid.NewGuid();
        Seed("sprk_invoicesubmissions", new Dictionary<string, object?>
        {
            ["sprk_invoicesubmissionid"] = submission.ToString(),
            ["_sprk_invoice_value"] = invoice.ToString(),
            ["_sprk_document_value"] = document?.ToString(),
            ["sprk_channel"] = channel,
            ["sprk_name"] = "SUB-0001",
        });
        return submission;
    }

    // ── Writer shapes (review round 4a; the 094 head and 095's contract): each seeds the submission row and the Stored File
    //    rows its route writes, in the shape the persister writes them ──────────────────────────────────────────────────

    public const int RestrictedPermission = 100000002;

    /// <summary>SPA upload (092/094): Spa, no submission document, a sourceless InvoicePdf (FirmVisible Yes).</summary>
    public Guid SpaUpload(Guid invoice, Guid? submissionId = null)
    {
        var submission = Submission(invoice, document: null, channel: SpaChannel, submissionId);
        WriterStoredFile(invoice, InvoicePdf, firmVisible: true);
        return submission;
    }

    /// <summary>Internal upload (092/094): Internal, no submission document, a sourceless InvoicePdf (FirmVisible No).</summary>
    public Guid InternalUpload(Guid invoice, Guid? submissionId = null)
    {
        var submission = Submission(invoice, document: null, channel: InternalChannel, submissionId);
        WriterStoredFile(invoice, InvoicePdf);
        return submission;
    }

    /// <summary>Confirm / from-document on 094: Internal naming <paramref name="document"/>, and an InvoicePdf copy whose
    /// source document is that document (FirmVisible No).</summary>
    public Guid FromDocument(Guid invoice, Guid document, Guid? submissionId = null)
    {
        var submission = Submission(invoice, document, InternalChannel, submissionId);
        WriterStoredFile(invoice, InvoicePdf, sourceDocument: document);
        return submission;
    }

    /// <summary>
    /// From-email 095: Internal with an EMPTY document link; an InvoicePdf whose source document is <paramref name="eml"/>,
    /// with the email's communication, part identity "eml-part-1", source version and hash (FirmVisible No); and
    /// <paramref name="supportingParts"/> InvoiceSupportingFile rows at "eml-part-{n}" with the same source columns.
    /// </summary>
    public Guid FromEmail(Guid invoice, Guid eml, Guid communication, int supportingParts = 1, Guid? submissionId = null)
    {
        var submission = Submission(invoice, document: null, channel: InternalChannel, submissionId);
        WriterStoredFile(invoice, InvoicePdf, eml, communication, "eml-part-1");
        for (var part = 2; part < 2 + supportingParts; part++)
            WriterStoredFile(invoice, InvoiceSupportingFile, eml, communication, $"eml-part-{part}");
        return submission;
    }

    /// <summary>The .eml a from-email submission creates (095): its own Restricted narrowing, linked to its communication, and
    /// the submission marker (<paramref name="marked"/>; an unmarked one differs only there).</summary>
    public Guid SubmissionEml(bool marked, Guid communication, Guid? matter = null) =>
        Document(Guid.NewGuid(), permission: RestrictedPermission, eml: true, marker: marked, communication: communication,
            matter: matter);

    /// <summary>A Stored File as the persister writes it (every source column the route sets); returns its id.</summary>
    public Guid WriterStoredFile(
        Guid invoice, object? purpose, Guid? sourceDocument = null, Guid? sourceCommunication = null,
        string? attachmentIdentity = null, bool firmVisible = false)
    {
        var id = Guid.NewGuid();
        var sourced = sourceDocument is not null || sourceCommunication is not null || attachmentIdentity is not null;
        Seed("sprk_storedfiles", new Dictionary<string, object?>
        {
            ["sprk_storedfileid"] = id.ToString(),
            ["_sprk_invoice_value"] = invoice.ToString(),
            ["sprk_purpose"] = purpose,
            ["sprk_firmvisible"] = firmVisible,
            ["_sprk_sourcedocument_value"] = sourceDocument?.ToString(),
            ["_sprk_sourcecommunication_value"] = sourceCommunication?.ToString(),
            ["sprk_sourceattachmentidentity"] = attachmentIdentity,
            ["sprk_sourceversion"] = sourced ? "1" : null,
            ["sprk_sourcesha256"] = sourced ? new string('a', 64) : null,
            ["sprk_name"] = "file.pdf",
        });
        return id;
    }

    /// <summary>The Stored Files of <paramref name="invoice"/>, in the order they were seeded.</summary>
    public IReadOnlyList<Guid> StoredFilesOf(Guid invoice)
    {
        lock (_sets)
        {
            return _sets["sprk_storedfiles"].Where(r => (string?)r["_sprk_invoice_value"] == invoice.ToString())
                .Select(r => Guid.Parse((string)r["sprk_storedfileid"]!)).ToList();
        }
    }

    /// <summary>Sets <paramref name="column"/> on the Stored File <paramref name="id"/>.</summary>
    public void SetStoredFile(Guid id, string column, object? value) =>
        SetRow("sprk_storedfiles", "sprk_storedfileid", id, column, value);

    /// <summary>Sets <paramref name="column"/> on the row <paramref name="id"/> of <paramref name="set"/>.</summary>
    public void SetRow(string set, string idColumn, Guid id, string column, object? value)
    {
        lock (_sets)
            _sets[set].Single(r => (string?)r[idColumn] == id.ToString())[column] = value;
    }

    /// <summary>The Stored File <paramref name="id"/> as a reader without the field-security profile reads it: every secured
    /// column but the invoice link reads null (088: purpose, firm-visible and the source columns are secured).</summary>
    public void MaskStoredFile(Guid id)
    {
        foreach (var column in new[]
                 {
                     "sprk_purpose", "sprk_firmvisible", "_sprk_sourcedocument_value", "_sprk_sourcecommunication_value",
                     "sprk_sourceattachmentidentity", "sprk_sourceversion", "sprk_sourcesha256",
                 })
            SetStoredFile(id, column, null);
    }

    /// <summary>A document recorded as <paramref name="invoice"/>'s source ONLY on a submission (no Stored File).</summary>
    public Guid SubmissionSource(Guid invoice, object? permission = null, Guid? matter = null)
    {
        var document = Document(Guid.NewGuid(), permission, matter: matter);
        Submission(invoice, document);
        return document;
    }

    public Guid Communication(bool? internalOnly, Guid? thread)
    {
        var id = Guid.NewGuid();
        Seed("sprk_communications", new Dictionary<string, object?>
        {
            ["sprk_communicationid"] = id.ToString(),
            ["sprk_isinternalonly"] = internalOnly,
            ["_sprk_communicationthread_value"] = thread?.ToString(),
        });
        Filed("sprk_communication", id, thread is { } t
            ? new[] { ("sprk_communicationthread", "sprk_communicationthread", t) }
            : Array.Empty<(string, string, Guid)>());
        return id;
    }

    public Guid Thread(int privacy)
    {
        var id = Guid.NewGuid();
        Seed("sprk_communicationthreads", new Dictionary<string, object?>
        {
            ["sprk_communicationthreadid"] = id.ToString(),
            ["sprk_privacystate"] = privacy,
        });
        Filed("sprk_communicationthread", id);
        return id;
    }

    /// <summary>A row of an invoice-family table, linked to its invoice and/or invoice line; returns its id.</summary>
    public Guid FamilyRow(string table, Guid? invoice, Guid? line = null)
    {
        var family = ExternalInvoiceVisibility.FamilyTables[table];
        var id = Guid.NewGuid();
        var row = new Dictionary<string, object?> { [family.IdColumn] = id.ToString() };
        if (family.InvoiceLink is { } invoiceLink) row[invoiceLink] = invoice?.ToString();
        if (family.LineLink is { } lineLink) row[lineLink] = line?.ToString();
        // A confirm-route submission: Internal, naming a clean document.
        if (table == "sprk_invoicesubmission" && invoice is not null)
        {
            row["_sprk_document_value"] = Document(Guid.NewGuid()).ToString();
            row["sprk_channel"] = InternalChannel;
        }

        // A Stored File always carries its purpose (the writer requires it): here a sourceless supporting file, not a source.
        if (table == "sprk_storedfile")
            row["sprk_purpose"] = InvoiceSupportingFile;
        Seed(family.EntitySet, row);
        return id;
    }

    /// <summary>Review round 5: a submission with NO invoice yet (Received, Processing, or a terminal outcome that produced none),
    /// naming <paramref name="document"/> on the raw <paramref name="channel"/>; returns its id.</summary>
    public Guid PendingSubmission(Guid? document, object? channel)
    {
        var submission = Guid.NewGuid();
        Seed("sprk_invoicesubmissions", new Dictionary<string, object?>
        {
            ["sprk_invoicesubmissionid"] = submission.ToString(),
            ["_sprk_invoice_value"] = null,
            ["_sprk_document_value"] = document?.ToString(),
            ["sprk_channel"] = channel,
            ["sprk_name"] = "SUB-0002",
        });
        return submission;
    }

    /// <summary>Sets <paramref name="column"/> on every submission of <paramref name="invoice"/>.</summary>
    public void SetSubmissions(Guid invoice, string column, object? value)
    {
        lock (_sets)
        {
            foreach (var row in _sets["sprk_invoicesubmissions"].Where(r => (string?)r["_sprk_invoice_value"] == invoice.ToString()))
                row[column] = value;
        }
    }

    public void SetDocument(Guid id, string column, object? value)
    {
        lock (_sets)
            _sets["sprk_documents"].Single(r => (string?)r["sprk_documentid"] == id.ToString())[column] = value;
    }

    private void Seed(string set, Dictionary<string, object?> row)
    {
        lock (_sets)
        {
            if (!_sets.TryGetValue(set, out var rows)) _sets[set] = rows = new();
            rows.Add(row);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Urls.Enqueue(url);
        var path = request.RequestUri.AbsolutePath;
        var set = path[(path.LastIndexOf('/') + 1)..];
        if (Faults.TryGetValue(set, out var fault))
            return Task.FromResult(new HttpResponseMessage(fault));

        var query = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : string.Empty;
        var select = Regex.Match(query, @"\$select=([^&]*)");
        var columns = select.Success ? select.Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal) : null;
        var filter = Regex.Match(query, @"\$filter=([^&]*)");
        var equals = filter.Success
            ? Regex.Matches(filter.Groups[1].Value, @"(\w+) eq ([0-9a-fA-F-]{36})").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList()
            : new List<(string, string)>();
        // "col ne null" conditions: a parenthesised "(a ne null or b ne null)" group is OR-ed; the groups are AND-ed.
        var notNull = new List<List<string>>();
        if (filter.Success)
        {
            var rest = filter.Groups[1].Value;
            foreach (System.Text.RegularExpressions.Match group in Regex.Matches(rest, @"\((\w+ ne null(?: or \w+ ne null)*)\)"))
                notNull.Add(Regex.Matches(group.Groups[1].Value, @"(\w+) ne null").Select(m => m.Groups[1].Value).ToList());
            rest = Regex.Replace(rest, @"\((\w+ ne null(?: or \w+ ne null)*)\)", string.Empty);
            notNull.AddRange(Regex.Matches(rest, @"(\w+) ne null").Select(m => new List<string> { m.Groups[1].Value }));
        }

        var top = Regex.Match(query, @"\$top=(\d+)");

        List<Dictionary<string, object?>> rows;
        lock (_sets)
        {
            rows = (_sets.TryGetValue(set, out var seeded) ? seeded : new())
                .Where(r => equals.Count == 0 || equals.Any(e =>
                    r.TryGetValue(e.Item1, out var v) && v is string s && string.Equals(s, e.Item2, StringComparison.OrdinalIgnoreCase)))
                .Where(r => notNull.All(any => any.Any(c => r.TryGetValue(c, out var v) && v is not null)))
                .Select(r => r.Where(kv => columns is null || columns.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
        }

        if (top.Success)
            rows = rows.Take(int.Parse(top.Groups[1].Value)).ToList();

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { value = rows }), System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
