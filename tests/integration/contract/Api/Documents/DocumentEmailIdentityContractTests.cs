using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.Services.Documents;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract coverage for <c>POST /api/documents/resolve-email-identity</c> (spaarkeai-word-add-in-r1 task 120, UAT
/// round 12 O6) — the email sibling of <c>POST /api/documents/resolve-identity</c>: is the email open in Outlook
/// already saved to Spaarke, and to which record?
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> <see cref="DocumentEmailIdentityResolutionTests"/> pins the input rules and the query shape at
/// the unit level. THIS file proves the full pipeline — real routing, the resolution filter, then
/// <c>DocumentAuthorizationFilter("read")</c> on the id it resolved, real authentication, real JSON — for each status
/// the route can answer: 200 resolved (filed / unfiled), 200 <c>not_saved</c>, 403 with no metadata, 400 without
/// echoing the key, 401, 503.</para>
/// <para><b>Fixture.</b> Reuses <see cref="DocumentIdentityTestWebAppFactory"/> as-is (CLAUDE.md §11). The route never
/// touches SharePoint Embedded, so its <c>SpeFileStore</c> double is a placeholder the route never calls.</para>
/// </remarks>
public class DocumentEmailIdentityContractTests
{
    private const string Route = "/api/documents/resolve-email-identity";
    private const string MessageId = "<CAF0a1b2c3@mail.example.com>";
    private const string ItemId = "AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=";

    // ── Saved, and the caller may read it ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_ForASavedCopyFiledToAMatter_Returns200WithTheSameShapeAsResolveIdentity()
    {
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = SavedCopy(
            DocumentEmailIdentityResolutionTests.Row(
                documentId, ("sprk_matter", new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" })));
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(
            Route, new { internetMessageId = MessageId, exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("documentName").GetString().Should().Be("RE: Discovery schedule");
        body.GetProperty("fileName").GetString().Should().Be("RE_ Discovery schedule_a1b2.eml");
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        related.GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        related.GetProperty("name").GetString().Should().Be("MAT-000042");
        related.GetProperty("displayName").GetString().Should().Be("Acme v Globex");
        related.GetProperty("number").GetString().Should().Be("MAT-000042");
    }

    [Fact]
    public async Task ResolveEmail_ForAnUnfiledSavedCopy_Returns200WithANullRelatedRecord()
    {
        var documentId = Guid.NewGuid();

        using var factory = Factory(
            SavedCopy(DocumentEmailIdentityResolutionTests.Row(documentId)), AccessSource(documentId, AccessRights.Read));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("relatedRecord").ValueKind.Should().Be(JsonValueKind.Null);
        IdentityJson.RelatedRecordWithheld(body).Should().BeNull("an unfiled document is KNOWN unfiled, not withheld");
    }

    // ── Task 132: a captured archive's record comes from its communication ────────────────────

    [Fact]
    public async Task ResolveEmail_ForACapturedArchive_ShowsTheCommunicationsRecord_WhenTheCallerMayReadIt()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        CommunicationFiledTo(entityService, communicationId, new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" });
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });
        var probe = Probe();
        probe.Setup(p => p.GetCallerRightsAsync("test-token", "sprk_matters", matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        related.GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        related.GetProperty("displayName").GetString().Should().Be("Acme v Globex");
        related.GetProperty("number").GetString().Should().Be("MAT-000042");
        IdentityJson.RelatedRecordWithheld(body).Should().BeNull("a shown card is never also reported as withheld");
        probe.Verify(
            p => p.GetCallerRightsAsync("test-token", "sprk_matters", matterId, It.IsAny<CancellationToken>()), Times.Once,
            "the caller's own rights on the derived record are asked, with the caller's token");
    }

    public static TheoryData<AccessRights> RightsWithoutRead => new()
    {
        AccessRights.None,
        AccessRights.Write | AccessRights.AppendTo,
    };

    [Theory]
    [MemberData(nameof(RightsWithoutRead))]
    public async Task ResolveEmail_ForACapturedArchive_WhenTheCallerMayNotReadTheRecord_Returns200WithoutTheCard(
        AccessRights rights)
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        // STRICT: no setup for the matter's display read — it must never run for a record the caller may not read.
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        CommunicationFiledTo(entityService, communicationId, new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" });
        var probe = Probe();
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), "sprk_matters", matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        await ShouldBeTheSameAnswerWithoutACard(response, documentId, "MAT-000042", withheld: true);
    }

    [Fact]
    public async Task ResolveEmail_ForACapturedArchive_WhenTheAccessCheckThrows_Returns200WithoutTheCard()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        CommunicationFiledTo(entityService, communicationId, new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" });
        var probe = Probe();
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse is unreachable."));

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        await ShouldBeTheSameAnswerWithoutACard(response, documentId, "MAT-000042", withheld: true);
    }

    [Fact]
    public async Task ResolveEmail_ForACapturedArchive_WhenTheCommunicationCannotBeRead_Returns200WithoutTheCard_Withheld()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        entityService
            .Setup(e => e.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));
        // STRICT and EMPTY: with no record there is nothing to ask about.
        var probe = Probe();

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        // The communication may be filed: "unknown" (withheld), never "not filed" with a filing offer.
        await ShouldBeTheSameAnswerWithoutACard(response, documentId, unexpected: null, withheld: true);
    }

    // ── Review F2: only the CALLER's cancellation propagates ──────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WhenTheCommunicationReadTimesOutOutsideTheCaller_Returns200WithoutTheCard_NotA500()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        // An HTTP client timeout surfaces as a TaskCanceledException although the request was never cancelled.
        entityService
            .Setup(e => e.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), Probe());
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        await ShouldBeTheSameAnswerWithoutACard(response, documentId, unexpected: null, withheld: true);
    }

    [Fact]
    public async Task ResolveEmail_WhenTheDisplayReadTimesOutOutsideTheCaller_Returns200_WithTheCardsDisplayFieldBlank()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        CommunicationFiledTo(entityService, communicationId, new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" });
        entityService
            .Setup(e => e.RetrieveAsync("sprk_matter", matterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        var probe = Probe();
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), "sprk_matters", matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an optional display read that times out is never a 500");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var related = body.GetProperty("relatedRecord");
        related.GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        related.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null, "the display field renders blank");
        IdentityJson.RelatedRecordWithheld(body).Should().BeNull();
    }

    [Fact]
    public async Task ResolveEmail_ForADocumentWithItsOwnSlot_KeepsIt_NeitherReadsTheCommunicationNorAsksTheProbe()
    {
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var row = DocumentEmailIdentityResolutionTests.Row(
            documentId, ("sprk_matter", new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" }));
        row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", Guid.NewGuid());
        // STRICT: no sprk_communication read is set up, and the probe is STRICT and EMPTY.
        var entityService = SavedCopy(row);
        entityService
            .Setup(e => e.RetrieveAsync(
                "sprk_matter", matterId, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });
        var probe = Probe();

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.Read), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("relatedRecord").GetProperty("id").GetString().Should().Be(matterId.ToString("D"));
        IdentityJson.RelatedRecordWithheld(body).Should().BeNull("a direct slot is shown, never withheld");
        entityService.Verify(
            e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Not saved ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WhenNoSavedCopyExists_Returns200NotSaved_AndNeverAuthorizes()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        // STRICT and EMPTY: there is nothing to authorize, so the access source must never be asked.
        var accessSource = new Mock<IAccessDataSource>(MockBehavior.Strict);

        using var factory = Factory(entityService, accessSource);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(
            Route, new { internetMessageId = MessageId, exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_saved");
        body.GetProperty("documentId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Saved, but the caller may not read the newest copy: answered EXACTLY like no match ────
    // (UAC-r2 deploy gate: the keys are caller-supplied and, since task 132, the match covers captured and Spaarke-sent
    // mail, so a refusal that differs from "not saved" would tell a caller that the thing exists.)

    [Fact]
    public async Task ResolveEmail_WhenCallerLacksReadOnTheNewestCopy_AnswersByteForByteLikeNoMatch_AndReadsNoMore()
    {
        var documentId = Guid.NewGuid();
        // Only the lookup is set up (STRICT): no row read, no related-record read — the handler never runs.
        var entityService = SavedCopy(DocumentEmailIdentityResolutionTests.Row(
            documentId, ("sprk_matter", new EntityReference("sprk_matter", Guid.NewGuid()) { Name = "MAT-000042" })));

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.None));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        var answer = await Answer.OfAsync(response);
        answer.Should().Be(await NoMatchAnswerAsync());
        AssertIsTheNotSavedShape(answer);
        answer.Body.Should().NotContain("Discovery schedule").And.NotContain("MAT-000042")
            .And.NotContain(documentId.ToString("D")).And.NotContain("sdap.access");
        entityService.Verify(
            e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Once,
            "one candidate, one authorization — an older copy is never searched");
    }

    [Fact]
    public async Task ResolveEmail_ThroughTheCommunication_WhenCallerLacksRead_AnswersLikeNoMatch_NeitherReadsTheCommunicationNorAsksTheProbe()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        // A mailbox-captured archive: no sprk_emailmessageid, matched through its communication's sprk_internetmessageid.
        // STRICT: no sprk_communication read is set up, and the probe is STRICT and EMPTY.
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        var probe = Probe();

        using var factory = Factory(entityService, AccessSource(documentId, AccessRights.None), probe);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        var answer = await Answer.OfAsync(response);
        answer.Should().Be(await NoMatchAnswerAsync());
        AssertIsTheNotSavedShape(answer);
        answer.Body.Should().NotContain(documentId.ToString("D")).And.NotContain(communicationId.ToString("D"));
        entityService.Verify(
            e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the communication is never read for a refused match");
        probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "the caller-rights probe never runs for a refused match");
    }

    [Fact]
    public async Task ResolveEmail_ThroughAnInternalOnlyCommunication_ForAFlaggedExternalCaller_AnswersLikeNoMatch()
    {
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var world = InternalOnlyCommunicationWorld(documentId, communicationId);
        var entityService = SavedCopy(CapturedArchive(documentId, communicationId));
        var probe = Probe();

        // The root grants ALLOW read; only the external document narrowing hides the document.
        using var factory = ExternalCaller(
            Factory(entityService, AccessSource(documentId, AccessRights.Read), probe), NarrowingCallerMode.External, world);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        var answer = await Answer.OfAsync(response);
        answer.Should().Be(await NoMatchAnswerAsync());
        AssertIsTheNotSavedShape(answer);
        answer.Body.Should().NotContain(documentId.ToString("D")).And.NotContain("sdap.access");
        world.Calls.Should().Be(1, "it was the narrowing that refused");
        entityService.Verify(
            e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the communication is never read for a refused match");
        probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "the caller-rights probe never runs for a refused match");
    }

    [Fact]
    public async Task ResolveEmail_ThroughAnInternalOnlyCommunication_ForAnInternalCaller_StillResolves()
    {
        // Must-not-fire control for the test above: same data, same rights, but the caller is an internal systemuser, so
        // the narrowing does not apply and the match resolves.
        var documentId = Guid.NewGuid();
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var world = InternalOnlyCommunicationWorld(documentId, communicationId);
        var row = DocumentEmailIdentityResolutionTests.Row(
            documentId, ("sprk_matter", new EntityReference("sprk_matter", matterId) { Name = "MAT-000042" }));
        row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communicationId);
        var entityService = SavedCopy(row);
        entityService
            .Setup(e => e.RetrieveAsync("sprk_matter", matterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId) { ["sprk_mattername"] = "Acme v Globex" });

        using var factory = ExternalCaller(
            Factory(entityService, AccessSource(documentId, AccessRights.Read)), NarrowingCallerMode.Internal, world);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        world.Calls.Should().Be(0, "an internal systemuser makes no narrowing call");
    }

    // ── Scope pin: the conversion belongs to THIS route's filter, not to DocumentAuthorizationFilter ──

    [Fact]
    public async Task OtherRoutesCarryingTheReadFilter_StillAnswer403_ForTheSameUnreadableDocument()
    {
        var documentId = Guid.NewGuid();

        using var factory = Factory(new Mock<IGenericEntityService>(MockBehavior.Strict), AccessSource(documentId, AccessRights.None));
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "GET /{documentId}/identity is unchanged by the email route's fold");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny");
    }

    [Fact]
    public async Task OtherRoutesCarryingTheReadFilter_StillAnswer403_WhenTheNarrowingHidesTheDocument()
    {
        var documentId = Guid.NewGuid();
        var world = InternalOnlyCommunicationWorld(documentId, Guid.NewGuid());

        using var factory = ExternalCaller(
            Factory(new Mock<IGenericEntityService>(MockBehavior.Strict), AccessSource(documentId, AccessRights.Read)),
            NarrowingCallerMode.External, world);
        var response = await AuthenticatedClient(factory).GetAsync($"/api/documents/{documentId:D}/identity");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(InternalPlaneDocumentNarrowingReason);
    }

    // ── Bad input, no credentials, outage ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEmail_WithoutAMessageId_Returns400_AndNeverReadsDataverse()
    {
        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { exchangeItemId = ItemId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(ItemId);
    }

    [Fact]
    public async Task ResolveEmail_WithAnOverLongMessageId_Returns400_WithoutEchoingIt()
    {
        var tooLong = "<" + new string('q', 1200) + "@example.com>";

        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = tooLong });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("qqqqqqqqqq");
    }

    [Fact]
    public async Task ResolveEmail_WhenUnauthenticated_Returns401()
    {
        using var factory = Factory(
            new Mock<IGenericEntityService>(MockBehavior.Strict), new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResolveEmail_WhenDataverseCannotAnswer_Returns503_NeverNotSaved()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));

        using var factory = Factory(entityService, new Mock<IAccessDataSource>(MockBehavior.Strict));
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(MessageId);
    }

    [Fact]
    public async Task ResolveEmail_WhenTheAccessSourceFaultsForAMatchedCopy_Returns503_ByteEqualToTheDataverseOutage_NeverNotSaved()
    {
        var documentId = Guid.NewGuid();
        // The REAL AuthorizationService catches the access source's fault and fails closed with a 403 system_failure;
        // the email filter must answer that as the route's own 503, not fold it into "not saved" (task 120).
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse is unreachable."));
        var entityService = SavedCopy(DocumentEmailIdentityResolutionTests.Row(documentId));

        using var factory = Factory(entityService, accessSource);
        var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });

        var answer = await Answer.OfAsync(response);
        answer.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        answer.Body.Should().NotContain("not_saved").And.NotContain(documentId.ToString("D")).And.NotContain("sdap.access");

        var outage = await DataverseOutageAnswerAsync();
        outage.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        outage.ContentType.Should().Be("application/problem+json");
        answer.ContentType.Should().Be(outage.ContentType);
        // Byte for byte, except the per-request trace id (and anything else request-scoped the problem body carries).
        WithoutRequestScopedFields(answer.Body).Should().Be(WithoutRequestScopedFields(outage.Body));
        answer.Body.Should().Contain("identity_resolution_unavailable");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The route's existing 503 for "Dataverse cannot answer the lookup" — the baseline for the access-fault 503.</summary>
    private static async Task<Answer> DataverseOutageAnswerAsync()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The request channel timed out while waiting for a reply."));

        using var factory = Factory(entityService, new Mock<IAccessDataSource>(MockBehavior.Strict));
        using var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });
        return await Answer.OfAsync(response);
    }

    /// <summary>
    /// Removes the per-request correlation ids (<c>correlationId</c> at the top level and inside <c>extensions</c>, plus
    /// <c>traceId</c> if present) before comparing — the only fields that differ between two otherwise identical answers.
    /// </summary>
    private static string WithoutRequestScopedFields(string body)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        node["extensions"]?.AsObject().Remove("correlationId");
        return node.ToJsonString();
    }

    private const string InternalPlaneDocumentNarrowingReason = Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing.HiddenReasonCode;
    private const int OpenThread = 100000000;

    /// <summary>
    /// Everything a caller can observe about an answer: status, content type, the raw body string and the header names
    /// (the volatile <c>Date</c> excluded). Two answers are indistinguishable exactly when these are equal.
    /// </summary>
    private sealed record Answer(HttpStatusCode Status, string? ContentType, string Body, string Headers)
    {
        public static async Task<Answer> OfAsync(HttpResponseMessage response)
        {
            var names = response.Headers.Select(h => h.Key)
                .Concat(response.Content.Headers.Select(h => h.Key))
                .Where(n => !string.Equals(n, "Date", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            return new Answer(
                response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                await response.Content.ReadAsStringAsync(),
                string.Join(",", names));
        }
    }

    /// <summary>The answer for a key that matches nothing — the baseline every converted refusal must equal.</summary>
    private static async Task<Answer> NoMatchAnswerAsync()
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        using var factory = Factory(entityService, new Mock<IAccessDataSource>(MockBehavior.Strict));
        using var response = await AuthenticatedClient(factory).PostAsJsonAsync(Route, new { internetMessageId = MessageId });
        return await Answer.OfAsync(response);
    }

    /// <summary>The answer is the plain "not saved" 200 — no id, no record, no reason code, no problem details.</summary>
    private static void AssertIsTheNotSavedShape(Answer answer)
    {
        answer.Status.Should().Be(HttpStatusCode.OK);
        answer.ContentType.Should().StartWith("application/json");
        using var json = JsonDocument.Parse(answer.Body);
        var body = json.RootElement;
        body.GetProperty("resolved").GetBoolean().Should().BeFalse();
        body.GetProperty("reason").GetString().Should().Be("not_saved");
        body.GetProperty("documentId").ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("reasonCode", out _).Should().BeFalse();
        body.TryGetProperty("title", out _).Should().BeFalse();
        IdentityJson.RelatedRecordWithheld(body).Should().BeNull();
        if (body.TryGetProperty("relatedRecord", out var related))
            related.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>An app-only world in which the document's communication is internal-only (the narrowing hides the document).</summary>
    private static AppOnlyDocumentWorld InternalOnlyCommunicationWorld(Guid documentId, Guid communicationId)
    {
        var world = new AppOnlyDocumentWorld();
        var thread = Guid.NewGuid();
        world.Thread(thread, OpenThread);
        world.Communication(communicationId, internalOnly: true, thread);
        world.Document(documentId, communication: communicationId);
        return world;
    }

    /// <summary>The same host with a caller stated as <paramref name="mode"/> for the external document narrowing.</summary>
    private static WebApplicationFactory<Program> ExternalCaller(
        DocumentIdentityTestWebAppFactory factory, NarrowingCallerMode mode, AppOnlyDocumentWorld world)
        => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddStatedNarrowingCaller(() => mode, () => world)));

    private static DocumentIdentityTestWebAppFactory Factory(
        Mock<IGenericEntityService> entityService,
        Mock<IAccessDataSource> accessSource,
        Mock<CallerRecordAccessProbe>? probe = null)
        => new(
            // Placeholder: this route never reaches SharePoint Embedded.
            DocumentUrlIdentityResolutionTests.Spe(DocumentUrlIdentityResolutionTests.ResolvedFor("unused-drive", "unused-item")),
            entityService,
            accessSource,
            probe);

    /// <summary>Task 132: a STRICT caller-rights probe — any question a test does not set up fails it.</summary>
    private static Mock<CallerRecordAccessProbe> Probe()
        => new(
            MockBehavior.Strict,
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);

    /// <summary>Task 132: a mailbox-captured archive — no <c>sprk_emailmessageid</c>, no direct slot, linked to its
    /// communication.</summary>
    private static Entity CapturedArchive(Guid documentId, Guid communicationId)
    {
        var row = DocumentEmailIdentityResolutionTests.Row(documentId);
        row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communicationId);
        return row;
    }

    /// <summary>Task 132: the communication's regarding read answers <paramref name="regarding"/>.</summary>
    private static void CommunicationFiledTo(
        Mock<IGenericEntityService> entityService, Guid communicationId, EntityReference regarding)
        => entityService
            .Setup(e => e.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId) { ["sprk_regarding" + regarding.LogicalName[5..]] = regarding });

    /// <summary>Task 132: the same 200 resolved answer, with no related-record card and nothing of the record in it.</summary>
    private static async Task ShouldBeTheSameAnswerWithoutACard(
        HttpResponseMessage response, Guid documentId, string? unexpected, bool withheld)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, "a derived record the caller may not see never fails the answer");
        var raw = await response.Content.ReadAsStringAsync();
        if (unexpected is not null)
            raw.Should().NotContain(unexpected);
        using var json = JsonDocument.Parse(raw);
        var body = json.RootElement;
        body.GetProperty("resolved").GetBoolean().Should().BeTrue();
        body.GetProperty("documentId").GetString().Should().Be(documentId.ToString("D"));
        body.GetProperty("documentName").GetString().Should().Be("RE: Discovery schedule");
        body.GetProperty("fileName").GetString().Should().Be("RE_ Discovery schedule_a1b2.eml");
        body.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("relatedRecord").ValueKind.Should().Be(JsonValueKind.Null);
        // Review F1: a record that exists but is not shown is reported, so the pane never claims "not filed".
        IdentityJson.RelatedRecordWithheld(body).Should().Be(withheld ? true : null);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>The saved-email lookup returns <paramref name="row"/>; nothing else is set up (STRICT).</summary>
    private static Mock<IGenericEntityService> SavedCopy(Entity row)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { row }));
        return entityService;
    }

    private static Mock<IAccessDataSource> AccessSource(Guid documentId, AccessRights rights)
    {
        var accessSource = new Mock<IAccessDataSource>();
        accessSource
            .Setup(a => a.GetUserAccessAsync(
                It.IsAny<string>(), documentId.ToString("D"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSnapshot
            {
                UserId = "caller",
                ResourceId = documentId.ToString("D"),
                AccessRights = rights,
            });
        return accessSource;
    }
}

/// <summary>
/// The fold in <see cref="Sprk.Bff.Api.Api.Filters.DocumentEmailIdentityFilter"/>, driven directly with a stand-in for the
/// authorization step: ONLY a 403 refusal becomes the "not saved" answer; every other status the chain can produce
/// (including the 500 "Authorization Error", which no live host test can reach because the authorization service fails
/// closed with a 403 on its own faults) passes through untouched, and the handler is not reached on a refusal.
/// </summary>
public class DocumentEmailIdentityFilterFoldTests
{
    private static async Task<(object? Result, bool NextRan)> RunAsync(Microsoft.AspNetCore.Http.IResult downstream)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { DocumentEmailIdentityResolutionTests.Row(Guid.NewGuid()) }));
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(entityService.Object)
            .BuildServiceProvider();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        var request = new Sprk.Bff.Api.Models.ResolveEmailIdentityRequest("<abc@mail.example.com>", null);
        var context = Microsoft.AspNetCore.Http.EndpointFilterInvocationContext.Create(http, request);

        var nextRan = false;
        var result = await new Sprk.Bff.Api.Api.Filters.DocumentEmailIdentityFilter().InvokeAsync(context, _ =>
        {
            nextRan = true;
            return ValueTask.FromResult<object?>(downstream);
        });
        return (result, nextRan);
    }

    [Theory]
    [InlineData("sdap.access.deny.insufficient_rights")]
    [InlineData("sdap.access.deny.no_rule")]
    [InlineData("sdap.access.deny.no_caller_token")]
    [InlineData("sdap.access.error.some_other_error")]
    public async Task A403DecisionRefusal_IsFoldedInto_TheSameValueAsNoMatch(string reasonCode)
    {
        await AssertFoldedAsync(reasonCode);
    }

    [Fact]
    public async Task TheNarrowingsHiddenRefusal_IsFoldedInto_TheSameValueAsNoMatch()
    {
        await AssertFoldedAsync(Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing.HiddenReasonCode);
    }

    private static async Task AssertFoldedAsync(string reasonCode)
    {
        var (result, _) = await RunAsync(Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.Forbidden(reasonCode));

        var ok = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.Ok<Sprk.Bff.Api.Models.DocumentIdentityResponse>>().Subject;
        ok.Value.Should().BeEquivalentTo(
            Sprk.Bff.Api.Models.DocumentIdentityResponse.NoIdentity(Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution.ReasonNotSaved));
    }

    [Fact]
    public async Task A403SystemFailure_IsAnsweredAsTheRoutes503_NeverFoldedIntoNotSaved()
    {
        Func<Task> act = () => RunAsync(
            Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.Forbidden(Spaarke.Core.Auth.AuthorizationResult.SystemFailureReasonCode));

        var thrown = (await act.Should().ThrowAsync<Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException>()).Which;
        thrown.Code.Should().Be("identity_resolution_unavailable");
        thrown.Title.Should().Be("Document Identity Unavailable");
        thrown.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task A403SystemFailure_CarriedByAnyTypedValueResult_IsAlsoAnsweredAs503()
    {
        // Not a ProblemHttpResult: the check reads the reason code from ANY IValueHttpResult<ProblemDetails>.
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = 403 };
        problem.Extensions["reasonCode"] = Spaarke.Core.Auth.AuthorizationResult.SystemFailureReasonCode;

        Func<Task> act = () => RunAsync(new ValueProblemResult(problem));

        (await act.Should().ThrowAsync<Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException>())
            .Which.Code.Should().Be("identity_resolution_unavailable");
    }

    private sealed class ValueProblemResult(Microsoft.AspNetCore.Mvc.ProblemDetails problem)
        : Microsoft.AspNetCore.Http.IResult,
          Microsoft.AspNetCore.Http.IStatusCodeHttpResult,
          Microsoft.AspNetCore.Http.IValueHttpResult<Microsoft.AspNetCore.Mvc.ProblemDetails>
    {
        public int? StatusCode => 403;
        public Microsoft.AspNetCore.Mvc.ProblemDetails? Value => problem;
        public Task ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext httpContext) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Any_OtherRefusal_PassesThroughUnchanged_SoAnOutageNeverReadsAsNotSaved(int status)
    {
        var downstream = Microsoft.AspNetCore.Http.Results.Problem(statusCode: status, title: "Authorization Error");

        var (result, _) = await RunAsync(downstream);

        result.Should().BeSameAs(downstream);
    }

    [Fact]
    public async Task ASuccessfulDownstreamAnswer_PassesThroughUnchanged()
    {
        var downstream = Microsoft.AspNetCore.Http.Results.Ok(new { resolved = true });

        var (result, nextRan) = await RunAsync(downstream);

        nextRan.Should().BeTrue();
        result.Should().BeSameAs(downstream);
    }
}
