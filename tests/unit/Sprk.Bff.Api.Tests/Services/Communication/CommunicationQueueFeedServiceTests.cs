using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the FR-17 ranked-exceptions queue-feed (task 032). Mirrors
/// <see cref="CommunicationThreadReadServiceTests"/>'s boundary-mock shape (ADR-038): the impersonated
/// <c>sprk_communications</c> read (<see cref="IImpersonatedCommunicationQuery"/>) and the caller resolver are
/// mocked; the REAL <see cref="CommunicationAccessFilter"/> runs (pure, cheap). The <c>sprk_emailreviewlog</c>
/// proposal read (<see cref="IGenericEntityService"/>) is mocked the same way task 030's seam tests model it.
/// </summary>
public class CommunicationQueueFeedServiceTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string CommunicationSet = "sprk_communications";
    private const int PrivilegeNone = 100000000;

    // As-built sprk_emailreviewlog.sprk_action option-set integers (task 012 verification; mirrors the
    // production constants in CommunicationQueueFeedService / CommunicationEnrichmentService).
    private const int ActionProposed = 100000001;
    private const int ActionApproved = 100000002;
    private const int ActionDismissed = 100000004;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new(MockBehavior.Strict);
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();
    private readonly Mock<ISystemUserIdentityResolver> _identity = new();
    private readonly Mock<IGenericEntityService> _entityService = new(MockBehavior.Strict);

    private CommunicationQueueFeedService Sut(bool isExternalCaller = false)
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identity
            .Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isExternalCaller);

        return new CommunicationQueueFeedService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            _identity.Object,
            _entityService.Object,
            Mock.Of<ILogger<CommunicationQueueFeedService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private void SetupCandidates(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    private void SetupReviewLog(params Entity[] rows) =>
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    // ─────────────────────────── (a) ranked feed: pending proposal + Suggested/Ambiguous associations ──────

    [Fact]
    public async Task GetQueueFeedAsync_ScopeWithProposalAndAssociationExceptions_ReturnsBothKindsExcludingResolved()
    {
        var suggestedId = Guid.NewGuid();
        var ambiguousId = Guid.NewGuid();
        var resolvedNoProposalId = Guid.NewGuid(); // Resolved + no open proposal — must NOT appear
        var proposalCommId = Guid.NewGuid(); // Resolved BUT carries an open Job B proposal — must appear as pending-proposal

        SetupCandidates(
            CandidateRow(suggestedId, "Suggested email", AssociationStatusCodes.Suggested, priority: 100000001, riConfidence: 0.7),
            CandidateRow(ambiguousId, "Ambiguous email", AssociationStatusCodes.Ambiguous, priority: 100000000, riConfidence: 0.9),
            CandidateRow(resolvedNoProposalId, "Resolved email", AssociationStatusCodes.Resolved, priority: 100000002, riConfidence: 0.5),
            CandidateRow(proposalCommId, "Proposal email", AssociationStatusCodes.Resolved, priority: 100000001, riConfidence: 0.6));

        SetupReviewLog(ProposedRow(
            proposalCommId, "sprk_matter", "sprk_closingdate", confidence: 0.9m,
            suggestionJson: """
                {"fieldType":"DateTime","oldValue":"2026-01-01","newValue":"2026-08-15",
                 "citation":{"source":"body","locator":"body: sentence 1","quotedText":"moved to August 15"},
                 "reason":"date change","requireConfirm":true,"privilegeFlagged":false}
                """));

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        result.Items.Should().HaveCount(3);
        result.Items.Select(i => i.CommunicationId).Should().Contain(new[] { suggestedId, ambiguousId, proposalCommId });
        result.Items.Select(i => i.CommunicationId).Should().NotContain(resolvedNoProposalId);

        var proposalItem = result.Items.Single(i => i.CommunicationId == proposalCommId);
        proposalItem.Kind.Should().Be(QueueFeedItemKinds.PendingProposal);
        proposalItem.AssociationStatus.Should().BeNull();
        proposalItem.TargetEntity.Should().Be("sprk_matter");
        proposalItem.TargetField.Should().Be("sprk_closingdate");
        proposalItem.OldValue.Should().Be("2026-01-01");
        proposalItem.NewValue.Should().Be("2026-08-15");
        proposalItem.CitationQuotedText.Should().Be("moved to August 15");
        proposalItem.RequireConfirm.Should().BeTrue();

        var ambiguousItem = result.Items.Single(i => i.CommunicationId == ambiguousId);
        ambiguousItem.Kind.Should().Be(QueueFeedItemKinds.AssociationException);
        ambiguousItem.AssociationStatus.Should().Be("Ambiguous");
        ambiguousItem.ReviewLogId.Should().BeNull();

        var suggestedItem = result.Items.Single(i => i.CommunicationId == suggestedId);
        suggestedItem.AssociationStatus.Should().Be("Suggested");
    }

    [Fact]
    public async Task GetQueueFeedAsync_ProposedRowWithLaterTerminalRow_IsExcludedAsClosed()
    {
        // The "open" definition (030): a Proposed row with a LATER terminal row (Approved/Applied/Dismissed/
        // Overriden) for the same (communication, targetEntity, targetField) is CLOSED — must not surface.
        var commId = Guid.NewGuid();
        SetupCandidates(CandidateRow(commId, "Closed proposal email", AssociationStatusCodes.Resolved, priority: 100000001, riConfidence: 0.6));

        // Chronological order: Proposed opens, Approved closes it (mock supplies rows already createdon-ascending,
        // matching what the real AddOrder(createdon, Ascending) query would return).
        SetupReviewLog(
            ProposedRow(commId, "sprk_matter", "sprk_closingdate", confidence: 0.9m, suggestionJson: "{}", createdOn: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            TerminalRow(commId, "sprk_matter", "sprk_closingdate", ActionApproved, createdOn: new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)));

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        result.Items.Should().BeEmpty("the proposal was approved (closed) after being proposed — it is no longer open");
    }

    [Fact]
    public async Task GetQueueFeedAsync_ReProposedAfterDismissal_ReopensAsPending()
    {
        // A NEW Proposed row after a Dismissed terminal row (append-only re-propose) re-opens the tuple.
        var commId = Guid.NewGuid();
        SetupCandidates(CandidateRow(commId, "Re-proposed email", AssociationStatusCodes.Resolved, priority: 100000001, riConfidence: 0.6));

        SetupReviewLog(
            ProposedRow(commId, "sprk_matter", "sprk_closingdate", confidence: 0.8m, suggestionJson: "{\"newValue\":\"2026-08-01\"}", createdOn: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            TerminalRow(commId, "sprk_matter", "sprk_closingdate", ActionDismissed, createdOn: new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)),
            ProposedRow(commId, "sprk_matter", "sprk_closingdate", confidence: 0.95m, suggestionJson: "{\"newValue\":\"2026-08-15\"}", createdOn: new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)));

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(QueueFeedItemKinds.PendingProposal);
        item.NewValue.Should().Be("2026-08-15", "the re-proposed (latest, currently-open) row must win, not the dismissed one");
    }

    // ─────────────────────────── (b) ranking reuses existing priority + RI-confidence (no 2nd scheme) ─────

    [Fact]
    public async Task GetQueueFeedAsync_RanksByExistingTriagePriorityThenRiConfidence_NoSecondScheme()
    {
        var urgentId = Guid.NewGuid();
        var highId = Guid.NewGuid();
        var mediumId = Guid.NewGuid();

        SetupCandidates(
            // Deliberately supplied out of urgency order, and the Medium item has the HIGHEST riconfidence, to
            // prove priority (the existing signal) is the primary key and riconfidence is only the tiebreaker —
            // not a recomputed/blended second score.
            CandidateRow(mediumId, "Medium", AssociationStatusCodes.Suggested, priority: 100000002, riConfidence: 0.95),
            CandidateRow(urgentId, "Urgent", AssociationStatusCodes.Suggested, priority: 100000000, riConfidence: 0.10),
            CandidateRow(highId, "High", AssociationStatusCodes.Suggested, priority: 100000001, riConfidence: 0.50));
        SetupReviewLog(); // no proposals in scope

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        result.Items.Select(i => i.CommunicationId).Should().ContainInOrder(urgentId, highId, mediumId);

        // The values ride through UNCHANGED off the already-persisted row — no re-scoring, no new formula (D-08).
        var urgentItem = result.Items.Single(i => i.CommunicationId == urgentId);
        urgentItem.TriagePriority.Should().Be(100000000);
        urgentItem.RiConfidence.Should().Be(0.10);
    }

    [Fact]
    public async Task GetQueueFeedAsync_PendingProposalItem_RanksOnParentCommunicationSignalsNotItsOwnConfidence()
    {
        // D-08: a pending-proposal item's rank inputs are the PARENT communication's triage priority + RI-confidence
        // — never its own extraction confidence (sprk_confidence), which is display-only (ProposalConfidence).
        var commId = Guid.NewGuid();
        SetupCandidates(CandidateRow(commId, "Low priority but proposal", AssociationStatusCodes.Resolved, priority: 100000003, riConfidence: 0.2));
        SetupReviewLog(ProposedRow(commId, "sprk_matter", "sprk_closingdate", confidence: 0.99m, suggestionJson: "{}"));

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.TriagePriority.Should().Be(100000003, "the rank input is the PARENT communication's priority");
        item.RiConfidence.Should().Be(0.2, "the rank input is the PARENT communication's RI-confidence");
        item.ProposalConfidence.Should().Be(0.99, "the proposal's own confidence is exposed for display only");
    }

    // ─────────────────────────── (c) NEGATIVE: no over-disclosure ─────────────────────────────────────────

    [Fact]
    public async Task GetQueueFeedAsync_InternalOnlyCommunicationForExternalCaller_IsAbsentFromFeedEvenWithAnOpenProposal()
    {
        // The SAME shared CommunicationAccessFilter every other communication read uses: an sprk_isinternalonly
        // row is hidden from a non-internal (external) caller. The proposal riding on that hidden communication
        // must ALSO be absent — a caller who cannot see the communication must not see a proposal about it either.
        var hiddenId = Guid.NewGuid();
        var visibleId = Guid.NewGuid();

        SetupCandidates(
            CandidateRow(hiddenId, "Internal-only email", AssociationStatusCodes.Suggested, priority: 100000000, riConfidence: 0.9, internalOnly: true),
            CandidateRow(visibleId, "Visible email", AssociationStatusCodes.Suggested, priority: 100000001, riConfidence: 0.5));
        SetupReviewLog(ProposedRow(hiddenId, "sprk_matter", "sprk_closingdate", confidence: 0.9m, suggestionJson: "{}"));

        var result = await Sut(isExternalCaller: true).GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items.Single().CommunicationId.Should().Be(visibleId);
        result.Items.Select(i => i.CommunicationId).Should().NotContain(hiddenId);
    }

    [Fact]
    public async Task GetQueueFeedAsync_UnresolvedCaller_ThrowsForbiddenAndNeverQueries()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var sut = new CommunicationQueueFeedService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            _identity.Object,
            _entityService.Object,
            Mock.Of<ILogger<CommunicationQueueFeedService>>());

        var act = () => sut.GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _query.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unresolved caller must never issue a query (fail closed, no app-only fallback)");
    }

    // ─────────────────────────── (d) dual-use DTO — one shape, no surface fork ────────────────────────────

    [Fact]
    public void QueueFeedItem_DualUseShape_ExposesBothAssociationAndProposalFieldGroupsOnOneType()
    {
        // D-10: ONE dual-use DTO both r5 surfaces render — Kind discriminates which group of fields is populated,
        // but both groups live on the SAME record type (no per-kind subtype / no surface fork).
        var propertyNames = typeof(QueueFeedItem).GetProperties().Select(p => p.Name).ToList();

        propertyNames.Should().Contain(new[] { "Kind", "AssociationStatus" }); // association-exception fields
        propertyNames.Should().Contain(new[]
        {
            "ReviewLogId", "TargetEntity", "TargetField", "OldValue", "NewValue", "ProposalConfidence",
        }); // pending-proposal fields
        propertyNames.Should().Contain(new[] { "TriagePriority", "RiConfidence" }); // shared ranking inputs
    }

    [Fact]
    public async Task GetQueueFeedAsync_AssociationExceptionAndPendingProposal_ShareOneListOfTheSameRuntimeType()
    {
        var associationId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        SetupCandidates(
            CandidateRow(associationId, "Suggested", AssociationStatusCodes.Suggested, priority: 100000000, riConfidence: 0.9),
            CandidateRow(proposalId, "Resolved w/ proposal", AssociationStatusCodes.Resolved, priority: 100000001, riConfidence: 0.6));
        SetupReviewLog(ProposedRow(proposalId, "sprk_matter", "sprk_closingdate", confidence: 0.9m, suggestionJson: "{}"));

        var result = await Sut().GetQueueFeedAsync(regarding: null, top: null, Caller(), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllBeOfType<QueueFeedItem>();
        result.Items.Select(i => i.Kind).Should().BeEquivalentTo(new[]
        {
            QueueFeedItemKinds.AssociationException, QueueFeedItemKinds.PendingProposal,
        });
    }

    // ─────────────────────────── row builders ──────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> CandidateRow(
        Guid id, string subject, int? associationStatus, int? priority, double? riConfidence,
        bool internalOnly = false, int privilege = PrivilegeNone)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["createdon"] = El("2026-07-16T12:00:00Z"),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_privilegeclassification"] = El(privilege),
            ["sprk_subject"] = El(subject),
        };
        if (associationStatus is not null) row["sprk_associationstatus"] = El(associationStatus.Value);
        if (priority is not null) row["sprk_triagepriority"] = El(priority.Value);
        if (riConfidence is not null) row["sprk_riconfidence"] = El(riConfidence.Value);
        return row;
    }

    private static Entity ProposedRow(
        Guid communicationId, string targetEntity, string targetField, decimal confidence, string suggestionJson,
        DateTime? createdOn = null) =>
        new("sprk_emailreviewlog", Guid.NewGuid())
        {
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_action"] = new OptionSetValue(ActionProposed),
            ["sprk_targetentity"] = targetEntity,
            ["sprk_targetrecordid"] = Guid.NewGuid().ToString(),
            ["sprk_targetfield"] = targetField,
            ["sprk_confidence"] = confidence,
            ["sprk_aisuggestion"] = suggestionJson,
            ["createdon"] = createdOn ?? new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        };

    private static Entity TerminalRow(
        Guid communicationId, string targetEntity, string targetField, int action, DateTime createdOn) =>
        new("sprk_emailreviewlog", Guid.NewGuid())
        {
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_action"] = new OptionSetValue(action),
            ["sprk_targetentity"] = targetEntity,
            ["sprk_targetfield"] = targetField,
            ["createdon"] = createdOn,
        };

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
