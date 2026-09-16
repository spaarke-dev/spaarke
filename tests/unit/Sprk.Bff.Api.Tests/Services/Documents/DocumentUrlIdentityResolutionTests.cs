using System.ServiceModel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// FR-01 identity resolution (spaarkeai-word-add-in-r1 task 012). The contracts pinned here:
/// <list type="bullet">
/// <item>three answers, never two — a Graph or Dataverse fault is 503, NEVER "not a Spaarke document", because that
/// answer makes the pane save a Spaarke document as new and mint a duplicate row;</item>
/// <item>a broken alternate key is reported (503), never worked around with a lookup that tolerates duplicates
/// (NFR-07);</item>
/// <item>the raw SPE item id reaches the alternate key untouched (ADR-044 does not apply to it);</item>
/// <item>the drive is corroborated (SPIKE-1 link 3), and a conflict is not reported as a new document.</item>
/// </list>
/// The <see cref="SpeFileStore"/> facade is mocked over real operation classes (the codebase idiom, ADR-007 seam);
/// Dataverse is <see cref="IGenericEntityService"/>, STRICT, so any call a test does not expect fails it.
/// </summary>
public class DocumentUrlIdentityResolutionTests
{
    private const string SpeUrl =
        "https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx";

    // An opaque SPE drive-item id: mixed case and NOT a GUID. It must reach the alternate key byte-for-byte.
    private const string ItemId = "01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD36K";
    private const string DriveId = "b!yLMdWD2AdkaWXsktRe9yIW7Hn0uXvZVBnuXhwwvLvZWY-YU6-G3sQ7t6c1tKzXJM";
    private const int ObjectDoesNotExist = -2147220969;

    private static readonly Guid DocumentId = Guid.Parse("3d3e6cbf-1111-4222-8333-944455556666");

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);

    // ── Graph outcomes ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileUrl_IsNotACloudDocument_AndGraphIsNeverAsked()
    {
        var spe = Spe(ResolvedFor(DriveId, ItemId));

        var result = await Resolve(spe, "file:///C:/Users/me/Documents/brief.docx");

        result.Identity.Should().BeNull();
        result.NoIdentityReason.Should().Be(DocumentUrlIdentityResolution.ReasonNotCloudDocument);
        spe.Verify(s => s.ResolveSharedItemAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SpeSharedItemOutcome.NotFound)]
    // Measured live 2026-09-10: Graph answers 403, not 404, for a path that does not exist inside an SPE container
    // the caller can reach. SharePoint does not distinguish the two, so a 403 is an answer, not an outage.
    [InlineData(SpeSharedItemOutcome.AccessDenied)]
    public async Task UrlGraphWillNotResolve_IsNotResolvable_AndDataverseIsNeverAsked(SpeSharedItemOutcome outcome)
    {
        // Dataverse is strict and never set up: any lookup would fail this test.
        var result = await Resolve(Spe(Outcome(outcome)));

        result.Identity.Should().BeNull();
        result.NoIdentityReason.Should().Be(DocumentUrlIdentityResolution.ReasonNotResolvable);
    }

    [Fact]
    public async Task GraphUnavailable_Is503_NeverNotASpaarkeDocument()
    {
        var act = () => Resolve(Spe(Outcome(SpeSharedItemOutcome.Unavailable)));

        var thrown = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        thrown.StatusCode.Should().Be(503);
        thrown.Code.Should().Be("identity_resolution_unavailable");
    }

    // ── Resolved ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolvedItem_ReturnsTheDocument_WithTheHighestPriorityDirectSlotAsRelatedRecord()
    {
        var matter = new EntityReference("sprk_matter", Guid.NewGuid()) { Name = "Acme v Globex" };
        var project = new EntityReference("sprk_project", Guid.NewGuid()) { Name = "Discovery" };
        AltKeyReturns(Row(DocumentId, DriveId, ("sprk_project", project), ("sprk_matter", matter)));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.NoIdentityReason.Should().BeNull();
        result.Identity!.DocumentId.Should().Be(DocumentId);
        result.Identity.DocumentName.Should().Be("Examiner report draft");
        result.Identity.FileName.Should().Be("Examiner report draft.docx");
        result.Identity.RelatedRecord.Should().BeSameAs(matter, "matter outranks project in the regarding priority");
    }

    [Fact]
    public async Task ResolvedItem_WithOnlyALowerPrioritySlot_ReturnsThatSlot()
    {
        var invoice = new EntityReference("sprk_invoice", Guid.NewGuid());
        AltKeyReturns(Row(DocumentId, DriveId, ("sprk_invoice", invoice)));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.Identity!.RelatedRecord.Should().BeSameAs(invoice);
    }

    [Fact]
    public async Task ResolvedItem_WithNoAssociation_HasNoRelatedRecord()
    {
        AltKeyReturns(Row(DocumentId, DriveId));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.Identity!.RelatedRecord.Should().BeNull();
    }

    [Fact]
    public async Task AlternateKey_ReceivesTheRawSpeItemId_Uncanonicalized()
    {
        AltKeyReturns(Row(DocumentId, DriveId));

        await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        _dataverse.Verify(d => d.RetrieveByAlternateKeyAsync(
            "sprk_document",
            It.Is<KeyAttributeCollection>(k => k.Count == 1 && (string)k["sprk_graphitemid"] == ItemId),
            It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── SPIKE-1 link 3: drive corroboration ───────────────────────────────────────────────────

    [Fact]
    public async Task ItemIdMatch_WithADifferentRecordedDrive_IsAnIdentityConflict_NotANewDocument()
    {
        AltKeyReturns(Row(DocumentId, "b!some-other-drive"));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.Identity.Should().BeNull();
        result.NoIdentityReason.Should().Be(DocumentUrlIdentityResolution.ReasonIdentityConflict);
    }

    [Fact]
    public async Task ItemIdMatch_OnARowWithNoRecordedDrive_ResolvesOnTheItemId()
    {
        AltKeyReturns(Row(DocumentId, driveId: null));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.Identity!.DocumentId.Should().Be(DocumentId);
    }

    // ── Absent vs. indeterminate ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AbsentRow_SignalledByTheObjectDoesNotExistFaultCode_IsNotASpaarkeDocument()
    {
        AltKeyThrows(Wrapped(Fault(ObjectDoesNotExist,
            "A record with the specified key values does not exist in sprk_document entity")));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.NoIdentityReason.Should().Be(DocumentUrlIdentityResolution.ReasonNotSpaarkeDocument);
    }

    [Fact]
    public async Task AbsentRow_SignalledByTheClientsOwnNoEntityMessage_IsNotASpaarkeDocument()
    {
        // DataverseServiceClientImpl throws this itself when the retrieve returns no entity, then re-wraps it.
        AltKeyThrows(Wrapped(new InvalidOperationException(
            "Entity sprk_document not found with provided alternate key values")));

        var result = await Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        result.NoIdentityReason.Should().Be(DocumentUrlIdentityResolution.ReasonNotSpaarkeDocument);
    }

    public static TheoryData<Exception> IndeterminateDataverseFailures => new()
    {
        new TimeoutException("The request channel timed out while waiting for a reply."),
        new HttpRequestException("No such host is known."),
        // A fault whose MESSAGE says "does not exist" but whose CODE is not ObjectDoesNotExist — a schema error.
        // Classification is by code, so this is NOT read as an absent row.
        Fault(-2147217149, "'sprk_document' entity doesn't contain attribute with Name = 'sprk_x' — does not exist"),
    };

    [Theory]
    [MemberData(nameof(IndeterminateDataverseFailures))]
    public async Task DataverseFailure_Is503_NeverNotASpaarkeDocument(Exception inner)
    {
        AltKeyThrows(Wrapped(inner));

        var act = () => Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    [Theory]
    [InlineData("Found multiple records for the alternate key sprk_graphitemid_uk")]
    [InlineData("The specified key attributes are not defined as keys for sprk_document")]
    [InlineData("Entity Key sprk_graphitemid_uk is Not Active")]
    public async Task UnhealthyAlternateKey_Is503_WithNoDuplicateTolerantFallback(string keyFault)
    {
        // The strict mock has no RetrieveMultipleAsync setup: a column-query fallback would throw a MockException
        // here instead of the 503 this asserts.
        AltKeyThrows(Wrapped(Fault(-2147088238, keyFault)));

        var act = () => Resolve(Spe(ResolvedFor(DriveId, ItemId)));

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task CallerCancellation_DuringTheDataverseLookup_PropagatesAsCancellation_NotAs503()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // DataverseServiceClientImpl wraps a cancellation in InvalidOperationException like any other failure.
        AltKeyThrows(Wrapped(new TaskCanceledException("A task was canceled.")));

        var act = () => Resolve(Spe(ResolvedFor(DriveId, ItemId)), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── URL validation ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void ParseDocumentUrl_EmptyOrMalformed_Is400(string? url)
    {
        var act = () => DocumentUrlIdentityResolution.ParseDocumentUrl(url);

        act.Should().Throw<SdapProblemException>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public void ParseDocumentUrl_OverTheLengthBound_Is400()
    {
        var url = "https://spaarke.sharepoint.com/" + new string('a', DocumentUrlIdentityResolution.MaxDocumentUrlLength);

        var act = () => DocumentUrlIdentityResolution.ParseDocumentUrl(url);

        act.Should().Throw<SdapProblemException>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public void ParseDocumentUrl_TheLiveHostCapture_ParsesAsAnAbsoluteHttpsUrl()
    {
        var uri = DocumentUrlIdentityResolution.ParseDocumentUrl(SpeUrl);

        uri.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private Task<DocumentUrlIdentityResolution.Result> Resolve(
        Mock<SpeFileStore> spe, string url = SpeUrl, CancellationToken ct = default)
        => DocumentUrlIdentityResolution.ResolveAsync(
            new Uri(url), new DefaultHttpContext(), spe.Object, _dataverse.Object, NullLogger.Instance, ct);

    private void AltKeyReturns(Entity row)
        => _dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.Is<KeyAttributeCollection>(k => (string)k["sprk_graphitemid"] == ItemId),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

    private void AltKeyThrows(Exception ex)
        => _dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    /// <summary>What <c>DataverseServiceClientImpl.RetrieveByAlternateKeyAsync</c> throws: every failure, wrapped once.</summary>
    private static InvalidOperationException Wrapped(Exception inner)
        => new($"Failed to retrieve sprk_document by alternate key: {inner.Message}", inner);

    private static FaultException<OrganizationServiceFault> Fault(int errorCode, string message)
        => new(new OrganizationServiceFault { ErrorCode = errorCode, Message = message }, new FaultReason(message));

    private static Entity Row(Guid id, string? driveId, params (string Attribute, EntityReference Reference)[] lookups)
    {
        var row = new Entity("sprk_document", id)
        {
            ["sprk_documentname"] = "Examiner report draft",
            ["sprk_filename"] = "Examiner report draft.docx",
        };
        if (driveId is not null)
            row["sprk_graphdriveid"] = driveId;
        foreach (var (attribute, reference) in lookups)
            row[attribute] = reference;
        return row;
    }

    private static SpeSharedItemResolution Outcome(SpeSharedItemOutcome outcome)
        => new(outcome, null, null, null, Array.Empty<SpeSharedItemAttempt>());

    internal static SpeSharedItemResolution ResolvedFor(string driveId, string itemId)
        => new(SpeSharedItemOutcome.Resolved, driveId, itemId, SharingUrlToken.EncodedForm, Array.Empty<SpeSharedItemAttempt>());

    internal static Mock<SpeFileStore> Spe(SpeSharedItemResolution result)
    {
        var gcf = Mock.Of<IGraphClientFactory>();
        var spe = new Mock<SpeFileStore>(MockBehavior.Loose,
            new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);
        spe.Setup(s => s.ResolveSharedItemAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return spe;
    }
}
