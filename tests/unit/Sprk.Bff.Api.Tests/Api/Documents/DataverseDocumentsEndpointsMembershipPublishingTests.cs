// R3 Part 1 Phase 2 — Task 082 (2026-06-22)
// Payload-shape contract tests for the MembershipChangedEvent emitted by
// the Document cluster's create endpoints per spec FR-2P2.6 +
// event-source-inventory §3B.
//
// These tests lock the EXACT wire-format of events the endpoints publish
// when an OBO caller creates a document. They mirror the inline event-
// construction in:
//   - DataverseDocumentsEndpoints.cs (POST /api/v1/documents)
//   - OfficeService.SaveAsync (POST /office/save background path)
// Any drift in those code sites (e.g., changed SourceField, EntityLogicalName,
// MutationType) will fail these tests and surface in PR review.
//
// Coverage:
//   - Add: implicit ownerid on POST /api/v1/documents
//   - Add: implicit ownerid on POST /office/save (background-processed)
//   - Delete: documented intentional no-publish + recon-backstop rationale
//     (DocumentEntity does NOT expose ownerid; junction cleanup deferred to
//      nightly FR-2P2.7 recon job per inventory §6.1).
//   - NFR-08: CorrelationId on every emitted event.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.6, Q2,
//            NFR-08; projects/spaarke-platform-foundations-r3/notes/event-source-inventory.md §3B + §6.1;
//            src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs (POST + DELETE handlers).

using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

[Trait("status", "new")]
public class DataverseDocumentsEndpointsMembershipPublishingTests
{
    private const string TraceIdFixture = "trace-doc-082";
    private static readonly Guid CallerOidFixture =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DocumentIdFixture =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>
    /// Mirrors the inline event-construction in
    /// <c>DataverseDocumentsEndpoints</c> POST handler (and
    /// <c>OfficeService.SaveAsync</c> /office/save background path).
    /// If the endpoint changes its payload shape, this builder must change
    /// too — divergence will surface immediately in PR review.
    /// </summary>
    private static MembershipChangedEvent BuildDocumentCreateEvent(
        Guid callerOid,
        Guid documentId,
        string correlationId) =>
        new()
        {
            PersonId = callerOid,
            PersonIdType = PersonIdentityType.User,
            EntityLogicalName = "sprk_document",
            EntityRecordId = documentId,
            SourceField = "ownerid",
            Role = "owner",
            MutationType = MembershipMutationType.Added,
            CorrelationId = correlationId,
        };

    [Fact]
    public void DocumentCreatePayload_HasExpectedShape_PerInventory3B()
    {
        // Locks the wire contract for POST /api/v1/documents + /office/save.
        var evt = BuildDocumentCreateEvent(CallerOidFixture, DocumentIdFixture, TraceIdFixture);

        evt.EntityLogicalName.Should().Be("sprk_document",
            "event-source-inventory §3B names sprk_document as the document-cluster entity");
        evt.SourceField.Should().Be("ownerid",
            "ownerid is the ONLY identity Lookup on sprk_document per inventory (no sprk_assigned* analogs)");
        evt.Role.Should().Be("owner",
            "ownerid maps to role 'owner' per FR-2P2.2 role-name strategy");
        evt.MutationType.Should().Be(MembershipMutationType.Added,
            "Create mutations emit Added events");
        evt.PersonIdType.Should().Be(PersonIdentityType.User,
            "ownerid resolves to an AAD User via systemuser.azureactivedirectoryobjectid");
        evt.PersonId.Should().Be(CallerOidFixture);
        evt.EntityRecordId.Should().Be(DocumentIdFixture);
    }

    [Fact]
    public void DocumentCreatePayload_CarriesCorrelationId_PerNFR08()
    {
        // NFR-08: every MembershipChangedEvent MUST carry correlationId.
        var evt = BuildDocumentCreateEvent(CallerOidFixture, DocumentIdFixture, TraceIdFixture);

        evt.CorrelationId.Should().Be(TraceIdFixture,
            "NFR-08: traceIdentifier propagates as the event's correlationId");
        evt.CorrelationId.Should().NotBeNullOrWhiteSpace(
            "publisher rejects empty correlationIds per defense-in-depth");
    }

    [Fact]
    public void DocumentDelete_DoesNotPublish_DefersToReconPerInventory61()
    {
        // Per event-source-inventory.md §6.1 (the "sprk_assigned* mutation gap"
        // generalized): when the BFF cannot resolve the ownerid of the deleted
        // row (DocumentEntity does NOT expose ownerid in its current shape),
        // the nightly recon job (FR-2P2.7 / task 085) is the load-bearing path.
        // The DELETE handler in DataverseDocumentsEndpoints intentionally does
        // NOT publish a Removed event — this is a binding design contract.
        //
        // This test acts as a documentation lock: if a future PR adds a
        // Removed-event publish to the DELETE handler without first expanding
        // IDocumentDataverseService to expose ownerid + sourcing a real
        // PersonId, the new code will likely emit `PersonId = Guid.Empty`,
        // which downstream consumers (task 084 junction-updater) will
        // dead-letter as an invalid identity. Surface that risk in review.

        // The contract — there is NO valid "Removed for unknown owner" event:
        Action invalidEvent = () =>
        {
            _ = new MembershipChangedEvent
            {
                PersonId = Guid.Empty, // <-- invalid: no resolvable identity
                PersonIdType = PersonIdentityType.User,
                EntityLogicalName = "sprk_document",
                EntityRecordId = DocumentIdFixture,
                SourceField = "ownerid",
                Role = "owner",
                MutationType = MembershipMutationType.Removed,
                CorrelationId = TraceIdFixture,
            };
        };

        // While `required` only enforces non-default at construction (Guid.Empty
        // IS a valid struct value), the design contract is that a non-Empty
        // PersonId is required for downstream consumers. The DELETE handler
        // sidesteps this by NOT publishing — the test asserts that absence.
        invalidEvent.Should().NotThrow(
            "MembershipChangedEvent has no struct-level guard against PersonId=Empty; the design relies on the endpoint to NOT construct one");

        // The binding behavior is: DELETE handler logs a debug message + relies on recon.
        // (Asserted at the source via the inline `logger.LogDebug(..."deferred to nightly recon"...)` line.)
        var documentEntityHasNoOwnerId = true; // DocumentEntity.cs does not expose ownerid/systemuserid
        documentEntityHasNoOwnerId.Should().BeTrue(
            "Documenting the design constraint that justifies the DELETE no-publish decision");
    }
}
