using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r1 (verifier item 5) — the writer families that had no test asserting their owner
/// or their refusal, each driven through its REAL writer and the REAL <see cref="RecordOwnershipResolver"/> over
/// <see cref="Directory"/> (business units, the Secure BU's named team beside its default-team decoy, parent rows).
/// The writer's own Dataverse writes are captured at its module boundary.
/// </summary>
/// <remarks>
/// <para>Families: outbound send (<see cref="CommunicationService"/> — owner decided BEFORE the send, verifier item 10);
/// inbound email (<see cref="IncomingCommunicationProcessor"/>'s filing step — the FR-26 stamps reach the resolver and an
/// undeterminable filing HOLDS, item 4); upload capture (<see cref="EmailUploadCaptureService"/>, item 4); spend signals
/// (<see cref="SignalEvaluationService"/>); generated to-dos (<see cref="TodoGenerationService"/>); and the analysis
/// create route (<c>POST /api/ai/analysis/create</c>).</para>
/// <para>Not driven end to end here, each for a stated reason (task note §8): the inbound attachment / .eml writes (they
/// sit behind the Graph message fetch, whose SDK request builders cannot be doubled — InboundPipelineTests skips the same
/// path); <see cref="SpendSnapshotService"/> and <c>DocumentCheckoutService</c> (they write through an unwrapped
/// <c>ServiceClient</c> / a raw <see cref="HttpClient"/>, whose only offline doubles are the transport mocks ADR-038 B1
/// rules out). Their owner writes are pinned per SITE by <c>RecordOwnerAssignmentCensusTests</c>.</para>
/// </remarks>
[Trait("status", "new")]
public class SecureChildOwnershipWriterTests
{
    private static readonly Guid SecureMatter = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OrdinaryMatter = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid FlaggedNotIsolatedProject = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OrdinaryInvoice = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>
    /// The standard world, plus an INVOICE that is still owned in an ordinary business unit (written before task 146) but
    /// whose core ancestor — its FR-26 stamp — is a secure matter.
    /// </summary>
    private static Directory World() => Directory.Standard()
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedNotIsolatedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_invoice", OrdinaryInvoice, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter) });

    // =====================================================================================
    // Outbound send — CommunicationService (shared mailbox and message)
    // =====================================================================================

    [Fact]
    public async Task Send_FiledToASecureMatter_RecordsTheCommunicationOwnedByTheNamedSecureTeam()
    {
        var harness = new SendHarness(World().Resolver());

        await harness.Service.SendAsync(SendFiledTo("sprk_matter", SecureMatter));

        harness.Sends.Should().ContainSingle();
        var communication = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task Send_ByASignedInCaller_RecordsThemAsThePersonWhoAsked_OnTheAppCreatedCommunication()
    {
        // c1-r1 (owner round 13 item 9): the communication is created by the application (createdby = the app), so the
        // signed-in caller — looked up by object id — is recorded as sprk_createdbyperson. Owner unchanged.
        var harness = new SendHarness(World().Resolver());

        await harness.Service.SendAsync(SendFiledTo("sprk_matter", SecureMatter), harness.SignedInUser());

        var communication = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
        communication.Attributes.Should().ContainKey("sprk_createdbyperson");
        communication.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Directory.CallerUserId);
    }

    [Fact]
    public async Task Send_FiledToAFlaggedButNotIsolatedProject_Is409BeforeTheSend_AndNothingIsSentOrCreated()
    {
        // Verifier item 10: the owner used to be decided after the email had gone, so a refusal left a delivered email
        // with no record behind a success response.
        var harness = new SendHarness(World().Resolver());

        var act = () => harness.Service.SendAsync(SendFiledTo("sprk_project", FlaggedNotIsolatedProject));

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(409);
        problem.Code.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        harness.Sends.Should().BeEmpty("nothing is sent when the record cannot be owned");
        harness.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_WhenOwnerResolutionFaults_PropagatesTheFault_AndNothingIsSent()
    {
        var harness = new SendHarness(World().Resolver(fault: new TimeoutException("throttled")));

        var act = () => harness.Service.SendAsync(SendFiledTo("sprk_matter", SecureMatter));

        await act.Should().ThrowAsync<TimeoutException>("a fault is the request's 5xx, never a refusal");
        harness.Sends.Should().BeEmpty();
        harness.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task MessageSend_FiledToAFlaggedButNotIsolatedProject_Is409_AndNoMessageReachesTheChannel()
    {
        var harness = new SendHarness(World().Resolver());
        var request = SendFiledTo("sprk_project", FlaggedNotIsolatedProject) with { CommunicationType = CommunicationType.Message };

        var act = () => harness.Service.SendAsync(request, harness.SignedInUser());

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(409);
        harness.Sends.Should().BeEmpty("a refused chat message is not sent — so no echo can persist it unfiled");
        harness.Created.Should().BeEmpty();
    }

    // =====================================================================================
    // Inbound email — IncomingCommunicationProcessor's filing step (owner decided before the create)
    // =====================================================================================

    [Fact]
    public async Task Inbound_FiledToAnInvoiceUnderASecureMatter_IsOwnedByTheNamedTeam_ThroughTheStamp()
    {
        // Verifier item 4: the invoice itself is still owned in an ordinary BU; only its FR-26 stamp names the secure
        // matter. The stamp used to be derived after the owner was decided, so the email took the invoice's team.
        var processor = InboundProcessor(
            World().Resolver(), CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", SecureMatter)),
            FiledTo("sprk_regardinginvoice", "sprk_invoice", OrdinaryInvoice));

        var (_, owner, _) = await processor.ResolveInboundFilingAsync(Envelope(), account: null, "graph-1", CancellationToken.None);

        owner.OwningTeamId.Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task Inbound_OwnedFromItsFiling_IsCreatedWithThatFiling_NeverFiledUnderNothing()
    {
        // r2 (verifier item 9): the email used to be created owned by the Secure team and filed in a SEPARATE, non-fatal
        // write — a failure there left a row owned by a memberless team, filed under nothing, that nobody can see. The
        // create itself now carries the regarding lookups and the FR-26 stamp beside the owner.
        var created = new List<DataverseEntity>();
        var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Strict);
        communications
            .Setup(c => c.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, string?, CancellationToken>((e, _, _) => created.Add(e))
            .ReturnsAsync((Guid.NewGuid(), false));
        var processor = InboundProcessor(
            World().Resolver(), CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", SecureMatter)),
            FiledTo("sprk_regardinginvoice", "sprk_invoice", OrdinaryInvoice), communications.Object);

        var (_, owner, filing) = await processor.ResolveInboundFilingAsync(Envelope(), account: null, "graph-4", CancellationToken.None);
        await processor.CreateCommunicationRecordAsync(
            new Microsoft.Graph.Models.Message { Subject = "Invoice 1042", InternetMessageId = "<m-146@vendor.example>" },
            "intake@contoso.com", "graph-4", owner, filing, CancellationToken.None);

        var row = created.Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
        row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(OrdinaryInvoice);
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(SecureMatter);
    }

    [Fact]
    public async Task Inbound_WhenItsFilingCannotBeBuiltForTheCreate_IsHeld_AndNothingIsCreated()
    {
        // The owner was decided (the first stamp derivation succeeded); building the filing the create carries failed.
        // R3: held — never a team-owned email filed under nothing.
        var processor = InboundProcessor(
            World().Resolver(), AncestorsOnceThenFailing(("sprk_regardingmatter", SecureMatter)),
            FiledTo("sprk_regardinginvoice", "sprk_invoice", OrdinaryInvoice));

        var act = () => processor.ResolveInboundFilingAsync(Envelope(), account: null, "graph-5", CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Fact]
    public async Task Inbound_WhenTheFilingCannotBeDetermined_IsHeld_NotCreatedUnfiled()
    {
        // R3: "secure parent cannot be determined → HELD". The stamp derivation failing used to leave the email unfiled
        // and creator-owned in an ordinary business unit.
        var processor = InboundProcessor(
            World().Resolver(), CoreAncestorResolverFixtures.Failing(),
            FiledTo("sprk_regardinginvoice", "sprk_invoice", OrdinaryInvoice));

        var act = () => processor.ResolveInboundFilingAsync(Envelope(), account: null, "graph-2", CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
    }

    [Fact]
    public async Task Inbound_FiledToAFlaggedButNotIsolatedProject_IsHeldWithTheRefusal()
    {
        var processor = InboundProcessor(
            World().Resolver(), CoreAncestorResolverFixtures.Inert(),
            FiledTo("sprk_regardingproject", "sprk_project", FlaggedNotIsolatedProject));

        var act = () => processor.ResolveInboundFilingAsync(Envelope(), account: null, "graph-3", CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
    }

    // =====================================================================================
    // Upload capture — EmailUploadCaptureService
    // =====================================================================================

    [Fact]
    public async Task UploadCapture_FiledToAnInvoiceUnderASecureMatter_CreatesTheCommunicationOwnedByTheNamedTeam()
    {
        var (capture, created) = UploadCapture(
            World().Resolver(), CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", SecureMatter)));

        var id = await capture.CaptureAsync(EmailSave("invoice", OrdinaryInvoice), "user-1", CancellationToken.None);

        id.Should().NotBeNull();
        var row = created.Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Directory.SecureNamedTeam, "the stamp's secure matter reaches the resolver (verifier item 4)");
        // r2 (verifier item 9): a capture owned from its filing is created WITH that filing. (Asserted as present first —
        // a null-conditional `?.Id.Should()` would skip the assertion entirely when the column is missing.)
        row.Attributes.Should().ContainKey("sprk_regardinginvoice");
        row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(OrdinaryInvoice);
        row.Attributes.Should().ContainKey("sprk_regardingmatter");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(SecureMatter);
    }

    [Fact]
    public async Task UploadCapture_RecordsTheSavingUserAsThePersonWhoAsked()
    {
        // c1-r1 (owner round 13 item 9): the Office user who saved the email, by object id.
        var (capture, created) = UploadCapture(
            World().Resolver(), CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", SecureMatter)));

        await capture.CaptureAsync(EmailSave("invoice", OrdinaryInvoice), Directory.CallerOid.ToString(), CancellationToken.None);

        var row = created.Should().ContainSingle().Subject;
        row.Attributes.Should().ContainKey("sprk_createdbyperson");
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Directory.CallerUserId);
    }

    [Fact]
    public async Task UploadCapture_WhenItsFilingCannotBeBuiltForTheCreate_IsSkipped_AndNothingIsCreated()
    {
        var (capture, created) = UploadCapture(World().Resolver(), AncestorsOnceThenFailing(("sprk_regardingmatter", SecureMatter)));

        var id = await capture.CaptureAsync(EmailSave("invoice", OrdinaryInvoice), "user-1", CancellationToken.None);

        id.Should().BeNull();
        created.Should().BeEmpty("never a team-owned capture filed under nothing (r2, verifier item 9)");
    }

    [Fact]
    public async Task UploadCapture_WhenTheFilingCannotBeDetermined_IsSkipped_NotCapturedUnfiled()
    {
        var (capture, created) = UploadCapture(World().Resolver(), CoreAncestorResolverFixtures.Failing());

        var id = await capture.CaptureAsync(EmailSave("invoice", OrdinaryInvoice), "user-1", CancellationToken.None);

        id.Should().BeNull("this best-effort writer's refusal is a skipped capture");
        created.Should().BeEmpty();
    }

    // =====================================================================================
    // Spend signals — SignalEvaluationService
    // =====================================================================================

    [Fact]
    public async Task SignalEvaluation_ForASecureMatter_UpsertsSignalsOwnedByTheNamedTeam()
    {
        var (service, upserts) = SignalEvaluation(World().Resolver(), SecureMatter);

        var count = await service.EvaluateAsync(SecureMatter, CancellationToken.None);

        count.Should().BeGreaterThan(0);
        upserts.Should().NotBeEmpty().And.OnlyContain(f =>
            (string)f["ownerid@odata.bind"]! == $"/teams({Directory.SecureNamedTeam})");
    }

    [Fact]
    public async Task SignalEvaluation_ForAnOrdinaryMatter_UpsertsSignalsOwnedByThatMattersTeam()
    {
        var (service, upserts) = SignalEvaluation(World().Resolver(), OrdinaryMatter);

        await service.EvaluateAsync(OrdinaryMatter, CancellationToken.None);

        upserts.Should().NotBeEmpty().And.OnlyContain(f =>
            (string)f["ownerid@odata.bind"]! == $"/teams({Directory.ChildTeam})");
    }

    [Fact]
    public async Task SignalEvaluation_ForAMissingMatter_IsSkipped_AndUpsertsNothing()
    {
        var missing = Guid.NewGuid();
        var (service, upserts) = SignalEvaluation(World().Resolver(), missing);

        var count = await service.EvaluateAsync(missing, CancellationToken.None);

        count.Should().Be(0);
        upserts.Should().BeEmpty("a background writer's refusal is a skipped row, never an app-owned one");
    }

    // =====================================================================================
    // Generated to-dos — TodoGenerationService
    // =====================================================================================

    [Fact]
    public async Task GeneratedTodo_RegardingASecureMatter_IsOwnedByTheNamedTeam()
    {
        var (service, created) = TodoGeneration(World().Resolver());

        await service.CreateTodoAsync(
            "Budget Alert: Acme", regardingEntityName: "sprk_matter", regardingId: SecureMatter,
            regardingDisplayName: "Acme", ct: CancellationToken.None);

        created.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task GeneratedTodo_FromAnEventOfASecureMatter_IsOwnedByTheNamedTeam()
    {
        var eventId = Guid.NewGuid();
        var (service, created) = TodoGeneration(World()
            .WithRecord("sprk_event", eventId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
            .Resolver());

        await service.CreateTodoAsync(
            "Overdue: Hearing", ownershipSource: new RecordOwnershipParent("sprk_event", eventId), ct: CancellationToken.None);

        created.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid").Id
            .Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task GeneratedTodo_RegardingAFlaggedButNotIsolatedProject_IsRefused_AndCreatesNothing()
    {
        var (service, created) = TodoGeneration(World().Resolver());

        var act = () => service.CreateTodoAsync(
            "Deadline", regardingEntityName: "sprk_project", regardingId: FlaggedNotIsolatedProject,
            regardingDisplayName: "P", ct: CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        created.Should().BeEmpty();
    }

    // =====================================================================================
    // Analysis create — POST /api/ai/analysis/create
    // =====================================================================================

    [Fact]
    public async Task AnalysisCreate_ForADocumentOfASecureMatter_IsOwnedByTheDocumentsNamedTeam()
    {
        var documentId = Guid.NewGuid();
        await using var host = await AnalysisHost.StartAsync(World()
            .WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
            .Resolver());

        var response = await host.Client.PostAsJsonAsync("/api/ai/analysis/create", new { name = "Review", documentId, skillIds = Array.Empty<Guid>(), knowledgeIds = Array.Empty<Guid>(), toolIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Analyses.Verify(a => a.CreateAnalysisAsync(
            documentId, "Review", It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            Directory.SecureNamedTeam, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalysisCreate_RecordsTheCallerAsThePersonWhoAsked()
    {
        // c1-r1 (owner round 13 item 9): the caller's object id (the fake auth's oid) resolves to their systemuser.
        var documentId = Guid.NewGuid();
        var callerSystemUser = Guid.Parse("c1c1c1c1-0000-4000-8000-0000000000a5");
        await using var host = await AnalysisHost.StartAsync(World()
            .WithUser(callerSystemUser, Guid.Parse("00000000-0000-0000-0000-000000000aaa"), Directory.ChildBu)
            .WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
            .Resolver());

        var response = await host.Client.PostAsJsonAsync("/api/ai/analysis/create", new { name = "Review", documentId, skillIds = Array.Empty<Guid>(), knowledgeIds = Array.Empty<Guid>(), toolIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Analyses.Verify(a => a.CreateAnalysisAsync(
            documentId, "Review", It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            Directory.SecureNamedTeam, callerSystemUser, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalysisCreate_ForAMissingDocument_Is409_AndCreatesNothing()
    {
        await using var host = await AnalysisHost.StartAsync(World().Resolver());

        var response = await host.Client.PostAsJsonAsync(
            "/api/ai/analysis/create", new { name = "Review", documentId = Guid.NewGuid(), skillIds = Array.Empty<Guid>(), knowledgeIds = Array.Empty<Guid>(), toolIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        host.Analyses.Verify(a => a.CreateAnalysisAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =====================================================================================
    // Review logs — CommunicationEnrichmentService (propose) and CommunicationProposalApplyService (apply audit)
    // =====================================================================================

    [Fact]
    public async Task EnrichmentReviewLog_OfASecureCommunication_IsOwnedByTheNamedTeam()
    {
        var communicationId = Guid.NewGuid();
        var (service, created) = Enrichment(World()
            .WithRecord("sprk_communication", communicationId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
            .Resolver());

        await service.EnrichAsync(communicationId, CommunicationDirection.Incoming, ProposeMessage(), archivedDocumentId: null, CancellationToken.None);

        created.Should().ContainSingle(e => e.LogicalName == "sprk_emailreviewlog")
            .Which.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task EnrichmentReviewLog_OfAUserOwnedCommunicationFiledToASecureMatter_IsOwnedByTheNamedTeam()
    {
        // Verifier item 3 through a real writer: the communication kept its creator (run-as-user / client / pre-146) but IS
        // filed to a secure matter, so its review log must not go to that creator in an ordinary business unit.
        var communicationId = Guid.NewGuid();
        var (service, created) = Enrichment(World()
            .WithRecord("sprk_communication", communicationId, Directory.GeneralBu, owningTeam: null,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter) })
            .Resolver());

        await service.EnrichAsync(communicationId, CommunicationDirection.Incoming, ProposeMessage(), archivedDocumentId: null, CancellationToken.None);

        created.Should().ContainSingle(e => e.LogicalName == "sprk_emailreviewlog")
            .Which.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task ApplyAuditRow_OfASecureCommunication_IsOwnedByTheNamedTeam()
    {
        var harness = new ApplyHarness(World()
            .WithRecord("sprk_communication", ApplyHarness.CommunicationId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam)
            .Resolver());

        await harness.Service.ApplyAsync(ApplyHarness.ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        harness.TargetWrites.Should().Be(1);
        var auditRow = harness.Created.Should().ContainSingle(e => e.LogicalName == "sprk_emailreviewlog").Subject;
        auditRow.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.SecureNamedTeam);
        // c1-r1 (owner round 13 item 9): the confirming user asked for the app-created audit row.
        auditRow.Attributes.Should().ContainKey("sprk_createdbyperson");
        auditRow.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Directory.CallerUserId);
    }

    [Fact]
    public async Task ApplyAuditRow_WhenItsCommunicationCannotBeRead_Is409_BeforeTheTargetIsWritten()
    {
        var harness = new ApplyHarness(World().Resolver()); // the communication is not in the directory

        var act = () => harness.Service.ApplyAsync(ApplyHarness.ReviewLogId, new ClaimsPrincipal(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(409);
        harness.TargetWrites.Should().Be(0, "the audit row's owner is resolved BEFORE the target record is written");
        harness.Created.Should().BeEmpty();
    }

    // =====================================================================================
    // Inbound attachment documents — EmailAttachmentProcessor
    // =====================================================================================

    [Fact]
    public async Task EmailAttachment_OfAnEmailDocumentFiledToASecureMatter_IsOwnedByTheNamedTeam()
    {
        var parentDocument = Guid.NewGuid();
        var (processor, creates) = AttachmentProcessor(World()
            .WithRecord("sprk_document", parentDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam)
            .Resolver());

        await processor.ProcessAttachmentsAsync(AttachmentRequest(parentDocument, "sprk_matter", SecureMatter), CancellationToken.None);

        creates.Should().ContainSingle().Which.OwningTeamId.Should().Be(Directory.SecureNamedTeam,
            "secure-if-any over the parent email document and the record it is filed to");
    }

    [Fact]
    public async Task EmailAttachment_FiledToAFlaggedButNotIsolatedProject_CreatesNoDocument()
    {
        var parentDocument = Guid.NewGuid();
        var (processor, creates) = AttachmentProcessor(World()
            .WithRecord("sprk_document", parentDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam)
            .Resolver());

        var result = await processor.ProcessAttachmentsAsync(
            AttachmentRequest(parentDocument, "sprk_project", FlaggedNotIsolatedProject), CancellationToken.None);

        creates.Should().BeEmpty();
        result.FailedCount.Should().Be(1, "this writer's per-attachment refusal contract");
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static NormalizedMessage ProposeMessage() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "reviewer@example.com" },
        Subject = "Closing moved",
        BodyText = "Counsel, the closing has been moved to August 15, 2026. Update your calendars.",
    };

    /// <summary>The propose step over the seam suite's boundary doubles (EmailProposeSeamTests), with the REAL resolver.</summary>
    private static (CommunicationEnrichmentService Service, List<DataverseEntity> Created) Enrichment(IRecordOwnershipResolver ownership)
    {
        var created = new List<DataverseEntity>();
        var matterId = Guid.NewGuid();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(s => s.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication", Guid.NewGuid())
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
            });
        entities.Setup(s => s.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_matter", matterId) { ["sprk_closingdate"] = new DateTime(2026, 1, 1) });
        entities.Setup(s => s.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                "sprk_recordtype_ref" => new EntityCollection(new List<DataverseEntity> { new("sprk_recordtype_ref", Guid.NewGuid()) }),
                "sprk_emailupdatefield" => new EntityCollection(new List<DataverseEntity>
                {
                    new("sprk_emailupdatefield", Guid.NewGuid())
                    {
                        ["sprk_targetfieldlogicalname"] = "sprk_closingdate",
                        ["sprk_fieldtype"] = new OptionSetValue(100000004),
                        ["sprk_extractionguidance"] = "The scheduled closing date.",
                        ["sprk_requireconfirm"] = true,
                    },
                }),
                _ => new EntityCollection(),
            });
        entities.Setup(s => s.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid);

        var proposeAi = new Mock<ICommunicationProposeAi>(MockBehavior.Loose);
        proposeAi.Setup(p => p.ProposeAsync(It.IsAny<CommunicationProposeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ProposedFieldCandidate(
                    Field: "sprk_closingdate",
                    NewValue: "August 15, 2026",
                    Citation: new ProposalCitation("body", "body: sentence 1", "the closing has been moved to August 15, 2026"),
                    Reason: "The closing date changed.",
                    Confidence: 0.9),
            });

        var service = new CommunicationEnrichmentService(
            Sprk.Bff.Api.Tests.Seam.Communication.EnrichmentScopeFactoryStub.Create(
                Mock.Of<IPostUploadIndexingEnqueuer>(), Mock.Of<ICommunicationTriageAi>(), proposeAi.Object,
                new NullCommunicationCreateTaskAi()),
            entities.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<ICommunicationAssessedProducer>(),
            Mock.Of<IActionSeam>(),
            TestRoutingGate.Disabled(),
            ownership,
            Mock.Of<Spaarke.Dataverse.IFieldMappingDataverseService>(),
            NullLogger<CommunicationEnrichmentService>.Instance);
        return (service, created);
    }

    /// <summary>The apply step over the seam suite's boundary doubles (CommunicationProposalApplySeamTests), with the REAL resolver.</summary>
    private sealed class ApplyHarness
    {
        public static readonly Guid ReviewLogId = Guid.Parse("a2460000-0000-4000-8000-000000000001");
        public static readonly Guid CommunicationId = Guid.Parse("a2460000-0000-4000-8000-000000000002");
        private static readonly Guid TargetRecordId = Guid.Parse("a2460000-0000-4000-8000-000000000003");
        private const string QuotedText = "the closing has been moved to August 15, 2026";

        public ApplyHarness(IRecordOwnershipResolver ownership)
        {
            var callers = new Mock<ICallerSystemUserResolver>();
            callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(Directory.CallerUserId.ToString("D")));

            var generic = new Mock<IGenericEntityService>(MockBehavior.Loose);
            generic.Setup(g => g.RetrieveAsync("sprk_emailreviewlog", ReviewLogId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProposedRow());
            generic.Setup(g => g.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) => q.EntityName switch
                {
                    "sprk_emailreviewlog" => new EntityCollection(new List<DataverseEntity> { ProposedRow() }),
                    "sprk_recordtype_ref" => new EntityCollection(new List<DataverseEntity> { new("sprk_recordtype_ref") { Id = Guid.NewGuid() } }),
                    "sprk_emailupdatefield" => new EntityCollection(new List<DataverseEntity>
                    {
                        new("sprk_emailupdatefield") { Id = Guid.NewGuid(), ["sprk_fieldtype"] = new OptionSetValue(100000004) },
                    }),
                    _ => new EntityCollection(),
                });
            generic.Setup(g => g.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseEntity, CancellationToken>((e, _) => Created.Add(e))
                .ReturnsAsync(Guid.NewGuid);

            var actions = new Mock<IActionSeam>();
            actions.Setup(s => s.UpdateRecordAsync(It.IsAny<UpdateRecordRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => TargetWrites++)
                .ReturnsAsync(new UpdateRecordResult(true, new[] { "sprk_closingdate" }, null));

            var envelopes = new Mock<ICommunicationEnvelopeReader>();
            envelopes.Setup(r => r.ReconstructEnvelopeAsync(CommunicationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((new NormalizedMessage
                {
                    Direction = CommunicationDirection.Incoming,
                    Subject = "Closing update",
                    BodyText = $"Hello counsel — please note {QuotedText}. Regards.",
                }, new AssociationContext()));

            Service = new CommunicationProposalApplyService(
                callers.Object, generic.Object, actions.Object, envelopes.Object, ownership,
                NullLogger<CommunicationProposalApplyService>.Instance);
        }

        public CommunicationProposalApplyService Service { get; }
        public List<DataverseEntity> Created { get; } = new();
        public int TargetWrites { get; private set; }

        private static DataverseEntity ProposedRow() => new("sprk_emailreviewlog")
        {
            Id = ReviewLogId,
            ["sprk_communication"] = new EntityReference("sprk_communication", CommunicationId),
            ["sprk_action"] = new OptionSetValue(100000001), // Proposed
            ["sprk_targetentity"] = "sprk_matter",
            ["sprk_targetrecordid"] = TargetRecordId.ToString(),
            ["sprk_targetfield"] = "sprk_closingdate",
            ["sprk_confidence"] = 0.9m,
            ["sprk_aisuggestion"] = System.Text.Json.JsonSerializer.Serialize(new
            {
                field = "sprk_closingdate",
                fieldType = "DateTime",
                oldValue = "2026-01-01",
                newValue = "2026-08-15",
                citation = new { source = "body", locator = "body: sentence 1", quotedText = QuotedText },
                reason = "The closing date changed.",
                confidence = 0.9,
                requireConfirm = true,
                privilegeFlagged = false,
            }),
        };
    }

    private static ProcessAttachmentsRequest AttachmentRequest(Guid parentDocument, string associatedType, Guid associatedId) => new()
    {
        EmailId = Guid.NewGuid(),
        ParentDocumentId = parentDocument,
        ContainerId = "b!container",
        DriveId = "drive-1",
        QueueForAiProcessing = false,
        AssociatedEntityType = associatedType,
        AssociatedEntityId = associatedId,
        Attachments = new[]
        {
            new EmailAttachmentDto
            {
                FileName = "contract.pdf",
                ContentType = "application/pdf",
                SizeBytes = 50_000,
                Content = new MemoryStream(new byte[] { 1, 2, 3 }),
            },
        },
    };

    private static (EmailAttachmentProcessor Processor, List<CreateDocumentRequest> Creates) AttachmentProcessor(
        IRecordOwnershipResolver ownership)
    {
        var creates = new List<CreateDocumentRequest>();
        var documents = new Mock<IDocumentDataverseService>(MockBehavior.Loose);
        documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateDocumentRequest, CancellationToken>((r, _) => creates.Add(r))
            .ReturnsAsync(() => Guid.NewGuid().ToString());

        var processor = new EmailAttachmentProcessor(
            new UploadingSpeFileStore(), documents.Object, ownership,
            Options.Create(new EmailProcessingOptions()), NullLogger<EmailAttachmentProcessor>.Instance);
        return (processor, creates);
    }

    /// <summary>The SPE byte store at its virtual upload seam: every upload succeeds with a fresh item id.</summary>
    private sealed class UploadingSpeFileStore : SpeFileStore
    {
        private static readonly IGraphClientFactory Graph = Mock.Of<IGraphClientFactory>();

        public UploadingSpeFileStore()
            : base(
                new ContainerOperations(Graph, TestSpeOwnership.AllowAll(Graph), NullLogger<ContainerOperations>.Instance),
                new DriveItemOperations(Graph, TestSpeOwnership.AllowAll(Graph), NullLogger<DriveItemOperations>.Instance),
                new UploadSessionManager(Graph, TestSpeOwnership.AllowAll(Graph), Mock.Of<IHttpClientFactory>(), NullLogger<UploadSessionManager>.Instance),
                new UserOperations(Graph, NullLogger<UserOperations>.Instance))
        {
        }

        public override Task<FileHandleDto?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct = default) =>
            Task.FromResult<FileHandleDto?>(new FileHandleDto(
                Guid.NewGuid().ToString("N"), path, null, 3, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, driveId));
    }

    private static SendCommunicationRequest SendFiledTo(string entity, Guid id) => new()
    {
        To = new[] { "counsel@other.example" },
        Subject = "Settlement",
        Body = "<p>Draft attached.</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        Associations = new[] { new CommunicationAssociation { EntityType = entity, EntityId = id, EntityName = "Record" } },
        CorrelationId = "corr-146",
    };

    private sealed class SendHarness
    {
        public SendHarness(IRecordOwnershipResolver ownership)
        {
            var options = new CommunicationOptions
            {
                ApprovedSenders = new[] { new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true } },
                DefaultMailbox = "noreply@contoso.com",
            };
            var accounts = new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance);
            var validator = new ApprovedSenderValidator(
                Options.Create(options), accounts, Mock.Of<IDistributedCache>(), NullLogger<ApprovedSenderValidator>.Instance);

            var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
            entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseEntity, CancellationToken>((e, _) => Created.Add(e))
                .ReturnsAsync(Guid.NewGuid);
            var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
            communications.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Directory.CallerUserId);

            var dispatcher = new CommunicationChannelDispatcher(
                new ICommunicationChannelSender[]
                {
                    new RecordingSender(CommunicationType.Email, Sends),
                    new RecordingSender(CommunicationType.Message, Sends),
                },
                Array.Empty<ICommunicationArchiver>());

            Service = new CommunicationService(
                dispatcher, validator, communications.Object, entities.Object, Mock.Of<IDocumentDataverseService>(),
                accountService: null!, jobSubmissionService: null!, Mock.Of<ICommunicationEnrichmentService>(),
                Options.Create(options), CoreAncestorResolverFixtures.Inert(), ownership,
                NullLogger<CommunicationService>.Instance);
        }

        public CommunicationService Service { get; }
        public List<DataverseEntity> Created { get; } = new();
        public List<ChannelSendRequest> Sends { get; } = new();

        public HttpContext SignedInUser() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", Directory.CallerOid.ToString()), new Claim("preferred_username", "user@contoso.com") },
                "test")),
        };
    }

    /// <summary>A channel sender that records what reached the channel and answers success.</summary>
    private sealed class RecordingSender(CommunicationType type, List<ChannelSendRequest> sends) : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => type;

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            sends.Add(request);
            return Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress });
        }
    }

    /// <summary>A rung that files the email to one record, as a deterministic explicit reference.</summary>
    private sealed class FilingRung(string field, string entity, Guid id) : IAssociationRung
    {
        public RungKind Kind => RungKind.ExplicitReference;
        public int Order => 0;

        public Task<IReadOnlyList<RungMatch>> EvaluateAsync(
            NormalizedMessage message, AssociationContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RungMatch>>(new[]
            {
                new RungMatch
                {
                    RegardingFieldName = field,
                    Target = new EntityReference(entity, id),
                    Confidence = 1.0,
                    Provenance = "test:filing",
                    Rung = RungKind.ExplicitReference,
                },
            });
    }

    private static IAssociationRung FiledTo(string field, string entity, Guid id) => new FilingRung(field, entity, id);

    /// <summary>
    /// A core-ancestor resolver whose FIRST derivation succeeds (so the owner is decided from the stamp) and whose
    /// later ones fault — the case where the owner is known but the filing the create carries cannot be built (r2).
    /// </summary>
    private static CoreAncestorResolver AncestorsOnceThenFailing(params (string LookupAttribute, Guid RecordId)[] ancestors)
    {
        var reads = 0;
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string logicalName, Guid id, string[] _, CancellationToken __) =>
            {
                if (Interlocked.Increment(ref reads) > 1)
                    return Task.FromException<DataverseEntity>(new TimeoutException("throttled on the second read"));

                // Batch 4 integration (task 156): each ancestor sits in the column THAT row names its root with (an
                // invoice's matter is its typed sprk_matter), exactly as CoreAncestorResolverFixtures.WithAncestors does.
                var row = new DataverseEntity(logicalName, id);
                foreach (var (lookupAttribute, recordId) in ancestors)
                {
                    var entityType = CoreAncestorResolver.CoreAncestorLookups
                        .First(c => string.Equals(c.LookupAttribute, lookupAttribute, StringComparison.OrdinalIgnoreCase))
                        .EntityType;
                    var column = CoreAncestorResolver.IntermediateRootColumns.TryGetValue(logicalName, out var roots)
                        ? roots.FirstOrDefault(r => string.Equals(r.RootEntity, entityType, StringComparison.OrdinalIgnoreCase)).Column
                        : null;
                    row[column ?? lookupAttribute] = new EntityReference(entityType, recordId);
                }

                return Task.FromResult(row);
            });

        return new CoreAncestorResolver(
            entityService.Object,
            CoreAncestorResolverFixtures.ProbeReturning(CoreAncestorResolverFixtures.AllRootColumns),
            NullLogger<CoreAncestorResolver>.Instance);
    }

    /// <summary>
    /// The association mapper for a tenant whose auto-writable set includes INVOICES
    /// (<see cref="AutoFileOptions.CoreWritableEntities"/> is tenant configuration; the default writes only matter,
    /// project and service request, whose stamps are themselves). With a CHILD type writable, the engine files an email
    /// to the invoice and stamps the invoice's core ancestor — the case verifier item 4 is about.
    /// </summary>
    private static AssociationStatusMapper MapperFilingInvoices()
    {
        var options = new AutoFileOptions
        {
            Enabled = true,
            Threshold = 0.85,
            CoreWritableEntities = new() { "sprk_matter", "sprk_project", "sprk_servicerequest", "sprk_invoice" },
        };
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        return new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance);
    }

    private static NormalizedMessage Envelope() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "counsel@other.example",
        To = new[] { "intake@contoso.com" },
        Subject = "Invoice 1042",
        BodyText = "Please find the invoice attached.",
    };

    private static IncomingAssociationResolver Association(CoreAncestorResolver ancestors, params IAssociationRung[] rungs) =>
        new(rungs, Mock.Of<ICommunicationDataverseService>(), Mock.Of<IGenericEntityService>(), MapperFilingInvoices(),
            ancestors, new RecordOwnershipResolverDouble(), NullLogger<IncomingAssociationResolver>.Instance);

    /// <summary>The processor with only what its filing step uses; Graph, SPE and the job pipeline are never reached.</summary>
    private static IncomingCommunicationProcessor InboundProcessor(
        IRecordOwnershipResolver ownership, CoreAncestorResolver ancestors, IAssociationRung rung,
        ICommunicationDataverseService? communications = null) =>
        new(
            graphClientFactory: null!, communicationService: communications!, genericEntityService: null!, accountService: null!,
            associationResolver: Association(ancestors, rung), messageNormalizer: new GraphMessageNormalizer(),
            emlConverter: null!, scopeFactory: null!, jobSubmissionService: null!, notificationService: null!,
            enrichmentService: null!, options: Options.Create(new CommunicationOptions()), textExtractor: null!,
            attachmentMatchOptions: Options.Create(new AttachmentMatchOptions { Enabled = false }),
            configuration: new ConfigurationBuilder().Build(), ownership: ownership,
            logger: NullLogger<IncomingCommunicationProcessor>.Instance);

    private static (EmailUploadCaptureService Capture, List<DataverseEntity> Created) UploadCapture(
        IRecordOwnershipResolver ownership, CoreAncestorResolver ancestors)
    {
        var created = new List<DataverseEntity>();
        var dv = new Mock<IDataverseService>(MockBehavior.Loose);
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, string, CancellationToken>((e, _, _) => created.Add(e))
            .ReturnsAsync((Guid.NewGuid(), false));

        var association = Association(ancestors, new ExplicitReferenceRung(dv.Object));
        var capture = new EmailUploadCaptureService(
            dv.Object, association, Mock.Of<ICommunicationEnrichmentService>(), ownership,
            NullLogger<EmailUploadCaptureService>.Instance);
        return (capture, created);
    }

    private static SaveRequest EmailSave(string entityType, Guid id) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = entityType, EntityId = id, DisplayName = "INV-1042" },
        Email = new EmailMetadata
        {
            Subject = "Invoice 1042",
            SenderEmail = "billing@vendor.example",
            InternetMessageId = $"<{Guid.NewGuid():N}@vendor.example>",
            Body = "Invoice attached.",
            IsBodyHtml = false,
        },
    };

    private static (SignalEvaluationService Service, List<Dictionary<string, object?>> Upserts) SignalEvaluation(
        IRecordOwnershipResolver ownership, Guid matterId)
    {
        var snapshotId = Guid.NewGuid();
        var upserts = new List<Dictionary<string, object?>>();
        var fields = new Mock<IFieldMappingDataverseService>(MockBehavior.Loose);
        fields.Setup(f => f.QueryChildRecordIdsAsync("sprk_spendsnapshot", "sprk_matter", matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { snapshotId });
        fields.Setup(f => f.RetrieveRecordFieldsAsync("sprk_spendsnapshot", snapshotId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object?>
            {
                ["sprk_periodtype"] = 100000003, // ToDate
                ["sprk_periodkey"] = "2026-todate",
                ["sprk_bucketkey"] = "total",
                ["sprk_invoicedamount"] = 150m,
                ["sprk_budgetamount"] = 100m, // budget exceeded → a signal
                ["sprk_velocitypct"] = null,
            });
        fields.Setup(f => f.UpdateRecordFieldsAsync(
                "sprk_spendsignal", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, _, f, _, _) => upserts.Add(f))
            .Returns(Task.CompletedTask);

        var service = new SignalEvaluationService(
            fields.Object, ownership, Options.Create(new FinanceOptions()), new FinanceTelemetry(),
            NullLogger<SignalEvaluationService>.Instance);
        return (service, upserts);
    }

    private static (TodoGenerationService Service, List<DataverseEntity> Created) TodoGeneration(IRecordOwnershipResolver ownership)
    {
        var created = new List<DataverseEntity>();
        var dataverse = new Mock<IDataverseService>(MockBehavior.Loose);
        dataverse.Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid);
        var communications = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);

        var services = new ServiceCollection();
        services.AddSingleton(dataverse.Object);
        services.AddSingleton(communications.Object);
        var service = new TodoGenerationService(
            services.BuildServiceProvider(), NullLogger<TodoGenerationService>.Instance,
            Options.Create(new TodoGenerationOptions()));

        // The established harness for this background service (TodoGenerationServiceTests): its Dataverse client is
        // resolved lazily in ExecuteAsync, so the test sets it directly and uses the internal seams for the rest.
        typeof(TodoGenerationService)
            .GetField("_dataverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(service, dataverse.Object);
        service.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            communications.Object, CoreAncestorResolverFixtures.Inert(), NullLogger<TodoRegardingBuilder>.Instance));
        service.SetOwnershipResolverForTest(ownership);
        return (service, created);
    }

    /// <summary>The analysis routes over the real owner resolver (registrations mirror AnalysisForkEndpointTestFixture).</summary>
    private sealed class AnalysisHost : IAsyncDisposable
    {
        private WebApplication? _app;

        public Mock<IAnalysisDataverseService> Analyses { get; } = new(MockBehavior.Loose);
        public HttpClient Client { get; private set; } = null!;

        public static async Task<AnalysisHost> StartAsync(IRecordOwnershipResolver ownership)
        {
            var host = new AnalysisHost();
            await host.InitializeAsync(ownership);
            return host;
        }

        private async Task InitializeAsync(IRecordOwnershipResolver ownership)
        {
            Analyses.Setup(a => a.CreateAnalysisAsync(
                    It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
                    It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddSingleton(new SummarizeFakeAuthOptions(includeTid: true))
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = SummarizeFakeAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = SummarizeFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, SummarizeFakeAuthHandler>(SummarizeFakeAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
            {
                opt.AddPolicy("ai-batch", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-batch-test"));
                opt.AddPolicy("ai-stream", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-stream-test"));
            });

            // The route's document authorization is not the subject here (it is pinned by the AI authorization suites):
            // it allows the requested document so the request reaches the owner decision.
            var aiAuthorization = new Mock<IAiAuthorizationService>();
            aiAuthorization
                .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ClaimsPrincipal _, IReadOnlyList<Guid> ids, HttpContext _, CancellationToken _) => AuthorizationResult.Authorized(ids));
            builder.Services.AddSingleton(aiAuthorization.Object);
            builder.Services.AddSingleton(ownership);
            builder.Services.AddSingleton(Analyses.Object);

            // Task 162 (sweep integration): /create is gated as the caller BEFORE the owner question (G5: the Create
            // privilege, analysis.attach on the document, Read on each scope row). Not this suite's subject (162's
            // AnalysisEndpointsAuthorizationContractTests pins it) — the gate is open here so the owner decision runs.
            builder.Services.AddSingleton<Spaarke.Dataverse.IAccessDataSource>(new GateOpenAccessDataSource());
            builder.Services.AddScoped<Spaarke.Core.Auth.IAuthorizationRule, Spaarke.Core.Auth.Rules.OperationAccessRule>();
            builder.Services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
            builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(new GateOpenProbe());

            // Sibling handlers' services — registered so minimal-API parameter inference treats them as services.
            var cache = new InMemoryTenantCache();
            var chatRepository = new CapturingChatDataverseRepository();
            builder.Services.AddSingleton<ITenantCache>(cache);
            builder.Services.AddSingleton<IChatDataverseRepository>(chatRepository);
            builder.Services.AddSingleton(new ChatSessionManager(
                cache, chatRepository, NullLogger<ChatSessionManager>.Instance, persistence: null, cleanupSignal: null));
            builder.Services.AddSingleton(Mock.Of<IGenericEntityService>());
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(Options.Create(new AnalysisOptions { Enabled = true }));
            builder.Services.AddSingleton(Mock.Of<IPlaybookOrchestrationService>());
            builder.Services.AddSingleton(Mock.Of<IConsumerRoutingService>());
            builder.Services.AddSingleton(Mock.Of<IActionResolver>());
            builder.Services.AddSingleton(Mock.Of<IDocumentTextSource>());
            builder.Services.AddSingleton(Mock.Of<IActionRunner>());
            builder.Services.AddSingleton(Mock.Of<IPostUploadIndexingEnqueuer>());
            builder.Services.AddSingleton(Mock.Of<IAnalysisOrchestrationService>());
            builder.Services.AddSingleton(Mock.Of<IDocumentDataverseService>());
            builder.Services.AddSingleton(Mock.Of<ISpeFileOperations>());
            builder.Services.AddSingleton(Mock.Of<ITextExtractor>());
            builder.Services.AddSingleton<AnalysisDocumentLoader>();
            builder.Services.AddSingleton<NotificationService>();

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapAnalysisEndpoints();
            await _app.StartAsync();

            Client = _app.GetTestClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        /// <summary>Every record answers full rights (task 162's gate is open; see the registration).</summary>
        private sealed class GateOpenAccessDataSource : Spaarke.Dataverse.IAccessDataSource
        {
            private static readonly Spaarke.Dataverse.AccessRights All =
                Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write | Spaarke.Dataverse.AccessRights.Append
                | Spaarke.Dataverse.AccessRights.AppendTo | Spaarke.Dataverse.AccessRights.Create | Spaarke.Dataverse.AccessRights.Delete
                | Spaarke.Dataverse.AccessRights.Share;

            public Task<Spaarke.Dataverse.AccessSnapshot> GetUserAccessAsync(
                string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
                Task.FromResult(new Spaarke.Dataverse.AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = All });

            public Task<Spaarke.Dataverse.AccessSnapshot> GetRecordAccessAsync(
                string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
                GetUserAccessAsync(userId, recordId.ToString(), userAccessToken, ct);
        }

        /// <summary>The caller holds every table privilege (task 162's gate is open; see the registration).</summary>
        private sealed class GateOpenProbe : Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe
        {
            public GateOpenProbe()
                : base(new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                    NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance)
            {
            }

            public override Task<bool> CallerHoldsPrivilegeAsync(
                string? callerBearerToken, string privilegeName, CancellationToken ct = default) =>
                Task.FromResult(!string.IsNullOrEmpty(callerBearerToken));
        }
    }
}
