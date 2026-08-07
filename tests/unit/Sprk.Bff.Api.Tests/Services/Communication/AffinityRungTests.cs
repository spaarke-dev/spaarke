using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior tests for the affinity / deterministic-learning rung (FR-A4). Each test protects a concrete
/// contract that would break in production if deleted: the learned suggestion surfaces after N confirmations,
/// the kill-switch spends no Dataverse cost, and — the load-bearing guarantee — an affinity match at ANY
/// confidence NEVER auto-files. The rung is tested over a real <see cref="AffinityStore"/> backed by a mocked
/// <see cref="IGenericEntityService"/> (the module boundary — ADR-038-permitted), and the never-auto-file
/// guarantee is asserted through the REAL <see cref="AssociationStatusMapper"/>.
/// </summary>
public class AffinityRungTests
{
    private const string Matter = "sprk_matter";
    private const string RegardingMatter = "sprk_regardingmatter";

    private static Entity AffinityRow(string targetEntity, Guid targetId, int count, AffinitySignalType type, string value)
    {
        var e = new Entity("sprk_affinity", Guid.NewGuid())
        {
            ["sprk_targetentity"] = targetEntity,
            ["sprk_targetid"] = targetId.ToString("D"),
            ["sprk_confirmationcount"] = count,
            ["sprk_signaltype"] = new OptionSetValue((int)type),
            ["sprk_signalvalue"] = value,
        };
        return e;
    }

    private static (AffinityRung Rung, Mock<IGenericEntityService> Ds) BuildRung(
        AffinityOptions opts, params Entity[] topRow)
    {
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var collection = new EntityCollection();
        collection.Entities.AddRange(topRow);
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        var store = new AffinityStore(ds.Object, NullLogger<AffinityStore>.Instance);
        var monitor = Mock.Of<IOptionsMonitor<AffinityOptions>>(m => m.CurrentValue == opts);
        return (new AffinityRung(store, monitor, NullLogger<AffinityRung>.Instance), ds);
    }

    private static NormalizedMessage Message(string? from = "counsel@firm.com", string? subject = "Status update on the filing", string[]? to = null) =>
        new()
        {
            Direction = CommunicationDirection.Incoming,
            From = from,
            To = to ?? new[] { "intake@ourfirm.com" },
            Subject = subject,
        };

    // ── Surfacing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_AffinityRowAboveMinConfirmations_SurfacesTargetAsMatchCitingCount()
    {
        var matterId = Guid.NewGuid();
        var opts = new AffinityOptions { Enabled = true, MinConfirmations = 3, SuggestConfidence = 0.60 };
        var (rung, _) = BuildRung(opts, AffinityRow(Matter, matterId, count: 5, AffinitySignalType.Sender, "counsel@firm.com"));

        var matches = await rung.EvaluateAsync(Message(), new AssociationContext { TenantKey = "t1" }, CancellationToken.None);

        matches.Should().HaveCount(1);
        var m = matches[0];
        m.RegardingFieldName.Should().Be(RegardingMatter);
        m.Target!.LogicalName.Should().Be(Matter);
        m.Target.Id.Should().Be(matterId);
        m.Rung.Should().Be(RungKind.Affinity);
        m.Confidence.Should().Be(0.60);
        m.Provenance.Should().Contain("confirmations=5", "the provenance must cite the confirmation count so the suggestion is explainable");
    }

    [Fact]
    public async Task EvaluateAsync_NoQualifyingAffinityRow_ReturnsEmpty()
    {
        var opts = new AffinityOptions { Enabled = true };
        var (rung, _) = BuildRung(opts /* no rows */);

        var matches = await rung.EvaluateAsync(Message(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_TargetEntityNotInRegardingMap_ReturnsEmpty()
    {
        var opts = new AffinityOptions { Enabled = true };
        var (rung, _) = BuildRung(opts, AffinityRow("sprk_notaregardingtarget", Guid.NewGuid(), 9, AffinitySignalType.Sender, "counsel@firm.com"));

        var matches = await rung.EvaluateAsync(Message(), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty("an affinity target whose entity has no ADR-024 regarding field cannot be written and is a non-match");
    }

    // ── Kill-switch (no Dataverse cost) ──────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_GloballyDisabled_ReturnsEmptyAndDoesNotQueryDataverse()
    {
        var opts = new AffinityOptions { Enabled = false };
        var (rung, ds) = BuildRung(opts, AffinityRow(Matter, Guid.NewGuid(), 9, AffinitySignalType.Sender, "counsel@firm.com"));

        var matches = await rung.EvaluateAsync(Message(), new AssociationContext { TenantKey = "t1" }, CancellationToken.None);

        matches.Should().BeEmpty();
        ds.Verify(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never,
            "a disabled kill-switch must spend no Dataverse cost");
    }

    [Fact]
    public async Task EvaluateAsync_DisabledForTenantViaOverride_ReturnsEmpty()
    {
        var opts = new AffinityOptions { Enabled = true, Tenants = { ["t-off"] = false } };
        var (rung, ds) = BuildRung(opts, AffinityRow(Matter, Guid.NewGuid(), 9, AffinitySignalType.Sender, "counsel@firm.com"));

        var matches = await rung.EvaluateAsync(Message(), new AssociationContext { TenantKey = "t-off" }, CancellationToken.None);

        matches.Should().BeEmpty();
        ds.Verify(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The load-bearing guarantee: NEVER auto-files ─────────────────────────────

    [Theory]
    [InlineData(0.60)]
    [InlineData(0.99)]
    public void Decide_AffinityMatchAtAnyConfidence_NeverAutoFiles(double confidence)
    {
        // Feed a lone affinity match on a CORE matter (the strongest case for auto-file) through the REAL mapper.
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);
        var match = new RungMatch
        {
            RegardingFieldName = RegardingMatter,
            Target = new EntityReference(Matter, Guid.NewGuid()),
            Confidence = confidence,
            Provenance = "affinity:signal=Sender:confirmations=42",
            Rung = RungKind.Affinity,
        };

        var decision = mapper.Decide(new[] { match }, CommunicationDirection.Incoming, tenantKey: null);

        decision.AutoFiled.Should().BeFalse("an affinity signal is excluded from the mapper's auto-file-eligible set");
        decision.Status.Should().NotBe(AssociationStatusCodes.Resolved, "affinity is SUGGEST-ONLY — at most Suggested, never Resolved");
    }

    // ── Signal extraction (pure logic) ───────────────────────────────────────────

    [Fact]
    public void ExtractSignals_TypicalMessage_ProducesSenderDomainKeywordAndParticipantSignals()
    {
        var opts = new AffinityOptions { MaxSubjectKeywords = 5, MinKeywordLength = 4 };
        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "Counsel@Firm.com",
            To = new[] { "Intake@ourfirm.com" },
            Cc = new[] { "paralegal@firm.com" },
            Subject = "Re: Discovery deadline for Acme",
        };

        var signals = AffinityRung.ExtractSignals(message, opts);

        signals.Should().Contain(s => s.Type == AffinitySignalType.Sender && s.Value == "counsel@firm.com");
        signals.Should().Contain(s => s.Type == AffinitySignalType.SenderDomain && s.Value == "firm.com");
        signals.Should().Contain(s => s.Type == AffinitySignalType.SubjectKeyword && s.Value == "discovery");
        signals.Should().Contain(s => s.Type == AffinitySignalType.SubjectKeyword && s.Value == "deadline");
        signals.Should().NotContain(s => s.Type == AffinitySignalType.SubjectKeyword && s.Value == "re",
            "short/noise tokens below MinKeywordLength are dropped");
        // Participant set = from + to + cc, lower-cased, distinct, sorted (ordinal).
        signals.Should().ContainSingle(s => s.Type == AffinitySignalType.ParticipantSet)
            .Which.Value.Should().Be("counsel@firm.com;intake@ourfirm.com;paralegal@firm.com");
    }

    [Fact]
    public void ExtractSignals_EmptyEnvelope_ProducesNoSignals()
    {
        var signals = AffinityRung.ExtractSignals(
            new NormalizedMessage { Direction = CommunicationDirection.Incoming }, new AffinityOptions());

        signals.Should().BeEmpty();
    }

    // ── Increment-on-confirmation writer (best-effort) ───────────────────────────

    [Fact]
    public async Task RecordConfirmationAsync_ExistingRow_IncrementsCount()
    {
        var existing = new Entity("sprk_affinity", Guid.NewGuid()) { ["sprk_confirmationcount"] = 2 };
        var found = new EntityCollection();
        found.Entities.Add(existing);

        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict);
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(found);
        Dictionary<string, object>? updatedFields = null;
        ds.Setup(g => g.UpdateAsync("sprk_affinity", existing.Id, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => updatedFields = f)
            .Returns(Task.CompletedTask);

        var store = new AffinityStore(ds.Object, NullLogger<AffinityStore>.Instance);
        await store.RecordConfirmationAsync(AffinitySignalType.Sender, "counsel@firm.com", Matter, Guid.NewGuid().ToString("D"), "t1");

        updatedFields.Should().NotBeNull();
        updatedFields!["sprk_confirmationcount"].Should().Be(3, "an existing affinity row is incremented, not reset");
        updatedFields.Should().ContainKey("sprk_lastconfirmed");
        ds.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordConfirmationAsync_NoExistingRow_CreatesWithCountOne()
    {
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict);
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        Entity? created = null;
        ds.Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created = e)
            .ReturnsAsync(Guid.NewGuid());

        var store = new AffinityStore(ds.Object, NullLogger<AffinityStore>.Instance);
        await store.RecordConfirmationAsync(AffinitySignalType.SenderDomain, "firm.com", Matter, Guid.NewGuid().ToString("D"), tenantKey: "t1");

        created.Should().NotBeNull();
        created!.GetAttributeValue<int>("sprk_confirmationcount").Should().Be(1);
        created.GetAttributeValue<OptionSetValue>("sprk_signaltype").Value.Should().Be((int)AffinitySignalType.SenderDomain);
        created.GetAttributeValue<string>("sprk_tenantkey").Should().Be("t1");
    }

    [Fact]
    public async Task RecordConfirmationAsync_WhenDataverseThrows_DoesNotThrow()
    {
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict);
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var store = new AffinityStore(ds.Object, NullLogger<AffinityStore>.Instance);
        var act = async () => await store.RecordConfirmationAsync(AffinitySignalType.Sender, "x@y.com", Matter, Guid.NewGuid().ToString("D"), "t1");

        await act.Should().NotThrowAsync("recording affinity is best-effort — a confirmation must never fail because the learning write did");
    }
}
