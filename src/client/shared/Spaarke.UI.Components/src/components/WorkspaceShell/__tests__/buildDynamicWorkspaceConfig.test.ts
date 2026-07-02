/**
 * buildDynamicWorkspaceConfig — unit tests
 *
 * Covers FR-01 (spaarke-dataset-grid-framework-r2, task 001, 2026-07-02) invariants:
 *
 *   (a) A registration with `contentSizing: 'grow'` and a `defaultHeight` produces
 *       a section whose `style.minHeight === defaultHeight` and does NOT set
 *       `maxHeight` / `overflow` / `display`.
 *   (b) A registration with `contentSizing: 'clamped'` and a `defaultHeight` produces
 *       a section whose `style.maxHeight === defaultHeight`, `style.overflow === 'hidden'`,
 *       and `style.display === 'flex'`. It does NOT set `minHeight`.
 *   (c) A registration with `contentSizing` OMITTED and a `defaultHeight` behaves
 *       identically to `'grow'` (back-compat with all sections that predate FR-01).
 *   (d) When the factory has already set `style.minHeight` (operator override),
 *       the framework does NOT overwrite it — even for `'grow'` registrations.
 *   (e) When the factory has already set `style.maxHeight` (operator override),
 *       the framework does NOT overwrite it — even for `'clamped'` registrations.
 *
 * Classification (ADR-038 §7): MAINTAIN-class framework-contract test. Verifies the
 * public contract of `buildDynamicWorkspaceConfig` — the load-bearing merge step of
 * the workspace layout pipeline. No `Mock<HttpMessageHandler>`, no DI registration
 * checks, no coverage-as-gate.
 *
 * Test harness: jest, matching the conventions in
 * `src/client/shared/Spaarke.UI.Components/jest.config.js`.
 */

import type * as React from 'react';
import { buildDynamicWorkspaceConfig, type LayoutJson } from '../buildDynamicWorkspaceConfig';
import type { SectionFactoryContext, SectionRegistration } from '../types';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

/** Minimal SectionFactoryContext — every field required but nothing exercises them here. */
function makeContext(): SectionFactoryContext {
  return {
    webApi: {},
    userId: '00000000-0000-0000-0000-000000000001',
    service: {},
    bffBaseUrl: 'https://bff.test.local',
    onNavigate: () => undefined,
    onOpenWizard: () => undefined,
    onBadgeCountChange: () => undefined,
    onRefetchReady: () => undefined,
  };
}

/**
 * Builds a SectionRegistration whose factory returns a bare-bones content section.
 * `initialStyle` lets a test simulate a factory that has already set style keys
 * (operator override case).
 */
function makeRegistration(
  id: string,
  overrides: Partial<Pick<SectionRegistration, 'defaultHeight' | 'contentSizing'>>,
  initialStyle?: React.CSSProperties
): SectionRegistration {
  return {
    id,
    label: id,
    description: `test section ${id}`,
    // Icon is required on the interface; the field is never read by
    // buildDynamicWorkspaceConfig, so a cast is safe here.
    icon: (() => null) as unknown as SectionRegistration['icon'],
    category: 'data',
    defaultHeight: overrides.defaultHeight,
    contentSizing: overrides.contentSizing,
    factory: () => ({
      id,
      type: 'content',
      title: id,
      renderContent: () => null,
      style: initialStyle,
    }),
  };
}

/** Minimal single-section layout JSON for a given section id. */
function makeLayoutJson(sectionId: string): LayoutJson {
  return {
    schemaVersion: 1,
    rows: [
      {
        id: 'row-1',
        columns: '1fr',
        sections: [sectionId],
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('buildDynamicWorkspaceConfig — contentSizing (FR-01)', () => {
  const ctx = makeContext();

  it('(a) contentSizing: "grow" applies defaultHeight as min-height only', () => {
    const registry = [
      makeRegistration('grow-section', {
        defaultHeight: '480px',
        contentSizing: 'grow',
      }),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJson('grow-section'), registry, ctx);
    const section = config.sections[0];

    expect(section.style?.minHeight).toBe('480px');
    expect(section.style?.maxHeight).toBeUndefined();
    expect(section.style?.overflow).toBeUndefined();
    expect(section.style?.display).toBeUndefined();
  });

  it('(b) contentSizing: "clamped" applies defaultHeight as max-height + overflow:hidden + display:flex', () => {
    const registry = [
      makeRegistration('clamped-section', {
        defaultHeight: '480px',
        contentSizing: 'clamped',
      }),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJson('clamped-section'), registry, ctx);
    const section = config.sections[0];

    expect(section.style?.maxHeight).toBe('480px');
    expect(section.style?.overflow).toBe('hidden');
    expect(section.style?.display).toBe('flex');
    expect(section.style?.minHeight).toBeUndefined();
  });

  it('(c) contentSizing omitted defaults to "grow" behavior (back-compat)', () => {
    const registry = [makeRegistration('default-section', { defaultHeight: '325px' })];

    const config = buildDynamicWorkspaceConfig(makeLayoutJson('default-section'), registry, ctx);
    const section = config.sections[0];

    expect(section.style?.minHeight).toBe('325px');
    expect(section.style?.maxHeight).toBeUndefined();
    expect(section.style?.overflow).toBeUndefined();
    expect(section.style?.display).toBeUndefined();
  });

  it('(d) operator-set style.minHeight is NOT overwritten (grow branch)', () => {
    const registry = [
      makeRegistration(
        'grow-with-override',
        { defaultHeight: '480px', contentSizing: 'grow' },
        { minHeight: '600px' } // factory already set an override
      ),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJson('grow-with-override'), registry, ctx);
    const section = config.sections[0];

    expect(section.style?.minHeight).toBe('600px'); // override preserved
  });

  it('(e) operator-set style.maxHeight is NOT overwritten (clamped branch)', () => {
    const registry = [
      makeRegistration(
        'clamped-with-override',
        { defaultHeight: '480px', contentSizing: 'clamped' },
        { maxHeight: '720px' } // factory already set an override
      ),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJson('clamped-with-override'), registry, ctx);
    const section = config.sections[0];

    expect(section.style?.maxHeight).toBe('720px'); // override preserved
    // The overflow / display flags are gated behind the maxHeight-not-set check,
    // so they should NOT be applied when the factory already supplied maxHeight.
    // (Documents this framework contract explicitly.)
    expect(section.style?.overflow).toBeUndefined();
    expect(section.style?.display).toBeUndefined();
  });

  it('registration without defaultHeight does not add any sizing keys, regardless of contentSizing', () => {
    const registryGrow = [makeRegistration('no-height-grow', { contentSizing: 'grow' })];
    const registryClamped = [makeRegistration('no-height-clamped', { contentSizing: 'clamped' })];

    const grow = buildDynamicWorkspaceConfig(makeLayoutJson('no-height-grow'), registryGrow, ctx);
    const clamped = buildDynamicWorkspaceConfig(makeLayoutJson('no-height-clamped'), registryClamped, ctx);

    expect(grow.sections[0].style?.minHeight).toBeUndefined();
    expect(grow.sections[0].style?.maxHeight).toBeUndefined();
    expect(clamped.sections[0].style?.minHeight).toBeUndefined();
    expect(clamped.sections[0].style?.maxHeight).toBeUndefined();
  });
});
