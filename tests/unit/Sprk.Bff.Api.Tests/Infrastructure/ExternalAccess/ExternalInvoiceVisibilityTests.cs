using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
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
        _world.Submission(invoice, document: null); // a submission naming no document is not a source

        await ShouldBeVisible(caller, invoice);
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
        // The identity resolver answers "not external" for a flag that reads false AND for a blank one (owner round 67).
        var user = Guid.NewGuid();
        _identity.Setup(i => i.IsExternalAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var invoice = Guid.NewGuid();
        _world.Source(invoice, permission: Restricted);

        var decision = await _world.NewService(_identity.Object)
            .HideInvoicesForAsync(SystemUser(user), new[] { invoice }, _world.RootFlags);

        decision.IsVisible(invoice).Should().BeTrue($"an internal systemuser (flag {flag?.ToString() ?? "blank"}) is never hidden");
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

        hidden.Should().BeEquivalentTo(new[] { ofHidden, missing },
            "a row of a hidden invoice and a row that does not come back are hidden; a row of a visible invoice or of none is not");
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

    public void StoredFileRaw(Guid invoice, object? rawSource, Guid? sourceCommunication = null, string? attachmentIdentity = null) =>
        Seed("sprk_storedfiles", new Dictionary<string, object?>
        {
            ["sprk_storedfileid"] = Guid.NewGuid().ToString(),
            ["_sprk_invoice_value"] = invoice.ToString(),
            ["_sprk_sourcedocument_value"] = rawSource,
            ["_sprk_sourcecommunication_value"] = sourceCommunication?.ToString(),
            ["sprk_sourceattachmentidentity"] = attachmentIdentity,
            ["sprk_name"] = "file.pdf",
        });

    /// <summary>A submission of <paramref name="invoice"/> naming <paramref name="document"/> (the confirm route's only record
    /// of its source: <c>sprk_invoicesubmission.sprk_document</c>).</summary>
    public void Submission(Guid invoice, Guid? document) =>
        Seed("sprk_invoicesubmissions", new Dictionary<string, object?>
        {
            ["sprk_invoicesubmissionid"] = Guid.NewGuid().ToString(),
            ["_sprk_invoice_value"] = invoice.ToString(),
            ["_sprk_document_value"] = document?.ToString(),
            ["sprk_name"] = "SUB-0001",
        });

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
        Seed(family.EntitySet, row);
        return id;
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
