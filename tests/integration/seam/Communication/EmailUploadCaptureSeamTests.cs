using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038) for FR-B3 — the user-upload ("Save to Spaarke") capture path. Wires the REAL
/// <see cref="EmailUploadCaptureService"/> → REAL <see cref="IncomingAssociationResolver"/> (real rungs, real
/// <see cref="AssociationStatusMapper"/> + <see cref="AutoFileGate"/> over real <see cref="AutoFileOptions"/>),
/// faking only the Dataverse boundary and the separately-tested enrichment step. Proves a user-saved email is
/// routed through the SAME association engine as mailbox capture (parity), deduped structurally on
/// internet-message-id (FR-C1 / NFR-02), and that the whole capture is non-fatal (NFR-04) — a router-unit test
/// alone would not exercise this slice.
/// </summary>
public sealed class EmailUploadCaptureSeamTests
{
    private static readonly Guid CommId = Guid.NewGuid();

    /// <summary>
    /// Builds the real capture service over the real association spine, faking the Dataverse boundary and the
    /// enrichment step. <paramref name="dv"/> doubles as BOTH the race-proof create seam
    /// (<see cref="ICommunicationDataverseService"/>) and the rung/resolver Dataverse boundary
    /// (<see cref="IGenericEntityService"/>) — the production composite implements both.
    /// </summary>
    private static EmailUploadCaptureService BuildCapture(
        Mock<IDataverseService> dv,
        out List<(Guid Id, Dictionary<string, object> Fields)> writes,
        Mock<ICommunicationEnrichmentService>? enrichment = null)
    {
        var captured = new List<(Guid, Dictionary<string, object>)>();
        writes = captured;
        dv.Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, id, f, _) => captured.Add((id, new Dictionary<string, object>(f))))
            .Returns(Task.CompletedTask);

        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        var mapper = new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance);

        var rungs = new IAssociationRung[]
        {
            new ExplicitReferenceRung(dv.Object),
            new ThreadContinuityRung(dv.Object),
            new ParticipantCorrelationRung(dv.Object),
        };
        var resolver = new IncomingAssociationResolver(
            rungs, dv.Object, dv.Object, mapper, Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(), NullLogger<IncomingAssociationResolver>.Instance);

        var enrich = enrichment ?? new Mock<ICommunicationEnrichmentService>();

        return new EmailUploadCaptureService(
            dv.Object, resolver, enrich.Object, NullLogger<EmailUploadCaptureService>.Instance);
    }

    private static SaveRequest EmailSave(string? internetMessageId, SaveEntityReference? target)
        => new()
        {
            ContentType = SaveContentType.Email,
            TargetEntity = target,
            Email = new EmailMetadata
            {
                Subject = "Re: Acme acquisition",
                SenderEmail = "jane@external.com",
                InternetMessageId = internetMessageId,
                Body = "Please file this to the matter.",
                IsBodyHtml = false,
            },
        };

    [Fact]
    public async Task CaptureAsync_EmailWithSavePaneSelection_CreatesCommunicationAndAssociatesToSelectedRegarding()
    {
        var dv = new Mock<IDataverseService>();
        var matterId = Guid.NewGuid();
        // The email was never captured before → the race-proof create inserts a fresh canonical row.
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(
                It.IsAny<DataverseEntity>(), "<m@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommId, false));
        var capture = BuildCapture(dv, out var writes);

        // Save-pane selection = matter M → the authoritative caller-supplied regarding (rung 0).
        var request = EmailSave("<m@x.com>",
            new SaveEntityReference { EntityType = "Matter", EntityId = matterId, DisplayName = "Acme v Beta" });

        var result = await capture.CaptureAsync(request, "user-1", CancellationToken.None);

        // A canonical sprk_communication was created (not merely a sprk_document archive).
        result.Should().Be(CommId);
        dv.Verify(d => d.CreateCommunicationRaceProofAsync(
            It.Is<DataverseEntity>(e =>
                e.LogicalName == "sprk_communication" &&
                (string)e["sprk_internetmessageid"] == "<m@x.com>"),
            "<m@x.com>", It.IsAny<CancellationToken>()), Times.Once);

        // Parity: routed through the SAME IncomingAssociationResolver as mailbox capture → the save-pane
        // selection is auto-filed as the regarding via the ExplicitReference rung, with provenance persisted.
        var (_, fields) = writes.Should().ContainSingle().Subject;
        ((Microsoft.Xrm.Sdk.EntityReference)fields["sprk_regardingmatter"]).Id.Should().Be(matterId);
        ((OptionSetValue)fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Resolved);

        var prov = JsonDocument.Parse((string)fields["sprk_associationprovenance"]).RootElement;
        prov.GetProperty("decision").GetProperty("autoFiled").GetBoolean().Should().BeTrue();
        prov.GetProperty("rungsFired").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("ExplicitReference");
    }

    [Fact]
    public async Task CaptureAsync_SameEmailAlreadyCaptured_ReconcilesToCanonicalAndSkipsReassociation()
    {
        var dv = new Mock<IDataverseService>();
        var canonicalId = Guid.NewGuid();
        // The race-proof create reconciles to the existing canonical (WasDuplicate=true) — the SINGLE dedup
        // authority (the sprk_internetmessageid alternate key), no app-level check-then-insert.
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(
                It.IsAny<DataverseEntity>(), "<dup@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync((canonicalId, true));
        var capture = BuildCapture(dv, out var writes);

        var request = EmailSave("<dup@x.com>",
            new SaveEntityReference { EntityType = "Matter", EntityId = Guid.NewGuid() });

        var result = await capture.CaptureAsync(request, "user-2", CancellationToken.None);

        // Reconciled to the one canonical row; association must NOT re-run (the canonical keeps its association).
        result.Should().Be(canonicalId);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_WhenAssociationThrows_IsNonFatalAndStillReturnsCommunicationId()
    {
        var dv = new Mock<IDataverseService>();
        dv.Setup(d => d.CreateCommunicationRaceProofAsync(
                It.IsAny<DataverseEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommId, false));
        // The association write throws — NFR-04 requires the capture to swallow it and the save to proceed.
        dv.Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));
        var enrichment = new Mock<ICommunicationEnrichmentService>();
        var capture = BuildCapture(dv, out _, enrichment);

        var request = EmailSave("<err@x.com>",
            new SaveEntityReference { EntityType = "Matter", EntityId = Guid.NewGuid() });

        // Act + assert: does not throw; the record was created so the caller still gets the id.
        var result = await capture.CaptureAsync(request, "user-3", CancellationToken.None);
        result.Should().Be(CommId);
    }

    [Fact]
    public async Task CaptureAsync_NonEmailSave_IsNoOpAndCreatesNoCommunication()
    {
        var dv = new Mock<IDataverseService>();
        var capture = BuildCapture(dv, out var writes);

        var request = new SaveRequest
        {
            ContentType = SaveContentType.Document,
            Document = new DocumentMetadata { FileName = "brief.docx" },
        };

        var result = await capture.CaptureAsync(request, "user-4", CancellationToken.None);

        result.Should().BeNull();
        writes.Should().BeEmpty();
        dv.Verify(d => d.CreateCommunicationRaceProofAsync(
            It.IsAny<DataverseEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
