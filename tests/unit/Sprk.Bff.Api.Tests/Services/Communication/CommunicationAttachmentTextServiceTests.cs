using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="CommunicationAttachmentTextService"/> — the reconciliation browse reader's
/// attachment-text read model (email-communication-intelligence-r2 B2.1). These protect the load-bearing
/// properties that would regress silently:
/// <list type="number">
///   <item>a supported, downloadable, caller-visible attachment yields <c>Extractable=true</c> with its text;</item>
///   <item>EVERY non-text outcome (unsupported type, missing SPE reference, a document the caller cannot see,
///   image/vision-required, a download miss, an extraction failure, a thrown exception) degrades to the SAME
///   non-fatal <c>Extractable=false</c> fold — never an error, never a lost sibling attachment;</item>
///   <item>the two Dataverse reads are IMPERSONATED (NFR-06 no-leak) and an unresolvable caller is refused 403
///   (fail-closed).</item>
/// </list>
/// The impersonated query seam (<see cref="IImpersonatedCommunicationQuery"/>), the caller resolver
/// (<see cref="ICallerSystemUserResolver"/>), the SPE facade (<see cref="ISpeFileOperations"/>, ADR-007), and the
/// shared cache-aware extractor (<see cref="ITextExtractor"/>) are mocked at the module boundary — no live
/// Dataverse/SPE/Document-Intelligence is provisioned, and these side effects have no other observable surface
/// (same rationale + shape as <c>MessageAttachmentMaterializerTests</c> / <c>CommunicationThreadReadService</c> tests).
/// </summary>
public class CommunicationAttachmentTextServiceTests
{
    private static readonly Guid CommunicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string DocumentSet = "sprk_documents";
    private const string CommunicationSet = "sprk_communications";
    private const string ThreadSet = "sprk_communicationthreads";
    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;
    private const string DriveId = "b!drive-000";
    private const string ItemId = "item-000";

    private static Dictionary<string, JsonElement> Row(params (string Key, object? Val)[] fields)
    {
        var obj = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
            obj[k] = v;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    private static Dictionary<string, JsonElement> AttachmentRow(Guid attachmentId, Guid documentId, string name) =>
        Row(
            ("sprk_communicationattachmentid", attachmentId.ToString()),
            ("_sprk_document_value", documentId.ToString()),
            ("sprk_name", name));

    private static Dictionary<string, JsonElement> DocumentRow(
        Guid documentId, string fileName, string? driveId = DriveId, string? itemId = ItemId) =>
        Row(
            ("sprk_documentid", documentId.ToString()),
            ("sprk_graphdriveid", driveId),
            ("sprk_graphitemid", itemId),
            ("sprk_filename", fileName));

    private sealed class Harness
    {
        public Mock<IImpersonatedCommunicationQuery> Query { get; } = new(MockBehavior.Loose);
        public Mock<ICallerSystemUserResolver> Resolver { get; } = new(MockBehavior.Loose);
        public Mock<ISpeFileOperations> Spe { get; } = new(MockBehavior.Loose);
        public Mock<ITextExtractor> Extractor { get; } = new(MockBehavior.Loose);

        // Task 065 fix round 6 (F1): the communication's own visibility, decided before any attachment is listed. The default is
        // an internal caller reading a shareable email in an Open thread (the case every pre-existing test is about).
        public Mock<ISystemUserIdentityResolver> Identity { get; } = new();
        public bool CommunicationExists { get; set; } = true;
        public bool CommunicationInternalOnly { get; set; }
        public int? ThreadPrivacy { get; set; } = PrivacyOpen;
        public Guid ThreadId { get; } = Guid.NewGuid();

        // Task 188 round 4b (F1-4): who the caller is for THE external document narrowing, and what the BFF's app-only
        // identity reads (the REAL predicate behind the real seam). Internal by default; CallerIsExternal() narrows.
        public NarrowingCallerMode NarrowingMode { get; set; } = NarrowingCallerMode.Internal;
        public AppOnlyDocumentWorld World { get; } = new();

        public HttpContext HttpContext()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
            services.AddStatedNarrowingCaller(() => NarrowingMode, () => World);
            return new DefaultHttpContext
            {
                RequestServices = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services),
            };
        }

        public Harness()
        {
            // Default: a resolvable caller. Individual tests override for the fail-closed case.
            Resolver
                .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString()));

            Query
                .Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CommunicationExists
                    ? new List<Dictionary<string, JsonElement>>
                    {
                        Row(
                            ("sprk_communicationid", CommunicationId.ToString()),
                            ("sprk_isinternalonly", CommunicationInternalOnly),
                            ("_sprk_communicationthread_value", ThreadId.ToString())),
                    }
                    : new List<Dictionary<string, JsonElement>>());
            Query
                .Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new List<Dictionary<string, JsonElement>>
                {
                    ThreadPrivacy is { } privacy
                        ? Row(("sprk_communicationthreadid", ThreadId.ToString()), ("sprk_privacystate", privacy))
                        : Row(("sprk_communicationthreadid", ThreadId.ToString())),
                });
        }

        /// <summary>The caller is a systemuser flagged <c>sprk_isexternal</c> (a licensed partner).</summary>
        public Harness CallerIsExternal()
        {
            Identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            NarrowingMode = NarrowingCallerMode.External;
            return this;
        }

        public Harness CallerUnresolved()
        {
            Resolver
                .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallerSystemUserResolution { IsResolved = false, UnresolvedReason = "no_oid" });
            return this;
        }

        public Harness Attachments(params Dictionary<string, JsonElement>[] rows)
        {
            Query
                .Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
            return this;
        }

        public Harness AttachmentsThrow(Exception ex)
        {
            Query
                .Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);
            return this;
        }

        public Harness Documents(params Dictionary<string, JsonElement>[] rows)
        {
            // The app-only world holds each document as visible (Standard, no link, no parent) unless a test re-seeds it.
            foreach (var row in rows)
            {
                if (Guid.TryParse(row["sprk_documentid"].GetString(), out var id))
                    World.Document(id);
            }

            Query
                .Setup(q => q.QueryAsync(DocumentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
            return this;
        }

        public Harness Supported(bool supported = true)
        {
            Extractor.Setup(x => x.IsSupported(It.IsAny<string>())).Returns(supported);
            return this;
        }

        public Harness DownloadReturns(Func<Stream?> stream)
        {
            Spe
                .Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stream);
            Spe
                .Setup(s => s.GetFileMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileHandleDto(
                    Id: ItemId, Name: "f", ParentId: null, Size: 1,
                    CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                    ETag: "etag-1", IsFolder: false, WebUrl: null));
            return this;
        }

        public Harness Extracts(TextExtractionResult result)
        {
            Extractor
                .Setup(x => x.ExtractAsync(
                    It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            return this;
        }

        // Task 171: the bytes are read app-only after the document-pointer check. By default every b! container is a
        // business-unit container of the owner's subtree and every item was uploaded by the row's creator.
        public Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver ContainerResolver { get; set; } =
            TestRecordContainerResolver.ForBusinessUnitContainers(c => c.StartsWith("b!", StringComparison.Ordinal));

        public CommunicationAttachmentTextService Build() =>
            new(Query.Object, Resolver.Object,
                new CommunicationThreadReadService(
                    Query.Object, new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
                    Resolver.Object, Identity.Object, Mock.Of<ILogger<CommunicationThreadReadService>>()),
                Spe.Object, ContainerResolver, Extractor.Object,
                Mock.Of<ILogger<CommunicationAttachmentTextService>>());
    }

    private static Stream Bytes(string s = "file bytes") => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s));

    private static Task<CommunicationAttachmentTextResponse> Run(Harness h) =>
        h.Build().GetAttachmentTextAsync(CommunicationId, new ClaimsPrincipal(), h.HttpContext(), default);

    [Fact]
    public async Task GetAttachmentText_WhenSupportedAndDownloadable_ReturnsExtractedTextExtractable()
    {
        var att = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(att, doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Succeeded("SECTION 1. The registrant hereby files.", TextExtractionMethod.DocumentIntelligence)));

        var item = result.Attachments.Should().ContainSingle().Subject;
        item.AttachmentId.Should().Be(att);
        item.DocumentId.Should().Be(doc);
        item.FileName.Should().Be("brief.pdf");
        item.Extractable.Should().BeTrue();
        item.Text.Should().Be("SECTION 1. The registrant hereby files.");
        item.Method.Should().Be("DocumentIntelligence");
    }

    [Fact(DisplayName = "Task 171: an attachment whose document pointer cannot be verified is not extractable and its bytes are never read app-only")]
    public async Task GetAttachmentText_WhenPointerUnverifiable_ReturnsNotExtractableAndSkipsDownload()
    {
        var doc = Guid.NewGuid();
        var harness = new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported(true)
            .DownloadReturns(() => Bytes());
        // No container is a business-unit container of the owner's subtree: the pointer check refuses.
        harness.ContainerResolver = TestRecordContainerResolver.ForBusinessUnitContainers();

        var result = await Run(harness);

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
        harness.Spe.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the BFF follows the row's pointer as the application only after the pointer is verified");
    }

    [Fact]
    public async Task GetAttachmentText_WhenFileTypeUnsupported_ReturnsNotExtractableAndSkipsDownload()
    {
        var doc = Guid.NewGuid();
        var harness = new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "photo.heic"))
            .Documents(DocumentRow(doc, "photo.heic"))
            .Supported(false);

        var result = await Run(harness);

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
        // Unsupported types must never trigger an SPE download.
        harness.Spe.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAttachmentText_WhenExtractionRequiresVision_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "scan.png"))
            .Documents(DocumentRow(doc, "scan.png"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.RequiresVision())); // Success=true but Text=null

        var item = result.Attachments.Should().ContainSingle().Subject;
        item.Extractable.Should().BeFalse();
        item.Text.Should().BeNull();
    }

    [Fact]
    public async Task GetAttachmentText_WhenExtractionFails_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "corrupt.pdf"))
            .Documents(DocumentRow(doc, "corrupt.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Failed("boom", TextExtractionMethod.DocumentIntelligence)));

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenDownloadReturnsNull_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported()
            .DownloadReturns(() => null));

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenDocumentNotVisibleToCaller_ReturnsNotExtractable()
    {
        // The attachment is visible, but the impersonated DOCUMENT read returns nothing (caller cannot see the
        // sprk_document) → no SPE pointer → not extractable, no download attempted.
        var harness = new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), Guid.NewGuid(), "brief.pdf"))
            .Documents() // empty: document not visible to the caller
            .Supported();

        var result = await Run(harness);

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
        harness.Spe.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAttachmentText_WhenOneAttachmentThrows_StillReturnsBothNonFatally()
    {
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var okDoc = Guid.NewGuid();
        var badDoc = Guid.NewGuid();
        var harness = new Harness()
            .Attachments(AttachmentRow(ok, okDoc, "good.pdf"), AttachmentRow(bad, badDoc, "bad.pdf"))
            .Documents(
                DocumentRow(okDoc, "good.pdf", driveId: "b!ok", itemId: "ok"),
                DocumentRow(badDoc, "bad.pdf", driveId: "b!bad", itemId: "bad"))
            .Supported();
        harness.Spe
            .Setup(s => s.GetFileMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);
        harness.Spe
            .Setup(s => s.DownloadFileAsync("b!ok", "ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Bytes());
        harness.Spe
            .Setup(s => s.DownloadFileAsync("b!bad", "bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient SPE failure"));
        harness.Extracts(TextExtractionResult.Succeeded("good text", TextExtractionMethod.Native));

        var result = await Run(harness);

        result.Attachments.Should().HaveCount(2);
        result.Attachments.Single(a => a.AttachmentId == ok).Extractable.Should().BeTrue();
        result.Attachments.Single(a => a.AttachmentId == bad).Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenAttachmentQueryThrows_ReturnsEmptyNonFatally()
    {
        var result = await Run(new Harness().AttachmentsThrow(new InvalidOperationException("dataverse down")));

        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentText_WhenNoAttachments_ReturnsEmptyList()
    {
        var result = await Run(new Harness().Attachments());

        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentText_WhenCallerUnresolvable_Throws403FailClosed()
    {
        var harness = new Harness().CallerUnresolved();

        var act = async () => await Run(harness);

        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(403);
        // Fail-closed: never touches Dataverse or SPE when the caller cannot be resolved.
        harness.Query.Verify(
            q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    // ── Task 065 fix round 6 (F1): the communication must be visible before its attachments are listed ──

    private static Harness WithOneExtractableAttachment(Harness h) => WithOneExtractableAttachment(h, Guid.NewGuid());

    private static Harness WithOneExtractableAttachment(Harness h, Guid doc)
    {
        return h
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Succeeded("Privileged strategy memo.", TextExtractionMethod.DocumentIntelligence));
    }

    private static void ShouldHaveReadNoAttachment(Harness h)
    {
        h.Query.Verify(
            q => q.QueryAsync(AttachmentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the communication is not visible to this caller, so its attachments are not even listed");
        h.Spe.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "F1: an external caller gets NO attachment text for an internal-only email")]
    public async Task GetAttachmentText_ExternalCaller_InternalOnlyEmail_ReturnsNothing()
    {
        var h = WithOneExtractableAttachment(new Harness().CallerIsExternal());
        h.CommunicationInternalOnly = true;

        var result = await Run(h);

        result.Attachments.Should().BeEmpty();
        ShouldHaveReadNoAttachment(h);
    }

    [Fact(DisplayName = "F1: an external caller gets NO attachment text for an email in a Private thread (D7)")]
    public async Task GetAttachmentText_ExternalCaller_PrivateThread_ReturnsNothing()
    {
        var h = WithOneExtractableAttachment(new Harness().CallerIsExternal());
        h.ThreadPrivacy = PrivacyPrivate;

        var result = await Run(h);

        result.Attachments.Should().BeEmpty();
        ShouldHaveReadNoAttachment(h);
    }

    [Fact(DisplayName = "F1: an external caller gets NO attachment text for an email whose thread has no privacy value")]
    public async Task GetAttachmentText_ExternalCaller_ThreadWithNoPrivacyValue_ReturnsNothing()
    {
        var h = WithOneExtractableAttachment(new Harness().CallerIsExternal());
        h.ThreadPrivacy = null;

        (await Run(h)).Attachments.Should().BeEmpty();
        ShouldHaveReadNoAttachment(h);
    }

    [Fact(DisplayName = "F1 control: an external caller DOES get the text of a shareable email in an Open thread")]
    public async Task GetAttachmentText_ExternalCaller_ShareableEmailInOpenThread_ReturnsTheText()
    {
        var h = WithOneExtractableAttachment(new Harness().CallerIsExternal());

        var item = (await Run(h)).Attachments.Should().ContainSingle().Subject;

        item.Extractable.Should().BeTrue();
        item.Text.Should().Be("Privileged strategy memo.");
    }

    [Fact(DisplayName = "F1: an internal caller still gets the text, even of an internal-only email in a Private thread")]
    public async Task GetAttachmentText_InternalCaller_InternalOnlyEmailInPrivateThread_ReturnsTheText()
    {
        var h = WithOneExtractableAttachment(new Harness());
        h.CommunicationInternalOnly = true;
        h.ThreadPrivacy = PrivacyPrivate;

        var item = (await Run(h)).Attachments.Should().ContainSingle().Subject;

        item.Extractable.Should().BeTrue();
        item.Text.Should().Be("Privileged strategy memo.");
    }

    [Fact(DisplayName = "F1: a communication the caller cannot read answers like one with no attachments")]
    public async Task GetAttachmentText_CommunicationNotReadable_ReturnsNothing()
    {
        var h = WithOneExtractableAttachment(new Harness());
        h.CommunicationExists = false;

        (await Run(h)).Attachments.Should().BeEmpty();
        ShouldHaveReadNoAttachment(h);
    }

    // ── Task 188 round 4b (F1-4): THE external document narrowing on the attachment text ───────────────────────────

    private const int Restricted = 100000002;

    /// <summary>
    /// Arranges a must-fire case for the attachment's document <paramref name="doc"/> (the communication itself is a
    /// shareable email in an Open thread, so the 065 rule passes and only the narrowing can withhold the text).
    /// </summary>
    private static void ArrangeHidden(Harness h, Guid doc, string @case)
    {
        switch (@case)
        {
            case "own-restricted":
                h.World.Document(doc, permission: Restricted);
                break;
            case "internal-only-communication":
                var communication = Guid.NewGuid();
                var thread = Guid.NewGuid();
                h.World.Thread(thread, PrivacyOpen);
                h.World.Communication(communication, internalOnly: true, thread);
                h.World.Document(doc, communication: communication);
                break;
            case "restricted-parent":
                var parent = h.World.Document(Guid.NewGuid(), permission: Restricted);
                h.World.Document(doc, parent: parent);
                break;
            case "narrowing-fault":
                h.World.Faults["sprk_documents"] = System.Net.HttpStatusCode.ServiceUnavailable;
                break;
            case "lookup-fault":
                h.NarrowingMode = NarrowingCallerMode.LookupFault;
                h.World.Document(doc, permission: Restricted);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }
    }

    [Theory(DisplayName = "Task 188 r4b F1-4: a caller who is not an internal systemuser gets no text of an attachment whose document the narrowing hides")]
    [InlineData("own-restricted")]
    [InlineData("internal-only-communication")]
    [InlineData("restricted-parent")]
    [InlineData("narrowing-fault")]
    [InlineData("lookup-fault")]
    public async Task GetAttachmentText_ExternalCaller_HiddenDocument_IsNotExtractable_AndItsBytesAreNeverRead(string @case)
    {
        var doc = Guid.NewGuid();
        var h = WithOneExtractableAttachment(new Harness().CallerIsExternal(), doc);
        ArrangeHidden(h, doc, @case);

        var item = (await Run(h)).Attachments.Should().ContainSingle().Subject;

        item.Extractable.Should().BeFalse();
        item.Text.Should().BeNull();
        h.Spe.Verify(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.World.Calls.Should().Be(1, "ONE narrowing call over the page's documents");
    }

    [Fact(DisplayName = "Task 188 r4b F1-4: a hidden attachment and a visible sibling — only the hidden one loses its text")]
    public async Task GetAttachmentText_ExternalCaller_HiddenAndVisibleSiblings_OnlyTheHiddenOneIsWithheld()
    {
        var hidden = Guid.NewGuid();
        var visible = Guid.NewGuid();
        var h = new Harness().CallerIsExternal()
            .Attachments(AttachmentRow(Guid.NewGuid(), hidden, "a.pdf"), AttachmentRow(Guid.NewGuid(), visible, "b.pdf"))
            .Documents(DocumentRow(hidden, "a.pdf"), DocumentRow(visible, "b.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Succeeded("text", TextExtractionMethod.DocumentIntelligence));
        h.World.Document(hidden, permission: Restricted);

        var items = (await Run(h)).Attachments;

        items.Single(i => i.DocumentId == hidden).Extractable.Should().BeFalse();
        items.Single(i => i.DocumentId == visible).Extractable.Should().BeTrue();
        h.World.Calls.Should().Be(1);
    }

    [Fact(DisplayName = "Task 188 r4b F1-4: must not fire — an internal caller gets a Restricted document's text, with no narrowing call and no app-only read")]
    public async Task GetAttachmentText_InternalCaller_RestrictedDocument_IsNotNarrowed()
    {
        var doc = Guid.NewGuid();
        var h = WithOneExtractableAttachment(new Harness(), doc);
        h.World.Document(doc, permission: Restricted);

        (await Run(h)).Attachments.Should().ContainSingle().Which.Extractable.Should().BeTrue();
        h.World.Calls.Should().Be(0);
        h.World.AllReads.Should().BeEmpty();
    }
}
