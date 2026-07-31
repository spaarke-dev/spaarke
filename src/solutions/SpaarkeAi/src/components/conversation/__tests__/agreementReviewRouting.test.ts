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
  resolveConfidenceThreshold,
  resolveFallbackKey,
  displayNameFor,
  formatConfidencePercent,
  isAgreementClassifyResult,
  buildAgreementReviewConfirmChips,
  buildAgreementReviewCompositeChips,
  buildAgreementReviewNonAgreementChips,
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
  it('confirm chips: exactly 2 — proposed type + pick-another (general)', () => {
    const chips = buildAgreementReviewConfirmChips('NDA');
    expect(chips).toHaveLength(2);
    expect(chips[0].bindingId).toBe(LOCAL_CHIP.agreementReviewConfirm);
    expect(chips[0].label).toContain('NDA');
    expect(chips[1].bindingId).toBe(LOCAL_CHIP.agreementReviewGeneral);
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

  it('toConsumerChipWire round-trips through the SAME wire shape parseConsumerChips expects', () => {
    const wire = toConsumerChipWire(buildAgreementReviewConfirmChips('NDA'));
    expect(wire[0]).toMatchObject({
      targetBindingId: LOCAL_CHIP.agreementReviewConfirm,
      chipLabel: expect.stringContaining('NDA'),
      requiresAttachments: true,
    });
  });
});
