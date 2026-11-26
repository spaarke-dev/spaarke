using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 140 (ISS-043, #3194; owner 2026-11-25 "confirm the suggestion"): an Office save to the record a
/// communication is already filed to as an unconfirmed suggestion CONFIRMS it (<c>sprk_associationstatus</c> = Resolved), after
/// the caller's Write check on that communication (ADR-008 A1, the filing's own <see cref="CallerRecordAccessProbe"/>). A refusal
/// or a fault leaves it as it was and the save still succeeds. A communication filed to another record is neither moved nor
/// confirmed; a Resolved one gets no write. The unfiled path ends Resolved too, auto-file on or off: the user picked the record.
/// </summary>
/// <remarks>
/// The REAL capture, association engine (rung 0 + status ladder), filing and <c>POST /api/office/save</c> run; only the
/// Dataverse boundary (an in-memory <c>sprk_communication</c> table keyed by Message-ID), the caller's rights and the ownership
/// resolver are doubled (ADR-038 §4). The communication's row is the dev row of ISS-043 with synthetic ids: Suggested, the
/// matter written to its typed lookup and its ADR-024 pair.
/// </remarks>
[Trait("status", "task-140-word-add-in-r1")]
public class OfficeSaveConfirmsSuggestedFilingTests
{
    private const string RfcId = "<CAF0-140@mail.example.com>";
    private const string CallerToken = "test-caller-token-140";
    private static readonly Guid MatterM = Guid.Parse("14000000-0000-4000-8000-0000000000a1");
    private static readonly Guid MatterN = Guid.Parse("14000000-0000-4000-8000-0000000000a2");
    private static readonly Guid OrganizationO = Guid.Parse("14000000-0000-4000-8000-0000000000a3");

    // ── Must fire: the user's pick confirms the suggestion ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveToTheRecordASuggestedCommunicationIsFiledTo_ConfirmsIt_AfterTheCallersWriteCheckOnThatCommunication()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.Confirmed);
        var write = store.CommunicationWrites.Should().ContainSingle().Subject;
        write.Id.Should().Be(id);
        write.Fields.Keys.Should().Equal(new[] { "sprk_associationstatus" }, "the confirm is the panel's plain status write, nothing else");
        ((OptionSetValue)write.Fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Resolved);
        store.Probe.Verify(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", id, It.IsAny<CancellationToken>()), Times.Once);
        store.Events.Should().Equal(new[] { "check", "write" }, "the caller's Write is asked before the write (ADR-008 A1)");
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
        store.RegardingMatter(id).Should().Be(MatterM, "the filing itself is not touched");
    }

    [Fact]
    public async Task SaveToTheRecordOfAPendingReviewRow_WithThatRecordWritten_ConfirmsIt()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.PendingReview);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.Confirmed);
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
    }

    [Fact]
    public async Task OverTheWire_TheConfirmingSaveIsAccepted_AndReports_AlreadyFiled()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        using var factory = Host(store);

        var response = await PostAsync(factory, EmailSave(MatterM));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await EmailFilingWireAsync(response)).Should().Be("AlreadyFiled",
            "the email was already filed to this record; the pane needs no new outcome (it adds no notice for it)");
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
    }

    // ── Fail closed: a refusal or a fault leaves the row as it was, and the save succeeds ──────────────────────────────

    [Fact]
    public async Task WhenTheCallerMayNotWriteTheCommunication_ItIsLeftUnconfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.DenyWrite();
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.ConfirmNotPermitted);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task WhenTheWriteCheckFaults_ItIsLeftUnconfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.FaultCheck();
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.ConfirmFailed);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task WhenTheConfirmWriteFaults_ItIsLeftAsItWas()
    {
        var store = new CommunicationStore { FailWrites = true };
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.ConfirmFailed);
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task OverTheWire_ARefusedOrFaultedConfirm_StillSaves_AndReports_FiledUnconfirmed()
    {
        foreach (var refuse in new Action<CommunicationStore>[] { s => s.DenyWrite(), s => s.FaultCheck(), s => s.FailWrites = true })
        {
            var store = new CommunicationStore();
            var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
            refuse(store);
            var world = new OfficeVersionSaveWorld();
            using var factory = Host(store, world);

            var response = await PostAsync(factory, EmailSave(MatterM));

            response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the confirm never fails the save");
            (await EmailFilingWireAsync(response)).Should().Be("FiledUnconfirmed",
                "the email IS in the record's communications but still needs confirming; NotPermitted / Failed would say it is not, and AlreadyFiled would say nothing");
            store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
            world.CreatedDocumentRequests.Should().ContainSingle().Which.RelatedCommunicationId.Should().Be(id);
        }
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACommunicationFiledToAnotherRecord_IsNeitherMovedNorConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterN, AssociationStatusCodes.Suggested);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
        store.RegardingMatter(id).Should().Be(MatterN);
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "nothing is asked for a communication the save does not change");
    }

    [Fact]
    public async Task APairNamingTheSavesRecordWithoutTheTypedLookupHoldingIt_IsNotConfirmed()
    {
        // Filed (typed lookup) to N; the pair names M: the primary regarding is not unambiguously the save's record.
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterN, AssociationStatusCodes.Suggested, pair: MatterM);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ACommunicationFiledToTheSavesRecordAsAnotherRegardingButNotItsPrimary_IsNotConfirmed()
    {
        // The typed lookup holds M, but the pair (the primary) is a project.
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested, pair: Guid.NewGuid());
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task AnAlreadyResolvedCommunication_GetsNoWrite_AndNoCheck()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Resolved);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
    }

    [Fact]
    public async Task ConfirmedBySomeoneElseBetweenTheCheckAndTheWrite_IsNotWrittenAgain()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.AfterCheck = () => store.SetStatus(id, AssociationStatusCodes.Resolved);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ReFiledToAnotherRecordBetweenTheCheckAndTheWrite_IsNotConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.AfterCheck = () => store.Refile(id, MatterN);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    // ── Binding condition (peer review): only the one record the user picked is confirmed ───────────────────────────────
    // A reply to a Resolved communication inherits EVERY regarding it holds at confidence 1.0 (ThreadContinuityRung), so a
    // row filed to the save's record AND another one is never confirmed by the save.

    [Fact]
    public async Task APrimaryThatIsTheSavesRecord_WithASecondRegarding_IsNotConfirmed_AndNothingIsAsked()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.AddRegarding(id, "sprk_regardingorganization", new EntityReference("sprk_organization", OrganizationO));
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
        store.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ASecondRegardingAddedBetweenTheCheckAndTheWrite_IsNotConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.AfterCheck = () =>
            store.AddRegarding(id, "sprk_regardingorganization", new EntityReference("sprk_organization", OrganizationO));
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task APairTypeThatIsTheSavesRecordsTable_IsConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested, pairType: store.RecordType("sprk_matter"));
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.Confirmed);
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
    }

    [Fact]
    public async Task APairTypeOfAnotherTable_IsNotConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested, pairType: store.RecordType("sprk_project"));
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public async Task TheSavesRecordHeldInAnotherTablesLookup_IsNotConfirmed()
    {
        // The pair names the id, but the only typed lookup holding it is the project's, not the matter's the save names.
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.MoveRegarding(id, "sprk_regardingmatter", "sprk_regardingproject", "sprk_project");
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
    }

    // ── Review B2: an Ambiguous row is the reviewer's ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAmbiguousRowFiledToTheSavesRecord_IsNotConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Ambiguous);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Ambiguous);
    }

    [Fact]
    public async Task ARowReResolvedAmbiguousBetweenTheCheckAndTheWrite_IsNotConfirmed()
    {
        var store = new CommunicationStore();
        var id = store.SeedFiled(RfcId, MatterM, AssociationStatusCodes.Suggested);
        store.AfterCheck = () => store.SetStatus(id, AssociationStatusCodes.Ambiguous);
        using var factory = Host(store);

        var (_, result) = await FileAsync(factory, EmailSave(MatterM));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        store.CommunicationWrites.Should().BeEmpty();
        store.Status(id).Should().Be(AssociationStatusCodes.Ambiguous);
    }

    // ── The unfiled-then-filed path (task 121) ends Resolved: the user picked the record ─────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnfiledCommunication_FiledByTheSave_IsWrittenResolved_WithAutoFileOnOrOff(bool autoFileEnabled)
    {
        var store = new CommunicationStore();
        var id = store.SeedUnfiled(RfcId);
        using var factory = Host(store, autoFileEnabled: autoFileEnabled);

        var (capture, result) = await FileAsync(factory, EmailSave(MatterM));

        // Evidence: the capture's decision for the caller-supplied record is what the ladder gives rung 0 at 1.0 —
        // Resolved with auto-file on, Suggested (suggest-only) with it off.
        capture!.Decision.Status.Should().Be(autoFileEnabled ? AssociationStatusCodes.Resolved : AssociationStatusCodes.Suggested);
        result.Should().Be(ReconciledFilingResult.Filed);
        var write = store.CommunicationWrites.Should().ContainSingle().Subject;
        ((EntityReference)write.Fields["sprk_regardingmatter"]).Id.Should().Be(MatterM);
        ((OptionSetValue)write.Fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Resolved);
        store.Status(id).Should().Be(AssociationStatusCodes.Resolved);
    }

    [Fact]
    public void ConfirmedByTheSave_TurnsOnlySuggestedIntoResolved()
    {
        static AssociationDecision Of(int status) => Decision(
            status, new Dictionary<string, EntityReference> { ["sprk_regardingmatter"] = new("sprk_matter", MatterM) }, []);

        ReconciledEmailFiling.ConfirmedByTheSave(Of(AssociationStatusCodes.Suggested), MatterM).Status
            .Should().Be(AssociationStatusCodes.Resolved, "the save's record is the decision's only regarding");
        ReconciledEmailFiling.ConfirmedByTheSave(Of(AssociationStatusCodes.Resolved), MatterM).Status.Should().Be(AssociationStatusCodes.Resolved);
        ReconciledEmailFiling.ConfirmedByTheSave(Of(AssociationStatusCodes.Ambiguous), MatterM).Status
            .Should().Be(AssociationStatusCodes.Ambiguous, "a conflict on another field is still the reviewer's");
        var suggested = Of(AssociationStatusCodes.Suggested);
        ReconciledEmailFiling.ConfirmedByTheSave(suggested, MatterM).Provenance.Should().BeSameAs(suggested.Provenance,
            "the provenance keeps the engine's own verdict, as the panel's Confirm leaves it");
    }

    [Fact]
    public void ConfirmedByTheSave_ADecisionThatAlsoWritesAnotherRegarding_StaysSuggested()
    {
        var decision = Decision(
            AssociationStatusCodes.Suggested,
            new Dictionary<string, EntityReference>
            {
                ["sprk_regardingmatter"] = new("sprk_matter", MatterM),
                ["sprk_regardingorganization"] = new("sprk_organization", OrganizationO),
            },
            []);

        ReconciledEmailFiling.ConfirmedByTheSave(decision, MatterM).Status.Should().Be(AssociationStatusCodes.Suggested,
            "the user picked one record; a reply to a Resolved row would inherit the organization too");
    }

    [Fact]
    public void ConfirmedByTheSave_ADecisionWhoseProvenanceMarksAnotherCandidateWritten_StaysSuggested()
    {
        var decision = Decision(
            AssociationStatusCodes.Suggested,
            new Dictionary<string, EntityReference> { ["sprk_regardingmatter"] = new("sprk_matter", MatterM) },
            [Candidate("sprk_regardingmatter", "sprk_matter", MatterM, written: true),
             Candidate("sprk_regardingorganization", "sprk_organization", OrganizationO, written: true)]);

        ReconciledEmailFiling.ConfirmedByTheSave(decision, MatterM).Status.Should().Be(AssociationStatusCodes.Suggested);
    }

    [Fact]
    public void ConfirmedByTheSave_AnotherCandidateThatIsOnlySuggested_DoesNotStopTheConfirm()
    {
        var decision = Decision(
            AssociationStatusCodes.Suggested,
            new Dictionary<string, EntityReference> { ["sprk_regardingmatter"] = new("sprk_matter", MatterM) },
            [Candidate("sprk_regardingmatter", "sprk_matter", MatterM, written: true),
             Candidate("sprk_regardingmatter", "sprk_matter", MatterN, written: false)]);

        ReconciledEmailFiling.ConfirmedByTheSave(decision, MatterM).Status.Should().Be(AssociationStatusCodes.Resolved,
            "a candidate that is not written is not filed and is not inherited by a reply");
    }

    [Fact]
    public async Task AnUnfiledCommunication_WhoseDecisionAlsoNamesAnotherRecord_IsFiledToTheSavesRecord_ButStaysSuggested()
    {
        // The unfiled path with auto-file off and a second rung-0 regarding (an organization) beside the save's matter.
        var store = new CommunicationStore();
        var id = store.SeedUnfiled(RfcId);
        using var factory = Host(store, autoFileEnabled: false);
        var request = EmailSave(MatterM);

        using var scope = factory.Services.CreateScope();
        var capture = await scope.ServiceProvider.GetRequiredService<EmailUploadCaptureService>()
            .CaptureWithOutcomeAsync(request, "test-user-oid", CancellationToken.None);
        capture!.Decision.Status.Should().Be(AssociationStatusCodes.Suggested, "precondition: auto-file off");
        var widened = capture with
        {
            Decision = capture.Decision with
            {
                RegardingWrites = new Dictionary<string, EntityReference>(capture.Decision.RegardingWrites)
                {
                    ["sprk_regardingorganization"] = new("sprk_organization", OrganizationO),
                },
            },
        };

        var result = await scope.ServiceProvider.GetRequiredService<ReconciledEmailFiling>()
            .FileIfUnfiledAsync(request, widened, CallerToken, CancellationToken.None);

        result.Should().Be(ReconciledFilingResult.Filed);
        var write = store.CommunicationWrites.Should().ContainSingle().Subject;
        ((EntityReference)write.Fields["sprk_regardingmatter"]).Id.Should().Be(MatterM);
        write.Fields.Should().NotContainKey("sprk_regardingorganization", "only the save's record is written (task 121)");
        store.Status(id).Should().Be(AssociationStatusCodes.Suggested, "the user's pick of one record confirms no other");
    }

    private static CandidateTrace Candidate(string field, string entity, Guid id, bool written) => new()
    {
        Field = field,
        TargetEntity = entity,
        TargetId = id.ToString("D"),
        ReinforcedConfidence = 1,
        DeterministicConfidence = 1,
        Written = written,
        Conflict = false,
        Contributors = [],
    };

    private static AssociationDecision Decision(
        int status, IReadOnlyDictionary<string, EntityReference> writes, IReadOnlyList<CandidateTrace> candidates) => new()
        {
            Status = status,
            RegardingWrites = writes,
            AutoFiled = false,
            Provenance = new AssociationProvenance
            {
                Direction = "Incoming",
                Decision = new AssociationDecisionTrace
                {
                    Status = AssociationStatusCodes.Name(status),
                    AutoFiled = false,
                    KillSwitchEnabled = false,
                    AutoFileThreshold = 0.85,
                    TopDeterministicConfidence = 1,
                    TopConfidence = 1,
                    AiInvolved = false,
                    Reason = "test",
                },
                RungsFired = ["ExplicitReference"],
                Candidates = candidates,
                Signals = [],
            },
        };

    // ── Harness ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static SaveRequest EmailSave(Guid matter) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = matter, DisplayName = "Acme v Beta" },
        Email = new EmailMetadata
        {
            Subject = "RE: Acme v Beta - discovery schedule",
            SenderEmail = "counsel@test.com",
            SentDate = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero),
            InternetMessageId = RfcId,
            ExchangeItemId = "AAMkAGI2TG93-140=",
            Body = "<p>The email text</p>",
            IsBodyHtml = true,
            IsNameSystemDerived = true,
        },
    };

    /// <summary>The save's capture and filing, as <c>OfficeService</c> runs them, returning the filing's own result.</summary>
    private static async Task<(EmailCaptureOutcome? Capture, ReconciledFilingResult Result)> FileAsync(
        WebApplicationFactory<Program> factory, SaveRequest request)
    {
        using var scope = factory.Services.CreateScope();
        var capture = await scope.ServiceProvider.GetRequiredService<EmailUploadCaptureService>()
            .CaptureWithOutcomeAsync(request, "test-user-oid", CancellationToken.None);
        var result = await scope.ServiceProvider.GetRequiredService<ReconciledEmailFiling>()
            .FileIfUnfiledAsync(request, capture, CallerToken, CancellationToken.None);
        return (capture, result);
    }

    private static async Task<HttpResponseMessage> PostAsync(WebApplicationFactory<Program> factory, SaveRequest request)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallerToken);
        return await client.PostAsJsonAsync("/api/office/save", request);
    }

    private static async Task<string?> EmailFilingWireAsync(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("emailFiling", out var value) ? value.GetString() : null;
    }

    /// <summary>The Office save host with the email capture and the filing over <paramref name="store"/> (as
    /// <c>OfficeEmailDocumentCommunicationLinkTests</c> builds it), and the tenant's auto-file switch as given.</summary>
    private static WebApplicationFactory<Program> Host(
        CommunicationStore store, OfficeVersionSaveWorld? world = null, bool autoFileEnabled = true)
    {
        var dataverse = store.Dataverse;
        var ownership = new RecordOwnershipResolverDouble();
        var options = new AutoFileOptions { Enabled = autoFileEnabled, Threshold = 0.85 };
        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(dataverse.Object) }, dataverse.Object, dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), ownership, NullLogger<IncomingAssociationResolver>.Instance);

        return new OfficeVersionSaveTestWebAppFactory(world ?? new OfficeVersionSaveWorld()).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
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
        }));
    }

    /// <summary>
    /// <c>sprk_communication</c> rows keyed by Message-ID, race-proof as the alternate key makes the real create, and read and
    /// written by id: a write changes the row the next read sees.
    /// </summary>
    private sealed class CommunicationStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Guid> _byMessageId = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, DataverseEntity> _rows = new();

        public CommunicationStore()
        {
            Dataverse
                .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DataverseEntity _, string? messageId, CancellationToken _) => Create(messageId));
            Dataverse
                .Setup(d => d.GetCommunicationByInternetMessageIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string messageId, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        return _byMessageId.TryGetValue(messageId, out var id) ? Copy(_rows[id]) : null;
                    }
                });
            Dataverse
                .Setup(d => d.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        return _rows.TryGetValue(id, out var row) ? Copy(row) : new DataverseEntity("sprk_communication", id);
                    }
                });
            Dataverse
                .Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    if (FailWrites)
                        throw new InvalidOperationException("Test: Dataverse refused the sprk_communication update.");
                    lock (_gate)
                    {
                        Events.Add("write");
                        CommunicationWrites.Add((id, new Dictionary<string, object>(fields)));
                        var row = _rows[id];
                        foreach (var (column, value) in fields)
                            row[column] = value;
                    }

                    return Task.CompletedTask;
                });

            // The caller may change any communication — but only when asked with the caller's own token.
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AccessRights.None);
            CallerMay(AccessRights.Read | AccessRights.Write);
        }

        public Mock<IDataverseService> Dataverse { get; } = new();

        public Mock<CallerRecordAccessProbe> Probe { get; } = new(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };

        /// <summary>Every write to a <c>sprk_communication</c>, in order.</summary>
        public List<(Guid Id, Dictionary<string, object> Fields)> CommunicationWrites { get; } = [];

        /// <summary>"check" (the caller's rights asked) and "write", in the order they happened.</summary>
        public List<string> Events { get; } = [];

        /// <summary>Runs right after the caller's rights are asked (another writer acting in between).</summary>
        public Action? AfterCheck { get; set; }

        /// <summary>Every update of a communication throws.</summary>
        public bool FailWrites { get; set; }

        public void DenyWrite() => CallerMay(AccessRights.Read);

        public void FaultCheck() =>
            Probe.Setup(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Test: the access check could not be made."));

        private void CallerMay(AccessRights rights) =>
            Probe.Setup(p => p.GetCallerRightsAsync(CallerToken, "sprk_communications", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    lock (_gate)
                    {
                        Events.Add("check");
                    }

                    AfterCheck?.Invoke();
                    return rights;
                });

        /// <summary>An existing communication filed to <paramref name="matter"/> (typed lookup + ADR-024 pair, as the engine
        /// writes it) with <paramref name="status"/>; the pair may name another record, and may carry its record type (a
        /// <c>sprk_recordtype_ref</c> id from <see cref="RecordType"/>).</summary>
        public Guid SeedFiled(string messageId, Guid matter, int status, Guid? pair = null, Guid? pairType = null)
        {
            var id = SeedUnfiled(messageId);
            lock (_gate)
            {
                var row = _rows[id];
                row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
                row["sprk_regardingrecordid"] = (pair ?? matter).ToString("D");
                row["sprk_regardingrecordname"] = "Acme v Beta";
                row["sprk_associationstatus"] = new OptionSetValue(status);
                if (pairType is { } type)
                    row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", type);
            }

            return id;
        }

        /// <summary>A <c>sprk_recordtype_ref</c> row standing for <paramref name="logicalName"/>; returns its id.</summary>
        public Guid RecordType(string logicalName)
        {
            var typeId = Guid.NewGuid();
            Dataverse
                .Setup(d => d.RetrieveAsync("sprk_recordtype_ref", typeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("sprk_recordtype_ref", typeId) { ["sprk_recordlogicalname"] = logicalName });
            return typeId;
        }

        /// <summary>Another typed regarding written beside what the row holds.</summary>
        public void AddRegarding(Guid id, string column, EntityReference target)
        {
            lock (_gate)
            {
                _rows[id][column] = target;
            }
        }

        /// <summary>The record one typed lookup holds, moved to another lookup (and table).</summary>
        public void MoveRegarding(Guid id, string from, string to, string table)
        {
            lock (_gate)
            {
                var held = (EntityReference)_rows[id][from];
                _rows[id].Attributes.Remove(from);
                _rows[id][to] = new EntityReference(table, held.Id);
            }
        }

        /// <summary>An existing communication filed to nothing (captured from a mailbox earlier).</summary>
        public Guid SeedUnfiled(string messageId)
        {
            lock (_gate)
            {
                var id = Guid.NewGuid();
                _byMessageId[messageId] = id;
                _rows[id] = new DataverseEntity("sprk_communication", id);
                return id;
            }
        }

        public void SetStatus(Guid id, int status)
        {
            lock (_gate)
            {
                _rows[id]["sprk_associationstatus"] = new OptionSetValue(status);
            }
        }

        public void Refile(Guid id, Guid matter)
        {
            lock (_gate)
            {
                _rows[id]["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
                _rows[id]["sprk_regardingrecordid"] = matter.ToString("D");
            }
        }

        public int? Status(Guid id)
        {
            lock (_gate)
            {
                return _rows[id].GetAttributeValue<OptionSetValue>("sprk_associationstatus")?.Value;
            }
        }

        public Guid? RegardingMatter(Guid id)
        {
            lock (_gate)
            {
                return _rows[id].GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id;
            }
        }

        private (Guid Id, bool WasDuplicate) Create(string? messageId)
        {
            lock (_gate)
            {
                if (messageId is not null && _byMessageId.TryGetValue(messageId, out var existing))
                    return (existing, true);

                var id = Guid.NewGuid();
                if (messageId is not null)
                    _byMessageId[messageId] = id;
                _rows[id] = new DataverseEntity("sprk_communication", id);
                return (id, false);
            }
        }

        private static DataverseEntity Copy(DataverseEntity row)
        {
            var copy = new DataverseEntity(row.LogicalName, row.Id);
            foreach (var attribute in row.Attributes)
                copy[attribute.Key] = attribute.Value;
            return copy;
        }
    }
}
