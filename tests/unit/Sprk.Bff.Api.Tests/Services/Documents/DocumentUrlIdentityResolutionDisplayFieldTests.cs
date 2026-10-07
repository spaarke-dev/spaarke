using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// <see cref="DocumentUrlIdentityResolution.ResolveRelatedRecordDisplayAsync"/> (spaarkeai-word-add-in-r1 task
/// 026, FR-09) — the descriptive-name/number pair the related-record card renders.
/// </summary>
/// <remarks>
/// A SEPARATE file from <see cref="DocumentUrlIdentityResolutionTests"/> deliberately: that file's
/// <c>IGenericEntityService</c> mock is STRICT and asserts NO extra Dataverse call happens inside
/// <c>ResolveAsync</c> for a populated related record — proving this method is called only later, from the
/// <c>resolve-identity</c> HANDLER, after <c>DocumentAuthorizationFilter</c> has already run. Adding coverage
/// here (a method the class exposes, exercised directly) means that invariant does not need touching.
///
/// The core fact under test (verified live against Dataverse metadata, 2026-09-14,
/// <c>notes/026-slot-scope-decision.md</c> §3): <c>sprk_matter</c>/<c>sprk_project</c>'s PRIMARY NAME
/// attribute IS their NUMBER, so <see cref="EntityReference.Name"/> is already the number for those two —
/// the complementary Dataverse fetch retrieves the DESCRIPTIVE name. <c>sprk_invoice</c>/
/// <c>sprk_workassignment</c> are the opposite: <see cref="EntityReference.Name"/> is already the descriptive
/// name, and the complementary fetch retrieves the NUMBER.
/// </remarks>
public class DocumentUrlIdentityResolutionDisplayFieldTests
{
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);

    // ── Matter / Project: primary name IS the number ────────────────────────────────────────────

    [Fact]
    public async Task Matter_FetchesTheDescriptiveName_AndTreatsEntityReferenceNameAsTheNumber()
    {
        var matterId = Guid.NewGuid();
        var related = new EntityReference("sprk_matter", matterId) { Name = "PAT-191111" };
        Returns("sprk_matter", matterId, "sprk_mattername", "Acme v Globex");

        var display = await Resolve(related);

        display.Number.Should().Be("PAT-191111", "sprk_matter's primary name attribute IS sprk_matternumber");
        display.DisplayName.Should().Be("Acme v Globex", "the descriptive name is a separate, non-primary column");
    }

    [Fact]
    public async Task Project_FetchesTheDescriptiveName_AndTreatsEntityReferenceNameAsTheNumber()
    {
        var projectId = Guid.NewGuid();
        var related = new EntityReference("sprk_project", projectId) { Name = "PROJ-2025-014" };
        Returns("sprk_project", projectId, "sprk_projectname", "Q1 Patent Filing");

        var display = await Resolve(related);

        display.Number.Should().Be("PROJ-2025-014");
        display.DisplayName.Should().Be("Q1 Patent Filing");
    }

    [Fact]
    public async Task Matter_WithABlankNumber_RendersTheNumberBlank_NotAnError()
    {
        // A pane-created Matter has no number until the separate numbering project ships
        // (notes/030-numbering-handoff.md). EntityReference.Name is then null/empty.
        var matterId = Guid.NewGuid();
        var related = new EntityReference("sprk_matter", matterId) { Name = null };
        Returns("sprk_matter", matterId, "sprk_mattername", "Acme v Globex");

        var display = await Resolve(related);

        display.Number.Should().BeNull();
        display.DisplayName.Should().Be("Acme v Globex");
    }

    // ── Invoice / WorkAssignment: primary name IS the descriptive name ─────────────────────────

    [Fact]
    public async Task Invoice_FetchesTheNumber_AndTreatsEntityReferenceNameAsTheDescriptiveName()
    {
        var invoiceId = Guid.NewGuid();
        var related = new EntityReference("sprk_invoice", invoiceId) { Name = "March retainer" };
        Returns("sprk_invoice", invoiceId, "sprk_invoicenumber", "INV-000482");

        var display = await Resolve(related);

        display.DisplayName.Should().Be("March retainer", "sprk_invoice's primary name attribute IS sprk_name");
        display.Number.Should().Be("INV-000482", "the number is a separate, non-primary column");
    }

    [Fact]
    public async Task WorkAssignment_FetchesTheNumber_AndTreatsEntityReferenceNameAsTheDescriptiveName()
    {
        var waId = Guid.NewGuid();
        var related = new EntityReference("sprk_workassignment", waId) { Name = "Discovery response" };
        Returns("sprk_workassignment", waId, "sprk_workassignmentnumber", "WA-000117");

        var display = await Resolve(related);

        display.DisplayName.Should().Be("Discovery response");
        display.Number.Should().Be("WA-000117");
    }

    // ── Best-effort: the complementary fetch failing must not fail the whole card ──────────────

    [Fact]
    public async Task ComplementaryFetchThrows_ReturnsTheHalfItHas_NeverThrows()
    {
        var matterId = Guid.NewGuid();
        var related = new EntityReference("sprk_matter", matterId) { Name = "PAT-191111" };
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", matterId, It.Is<string[]>(c => c.Single() == "sprk_mattername"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sprk_matter record not found"));

        var display = await Resolve(related);

        display.Number.Should().Be("PAT-191111", "the half already known (EntityReference.Name) is still returned");
        display.DisplayName.Should().BeNull("the failed half renders blank, not a 503 for the whole identity response");
    }

    [Fact]
    public async Task CallerCancellation_DuringTheComplementaryFetch_PropagatesAsCancellation_NotSwallowed()
    {
        var matterId = Guid.NewGuid();
        var related = new EntityReference("sprk_matter", matterId) { Name = "PAT-191111" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", matterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("A task was canceled."));

        var act = () => DocumentUrlIdentityResolution.ResolveRelatedRecordDisplayAsync(
            related, _dataverse.Object, NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Defensive: an entity type outside the four direct slots ────────────────────────────────

    [Fact]
    public async Task UnmappedEntityType_ReturnsEntityReferenceNameAsDisplayName_MakesNoDataverseCall()
    {
        // Strict mock with no setup: any call would fail this test. FirstRelatedRecord never returns anything
        // outside RelatedRecordAttributes in production, so this only proves the defensive branch is safe.
        var id = Guid.NewGuid();
        var related = new EntityReference("sprk_contact", id) { Name = "Jane Doe" };

        var display = await Resolve(related);

        display.DisplayName.Should().Be("Jane Doe");
        display.Number.Should().BeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private Task<DocumentUrlIdentityResolution.RelatedRecordDisplay> Resolve(EntityReference related)
        => DocumentUrlIdentityResolution.ResolveRelatedRecordDisplayAsync(
            related, _dataverse.Object, NullLogger.Instance, CancellationToken.None);

    private void Returns(string logicalName, Guid id, string column, string value)
        => _dataverse
            .Setup(d => d.RetrieveAsync(logicalName, id, It.Is<string[]>(c => c.Length == 1 && c[0] == column),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(logicalName, id) { [column] = value });
}
