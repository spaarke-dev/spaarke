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

// ---------------------------------------------------------------------------
// FR-02 tests: LayoutJsonRow.rowHeight (row-level height ceiling)
// ---------------------------------------------------------------------------

/**
 * FR-02 (spaarke-dataset-grid-framework-r2, task 010, 2026-07-02) invariants:
 *
 *   (f) A row WITHOUT `rowHeight` produces a `WorkspaceRowConfig` with
 *       `maxHeight === undefined` and `overflow === undefined` — back-compat with
 *       every existing published layout JSON (rows currently omit `rowHeight`).
 *   (g) A row WITH `rowHeight: '80vh'` produces a `WorkspaceRowConfig` where
 *       `maxHeight === '80vh'` and `overflow === 'hidden'`.
 *   (h) When a row has `rowHeight` set AND a section in the row has
 *       `contentSizing: 'clamped'`, BOTH constraints coexist: the row wrapper
 *       carries the `maxHeight`/`overflow: 'hidden'` ceiling, and the section
 *       still applies its own `defaultHeight` on the inner card style. This is
 *       the "row's ceiling wins for the outer wrapper" contract — the row's
 *       overflow prevents any over-constraint from the section's inner style.
 *   (i) A row that overflows its column template (more sections than slots)
 *       propagates `rowHeight` to every auto-appended overflow row.
 *
 * Classification (ADR-038 §7): MAINTAIN-class framework-contract tests. Same
 * classification as the FR-01 tests above.
 */

/** Layout JSON factory that accepts a rowHeight override. */
function makeLayoutJsonWithRowHeight(sectionId: string, rowHeight?: string): LayoutJson {
  return {
    schemaVersion: 1,
    rows: [
      {
        id: 'row-1',
        columns: '1fr',
        sections: [sectionId],
        ...(rowHeight !== undefined ? { rowHeight } : {}),
      },
    ],
  };
}

describe('buildDynamicWorkspaceConfig — rowHeight (FR-02)', () => {
  const ctx = makeContext();

  it('(f) row without rowHeight leaves WorkspaceRowConfig.maxHeight + overflow undefined (back-compat)', () => {
    const registry = [
      makeRegistration('grow-section', {
        defaultHeight: '480px',
        contentSizing: 'grow',
      }),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJsonWithRowHeight('grow-section'), registry, ctx);

    expect(config.rows).toBeDefined();
    expect(config.rows![0].maxHeight).toBeUndefined();
    expect(config.rows![0].overflow).toBeUndefined();
  });

  it('(g) row with rowHeight: "80vh" produces maxHeight: "80vh" + overflow: "hidden" on WorkspaceRowConfig', () => {
    const registry = [
      makeRegistration('grow-section', {
        defaultHeight: '480px',
        contentSizing: 'grow',
      }),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJsonWithRowHeight('grow-section', '80vh'), registry, ctx);

    expect(config.rows).toBeDefined();
    expect(config.rows![0].maxHeight).toBe('80vh');
    expect(config.rows![0].overflow).toBe('hidden');
  });

  it('(h) row rowHeight + section contentSizing:"clamped" — row ceiling coexists with section defaultHeight (row wins on outer wrapper)', () => {
    // Section is clamped with defaultHeight 480px; row imposes a 100vh ceiling.
    // Expected: WorkspaceRowConfig carries maxHeight:100vh + overflow:hidden
    // (row wrapper wins). The section still carries its own maxHeight:480px on
    // the inner card style — but the row's overflow:hidden prevents over-constraint.
    const registry = [
      makeRegistration('clamped-section', {
        defaultHeight: '480px',
        contentSizing: 'clamped',
      }),
    ];

    const config = buildDynamicWorkspaceConfig(makeLayoutJsonWithRowHeight('clamped-section', '100vh'), registry, ctx);

    // Row-level ceiling is authoritative
    expect(config.rows).toBeDefined();
    expect(config.rows![0].maxHeight).toBe('100vh');
    expect(config.rows![0].overflow).toBe('hidden');

    // Section still gets its own maxHeight (per FR-01 clamped behavior);
    // the row wrapper's overflow:hidden ensures the section's inner style
    // does not over-constrain the outer wrapper.
    const section = config.sections[0];
    expect(section.style?.maxHeight).toBe('480px');
    expect(section.style?.overflow).toBe('hidden');
    expect(section.style?.display).toBe('flex');
  });

  it('(i) row overflow (more sections than slots) — rowHeight propagates to every auto-appended overflow row', () => {
    // Layout: single column template ("1fr") with 3 sections and rowHeight: "80vh".
    // Expected: 3 rows total (1 original + 2 overflow), each carrying maxHeight:80vh
    // + overflow:hidden. Confirms the row-height ceiling is not lost during
    // overflow splitting.
    const registry = [
      makeRegistration('s1', { defaultHeight: '200px' }),
      makeRegistration('s2', { defaultHeight: '200px' }),
      makeRegistration('s3', { defaultHeight: '200px' }),
    ];

    const layoutJson: LayoutJson = {
      schemaVersion: 1,
      rows: [
        {
          id: 'row-1',
          columns: '1fr',
          sections: ['s1', 's2', 's3'],
          rowHeight: '80vh',
        },
      ],
    };

    const config = buildDynamicWorkspaceConfig(layoutJson, registry, ctx);

    expect(config.rows).toBeDefined();
    expect(config.rows!.length).toBe(3);
    for (const row of config.rows!) {
      expect(row.maxHeight).toBe('80vh');
      expect(row.overflow).toBe('hidden');
    }
  });
});
