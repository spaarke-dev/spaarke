using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Services.Documents;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// spaarkeai-word-add-in-r1 task 143: <c>POST /api/documents/resolve-email-identity</c>'s <c>savedAttachmentNames</c> also lists
/// the email's STANDALONE attachment documents — saved on their own (without the email), so they are not the archive and have no
/// <c>.eml</c> parent, and are linked to the email's <c>sprk_communication</c> (found by its Message-ID) — that the caller may
/// read. When no <c>.eml</c> is saved (or the caller may not read it) the <c>not_saved</c> answer carries those names too, so
/// the pane marks those attachments saved on the first save.
/// </summary>
/// <remarks>
/// The full pipeline with module-boundary doubles, as <c>DocumentEmailIdentitySavedAttachmentsContractTests</c>: the app-only
/// <c>sprk_document</c> queries (the archive lookup and the standalone lookup, told apart by their conditions), the child read,
/// and the access data source (each document's check, ADR-008 A1).
/// </remarks>
[Trait("status", "task-143-word-add-in-r1")]
public class DocumentEmailIdentityStandaloneAttachmentsContractTests
{
    private const string Route = "/api/documents/resolve-email-identity";
    private const string MessageId = "<CAF0-143-identity@mail.example.com>";

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavedEmail_ListsItsChildren_AndTheStandaloneAttachmentsTheCallerMayRead()
    {
        var emlId = Guid.NewGuid();
        var child = Document("Settlement terms.pdf");
        var standalone = Document("Exhibit A", fileName: "Exhibit A.pdf");
        using var host = Host(
            eml: emlId, children: new[] { child }, standalone: new[] { standalone },
            rights: Readable(emlId, child, standalone));

        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        Names(body).Should().BeEquivalentTo(new[] { "Settlement terms.pdf", "Exhibit A", "Exhibit A.pdf" });
    }

    [Fact]
    public async Task NoSavedEmail_AnswersNotSaved_WithTheStandaloneAttachmentsTheCallerMayRead()
    {
        var standalone = Document("Exhibit A.pdf");
        using var host = Host(eml: null, children: null, standalone: new[] { standalone }, rights: Readable(null, standalone));

        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_saved");
        Names(body).Should().Equal("Exhibit A.pdf");
    }

    [Fact]
    public async Task SavedEmailTheCallerMayNotRead_AnswersExactlyAsNoSavedEmail_WithoutItsChildren()
    {
        var emlId = Guid.NewGuid();
        var hiddenChild = Document("Privileged memo.pdf");
        var standalone = Document("Exhibit A.pdf");
        var rights = Readable(null, hiddenChild, standalone);
        rights[emlId] = AccessRights.None;

        string refused, noMatch;
        using (var host = Host(eml: emlId, children: new[] { hiddenChild }, standalone: new[] { standalone }, rights: rights,
                   out var dataverse))
        {
            refused = await RawResolve(host);
            dataverse.Verify(d => d.GetDocumentsByParentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
                "the children of an .eml the caller may not read are never read");
        }

        using (var host = Host(eml: null, children: null, standalone: new[] { standalone }, rights: rights))
            noMatch = await RawResolve(host);

        refused.Should().Be(noMatch, "a saved email the caller may not read is answered exactly like none");
        refused.Should().NotContain("Privileged memo");
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoSavedEmail_LeavesOutAStandaloneAttachmentTheCallerMayNotRead()
    {
        var readable = Document("Exhibit A.pdf");
        var hidden = Document("Privileged memo.pdf");
        var rights = Readable(null, readable);
        rights[hidden.Id] = AccessRights.None;
        using var host = Host(eml: null, children: null, standalone: new[] { readable, hidden }, rights: rights);

        var raw = await RawResolve(host);

        raw.Should().NotContain("Privileged memo");
        Names(JsonDocument.Parse(raw).RootElement).Should().Equal("Exhibit A.pdf");
    }

    [Fact]
    public async Task NoSavedEmail_AndNoStandaloneAttachment_AnswersTheUnchangedNotSaved()
    {
        using var host = Host(eml: null, children: null, standalone: Array.Empty<Entity>(), rights: new());

        var body = await Resolve(host);

        body.GetProperty("reason").GetString().Should().Be("not_saved");
        body.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse("the no-match answer is unchanged");
    }

    [Fact]
    public async Task NoSavedEmail_WhenTheStandaloneReadFails_OmitsTheField()
    {
        using var host = Host(eml: null, children: null, standalone: null, rights: new());

        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_saved", "the identity never fails on the attachments read");
        body.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse();
    }

    [Fact]
    public async Task NoSavedEmail_WhenAStandaloneCheckFaults_OmitsTheField()
    {
        var readable = Document("Exhibit A.pdf");
        var faulting = Document("Faulting check.pdf");
        using var host = Host(
            eml: null, children: null, standalone: new[] { readable, faulting }, rights: Readable(null, readable),
            faultFor: faulting.Id);

        var raw = await RawResolve(host);

        raw.Should().NotContain("Faulting check");
        JsonDocument.Parse(raw).RootElement.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse(
            "a check that did not decide cannot say the attachment is unsaved");
    }

    [Fact]
    public async Task SavedEmail_WhenTheStandaloneReadFails_OmitsTheField_AndStillAnswersTheIdentity()
    {
        var emlId = Guid.NewGuid();
        var child = Document("Settlement terms.pdf");
        using var host = Host(eml: emlId, children: new[] { child }, standalone: null, rights: Readable(emlId, child));

        var body = await Resolve(host);

        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.TryGetProperty("savedAttachmentNames", out _).Should().BeFalse(
            "an attachment saved on its own may be missing from a partial list, so nothing is said");
    }

    [Fact]
    public async Task TheStandaloneLookup_FindsNonArchiveDocumentsWithNoParent_LinkedToTheEmailsCommunicationByItsMessageId()
    {
        QueryExpression? captured = null;
        using var host = Host(eml: null, children: null, standalone: Array.Empty<Entity>(), rights: new(),
            onStandaloneQuery: q => captured = q);

        await Resolve(host);

        captured.Should().NotBeNull();
        captured!.Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_parentdocument" && c.Operator == ConditionOperator.Null);
        var notArchive = captured.Criteria.Filters.Should().ContainSingle().Subject;
        notArchive.FilterOperator.Should().Be(LogicalOperator.Or);
        notArchive.Conditions.Should().Contain(c => c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Null);
        notArchive.Conditions.Should().Contain(c =>
            c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal && Equals(c.Values.Single(), false));
        var communication = captured.LinkEntities.Should().ContainSingle().Subject;
        communication.LinkToEntityName.Should().Be("sprk_communication");
        communication.LinkFromAttributeName.Should().Be("sprk_relatedcommunication");
        communication.JoinOperator.Should().Be(JoinOperator.Inner);
        communication.LinkCriteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_internetmessageid" && Equals(c.Values.Single(), MessageId));
    }

    // ── The external document narrowing (a document hidden from an external caller is not named) ───────────────────

    /// <summary>The cases that hide ONE document from an external caller, leaving another shown (not the caller-wide faults).</summary>
    public static TheoryData<string> PerDocumentHiddenCases => new()
    {
        HiddenDocumentCases.OwnRestricted, HiddenDocumentCases.InternalOnlyCommunication, HiddenDocumentCases.RestrictedParent,
    };

    /// <summary>The cases that fault for the whole caller (the narrowing read, or the caller lookup).</summary>
    public static TheoryData<string> CallerWideFaultCases => new()
    {
        HiddenDocumentCases.NarrowingFault, HiddenDocumentCases.LookupFault,
    };

    [Theory]
    [MemberData(nameof(PerDocumentHiddenCases))]
    public async Task NoSavedEmail_ExternalCaller_LeavesOutAStandaloneAttachmentTheNarrowingHides_AndNamesTheShownOne(string @case)
    {
        var shown = Document("Exhibit A.pdf");
        var hidden = Document("Privileged memo.pdf");
        var world = new AppOnlyDocumentWorld();
        world.Document(shown.Id);
        var mode = HiddenDocumentCases.Arrange(world, hidden.Id, @case);
        using var host = Host(
            eml: null, children: null, standalone: new[] { shown, hidden }, rights: Readable(null, shown, hidden),
            caller: mode, world: world);

        var raw = await RawResolve(host);

        raw.Should().NotContain("Privileged memo", "the caller holds Read on it, but the external narrowing hides it");
        Names(JsonDocument.Parse(raw).RootElement).Should().Equal("Exhibit A.pdf");
        world.Calls.Should().BeGreaterThan(0, "the external narrowing decided");
    }

    [Theory]
    [MemberData(nameof(PerDocumentHiddenCases))]
    public async Task SavedEmail_ExternalCaller_LeavesOutAStandaloneAttachmentTheNarrowingHides_AndKeepsTheShownOnes(string @case)
    {
        var emlId = Guid.NewGuid();
        var child = Document("Settlement terms.pdf");
        var shown = Document("Exhibit A.pdf");
        var hidden = Document("Privileged memo.pdf");
        var world = new AppOnlyDocumentWorld();
        world.Document(emlId);
        world.Document(child.Id);
        world.Document(shown.Id);
        var mode = HiddenDocumentCases.Arrange(world, hidden.Id, @case);
        using var host = Host(
            eml: emlId, children: new[] { child }, standalone: new[] { shown, hidden },
            rights: Readable(emlId, child, shown, hidden), caller: mode, world: world);

        var raw = await RawResolve(host);

        raw.Should().NotContain("Privileged memo");
        Names(JsonDocument.Parse(raw).RootElement).Should().BeEquivalentTo(new[] { "Settlement terms.pdf", "Exhibit A.pdf" });
    }

    [Theory]
    [MemberData(nameof(CallerWideFaultCases))]
    public async Task NoSavedEmail_ExternalCaller_WhenTheNarrowingFaults_NeverNamesTheStandaloneAttachment(string @case)
    {
        var hidden = Document("Privileged memo.pdf");
        var world = new AppOnlyDocumentWorld();
        var mode = HiddenDocumentCases.Arrange(world, hidden.Id, @case);
        using var host = Host(
            eml: null, children: null, standalone: new[] { hidden }, rights: Readable(null, hidden), caller: mode, world: world);

        var raw = await RawResolve(host);

        raw.Should().NotContain("Privileged memo", "a narrowing that did not decide fails closed");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Entity Document(string name, string? fileName = null)
    {
        var row = new Entity("sprk_document", Guid.NewGuid()) { ["sprk_documentname"] = name };
        if (fileName is not null)
            row["sprk_filename"] = fileName;
        return row;
    }

    private static Dictionary<Guid, AccessRights> Readable(Guid? emlId, params Entity[] documents)
    {
        var rights = documents.ToDictionary(d => d.Id, _ => AccessRights.Read);
        if (emlId is { } id)
            rights[id] = AccessRights.Read;
        return rights;
    }

    private static DocumentEntity ChildOf(Entity row) => new()
    {
        Id = row.Id.ToString("D"),
        Name = row.GetAttributeValue<string>("sprk_documentname"),
        FileName = row.GetAttributeValue<string>("sprk_filename"),
    };

    private static WebApplicationFactory<Program> Host(
        Guid? eml, Entity[]? children, Entity[]? standalone, Dictionary<Guid, AccessRights> rights, Guid? faultFor = null,
        Action<QueryExpression>? onStandaloneQuery = null, NarrowingCallerMode? caller = null, AppOnlyDocumentWorld? world = null) =>
        Host(eml, children, standalone, rights, out _, faultFor, onStandaloneQuery, caller, world);

    /// <param name="eml">The saved <c>.eml</c> the archive lookup finds; <see langword="null"/> = none.</param>
    /// <param name="children">The <c>.eml</c>'s children; <see langword="null"/> = none read.</param>
    /// <param name="standalone">The standalone lookup's answer; <see langword="null"/> = the read throws.</param>
    private static WebApplicationFactory<Program> Host(
        Guid? eml, Entity[]? children, Entity[]? standalone, Dictionary<Guid, AccessRights> rights,
        out Mock<IDataverseService> dataverse, Guid? faultFor = null, Action<QueryExpression>? onStandaloneQuery = null,
        NarrowingCallerMode? caller = null, AppOnlyDocumentWorld? world = null)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsArchiveLookup(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(eml is { } emlId
                ? new List<Entity> { DocumentEmailIdentityResolutionTests.Row(emlId) }
                : new List<Entity>()));
        var standaloneRead = entityService
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsStandaloneLookup(q)), It.IsAny<CancellationToken>()))
            .Callback((QueryExpression q, CancellationToken _) => onStandaloneQuery?.Invoke(q));
        if (standalone is null)
            standaloneRead.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));
        else
            standaloneRead.ReturnsAsync(new EntityCollection(standalone.ToList()));

        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string user, string resourceId, string? _, CancellationToken _) =>
            {
                var id = Guid.Parse(resourceId);
                if (id == faultFor)
                    throw new InvalidOperationException("access check fault");
                return Task.FromResult(new AccessSnapshot
                {
                    UserId = user,
                    ResourceId = resourceId,
                    AccessRights = rights.TryGetValue(id, out var r) ? r : AccessRights.None,
                });
            });

        var documents = new Mock<IDataverseService>();
        if (eml is { } parent)
        {
            documents
                .Setup(d => d.GetDocumentsByParentAsync(parent, It.IsAny<CancellationToken>()))
                .ReturnsAsync((children ?? Array.Empty<Entity>()).Select(ChildOf).ToList());
        }

        dataverse = documents;
        var factory = new DocumentIdentityTestWebAppFactory(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(documents.Object);
            // The caller and the app-only world THE external document narrowing reads, when a test states them.
            if (caller is { } mode && world is not null)
                services.AddStatedNarrowingCaller(() => mode, () => world);
        }));
    }

    private static bool IsArchiveLookup(QueryExpression q) =>
        q.EntityName == "sprk_document"
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal
            && Equals(c.Values.SingleOrDefault(), true));

    private static bool IsStandaloneLookup(QueryExpression q) =>
        q.EntityName == "sprk_document"
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_parentdocument" && c.Operator == ConditionOperator.Null);

    private static async Task<string> RawResolve(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var response = await client.PostAsJsonAsync(Route, new { internetMessageId = MessageId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> Resolve(WebApplicationFactory<Program> host) =>
        JsonDocument.Parse(await RawResolve(host)).RootElement.Clone();

    private static List<string> Names(JsonElement body) =>
        body.GetProperty("savedAttachmentNames").EnumerateArray().Select(e => e.GetString()!).ToList();
}
