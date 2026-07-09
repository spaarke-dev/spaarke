/**
 * wizardRegistry Unit Tests
 *
 * Covers resolveWizard()'s key-vs-fallback resolution order, entity-logical-name
 * normalization, and null-on-unknown-key behavior (spec.md FR-03).
 *
 * @see ADR-012 - Shared component library
 */
import { resolveWizard, wizardRegistry } from '../wizardRegistry';

describe('resolveWizard', () => {
  it('resolveWizard_ExplicitKnownKey_ReturnsRegisteredComponent', () => {
    expect(resolveWizard('event', null)).toBe(wizardRegistry.event);
  });

  it('resolveWizard_EmptyKeyWithMatchingEntityFallback_ReturnsRegisteredComponent', () => {
    expect(resolveWizard(undefined, 'sprk_event')).toBe(wizardRegistry.event);
  });

  it('resolveWizard_NullKeyWithMatchingEntityFallback_ReturnsRegisteredComponent', () => {
    expect(resolveWizard(null, 'sprk_invoice')).toBe(wizardRegistry.invoice);
  });

  it('resolveWizard_CompoundEntityFallback_NormalizesToHyphenatedKey', () => {
    // sprk_kpiassessment -> report-card (RENAME NOTE, 2026-07-08: both
    // sprk_reportcard and sprk_kpiassessment fall back to the 'report-card'
    // wizard key — not a plain "sprk_" prefix-strip; see wizardRegistry.ts's
    // ENTITY_TO_WIZARD_KEY doc comment).
    expect(resolveWizard('', 'sprk_kpiassessment')).toBe(wizardRegistry['report-card']);
  });

  it('resolveWizard_WhitespaceOnlyKey_FallsBackToEntity', () => {
    expect(resolveWizard('   ', 'sprk_event')).toBe(wizardRegistry.event);
  });

  it('resolveWizard_ExplicitKeyTakesPrecedenceOverFallback', () => {
    // Even though the fallback entity would normalize to "invoice", an explicit
    // key always wins.
    expect(resolveWizard('event', 'sprk_invoice')).toBe(wizardRegistry.event);
  });

  it('resolveWizard_UnknownKey_ReturnsNull', () => {
    expect(resolveWizard('not-a-real-key', null)).toBeNull();
  });

  it('resolveWizard_UnknownFallbackEntity_ReturnsNull', () => {
    expect(resolveWizard(undefined, 'sprk_widget')).toBeNull();
  });

  it('resolveWizard_NoKeyNoFallback_ReturnsNull', () => {
    expect(resolveWizard(null, null)).toBeNull();
    expect(resolveWizard(undefined, undefined)).toBeNull();
    expect(resolveWizard('', '')).toBeNull();
  });
});

describe('wizardRegistry', () => {
  it('wizardRegistry_HasAllThreeDevDefinedKeys', () => {
    // RENAME NOTE, 2026-07-08: 'kpi-assessment' renamed to 'report-card' —
    // see wizardRegistry.ts RENAME NOTE doc comment.
    expect(Object.keys(wizardRegistry).sort()).toEqual(['event', 'invoice', 'report-card']);
  });
});
