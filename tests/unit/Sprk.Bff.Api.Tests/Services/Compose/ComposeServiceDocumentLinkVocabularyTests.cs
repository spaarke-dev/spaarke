using System.Linq;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Pins the invariant the unified-access-control-r2 hoist (2026-09-05) exists to create:
/// <see cref="ComposeService.DocumentAssociationLookupAttributes"/> and
/// <c>AttachmentDocumentAssociationRung</c>'s document-link iteration must NEVER independently fork
/// away from <see cref="DocumentLinkFields"/> again.
/// </summary>
/// <remarks>
/// <para>
/// Before the hoist, <c>ComposeService.DocumentAssociationLookupAttributes</c> was a hand-written
/// <c>string[]</c> literal — the SAME six entries as <c>AttachmentDocumentAssociationRung</c>'s own
/// hand-written list, and both were incomplete. This test does not need a counterpart list to compare
/// against on the Rung's side any more, because the Rung no longer HAS one — it reads
/// <see cref="DocumentLinkFields.All"/> directly (see
/// <c>AttachmentDocumentAssociationRungTests.Evaluate_DocumentLinkedThroughEveryVocabularyColumn_SurfacesAMatchForEveryMappedTarget</c>
/// for the Rung-side behavioral proof). What CAN still regress is <c>ComposeService</c> reverting to a
/// hand-written literal — this test exists to catch exactly that regression, and was verified to catch
/// it: temporarily reverting <c>DocumentAssociationLookupAttributes</c> to the old 6-entry literal
/// turned this test red (see the hoist decision note for the perturbation record).
/// </para>
/// <para>KEEP path: tests/unit/domain-equivalent (Compose service-layer unit test, no I/O, no mocks —
/// pure static-data equality).</para>
/// </remarks>
public class ComposeServiceDocumentLinkVocabularyTests
{
    [Fact]
    public void DocumentAssociationLookupAttributes_NeverForksFromTheSharedVocabulary()
    {
        ComposeService.DocumentAssociationLookupAttributes.Should().Equal(DocumentLinkFields.LogicalNames,
            "ComposeService must consume the ONE shared Spaarke.Dataverse.DocumentLinkFields vocabulary, " +
            "never a locally hand-written copy — a local fork is the exact drift that produced two " +
            "independently-incomplete lists before the 2026-09-05 hoist");
    }

    [Fact]
    public void DocumentAssociationLookupAttributes_ContainsAllSixteenLiveVerifiedColumns()
    {
        // Belt-and-suspenders: even if some future change made the equality check above pass by
        // coincidence (e.g. both sides forked identically), this independently pins the count and the
        // two columns the original two-copy vocabulary was missing.
        ComposeService.DocumentAssociationLookupAttributes.Should().HaveCount(16);
        ComposeService.DocumentAssociationLookupAttributes.Should().Contain("sprk_relatedinvoice",
            "invisible to both pre-hoist copies — a document linked ONLY via this column was unreachable");
        ComposeService.DocumentAssociationLookupAttributes.Should().Contain("sprk_relatedworkassignment",
            "invisible to both pre-hoist copies — a document linked ONLY via this column was unreachable");
    }
}
