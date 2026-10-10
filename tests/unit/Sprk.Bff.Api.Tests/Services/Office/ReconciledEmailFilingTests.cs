using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 121, owner decision 2026-10-09 ("file it if unfiled"): an Office email save that
/// reconciles to an EXISTING communication files it to the save's record when it is filed to nothing — never when it is
/// already filed, never for an unfiled save, never when the caller may not change it, and never by failing the save.
/// </summary>
/// <remarks>
/// The REAL <see cref="EmailUploadCaptureService"/> and <see cref="IncomingAssociationResolver"/> (real rungs and status
/// ladder) produce the decision exactly as the save does; only the Dataverse boundary, the caller's rights
/// (<see cref="CallerRecordAccessProbe"/>'s virtual seam) and the ownership resolver are doubled (ADR-038 §4).
/// </remarks>
[Trait("status", "task-121-word-add-in-r1")]
public class ReconciledEmailFilingTests
{
    private static readonly Guid CanonicalId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();

    private sealed class World
    {
        public Mock<IDataverseService> Dataverse { get; } = new();
        public List<Dictionary<string, object>> CommunicationWrites { get; } = new();
        public RecordOwnershipResolverDouble Ownership { get; } = new();
        public Mock<CallerRecordAccessProbe> Probe { get; } = new(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!)
        { CallBase = false };

        public World(bool reconciled, params DataverseEntity[] filingReads)
        {
            Dataverse
                .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CanonicalId, reconciled));
            var reads = new Queue<DataverseEntity>(filingReads);
            Dataverse
                .Setup(d => d.RetrieveAsync("sprk_communication", CanonicalId,
                    It.Is<string[]>(c => c.Contains("sprk_regardingmatter")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => reads.Count > 1 ? reads.Dequeue() : reads.Peek());
            Dataverse
                .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => CommunicationWrites.Add(new(f)))
                .Returns(Task.CompletedTask);
            CallerMay(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
        }

        public void CallerMay(AccessRights rights) =>
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), "sprk_communications", CanonicalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(rights);

        public async Task<(EmailCaptureOutcome? Capture, ReconciledFilingResult Result)> SaveAsync(SaveRequest request)
        {
            var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
            var mapper = new AssociationStatusMapper(
                new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance);
            var resolver = new IncomingAssociationResolver(
                new IAssociationRung[] { new ExplicitReferenceRung(Dataverse.Object) },
                Dataverse.Object, Dataverse.Object, mapper, CoreAncestorResolverFixtures.Inert(), Ownership,
                NullLogger<IncomingAssociationResolver>.Instance);
            var capture = new EmailUploadCaptureService(
                Dataverse.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), Ownership,
                NullLogger<EmailUploadCaptureService>.Instance);

            // The reconciler's share mirror and descendant pass run over loose doubles; it never throws by contract.
            var entities = new Mock<IGenericEntityService>();
            var configuration = new ConfigurationBuilder().Build();
            var children = new SecureChildReconciler(
                entities.Object, Ownership,
                new SecureChildShareSynchronizer(entities.Object, Mock.Of<IDataverseRecordShareService>(), null!, configuration,
                    NullLogger<SecureChildShareSynchronizer>.Instance),
                null!, configuration, NullLogger<SecureChildReconciler>.Instance);
            var filing = new ReconciledEmailFiling(
                resolver, Dataverse.Object, Probe.Object, children, NullLogger<ReconciledEmailFiling>.Instance);

            var outcome = await capture.CaptureWithOutcomeAsync(request, "user-oid", CancellationToken.None);
            if (outcome is not null && Rewrite is { } rewrite)
                outcome = outcome with { Decision = rewrite(outcome.Decision) };
            return (outcome, await filing.FileIfUnfiledAsync(request, outcome, "caller-token", CancellationToken.None));
        }

        /// <summary>Optionally changes the evaluated decision before the filing sees it (a second engine proposal).</summary>
        public Func<AssociationDecision, AssociationDecision>? Rewrite { get; set; }
    }

    private static DataverseEntity Unfiled() => new("sprk_communication", CanonicalId);

    private static DataverseEntity FiledTo(string column, string table) => new("sprk_communication", CanonicalId)
    {
        [column] = new EntityReference(table, Guid.NewGuid()),
    };

    private static SaveRequest PaneSave(SaveEntityReference? target) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target,
        Email = new EmailMetadata
        {
            Subject = "RE: Discovery schedule",
            SenderEmail = "counsel@contoso.com",
            InternetMessageId = "<CAF0a1b2c3@mail.example.com>",
            ExchangeItemId = "AAMkAGI2TG93AAA=",
            Body = "<p>text</p>",
            IsBodyHtml = true,
        },
    };

    private static SaveEntityReference Matter() => new() { EntityType = "Matter", EntityId = MatterId, DisplayName = "Acme v Beta" };

    [Fact]
    public async Task FileIfUnfiled_SaveReconciledToAnUnfiledCommunication_FilesItToTheSavesRecord_ThroughTheReparent()
    {
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        var write = world.CommunicationWrites.Should().ContainSingle().Subject;
        ((EntityReference)write["sprk_regardingmatter"]).Id.Should().Be(MatterId);
        world.Ownership.Reparents.Should().ContainSingle("the owner is re-derived from the record it is now filed under")
            .Which.EntityLogicalName.Should().Be("sprk_communication");
    }

    [Fact]
    public async Task FileIfUnfiled_EngineAlsoProposedAnotherRecord_FilesOnlyTheSavesRecord_AndMarksTheOtherNotWritten()
    {
        // The participant rung can propose the sender's contact beside the save's matter. The caller filed to the matter
        // only: the contact is neither written nor recorded as written in the provenance the review surface reads.
        var contactId = Guid.NewGuid();
        var world = new World(reconciled: true, Unfiled())
        {
            Rewrite = decision => decision with
            {
                RegardingWrites = new Dictionary<string, EntityReference>(decision.RegardingWrites)
                {
                    ["sprk_regardingperson"] = new EntityReference("contact", contactId),
                },
                Provenance = decision.Provenance with
                {
                    Candidates = decision.Provenance.Candidates.Append(new CandidateTrace
                    {
                        Field = "sprk_regardingperson", TargetEntity = "contact", TargetId = contactId.ToString("D"),
                        ReinforcedConfidence = 0.9, DeterministicConfidence = 0.9, Written = true, Conflict = false,
                        Contributors = new[] { new ContributorTrace { Rung = "ParticipantCorrelation", Confidence = 0.9, Provenance = "sender" } },
                    }).ToList(),
                },
            },
        };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        var write = world.CommunicationWrites.Should().ContainSingle().Subject;
        write.Should().ContainKey("sprk_regardingmatter").And.NotContainKey("sprk_regardingperson");
        var candidates = JsonDocument.Parse((string)write["sprk_associationprovenance"]).RootElement
            .GetProperty("candidates").EnumerateArray().ToList();
        candidates.Single(c => c.GetProperty("field").GetString() == "sprk_regardingperson")
            .GetProperty("written").GetBoolean().Should().BeFalse("a dropped proposal must not read as filed");
        candidates.Single(c => c.GetProperty("field").GetString() == "sprk_regardingmatter")
            .GetProperty("written").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FileIfUnfiled_CommunicationAlreadyFiledElsewhere_IsNeverMoved()
    {
        var world = new World(reconciled: true, FiledTo("sprk_regardingproject", "sprk_project"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
        world.Ownership.Reparents.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_CommunicationWithOnlyTheRegardingPair_CountsAsFiled()
    {
        var pairOnly = new DataverseEntity("sprk_communication", CanonicalId) { ["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D") };
        var world = new World(reconciled: true, pairOnly);

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_UnfiledSave_FilesNothing()
    {
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(target: null));

        result.Should().Be(ReconciledFilingResult.NotApplicable);
        world.CommunicationWrites.Should().BeEmpty();
        world.Probe.Verify(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FileIfUnfiled_CallerMayNotChangeTheCommunication_LeavesItUnfiled_AndTheSaveProceeds()
    {
        var world = new World(reconciled: true, Unfiled());
        world.CallerMay(AccessRights.Read);

        var (capture, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotPermitted);
        capture!.CommunicationId.Should().Be(CanonicalId, "the save still links its document to the communication");
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_FiledBySomeoneElseBetweenTheReadAndTheWrite_IsNotOverwritten()
    {
        var world = new World(reconciled: true, Unfiled(), FiledTo("sprk_regardingproject", "sprk_project"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_WhenTheWriteFails_ReportsFailed_AndNeverThrows()
    {
        var world = new World(reconciled: true, Unfiled());
        world.Dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Failed);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_SaveCreatedItsOwnCommunication_IsNotAReconcile()
    {
        var world = new World(reconciled: false, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotApplicable, "a new capture is created WITH its filing");
    }

    [Fact]
    public async Task FileIfUnfiled_QuickSaveRequest_FilesTheReconciledCommunicationToThePredictedRecord()
    {
        // The exact wire shape the ribbon Quick Save posts (quickSaveHelpers.buildEmailSaveRequest) — same route, same rule.
        var json = $$"""
        {
          "contentType": "Email",
          "triggerAiProcessing": true,
          "targetEntity": { "entityType": "Matter", "entityId": "{{MatterId}}", "displayName": "Acme v Beta" },
          "email": {
            "subject": "RE: Discovery schedule", "senderEmail": "counsel@contoso.com", "recipients": [],
            "isNameSystemDerived": true, "body": "<p>text</p>", "isBodyHtml": true,
            "internetMessageId": "<CAF0a1b2c3@mail.example.com>", "exchangeItemId": "AAMkAGI2TG93AAA="
          },
          "idempotencyKey": "qs-key"
        }
        """;
        var request = JsonSerializer.Deserialize<SaveRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })!;
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(request);

        result.Should().Be(ReconciledFilingResult.Filed);
        ((EntityReference)world.CommunicationWrites.Should().ContainSingle().Subject["sprk_regardingmatter"]).Id.Should().Be(MatterId);
    }
}
