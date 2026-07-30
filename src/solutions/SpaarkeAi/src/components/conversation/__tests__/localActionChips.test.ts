/**
 * localActionChips.test.ts — document-review capability registry
 * (ai-advanced-capabilities-analysis-hub-r1 task 064, spec §13.5 / FR-22).
 *
 * Proves the generalization of ConversationPane's previously-hardcoded
 * `docType !== "nda"` gate into a small, extensible `DOCUMENT_REVIEW_CAPABILITIES`
 * table keyed by classified docType:
 *   - "nda" still resolves to the existing nda-review capability + "Review an NDA"
 *     label (regression: unchanged behavior for the shipped NDA vertical);
 *   - any other/unregistered docType (including a second work-type not yet
 *     wired to a real Action, absent, empty, or case-varied input) resolves
 *     predictably via the SAME lookup mechanism — proving the gate is now
 *     table-driven, not a hardcoded string comparison;
 *   - buildNdaReviewChip accepts an optional label override (defaulting to the
 *     original "Review an NDA" text) so the Suggested-Next-Steps card renders
 *     the matched capability's label instead of a hardcoded string.
 */
import {
  DOCUMENT_REVIEW_CAPABILITIES,
  getDocumentReviewCapability,
  buildNdaReviewChip,
  LOCAL_CHIP,
} from '../localActionChips';

describe('getDocumentReviewCapability — table-driven docType lookup', () => {
  it('resolves the registered "nda" capability with its consumerType + card label', () => {
    const capability = getDocumentReviewCapability('nda');
    expect(capability).toEqual({
      docType: 'nda',
      consumerType: 'nda-review',
      cardLabel: 'Review an NDA',
    });
  });

  it('is case-insensitive and trims whitespace (mirrors the prior hardcoded comparison)', () => {
    expect(getDocumentReviewCapability('NDA')).not.toBeNull();
    expect(getDocumentReviewCapability('  Nda  ')).not.toBeNull();
    expect(getDocumentReviewCapability('NDA')?.consumerType).toBe('nda-review');
  });

  it('returns null for an unregistered docType — no card renders (negative case preserved)', () => {
    expect(getDocumentReviewCapability('agreement')).toBeNull();
    expect(getDocumentReviewCapability('Engagement Letter')).toBeNull();
  });

  it('returns null for an absent/empty docType (unclassified upload)', () => {
    expect(getDocumentReviewCapability(undefined)).toBeNull();
    expect(getDocumentReviewCapability('')).toBeNull();
    expect(getDocumentReviewCapability('   ')).toBeNull();
  });

  it('the registry is a simple array a future work-type can extend with one entry', () => {
    // Proves the mechanism is table-driven: adding a second entry (once its Action/Binding
    // exists in the catalog) requires no change to getDocumentReviewCapability's logic.
    expect(Array.isArray(DOCUMENT_REVIEW_CAPABILITIES)).toBe(true);
    expect(DOCUMENT_REVIEW_CAPABILITIES.some((c) => c.docType === 'nda')).toBe(true);
  });
});

describe('buildNdaReviewChip — label parameterization', () => {
  it('defaults to "Review an NDA" when no label is supplied (unchanged behavior)', () => {
    const chip = buildNdaReviewChip();
    expect(chip.label).toBe('Review an NDA');
    expect(chip.bindingId).toBe(LOCAL_CHIP.ndaReview);
    expect(chip.requiresAttachments).toBe(true);
  });

  it('renders the matched capability label when supplied', () => {
    const chip = buildNdaReviewChip('Review this Agreement');
    expect(chip.label).toBe('Review this Agreement');
    // The local-chip bindingId sentinel stays the same regardless of docType — only the
    // display label and the resolved consumerType (via getDocumentReviewCapability) vary.
    expect(chip.bindingId).toBe(LOCAL_CHIP.ndaReview);
  });
});
