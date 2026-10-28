using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126, UAC D7 T8), end to end through <c>POST /api/office/save</c>: every
/// <c>sprk_document</c> the Outlook add-in creates from an email is CREATED linked to that email's
/// <c>sprk_communication</c> (<c>sprk_relatedcommunication</c>), or — when no communication can be identified — with its
/// own Access Permission Restricted. A Document save gets neither.
/// </summary>
/// <remarks>
/// <para>Before task 130 the link was a separate best-effort write after the create, made only when a Message-ID lookup
/// found a communication: a single attachment was never linked, the capture's own id was discarded, and an unidentified
/// email's document was created open (Standard) with nothing to restrict it.</para>
/// <para>Module-boundary doubles only (ADR-038 §4): the real route, <c>OfficeService</c>, <c>EmailUploadCaptureService</c>
/// and the DI-built <c>OfficeDocumentPersistence</c>, over <see cref="OfficeVersionSaveWorld"/> plus a communication store
/// that is race-proof on the Message-ID, as the <c>sprk_internetmessageid</c> alternate key is. The arrange is a host with
/// several collaborators, inherent at this KEEP integration path (B15).</para>
/// </remarks>
[Trait("status", "task-130-word-add-in-r1")]
public class OfficeEmailDocumentCommunicationLinkTests
{
    private const string RfcId = "<CAF0-130@mail.example.com>";
    private const string ItemId = "AAMkAGI2TG93-130=";
    private const string Subject = "RE: Privileged settlement position";
    private const string Sender = "counsel@test.com";
    private const int Restricted = 100000002;
    private const string PersistenceCategory = "Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence";
    private const string CallerToken = "test-caller-token";

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>UAC-r2 task 189 (D7 T7a): a Restricted create carries the record that marks it the document's OWN narrowing,
    /// so the reconciler never widens it to the parents' value. Owner round 122: that narrowing is PROVISIONAL, so once the
    /// document is linked to a communication the reconciler releases it.</summary>
    private static void ShouldBeItsOwnRestriction(CreateDocumentRequest created)
    {
        var (record, unreadable) = AccessInheritance.Parse(created.AccessInheritanceRecord);
        unreadable.Should().BeFalse();
        record.Should().NotBeNull("the Restricted is written with its sprk_accessinheritance record in the same create");
        record!.OwnPermission.Should().Be(Restricted);
        record.OwnProvisional.Should().BeTrue(
            "round 122: the save-time Restricted is provisional (AccessInheritance.OwnNarrowingUntilEmailIdentified), " +
            "so a later link releases it");
    }

    [Fact]
    public async Task EmailSave_IsCreatedLinkedToTheCapturesCommunication_EvenWhenTheMessageIdLookupWouldMiss()
    {
        var store = new CommunicationStore { LookupMisses = true };
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var captured = store.ByMessageId[RfcId];
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(captured, "the capture's own id is authoritative — no re-resolve needed");
        created.AccessPermission.Should().BeNull("a linked document keeps the default Access Permission");
        created.AccessInheritanceRecord.Should().BeNull("no own narrowing to record");
        RelatedCommunicationQueued(world).Should().Be(captured.ToString(), "the worker creates the attachment children linked to it");
    }

    [Fact]
    public async Task EmailSave_WhenTheCaptureProducesNothing_TheHostsPersistenceFindsTheCommunicationByMessageId_AndLinksIt()
    {
        // Also the DI check, behaviourally: only a persistence built with its communication seam (the public constructor
        // the host's AddScoped selects) can find the email's existing communication.
        var store = new CommunicationStore { FailCapture = true };
        var existing = store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);

        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(existing);
    }

    [Fact]
    public async Task UnidentifiableEmail_IsCreatedRestricted_LogsAnErrorWithIdsOnly_AndTheSaveSucceeds()
    {
        var store = new CommunicationStore { FailCapture = true }; // nothing captured, and nothing to find
        var world = new OfficeVersionSaveWorld();
        var logs = new RecordingLoggerProvider();
        using var factory = Host(world, store, logs: logs);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "an unidentified email is still saved");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        ShouldBeItsOwnRestriction(created);
        created.RelatedCommunicationId.Should().BeNull();
        RelatedCommunicationQueued(world).Should().BeNull("its attachment children are created Restricted too");

        var documentId = world.Documents.Keys.Should().ContainSingle().Subject;
        var error = logs.Lines.Should().ContainSingle(l => l.Category == PersistenceCategory && l.Level == LogLevel.Error).Subject;
        error.Message.Should().Contain(documentId.ToString()).And.Contain("Message-ID present: True");
        error.Message.Should().NotContain(Subject).And.NotContain(Sender).And.NotContain(RfcId);
    }

    [Fact]
    public async Task AttachmentSave_WithoutAParentEmail_IsCreatedRestricted_AndLogsNoMessageId()
    {
        var world = new OfficeVersionSaveWorld();
        var logs = new RecordingLoggerProvider();
        using var factory = Host(world, new CommunicationStore(), logs: logs);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        ShouldBeItsOwnRestriction(created);
        created.RelatedCommunicationId.Should().BeNull();
        logs.Lines.Should().ContainSingle(l => l.Category == PersistenceCategory && l.Level == LogLevel.Error)
            .Which.Message.Should().Contain("Message-ID present: False", "no Message-ID existed to look up by");
    }

    [Fact]
    public async Task AttachmentSave_ByItemIdParent_WhenTheCaptureFails_IsLinkedByTheParentsRfcMessageId_NotRestricted()
    {
        // The pane sends the Exchange ITEM id as ParentEmailId; Graph reports the parent's RFC Message-ID. The lookup key
        // is the RFC id read from the parent — the item id could never match a sprk_communication.
        var store = new CommunicationStore { FailCapture = true };
        var existing = store.Seed(RfcId);
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(existing);
        created.AccessPermission.Should().BeNull("the parent's communication was identified, so the document is not Restricted");
        store.LookedUpIds.Should().NotContain(ItemId, "an Exchange item id is never a Message-ID lookup key");
    }

    [Fact]
    public async Task AttachmentSave_ByItemIdParent_NothingToFind_IsRestricted_AndLogsAMessageIdWasPresent()
    {
        var store = new CommunicationStore { FailCapture = true };
        var world = new OfficeVersionSaveWorld();
        var logs = new RecordingLoggerProvider();
        using var factory = Host(world, store, new ParentEmailGraph(), logs: logs);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        ShouldBeItsOwnRestriction(created);
        logs.Lines.Should().ContainSingle(l => l.Category == PersistenceCategory && l.Level == LogLevel.Error)
            .Which.Message.Should().Contain("Message-ID present: True", "the parent's RFC Message-ID was the key looked up");
        store.LookedUpIds.Should().Equal(RfcId);
    }

    [Fact]
    public async Task AttachmentSave_ByRfcParentId_IsLinkedToTheCanonicalCommunication_WithoutReadingGraphOrCapturing()
    {
        var store = new CommunicationStore();
        var canonical = store.Seed(RfcId);
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: RfcId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(canonical);
        created.AccessPermission.Should().BeNull("an identified email's attachment is not Restricted");
        graph.RequestedPaths.Should().BeEmpty("Graph cannot address a message by its RFC Message-ID");
        store.Inserts.Should().Be(0);

        // A client-sent Message-ID only links (K1); it never files a communication — filing needs the parent read from the
        // caller's own mailbox, and nothing here asks for the caller's rights on it.
        store.CommunicationWrites.Should().BeEmpty();
        store.Probe.Verify(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmailSave_FromAnOlderAddInSendingTheItemIdAsMessageId_NeverLooksUpByTheItemId_AndIsRestricted()
    {
        // An add-in older than task 121 put the Exchange item id in InternetMessageId (and sent no ExchangeItemId).
        var store = new CommunicationStore { FailCapture = true };
        var world = new OfficeVersionSaveWorld();
        var logs = new RecordingLoggerProvider();
        using var factory = Host(world, store, logs: logs);
        var request = EmailSave();
        request = new SaveRequest
        {
            ContentType = request.ContentType,
            TargetEntity = request.TargetEntity,
            Email = new EmailMetadata
            {
                Subject = Subject,
                SenderEmail = Sender,
                SentDate = request.Email!.SentDate,
                InternetMessageId = ItemId,
                Body = "<p>The email text</p>",
                IsBodyHtml = true,
                IsNameSystemDerived = true,
            },
        };

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.LookedUpIds.Should().BeEmpty("an Exchange item id is never a Message-ID lookup key");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(Restricted);
        ShouldBeItsOwnRestriction(created);
        logs.Lines.Should().ContainSingle(l => l.Category == PersistenceCategory && l.Level == LogLevel.Error)
            .Which.Message.Should().Contain("Message-ID present: False");
    }

    [Fact]
    public async Task AttachmentSave_OfAnEmailNotYetCaptured_IsLinkedToTheCommunicationItsParentCaptureCreates()
    {
        var store = new CommunicationStore { LookupMisses = true };
        var graph = new ParentEmailGraph();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, graph);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1, "the parent email is captured, as an email save would capture it");
        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(store.ByMessageId[RfcId]);
        created.AccessPermission.Should().BeNull();
        graph.RequestedPaths.Should().ContainSingle().Which.Should().EndWith($"/me/messages/{ItemId}");

        // Captured as an email save would capture it: with its attachment list (read without the bytes).
        graph.RequestedQueries.Should().ContainSingle().Which.Should().Contain("$expand=attachments($select=");
        var communication = store.CaptureCreates.Should().ContainSingle().Subject;
        communication["sprk_hasattachments"].Should().Be(true);
        communication["sprk_attachmentcount"].Should().Be(1);
    }

    [Fact]
    public async Task EmailThenItsAttachment_ReuseOneCommunication_TheSecondCaptureInsertsNothing()
    {
        var store = new CommunicationStore();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/office/save", EmailSave())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.Inserts.Should().Be(1, "the capture is race-proof on the Message-ID: the attachment's parent reconciles");
        world.CreatedDocumentRequests.Should().HaveCount(2)
            .And.OnlyContain(r => r.RelatedCommunicationId == store.ByMessageId[RfcId] && r.AccessPermission == null);
    }

    [Fact]
    public async Task AttachmentSave_OfAnUnfiledCapturedEmail_FilesThatEmailToTheRecord_AskingWithTheCallersToken()
    {
        // Owner decision 2026-10-20: saving one attachment files its parent email exactly as saving the email would —
        // task 121's rule for an email captured earlier and filed to nothing.
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = AttachmentSave(parentEmailId: ItemId);

        (await client.PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var write = store.CommunicationWrites.Should().ContainSingle().Subject;
        write.Id.Should().Be(communicationId);
        ((EntityReference)write.Fields["sprk_regardingmatter"]).Id.Should().Be(request.TargetEntity!.EntityId);
        store.Probe.Verify(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", communicationId, It.IsAny<CancellationToken>()), Times.Once);
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(communicationId);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachmentSave_OfAnAlreadyFiledEmail_NeverMovesIt()
    {
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        store.FiledElsewhere.Add(communicationId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        (await client.PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        store.CommunicationWrites.Should().BeEmpty("a communication already filed is never moved by a save");
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(communicationId);
    }

    [Fact]
    public async Task DocumentSave_GetsNeitherALinkNorRestricted()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);

        (await factory.CreateClient().PostAsJsonAsync(
                "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Encoding.UTF8.GetBytes("brief"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = world.CreatedDocumentRequests.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().BeNull();
        created.AccessPermission.Should().BeNull();
        RelatedCommunicationQueued(world).Should().BeNull();
    }

    // ── Task 131 (#2171): the filing outcome is on the 202 response ───────────────────────────────────────────────────

    [Fact]
    public async Task EmailSave_ReconciledToAnUnfiledCommunicationTheCallerMayNotWrite_Reports_NotPermitted_AndStillSaves()
    {
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("NotPermitted");
        store.CommunicationWrites.Should().BeEmpty("the communication is left unfiled");
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(communicationId);
    }

    [Fact]
    public async Task AttachmentSave_WhoseParentEmailTheCallerMayNotWrite_Reports_NotPermitted()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("NotPermitted");
        store.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task EmailSave_FilingAnUnfiledCommunication_Reports_Filed()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("Filed");
        store.CommunicationWrites.Should().ContainSingle();
    }

    [Fact]
    public async Task AttachmentSave_FilingItsUnfiledParentEmail_Reports_Filed()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("Filed");
    }

    [Fact]
    public async Task EmailSave_WhenTheFilingFails_Reports_Failed_AndTheSaveIsStillAccepted()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        store.FailFilingRead = true;
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("Failed");
        store.CommunicationWrites.Should().BeEmpty();
        world.CreatedDocumentRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task EmailSave_OfAnAlreadyFiledCommunication_Reports_AlreadyFiled()
    {
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        store.FiledElsewhere.Add(communicationId);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("AlreadyFiled");
        store.CommunicationWrites.Should().BeEmpty("a communication already filed is never moved");
    }

    [Fact]
    public async Task EmailSave_ThatCreatesItsOwnCommunication_CarriesNoFilingOutcome()
    {
        var store = new CommunicationStore(); // nothing seeded: the capture inserts the communication
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        store.Inserts.Should().Be(1);
        await AssertNoEmailFilingAsync(response);
    }

    [Fact]
    public async Task EmailSave_NamingNoRecord_CarriesNoFilingOutcome()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = EmailSave() with { TargetEntity = null };

        var response = await client.PostAsJsonAsync("/api/office/save", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await AssertNoEmailFilingAsync(response);
    }

    [Fact]
    public async Task DocumentSave_CarriesNoFilingOutcome()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);

        var response = await client.PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", Encoding.UTF8.GetBytes("brief")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await AssertNoEmailFilingAsync(response);
    }

    [Fact]
    public async Task DuplicateEmailSave_ReplaysNoFilingOutcome_ItNeverClaimsAFilingThatDidNotHappen()
    {
        var store = new CommunicationStore();
        store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = EmailSave();

        var first = await PostWithKeyAsync(client, request, "first");
        (await EmailFilingWireAsync(first)).Should().Be("NotPermitted", "precondition: the first save reported the refusal");
        var retry = await PostWithKeyAsync(client, request, "second");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync<SaveResponse>())!.Duplicate.Should().BeTrue();
        await AssertNoEmailFilingAsync(retry);
    }

    [Fact]
    public async Task RepeatOfAnEmailSave_UnderTheSameIdempotencyKey_ReplaysTheOriginalOutcome_AndFilesNothingAgain()
    {
        // What a real client does on a retry: the same key. Within the idempotency window the cached 202 is replayed —
        // emailFiling included — and the save does not run again, so no second filing attempt is made.
        var store = new CommunicationStore();
        var communicationId = store.Seed(RfcId);
        store.DenyWrite();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        var request = EmailSave();

        var first = await PostWithKeyAsync(client, request, "same-key");
        (await EmailFilingWireAsync(first)).Should().Be("NotPermitted", "precondition: the first save reported the refusal");
        var repeat = await PostWithKeyAsync(client, request, "same-key");

        repeat.StatusCode.Should().Be(HttpStatusCode.Accepted, "the original response is replayed as it was");
        (await EmailFilingWireAsync(repeat)).Should().Be("NotPermitted", "the replay carries the original filing outcome");
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", communicationId, It.IsAny<CancellationToken>()),
            Times.Once, "the repeat ran no second filing attempt");
        store.CommunicationWrites.Should().BeEmpty();
        world.Jobs.Should().ContainSingle("the repeat created no second job");
    }

    [Theory]
    [InlineData(ReconciledFilingResult.NotApplicable, null)]
    [InlineData(ReconciledFilingResult.Filed, EmailFilingOutcome.Filed)]
    [InlineData(ReconciledFilingResult.AlreadyFiled, EmailFilingOutcome.AlreadyFiled)]
    [InlineData(ReconciledFilingResult.NotPermitted, EmailFilingOutcome.NotPermitted)]
    [InlineData(ReconciledFilingResult.Failed, EmailFilingOutcome.Failed)]
    public void EveryFilingResult_MapsToItsWireOutcome_AndNotApplicableToNone(ReconciledFilingResult result, EmailFilingOutcome? expected) =>
        OfficeService.ToEmailFilingOutcome(result).Should().Be(expected);

    // ── UAC task 194b R4-2: the communication is in the document's owner question ─────────────────────────────────────

    private static readonly Guid SecureTeam = Guid.Parse("194b0042-0000-4000-8000-00000000005e");

    /// <summary>A secure-if-any stand-in: the Secure team when the question names <paramref name="secureCommunication"/>.</summary>
    private static RecordOwnershipResolverDouble SecureWhenAskedAbout(Func<Guid> secureCommunication) => new()
    {
        AnswerFor = q => q.Parents.Contains(new RecordOwnershipParent("sprk_communication", secureCommunication()))
            ? RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true }
            : null,
    };

    [Fact]
    [Trait("status", "task-194b-uac-r2")]
    public async Task EmailSave_ToAnOrdinaryMatter_OfASecureCommunication_IsOwnedByTheSecureTeam_AndHandsItToTheWorker()
    {
        var store = new CommunicationStore();
        var secure = store.Seed(RfcId); // captured earlier, on a secure matter: the capture reconciles to it
        store.FiledElsewhere.Add(secure);
        var ownership = SecureWhenAskedAbout(() => secure);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, documentOwnership: ownership);
        var request = EmailSave();

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        world.CreatedDocumentOwningTeams.Should().Equal(SecureTeam);
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(secure);
        Guid.Parse(OfficeVersionSaveWorld.PayloadValue(world.FinalizationPayloads.Should().ContainSingle().Subject, "OwningTeamId")!)
            .Should().Be(SecureTeam, "the worker's attachment children take the same team");
        ownership.Requests.Should().ContainSingle(q => q.Parents.Contains(new RecordOwnershipParent("sprk_communication", secure)))
            .Which.Parents.Should().Equal(
                new RecordOwnershipParent("sprk_matter", request.TargetEntity!.EntityId),
                new RecordOwnershipParent("sprk_communication", secure));
        store.LookedUpIds.Should().BeEmpty("the capture's id is reused: no read");
    }

    [Fact]
    [Trait("status", "task-194b-uac-r2")]
    public async Task AttachmentSave_WhenTheCaptureFails_ReadsTheSecureCommunicationByMessageId_AndIsOwnedByTheSecureTeam()
    {
        var store = new CommunicationStore { FailCapture = true };
        var secure = store.Seed(RfcId);
        var ownership = SecureWhenAskedAbout(() => secure);
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, new ParentEmailGraph(), documentOwnership: ownership);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", AttachmentSave(parentEmailId: ItemId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        world.CreatedDocumentOwningTeams.Should().Equal(SecureTeam);
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(secure);
        store.LookedUpIds.Should().Equal(new[] { RfcId }, "one read, before the owner question; the create reuses its answer");
    }

    [Fact]
    [Trait("status", "task-194b-uac-r2")]
    public async Task EmailSave_WhenTheCommunicationCannotBeRead_IsRefused_AndWritesNoJobUploadOrDocument()
    {
        var store = new CommunicationStore { FailCapture = true, LookupFails = true };
        var ownership = new RecordOwnershipResolverDouble();
        var world = new OfficeVersionSaveWorld();
        using var factory = Host(world, store, documentOwnership: ownership);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", EmailSave());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ownership.Requests.Should().NotContain(q => q.Parents.Any(p => p.EntityLogicalName == "sprk_matter"),
            "never a question about the association alone");
        world.ShouldHaveWrittenNothing();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, SaveRequest body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Idempotency-Key", key);
        return client.SendAsync(message);
    }

    /// <summary>The wire value of <c>emailFiling</c> in the response body, or <c>null</c> when the property is absent.</summary>
    private static async Task<string?> EmailFilingWireAsync(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("emailFiling", out var value) ? value.GetString() : null;
    }

    /// <summary>The property is omitted from the JSON — not merely null — so an older client sees an unchanged body.</summary>
    private static async Task AssertNoEmailFilingAsync(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.TryGetProperty("emailFiling", out _).Should().BeFalse();
    }

    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Email = new EmailMetadata
        {
            Subject = Subject,
            SenderEmail = Sender,
            SentDate = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            ExchangeItemId = ItemId,
            Body = "<p>The email text</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
        },
    };

    private static SaveRequest AttachmentSave(string? parentEmailId) => new()
    {
        ContentType = SaveContentType.Attachment,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Attachment = new AttachmentMetadata
        {
            AttachmentId = $"att-{Guid.NewGuid():N}",
            FileName = "Settlement terms.pdf",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"%PDF-1.7 {Guid.NewGuid()}")),
            ParentEmailId = parentEmailId,
        },
    };

    private static string? RelatedCommunicationQueued(OfficeVersionSaveWorld world) =>
        OfficeVersionSaveWorld.PayloadValue(world.FinalizationPayloads.Should().ContainSingle().Subject, "RelatedCommunicationId");

    /// <summary>
    /// The world's host with the email capture running over <paramref name="store"/> (the same construction
    /// <c>OfficeEmailSaveMessageKeyTests</c> uses), the store as the persistence's communication seam, and — when given —
    /// the parent email Graph returns for an attachment save.
    /// </summary>
    private static WebApplicationFactory<Program> Host(
        OfficeVersionSaveWorld world, CommunicationStore store, ParentEmailGraph? graph = null, RecordingLoggerProvider? logs = null,
        RecordOwnershipResolverDouble? documentOwnership = null)
    {
        var dataverse = store.Dataverse;
        var ownership = new RecordOwnershipResolverDouble();
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(dataverse.Object) }, dataverse.Object, dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), ownership, NullLogger<IncomingAssociationResolver>.Instance);

        return new OfficeVersionSaveTestWebAppFactory(world).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<EmailUploadCaptureService>();
            services.AddSingleton(new EmailUploadCaptureService(
                dataverse.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), ownership,
                NullLogger<EmailUploadCaptureService>.Instance));
            services.RemoveAll<ICommunicationDataverseService>();
            services.AddSingleton<ICommunicationDataverseService>(dataverse.Object);
            services.RemoveAll<ReconciledEmailFiling>();
            services.AddScoped(sp => new ReconciledEmailFiling(
                resolver, dataverse.Object, store.Probe.Object, sp.GetRequiredService<SecureChildReconciler>(),
                sp.GetRequiredService<CoreAncestorRestamper>(), NullLogger<ReconciledEmailFiling>.Instance));

            if (graph is not null)
            {
                services.RemoveAll<OfficeEmailEnricher>();
                services.AddScoped(_ => new OfficeEmailEnricher(graph.Factory, NullLogger<OfficeEmailEnricher>.Instance));
            }

            if (logs is not null)
                services.AddSingleton<ILoggerProvider>(logs);

            if (documentOwnership is not null)
            {
                services.RemoveAll<IRecordOwnershipResolver>();
                services.AddSingleton<IRecordOwnershipResolver>(documentOwnership);
            }
        }));
    }

    /// <summary>
    /// <c>sprk_communication</c> rows keyed by Message-ID, race-proof as the alternate key makes the real create: a second
    /// create for a known id reconciles to the existing row (<c>WasDuplicate</c>) and inserts nothing.
    /// </summary>
    private sealed class CommunicationStore
    {
        private readonly object _gate = new();

        public CommunicationStore()
        {
            Dataverse
                .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DataverseEntity entity, string? messageId, CancellationToken _) => Create(entity, messageId));
            Dataverse
                .Setup(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string messageId, CancellationToken _) => Find(messageId));

            // Task 121's filing of a reconciled communication: its filing columns, and the engine's write to it.
            Dataverse
                .Setup(d => d.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    if (FailFilingRead)
                        throw new InvalidOperationException("Test: Dataverse could not read the communication.");
                    var row = new DataverseEntity("sprk_communication", id);
                    if (FiledElsewhere.Contains(id))
                        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
                    return row;
                });
            Dataverse
                .Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, fields, _) =>
                {
                    lock (_gate)
                    {
                        CommunicationWrites.Add((id, fields));
                    }
                })
                .Returns(Task.CompletedTask);

            // The caller may change any communication — but only when asked with the caller's own token.
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AccessRights.None);
            Probe.Setup(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AccessRights.Read | AccessRights.Write);
        }

        public Mock<IDataverseService> Dataverse { get; } = new();

        public Mock<CallerRecordAccessProbe> Probe { get; } = new(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };

        /// <summary>Communications already filed to a record (task 121 never moves these).</summary>
        public HashSet<Guid> FiledElsewhere { get; } = [];

        /// <summary>Every write to a <c>sprk_communication</c> (the filing engine's), in order.</summary>
        public List<(Guid Id, Dictionary<string, object> Fields)> CommunicationWrites { get; } = [];

        /// <summary>Task 131: reading a communication's filing throws, so a filing attempt Fails.</summary>
        public bool FailFilingRead { get; set; }

        /// <summary>Task 131: even the caller's own token holds no Write on a communication (a secure thread).</summary>
        public void DenyWrite() =>
            Probe.Setup(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AccessRights.Read);

        public Dictionary<string, Guid> ByMessageId { get; } = new(StringComparer.Ordinal);

        public int Inserts { get; private set; }

        /// <summary>The capture's create throws, so the capture is skipped (it is best-effort and returns nothing).</summary>
        public bool FailCapture { get; init; }

        /// <summary>The document create's Message-ID lookup finds nothing, whatever the store holds.</summary>
        public bool LookupMisses { get; init; }

        /// <summary>UAC task 194b R4-2: the Message-ID lookup throws (Dataverse could not be read).</summary>
        public bool LookupFails { get; init; }

        /// <summary>A communication that already exists for <paramref name="messageId"/> (captured from a mailbox earlier).</summary>
        public Guid Seed(string messageId)
        {
            lock (_gate)
            {
                return ByMessageId[messageId] = Guid.NewGuid();
            }
        }

        /// <summary>Every <c>sprk_communication</c> a capture asked to create (inserted or reconciled), in order.</summary>
        public List<DataverseEntity> CaptureCreates { get; } = [];

        private (Guid Id, bool WasDuplicate) Create(DataverseEntity entity, string? messageId)
        {
            if (FailCapture)
                throw new InvalidOperationException("Test: Dataverse refused the sprk_communication create.");

            lock (_gate)
            {
                CaptureCreates.Add(entity);
                if (messageId is not null && ByMessageId.TryGetValue(messageId, out var existing))
                    return (existing, true);

                Inserts++;
                var id = Guid.NewGuid();
                if (messageId is not null)
                    ByMessageId[messageId] = id;
                return (id, false);
            }
        }

        /// <summary>Every Message-ID the document create's lookup was asked for, in order.</summary>
        public List<string> LookedUpIds { get; } = [];

        private DataverseEntity? Find(string messageId)
        {
            lock (_gate)
            {
                LookedUpIds.Add(messageId);
                if (LookupFails)
                    throw new InvalidOperationException("Test: Dataverse could not read the communication by Message-ID.");
                return !LookupMisses && ByMessageId.TryGetValue(messageId, out var id) ? new DataverseEntity("sprk_communication", id) : null;
            }
        }
    }

    /// <summary>Graph answering every message read with the attachment's parent email (Message-ID <see cref="RfcId"/>).</summary>
    private sealed class ParentEmailGraph : HttpMessageHandler
    {
        public ParentEmailGraph()
        {
            var client = new GraphServiceClient(new HttpClient(this) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") });
            var factory = new Mock<IGraphClientFactory>();
            factory.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Factory = factory.Object;
        }

        public IGraphClientFactory Factory { get; }

        public List<string> RequestedPaths { get; } = [];

        public List<string> RequestedQueries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (RequestedPaths)
            {
                RequestedPaths.Add(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
                RequestedQueries.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            }

            var json = $$"""
            { "id": "{{ItemId}}", "subject": "{{Subject}}", "internetMessageId": "{{RfcId}}", "conversationId": "conv-130",
              "sentDateTime": "2026-10-08T09:30:00Z",
              "from": { "emailAddress": { "address": "{{Sender}}", "name": "Counsel" } },
              "toRecipients": [ { "emailAddress": { "address": "associate@test.com", "name": "Associate" } } ],
              "body": { "contentType": "html", "content": "<p>The email text</p>" }, "hasAttachments": true,
              "attachments": [ { "@odata.type": "#microsoft.graph.fileAttachment", "id": "att-130", "name": "Settlement terms.pdf",
                                 "size": 2048, "contentType": "application/pdf", "isInline": false } ] }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
