/**
 * agreementReviewRouting.test.ts — task 021 (spec FR-07/FR-08/FR-09 interactive path).
 *
 * Proves the pure detection + gate-decision + chip-building logic in isolation:
 *   - detectAgreementReviewIntent fires on "review this document" and its close variants,
 *     never fires on messages already owned by the revise flow (verb/keyword overlap), and
 *     never fires without a document reference (the negative criterion the caller relies on).
 *   - resolveAgreementReviewGateDecision covers all four branches (non-agreement / auto-proceed
 *     / confirm / composite) exactly per FR-08's confidence-threshold semantics, incl. the
 *     per-type registry override vs the global 0.85 baseline.
 *   - The chip builders never exceed the "throwaway turn-follow-on" cap and always include the
 *     safe fallback (general review / pick-another / both).
 */
import {
  detectAgreementReviewIntent,
  resolveAgreementReviewGateDecision,
  resolveAgreementReviewSanityMismatch,
  buildAgreementReviewSanityMismatchMessage,
  resolveConfidenceThreshold,
  resolveFallbackKey,
  displayNameFor,
  formatConfidencePercent,
  isAgreementClassifyResult,
  buildAgreementReviewConfirmChips,
  buildAgreementReviewCompositeChips,
  buildAgreementReviewNonAgreementChips,
  buildAgreementReviewDepthChoiceChips,
  buildAgreementReviewDepthChoiceMessage,
  normalizeReviewDepth,
  DEFAULT_REVIEW_DEPTH,
  toConsumerChipWire,
  GLOBAL_CONFIDENCE_THRESHOLD,
  type AgreementClassifyResult,
  type AgreementTypeRegistryEntry,
} from '../agreementReviewRouting';
import { LOCAL_CHIP, decodeAgreementReviewLensChipId } from '../localActionChips';

describe('detectAgreementReviewIntent', () => {
  it('fires on the canonical "review this document" trigger phrase (design Lens 3d)', () => {
    expect(detectAgreementReviewIntent('review this document')).toBe(true);
    expect(detectAgreementReviewIntent('Please review this agreement')).toBe(true);
    expect(detectAgreementReviewIntent('can you assess this contract')).toBe(true);
  });

  it('does NOT fire on messages already owned by the generic revise flow (verb/keyword overlap)', () => {
    expect(detectAgreementReviewIntent('revise this document')).toBe(false);
    expect(detectAgreementReviewIntent('review the contract and identify risks')).toBe(false);
    expect(detectAgreementReviewIntent('review the agreement and make it clearer')).toBe(false);
    expect(detectAgreementReviewIntent('align this document to our playbook')).toBe(false);
    expect(detectAgreementReviewIntent('flag risks in this document')).toBe(false);
    expect(detectAgreementReviewIntent('tighten this document')).toBe(false);
  });

  it('negative criterion: a bare "review" with no document reference never fires', () => {
    expect(detectAgreementReviewIntent('review')).toBe(false);
    expect(detectAgreementReviewIntent('can you review that for me')).toBe(false);
    expect(detectAgreementReviewIntent('I reviewed it yesterday')).toBe(false);
  });

  it('never fires on empty text or slash commands', () => {
    expect(detectAgreementReviewIntent('')).toBe(false);
    expect(detectAgreementReviewIntent('   ')).toBe(false);
    expect(detectAgreementReviewIntent('/review this document')).toBe(false);
  });
});

describe('isAgreementClassifyResult', () => {
  it('accepts a well-formed classify result', () => {
    const result = { isAgreement: true, composite: false, candidates: [{ subDomainKey: 'nda', confidence: 0.9 }] };
    expect(isAgreementClassifyResult(result)).toBe(true);
  });

  it('rejects malformed/partial shapes', () => {
    expect(isAgreementClassifyResult(null)).toBe(false);
    expect(isAgreementClassifyResult({})).toBe(false);
    expect(isAgreementClassifyResult({ isAgreement: true, composite: false })).toBe(false);
    expect(
      isAgreementClassifyResult({ isAgreement: true, composite: false, candidates: [{ subDomainKey: 'nda' }] })
    ).toBe(false);
  });
});

const REGISTRY: readonly AgreementTypeRegistryEntry[] = [
  { key: 'nda', name: 'NDA', isFallback: false, confidenceThreshold: null },
  { key: 'employment', name: 'Employment', isFallback: false, confidenceThreshold: 0.7 },
  { key: 'general', name: 'General Agreement', isFallback: true, confidenceThreshold: null },
];

describe('resolveConfidenceThreshold / resolveFallbackKey / displayNameFor', () => {
  it('falls back to the global 0.85 baseline when the row has no override', () => {
    expect(resolveConfidenceThreshold('nda', REGISTRY)).toBe(GLOBAL_CONFIDENCE_THRESHOLD);
  });

  it('honors a per-type override (the registry column already exists per task 001/003)', () => {
    expect(resolveConfidenceThreshold('employment', REGISTRY)).toBe(0.7);
  });

  it('resolves the fallback key via IsFallback — never a hardcoded string when the registry is available', () => {
    expect(resolveFallbackKey(REGISTRY)).toBe('general');
  });

  it('degrades to the literal "general" sentinel when the registry is empty (read failure)', () => {
    expect(resolveFallbackKey([])).toBe('general');
  });

  it('displayNameFor prefers the registry sprk_name', () => {
    expect(displayNameFor('nda', REGISTRY)).toBe('NDA');
  });

  it('displayNameFor degrades to a key-derived title case when the registry is empty/unmatched', () => {
    expect(displayNameFor('asset-purchase', [])).toBe('Asset Purchase');
  });

  it('formatConfidencePercent rounds + clamps to [0,100]', () => {
    expect(formatConfidencePercent(0.847)).toBe(85);
    expect(formatConfidencePercent(1.5)).toBe(100);
    expect(formatConfidencePercent(-0.2)).toBe(0);
  });
});

describe('resolveAgreementReviewGateDecision — FR-08 confirmation gate', () => {
  it('non-agreement: isAgreement=false -> explicit decline, never a fabricated candidate', () => {
    const result: AgreementClassifyResult = { isAgreement: false, composite: false, candidates: [] };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({ kind: 'non-agreement' });
  });

  it('non-agreement: isAgreement=true but zero candidates also declines (defensive)', () => {
    const result: AgreementClassifyResult = { isAgreement: true, composite: false, candidates: [] };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({ kind: 'non-agreement' });
  });

  it('auto-proceed: single candidate AT the global threshold', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'nda', confidence: 0.85 }],
    };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({
      kind: 'auto-proceed',
      subDomainKey: 'nda',
    });
  });

  it('auto-proceed: single candidate above its PER-TYPE (lower) threshold', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'employment', confidence: 0.75 }],
    };
    // 0.75 < global 0.85 but >= employment's own 0.7 override — auto-proceeds.
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({
      kind: 'auto-proceed',
      subDomainKey: 'employment',
    });
  });

  it('confirm: single candidate below threshold — NEVER a silent wrong-grounding run', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'nda', confidence: 0.6 }],
    };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({
      kind: 'confirm',
      subDomainKey: 'nda',
      confidence: 0.6,
      threshold: GLOBAL_CONFIDENCE_THRESHOLD,
    });
  });

  it('composite: composite=true with >=2 candidates -> choice-of-lens regardless of confidence', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: true,
      candidates: [
        { subDomainKey: 'employment', confidence: 0.6 },
        { subDomainKey: 'nda', confidence: 0.55 },
      ],
    };
    const decision = resolveAgreementReviewGateDecision(result, REGISTRY);
    expect(decision.kind).toBe('composite');
    if (decision.kind === 'composite') {
      expect(decision.candidates).toHaveLength(2);
    }
  });

  it('composite flag with fewer than 2 candidates degrades to the single-candidate confirm/auto path (defensive)', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: true,
      candidates: [{ subDomainKey: 'nda', confidence: 0.9 }],
    };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({
      kind: 'auto-proceed',
      subDomainKey: 'nda',
    });
  });

  it('picks the HIGHEST-confidence candidate when multiple are present but composite=false', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [
        { subDomainKey: 'nda', confidence: 0.4 },
        { subDomainKey: 'employment', confidence: 0.9 },
      ],
    };
    expect(resolveAgreementReviewGateDecision(result, REGISTRY)).toEqual({
      kind: 'auto-proceed',
      subDomainKey: 'employment',
    });
  });
});

describe('chip builders — max 2-3 chips (ASSISTANT-UI-ELEMENT-CRITERIA throwaway turn-follow-on)', () => {
  it('confirm chips: exactly 3 — Quick/Thorough type-confirm pair + pick-another (general) (task 070)', () => {
    const chips = buildAgreementReviewConfirmChips('NDA');
    expect(chips).toHaveLength(3);
    expect(chips[0].bindingId).toBe(LOCAL_CHIP.agreementReviewConfirmQuick);
    expect(chips[0].label).toContain('NDA');
    expect(chips[0].label).toContain('Quick');
    expect(chips[1].bindingId).toBe(LOCAL_CHIP.agreementReviewConfirmThorough);
    expect(chips[1].label).toContain('NDA');
    expect(chips[1].label).toContain('Thorough');
    expect(chips[2].bindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);
  });

  it('composite chips: one per candidate + "Both" (design Lens 3d\'s "employment · just the NDA · both")', () => {
    const chips = buildAgreementReviewCompositeChips(
      [
        { subDomainKey: 'employment', confidence: 0.6 },
        { subDomainKey: 'nda', confidence: 0.55 },
      ],
      REGISTRY
    );
    expect(chips).toHaveLength(3);
    expect(decodeAgreementReviewLensChipId(chips[0].bindingId)).toBe('employment');
    expect(decodeAgreementReviewLensChipId(chips[1].bindingId)).toBe('nda');
    expect(chips[2].bindingId).toBe(LOCAL_CHIP.agreementReviewBoth);
    expect(chips[2].label).toBe('Both');
  });

  it('non-agreement chips: exactly 1 — the general-review escape hatch (never a silent decline)', () => {
    const chips = buildAgreementReviewNonAgreementChips();
    expect(chips).toHaveLength(1);
    expect(chips[0].bindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);
  });

  it('depth-choice chips (task 070): exactly 2 — Quick + Thorough, generic (no type name)', () => {
    const chips = buildAgreementReviewDepthChoiceChips();
    expect(chips).toHaveLength(2);
    expect(chips[0].bindingId).toBe(LOCAL_CHIP.agreementReviewDepthQuick);
    expect(chips[0].label).toContain('Quick');
    expect(chips[1].bindingId).toBe(LOCAL_CHIP.agreementReviewDepthThorough);
    expect(chips[1].label).toContain('Thorough');
  });

  it('toConsumerChipWire round-trips through the SAME wire shape parseConsumerChips expects', () => {
    const wire = toConsumerChipWire(buildAgreementReviewConfirmChips('NDA'));
    expect(wire[0]).toMatchObject({
      targetBindingId: LOCAL_CHIP.agreementReviewConfirmQuick,
      chipLabel: expect.stringContaining('NDA'),
      requiresAttachments: true,
    });
  });
});

describe('review depth (task 070, UAT2 review-depth selector)', () => {
  it('buildAgreementReviewDepthChoiceMessage names the settled type, asks a question', () => {
    const message = buildAgreementReviewDepthChoiceMessage('NDA');
    expect(message).toContain('NDA');
    expect(message).toContain('?');
  });

  it('normalizeReviewDepth: "quick" passes through, everything else defaults to Thorough', () => {
    expect(normalizeReviewDepth('quick')).toBe('quick');
    expect(normalizeReviewDepth('thorough')).toBe(DEFAULT_REVIEW_DEPTH);
    expect(normalizeReviewDepth(undefined)).toBe(DEFAULT_REVIEW_DEPTH);
    expect(normalizeReviewDepth(null)).toBe(DEFAULT_REVIEW_DEPTH);
    expect(normalizeReviewDepth('blazing-fast')).toBe(DEFAULT_REVIEW_DEPTH);
    expect(normalizeReviewDepth(42)).toBe(DEFAULT_REVIEW_DEPTH);
  });

  it('DEFAULT_REVIEW_DEPTH is Thorough (project constraint: legal-quality default)', () => {
    expect(DEFAULT_REVIEW_DEPTH).toBe('thorough');
  });
});

describe('resolveAgreementReviewSanityMismatch — task 023 explicit-door warn-only sanity check', () => {
  it('returns a mismatch when the classifier top candidate differs from the explicit choice at high confidence', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'employment', confidence: 0.93 }],
    };
    const mismatch = resolveAgreementReviewSanityMismatch('nda', result, REGISTRY);
    expect(mismatch).toEqual({
      explicitKey: 'nda',
      explicitDisplayName: 'NDA',
      topKey: 'employment',
      topDisplayName: 'Employment',
      topConfidence: 0.93,
    });
  });

  it('returns null when the top candidate matches the explicit choice (no mismatch)', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'nda', confidence: 0.93 }],
    };
    expect(resolveAgreementReviewSanityMismatch('nda', result, REGISTRY)).toBeNull();
  });

  it('returns null when the top candidate is below its OWN resolved threshold (not "high confidence")', () => {
    // employment's registry row overrides its threshold to 0.7 — 0.6 is below it.
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [{ subDomainKey: 'employment', confidence: 0.6 }],
    };
    expect(resolveAgreementReviewSanityMismatch('nda', result, REGISTRY)).toBeNull();
  });

  it('returns null when isAgreement is false (never a mismatch notice on a non-agreement classification)', () => {
    const result: AgreementClassifyResult = { isAgreement: false, composite: false, candidates: [] };
    expect(resolveAgreementReviewSanityMismatch('nda', result, REGISTRY)).toBeNull();
  });

  it('returns null when there are no candidates', () => {
    const result: AgreementClassifyResult = { isAgreement: true, composite: false, candidates: [] };
    expect(resolveAgreementReviewSanityMismatch('nda', result, REGISTRY)).toBeNull();
  });

  it('negative criterion: a null classifyResult (classifier error/unavailable) NEVER produces a notice', () => {
    expect(resolveAgreementReviewSanityMismatch('nda', null, REGISTRY)).toBeNull();
  });

  it('picks the HIGHEST-confidence candidate when multiple are present', () => {
    const result: AgreementClassifyResult = {
      isAgreement: true,
      composite: false,
      candidates: [
        { subDomainKey: 'general', confidence: 0.5 },
        { subDomainKey: 'employment', confidence: 0.91 },
      ],
    };
    const mismatch = resolveAgreementReviewSanityMismatch('nda', result, REGISTRY);
    expect(mismatch?.topKey).toBe('employment');
  });

  it('buildAgreementReviewSanityMismatchMessage is informational-only (no question, references both names)', () => {
    const message = buildAgreementReviewSanityMismatchMessage({
      explicitKey: 'nda',
      explicitDisplayName: 'NDA',
      topKey: 'employment',
      topDisplayName: 'Employment',
      topConfidence: 0.93,
    });
    expect(message).toContain('Employment');
    expect(message).toContain('NDA');
    expect(message).toContain('93%');
    expect(message).not.toContain('?');
  });
});
