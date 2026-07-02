/**
 * sectionInstance.test — unit tests for the FR-03 per-instance override schema.
 *
 * Covers spaarke-dataset-grid-framework-r2 task 012 (FR-03) acceptance criteria:
 *
 *   (a) Bare-string `sections` entries continue to work (back-compat guarantee).
 *       Verifies the `normalizeSection` helper + the `buildDynamicWorkspaceConfig`
 *       read-boundary path.
 *   (b) `SectionInstance` object with `configIdOverride` is forwarded to the
 *       factory via `context.sectionInstance.configIdOverride`.
 *   (c) `SectionInstance` object with `label` overrides the section header title
 *       post-factory; the underlying `SectionRegistration.label` is unchanged.
 *   (d) `SectionInstance` with `overrides.pageSize` is visible to the factory via
 *       `context.sectionInstance.overrides.pageSize`.
 *   (e) `SectionInstance.overrides.availableViews` wins over config-level
 *       `SourceSavedQuery.availableViews` (FR-05) — REPLACE semantics, not
 *       sequential-intersection.
 *   (f) Back-compat regression: a multi-section layout with the shape of the
 *       real published `sprk_workspacelayout` records (all bare strings) still
 *       parses + renders correctly.
 *
 * Precedence choice (documented for reviewer):
 *   `resolveEffectiveAvailableViews` implements REPLACE semantics — when both
 *   instance-level and config-level allowlists are set (non-empty), only the
 *   instance-level list is applied. Rationale: sequential-intersection would
 *   make the config-level list an upper bound on the operator's per-placement
 *   allowlist, preventing widening at the more-specific tier. Per spec.md
 *   line 98: "the per-instance value takes precedence over the global
 *   config-level allowlist when both are set" — "takes precedence" reads as
 *   authoritative-replace.
 *
 * Classification (ADR-038 §7): MAINTAIN-class framework-contract tests. Same
 * classification + rationale as `buildDynamicWorkspaceConfig.test.ts` above.
 * No `Mock<HttpMessageHandler>`, no DI-registration checks, no ctor null-checks,
 * no coverage-as-gate.
 */

import type * as React from 'react';
import {
  buildDynamicWorkspaceConfig,
  normalizeSection,
  type LayoutJson,
  type SectionInstance,
} from '../buildDynamicWorkspaceConfig';
import type { SectionFactoryContext, SectionRegistration } from '../types';
import { resolveEffectiveAvailableViews, resolveEffectivePageSize } from '../../DataGrid/configResolution';
import type { SavedQuerySummary } from '../../../services/IDataverseClient';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

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
 * Registration whose factory records the context it receives so tests can
 * inspect the per-instance `sectionInstance` field the framework forwards.
 */
interface SpyRegistration {
  registration: SectionRegistration;
  /** Captured contexts, one per factory invocation. */
  readonly calls: SectionFactoryContext[];
}

function makeSpyRegistration(id: string, label = id): SpyRegistration {
  const calls: SectionFactoryContext[] = [];
  const registration: SectionRegistration = {
    id,
    label,
    description: `test section ${id}`,
    icon: (() => null) as unknown as SectionRegistration['icon'],
    category: 'data',
    factory: ctx => {
      calls.push(ctx);
      return {
        id,
        type: 'content',
        title: label,
        renderContent: () => null,
      };
    },
  };
  return { registration, calls };
}

/** Layout JSON with a single row + arbitrary section entries. */
function makeLayoutJson(entries: Array<string | SectionInstance>, columns = '1fr'): LayoutJson {
  return {
    schemaVersion: 1,
    rows: [
      {
        id: 'row-1',
        columns,
        sections: entries,
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// normalizeSection — bare-string ↔ object shape
// ---------------------------------------------------------------------------

describe('normalizeSection (FR-03)', () => {
  it('widens a bare string into { id }', () => {
    expect(normalizeSection('communications')).toEqual({ id: 'communications' });
  });

  it('returns a SectionInstance object verbatim', () => {
    const input: SectionInstance = {
      id: 'communications',
      label: 'Email',
      configIdOverride: 'alt-config-id',
      overrides: { pageSize: 100, availableViews: ['view-1'] },
    };
    expect(normalizeSection(input)).toBe(input);
  });
});

// ---------------------------------------------------------------------------
// (a) Bare-string entries — back-compat with pre-FR-03 published layouts
// ---------------------------------------------------------------------------

describe('buildDynamicWorkspaceConfig — SectionInstance (FR-03)', () => {
  const ctx = makeContext();

  it('(a) bare-string entry produces a SectionConfig with the registration title (back-compat)', () => {
    const spy = makeSpyRegistration('communications', 'Communications');

    const config = buildDynamicWorkspaceConfig(makeLayoutJson(['communications']), [spy.registration], ctx);

    expect(config.sections).toHaveLength(1);
    expect(config.sections[0].title).toBe('Communications');
    // Factory received a `sectionInstance` = { id: "communications" } (normalized).
    expect(spy.calls).toHaveLength(1);
    expect(spy.calls[0].sectionInstance).toEqual({ id: 'communications' });
    // No overrides on a bare-string entry.
    expect(spy.calls[0].sectionInstance?.configIdOverride).toBeUndefined();
    expect(spy.calls[0].sectionInstance?.label).toBeUndefined();
    expect(spy.calls[0].sectionInstance?.overrides).toBeUndefined();
  });

  // -------------------------------------------------------------------------
  // (b) configIdOverride
  // -------------------------------------------------------------------------

  it('(b) SectionInstance with configIdOverride surfaces it via SectionFactoryContext.sectionInstance', () => {
    const spy = makeSpyRegistration('communications');
    const instance: SectionInstance = {
      id: 'communications',
      configIdOverride: '11111111-1111-1111-1111-111111111111',
    };

    buildDynamicWorkspaceConfig(makeLayoutJson([instance]), [spy.registration], ctx);

    expect(spy.calls).toHaveLength(1);
    expect(spy.calls[0].sectionInstance?.configIdOverride).toBe('11111111-1111-1111-1111-111111111111');
    // Registration `id` still matches — the override does not rename the section.
    expect(spy.calls[0].sectionInstance?.id).toBe('communications');
  });

  // -------------------------------------------------------------------------
  // (c) label
  // -------------------------------------------------------------------------

  it('(c) SectionInstance with label overrides the section header title (registration label unchanged)', () => {
    const spy = makeSpyRegistration('communications', 'Communications');
    const instance: SectionInstance = { id: 'communications', label: 'Email' };

    const config = buildDynamicWorkspaceConfig(makeLayoutJson([instance]), [spy.registration], ctx);

    // Render-time title uses the override.
    expect(config.sections[0].title).toBe('Email');
    // Registration metadata (source of wizard picker + catalog) unchanged.
    expect(spy.registration.label).toBe('Communications');
  });

  it('(c-empty) empty-string label is treated as "no override" — registration label wins', () => {
    // Wizard save contract: empty-string label means "clear override". Framework
    // should NOT overwrite the factory-produced title with an empty string.
    const spy = makeSpyRegistration('communications', 'Communications');
    const instance: SectionInstance = { id: 'communications', label: '' };

    const config = buildDynamicWorkspaceConfig(makeLayoutJson([instance]), [spy.registration], ctx);

    expect(config.sections[0].title).toBe('Communications');
  });

  // -------------------------------------------------------------------------
  // (d) overrides.pageSize
  // -------------------------------------------------------------------------

  it('(d) SectionInstance with overrides.pageSize surfaces it via SectionFactoryContext.sectionInstance', () => {
    const spy = makeSpyRegistration('communications');
    const instance: SectionInstance = {
      id: 'communications',
      overrides: { pageSize: 100 },
    };

    buildDynamicWorkspaceConfig(makeLayoutJson([instance]), [spy.registration], ctx);

    expect(spy.calls).toHaveLength(1);
    expect(spy.calls[0].sectionInstance?.overrides?.pageSize).toBe(100);
  });

  // -------------------------------------------------------------------------
  // (e) overrides.availableViews wins over config-level FR-05 (REPLACE semantics)
  // -------------------------------------------------------------------------

  it('(e) SectionInstance.overrides.availableViews surfaces via SectionFactoryContext.sectionInstance', () => {
    const spy = makeSpyRegistration('communications');
    const instance: SectionInstance = {
      id: 'communications',
      overrides: { availableViews: ['view-a', 'view-b'] },
    };

    buildDynamicWorkspaceConfig(makeLayoutJson([instance]), [spy.registration], ctx);

    expect(spy.calls).toHaveLength(1);
    expect(spy.calls[0].sectionInstance?.overrides?.availableViews).toEqual(['view-a', 'view-b']);
  });

  // -------------------------------------------------------------------------
  // Empty-slot semantics — SectionInstance with empty id treated like empty slot
  // -------------------------------------------------------------------------

  it('SectionInstance with an empty id is silently skipped (matches empty-slot policy)', () => {
    const spy = makeSpyRegistration('communications');
    const layout = makeLayoutJson([{ id: '' } as SectionInstance, 'communications']);

    const config = buildDynamicWorkspaceConfig(layout, [spy.registration], ctx);

    // Only the communications section rendered — empty slot dropped, no warning.
    expect(config.sections).toHaveLength(1);
    expect(config.sections[0].id).toBe('communications');
  });

  // -------------------------------------------------------------------------
  // (f) Back-compat regression on real published layout shape
  // -------------------------------------------------------------------------

  it('(f) multi-section all-bare-string layout (published-record shape) parses + renders without change', () => {
    // Mirrors the shape of every existing sprk_workspacelayout record predating
    // FR-03 — 3 rows, all bare-string entries. Verifies that adding the object
    // union to the type did not disturb the bare-string path.
    const s1 = makeSpyRegistration('get-started');
    const s2 = makeSpyRegistration('quick-summary');
    const s3 = makeSpyRegistration('latest-updates');
    const s4 = makeSpyRegistration('todo');
    const s5 = makeSpyRegistration('documents');

    const layout: LayoutJson = {
      schemaVersion: 1,
      rows: [
        { id: 'row-1', columns: '1fr 1fr', sections: ['get-started', 'quick-summary'] },
        { id: 'row-2', columns: '1fr', sections: ['latest-updates'] },
        { id: 'row-3', columns: '1fr 1fr', sections: ['todo', 'documents'] },
      ],
    };

    const config = buildDynamicWorkspaceConfig(
      layout,
      [s1.registration, s2.registration, s3.registration, s4.registration, s5.registration],
      ctx
    );

    // All five sections rendered.
    expect(config.sections.map(s => s.id)).toEqual([
      'get-started',
      'quick-summary',
      'latest-updates',
      'todo',
      'documents',
    ]);
    expect(config.rows).toBeDefined();
    expect(config.rows!.length).toBe(3);

    // Every factory saw a bare-normalized SectionInstance ({ id, no overrides }).
    for (const spy of [s1, s2, s3, s4, s5]) {
      expect(spy.calls).toHaveLength(1);
      expect(spy.calls[0].sectionInstance?.overrides).toBeUndefined();
      expect(spy.calls[0].sectionInstance?.configIdOverride).toBeUndefined();
      expect(spy.calls[0].sectionInstance?.label).toBeUndefined();
    }
  });

  // -------------------------------------------------------------------------
  // Mixed-shape layout — bare strings + SectionInstance objects in one row
  // -------------------------------------------------------------------------

  it('mixed-shape layout (bare + object entries in one row) is handled uniformly', () => {
    const s1 = makeSpyRegistration('get-started');
    const s2 = makeSpyRegistration('communications');

    const layout: LayoutJson = {
      schemaVersion: 1,
      rows: [
        {
          id: 'row-1',
          columns: '1fr 1fr',
          sections: [
            'get-started', // bare string — no overrides
            { id: 'communications', label: 'Email' }, // object with label
          ],
        },
      ],
    };

    const config = buildDynamicWorkspaceConfig(layout, [s1.registration, s2.registration], ctx);

    expect(config.sections[0].title).toBe('get-started'); // bare-string entry
    expect(config.sections[1].title).toBe('Email'); // label override
  });
});

// ---------------------------------------------------------------------------
// (e) resolveEffectiveAvailableViews — instance-level REPLACES config-level
// ---------------------------------------------------------------------------

describe('resolveEffectiveAvailableViews (FR-03 × FR-05 precedence)', () => {
  const fiveViews: SavedQuerySummary[] = [
    { id: 'view-1', name: 'A', isDefault: true, queryType: 0 },
    { id: 'view-2', name: 'B', isDefault: false, queryType: 0 },
    { id: 'view-3', name: 'C', isDefault: false, queryType: 0 },
    { id: 'view-4', name: 'D', isDefault: false, queryType: 0 },
    { id: 'view-5', name: 'E', isDefault: false, queryType: 0 },
  ];

  it('both tiers undefined → all views (back-compat with pre-FR-05)', () => {
    const result = resolveEffectiveAvailableViews(fiveViews, undefined, undefined);
    expect(result).toEqual(fiveViews);
  });

  it('config-level only (instance undefined) → config-level applied (FR-05 behavior)', () => {
    const result = resolveEffectiveAvailableViews(fiveViews, ['view-1', 'view-2'], undefined);
    expect(result.map(v => v.id)).toEqual(['view-1', 'view-2']);
  });

  it('instance-level only (config undefined) → instance-level applied', () => {
    const result = resolveEffectiveAvailableViews(fiveViews, undefined, ['view-3']);
    expect(result.map(v => v.id)).toEqual(['view-3']);
  });

  it('BOTH tiers set → instance-level REPLACES config-level (FR-03 precedence)', () => {
    // config-level: [view-1, view-2] (would restrict picker to A + B)
    // instance-level: [view-4] (per-placement narrows to D only)
    // Sequential-intersection would return []; REPLACE returns [view-4].
    const result = resolveEffectiveAvailableViews(fiveViews, ['view-1', 'view-2'], ['view-4']);
    expect(result.map(v => v.id)).toEqual(['view-4']);
  });

  it('BOTH tiers set with disjoint sets → instance-level wins (widening allowed)', () => {
    // config-level: [view-1] (config restricts to A)
    // instance-level: [view-5] (placement wants only E, which is NOT in config allowlist)
    // Under REPLACE semantics, the operator can widen at the instance tier —
    // exactly the scenario spec.md line 98 calls out.
    const result = resolveEffectiveAvailableViews(fiveViews, ['view-1'], ['view-5']);
    expect(result.map(v => v.id)).toEqual(['view-5']);
  });

  it('instance-level empty array → falls through to config-level (safer default)', () => {
    // Empty-array semantics mirror filterAvailableViews: instance-level `[]`
    // means "no per-instance filter", so config-level applies.
    const result = resolveEffectiveAvailableViews(fiveViews, ['view-1', 'view-2'], []);
    expect(result.map(v => v.id)).toEqual(['view-1', 'view-2']);
  });

  it('both tiers empty array → no filter (all views)', () => {
    const result = resolveEffectiveAvailableViews(fiveViews, [], []);
    expect(result).toEqual(fiveViews);
  });
});

// ---------------------------------------------------------------------------
// resolveEffectivePageSize — three-tier precedence
// ---------------------------------------------------------------------------

describe('resolveEffectivePageSize (FR-03 × FR-07 precedence)', () => {
  it('both tiers undefined → framework default (25 per FR-07)', () => {
    expect(resolveEffectivePageSize(undefined, undefined)).toBe(25);
  });

  it('config-record only (instance undefined) → config record wins', () => {
    expect(resolveEffectivePageSize(undefined, 50)).toBe(50);
  });

  it('instance-level only (config undefined) → instance wins', () => {
    expect(resolveEffectivePageSize(100, undefined)).toBe(100);
  });

  it('both tiers set → instance-level (highest precedence)', () => {
    expect(resolveEffectivePageSize(100, 50)).toBe(100);
  });

  it('zero / negative pageSize treated as unset (falls through)', () => {
    // Zero at instance tier → falls to config record (50).
    expect(resolveEffectivePageSize(0, 50)).toBe(50);
    // Negative at both tiers → framework default (25).
    expect(resolveEffectivePageSize(-1, -5)).toBe(25);
    // Non-finite (NaN) at instance tier → falls to config record.
    expect(resolveEffectivePageSize(Number.NaN, 50)).toBe(50);
  });
});
