/**
 * registration-contract-enforcement.test.ts
 *
 * spaarkeai-assistant-enhancements-r3 task 050 (FR-15) — makes the
 * `WidgetMetadata.assistantContract` field REQUIRED registration metadata and
 * enforces its presence STRUCTURALLY (NFR-09) across every registration site.
 * Task 022 defined the contract SHAPE + left the field OPTIONAL; task 040 made
 * `interactionPattern` authoritative. THIS suite proves the enforcement:
 *
 *   (a) COMPLETENESS SCAN — every widget currently registered in the shared
 *       WorkspaceWidgetRegistry declares EITHER a real WidgetAssistantContract
 *       OR an explicit `assistantContractOptOut('<reason>')` marker. No silent
 *       absence remains.
 *   (b) RUNTIME GUARD — a registration missing the field (or carrying a blank
 *       opt-out / a malformed contract) THROWS. Every one of the four shared-
 *       lib sites (register-workspace-widgets / register-document-viewer-widget
 *       / register-search-criteria-result-widget /
 *       register-structured-output-stream-widget) plus SpaarkeAi's
 *       registerComposeWidget funnels through `registerWorkspaceWidget`, so the
 *       one guard covers all five — this suite reproduces each site's shape
 *       minus the contract and asserts it fails.
 *   (c) DOCUMENTED OPT-OUTS remain VALID — an opt-out is transparent to the
 *       downstream accessors (`getWidgetAssistantContract` /
 *       `getWidgetInteractionPattern` still return `undefined` for it).
 *   (d) NO REGRESSION — the real per-item (email) + overview contracts still
 *       register and read back unchanged.
 *   (e) COMPILE-TIME enforcement — a `WidgetMetadata` literal missing
 *       `assistantContract` is a `tsc` error (the `@ts-expect-error` fixture).
 *
 * Mirrors the `jest.resetModules()` + re-require pattern used by
 * `WorkspaceWidgetRegistry.interactionPattern.test.ts` so the registration
 * side-effect files and the registry accessors always share ONE module
 * instance (see that file for the module-instance-split rationale).
 */

import React from 'react';
import type * as WorkspaceWidgetRegistryModule from '../registry/WorkspaceWidgetRegistry';
import type { WidgetMetadata } from '../types/shared';

const MockGenericText: React.FC = () => null;
MockGenericText.displayName = 'MockGenericTextWidget';
jest.mock('../widgets/GenericTextWidget', () => ({
  __esModule: true,
  default: MockGenericText,
}));

let registry: typeof WorkspaceWidgetRegistryModule;

function loadFullRegistry(): void {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  registry = require('../registry/WorkspaceWidgetRegistry');
  // Both shared-lib registration side-effect files run against the SAME fresh
  // module instance `registry` now points to. (Compose is registered in
  // SpaarkeAi, out of this package's require graph — its opt-out is asserted by
  // registerComposeWidget.test.ts in that package.)
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-workspace-widgets');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-document-viewer-widget');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-search-criteria-result-widget');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-structured-output-stream-widget');
}

beforeEach(() => {
  loadFullRegistry();
});

// A trivial always-resolving factory for guard-negative registrations. The
// factory is never invoked (these tests read/guard metadata only).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const okFactory = () => Promise.resolve({ default: (() => null) as any });

// ---------------------------------------------------------------------------
// (a) Completeness scan — no silent absence remains
// ---------------------------------------------------------------------------

describe('FR-15 completeness scan — every registered widget declares a contract OR an explicit opt-out', () => {
  it('every registered widget has a well-formed assistantContract (real contract or documented opt-out)', () => {
    const types = registry.getAllWorkspaceWidgetTypes();
    expect(types.length).toBeGreaterThan(0);

    let contractCount = 0;
    let optOutCount = 0;

    for (const type of types) {
      const contract = registry.getWorkspaceWidgetMetadata(type)!.assistantContract as
        | {
            optOut?: boolean;
            reason?: string;
            overviewTools?: unknown;
            perItemCards?: unknown;
            interactionPattern?: unknown;
          }
        | undefined;

      // Required member — never undefined post-050.
      expect(contract).toBeDefined();

      if (registry.isAssistantContractOptOut(contract as never)) {
        // Documented opt-out: reason MUST be a non-empty string.
        expect(typeof contract!.reason).toBe('string');
        expect(contract!.reason!.trim().length).toBeGreaterThan(0);
        optOutCount++;
      } else {
        // Real contract: the three required members are present + typed.
        expect(Array.isArray(contract!.overviewTools)).toBe(true);
        expect(Array.isArray(contract!.perItemCards)).toBe(true);
        expect(typeof contract!.interactionPattern).toBe('string');
        contractCount++;
      }
    }

    // Sanity: the scan actually exercised BOTH classes — not a vacuous pass.
    expect(contractCount).toBeGreaterThan(0);
    expect(optOutCount).toBeGreaterThan(0);
  });

  it('the documented opt-out widgets are valid opt-outs and resolve to undefined via the accessor (transparent)', () => {
    // The four shared-lib opt-out widgets covered by this package (compose is
    // in SpaarkeAi). Representative — not exhaustive of the full opt-out set.
    const OPT_OUT_WIDGETS = [
      'BudgetDashboard', // R1 output widget
      'create-matter-wizard', // intent dispatcher
      'search-criteria-result', // dedicated register-*.ts site
      'structured-output-stream', // dedicated register-*.ts site
    ] as const;

    for (const type of OPT_OUT_WIDGETS) {
      const declared = registry.getWorkspaceWidgetMetadata(type)!.assistantContract;
      expect(registry.isAssistantContractOptOut(declared as never)).toBe(true);
      // Transparent to downstream consumers — "no contract".
      expect(registry.getWidgetAssistantContract(type)).toBeUndefined();
      expect(registry.getWidgetInteractionPattern(type)).toBeUndefined();
    }
  });
});

// ---------------------------------------------------------------------------
// (b) Runtime guard — a contract-less / malformed registration FAILS at each site
// ---------------------------------------------------------------------------

describe('FR-15 runtime guard — a registration missing the contract fails at each site', () => {
  // Each entry reproduces a registration SITE's metadata shape WITHOUT the
  // required `assistantContract`. All five sites call `registerWorkspaceWidget`,
  // so the single guard fails every one of them.
  const SITE_METADATA_WITHOUT_CONTRACT: ReadonlyArray<{ site: string; metadata: Record<string, unknown> }> = [
    {
      site: 'register-workspace-widgets (grid/output/dispatcher)',
      metadata: {
        displayName: 'Bad Grid',
        category: 'data',
        allowMultiple: true,
        defaultOrder: 999,
        contextType: 'matter-grid',
      },
    },
    {
      site: 'register-document-viewer-widget',
      metadata: {
        displayName: 'Bad Doc Viewer',
        category: 'document',
        allowMultiple: true,
        defaultOrder: 998,
        contextType: 'document',
      },
    },
    {
      site: 'register-search-criteria-result-widget',
      metadata: { displayName: 'Bad Search Criteria', category: 'analysis', allowMultiple: true, defaultOrder: 997 },
    },
    {
      site: 'register-structured-output-stream-widget',
      metadata: { displayName: 'Bad Structured Output', category: 'analysis', allowMultiple: true, defaultOrder: 996 },
    },
    {
      site: 'registerComposeWidget (SpaarkeAi)',
      metadata: {
        displayName: 'Bad Compose',
        category: 'document',
        allowMultiple: false,
        defaultOrder: 995,
        contextType: 'compose-doc',
      },
    },
  ];

  it.each(SITE_METADATA_WITHOUT_CONTRACT)(
    'registering a widget without assistantContract THROWS — $site',
    ({ metadata }) => {
      expect(() =>
        // Cast bypasses the compile-time required member to exercise the RUNTIME guard.
        registry.registerWorkspaceWidget(
          `bad-${String(metadata.defaultOrder)}`,
          metadata as unknown as WidgetMetadata,
          okFactory
        )
      ).toThrow(/missing the REQUIRED assistantContract/i);
    }
  );

  it('an opt-out marker with a BLANK reason THROWS', () => {
    const meta = {
      displayName: 'Blank Opt-Out',
      category: 'test',
      allowMultiple: false,
      defaultOrder: 994,
      assistantContract: { optOut: true, reason: '   ' },
    };
    expect(() =>
      registry.registerWorkspaceWidget('bad-blank-optout', meta as unknown as WidgetMetadata, okFactory)
    ).toThrow(/opt-out with no reason/i);
  });

  it('a MALFORMED contract (missing interactionPattern) THROWS', () => {
    const meta = {
      displayName: 'Malformed Contract',
      category: 'test',
      allowMultiple: false,
      defaultOrder: 993,
      assistantContract: { overviewTools: [], perItemCards: [] },
    };
    expect(() =>
      registry.registerWorkspaceWidget('bad-malformed', meta as unknown as WidgetMetadata, okFactory)
    ).toThrow(/MALFORMED assistantContract/i);
  });

  it('a VALID opt-out registers WITHOUT throwing (positive control)', () => {
    const meta: WidgetMetadata = {
      displayName: 'Valid Opt-Out',
      category: 'test',
      allowMultiple: false,
      defaultOrder: 992,
      assistantContract: registry.assistantContractOptOut('Documented reason for the positive-control test.'),
    };
    expect(() => registry.registerWorkspaceWidget('good-optout', meta, okFactory)).not.toThrow();
    expect(registry.hasWorkspaceWidget('good-optout')).toBe(true);
    expect(registry.getWidgetAssistantContract('good-optout')).toBeUndefined();
  });

  it('a VALID contract registers WITHOUT throwing (positive control)', () => {
    const meta: WidgetMetadata = {
      displayName: 'Valid Contract',
      category: 'test',
      allowMultiple: false,
      defaultOrder: 991,
      assistantContract: {
        overviewTools: ['overview_query'],
        perItemCards: [],
        interactionPattern: 'respond',
      },
    };
    expect(() => registry.registerWorkspaceWidget('good-contract', meta, okFactory)).not.toThrow();
    expect(registry.getWidgetInteractionPattern('good-contract')).toBe('respond');
  });

  it('replaceWorkspaceWidget applies the same guard (a contract-less replacement THROWS)', () => {
    const meta = { displayName: 'Bad Replace', category: 'test', allowMultiple: false, defaultOrder: 990 };
    expect(() => registry.replaceWorkspaceWidget('bad-replace', meta as unknown as WidgetMetadata, okFactory)).toThrow(
      /missing the REQUIRED assistantContract/i
    );
  });
});

// ---------------------------------------------------------------------------
// (d) No regression — the real contracts still register + read back
// ---------------------------------------------------------------------------

describe('FR-15 no regression — real per-item + overview contracts unchanged', () => {
  it("'email' per-item cards (Reply/Reply All/Forward/Summarize the thread) still register (FR-09/FR-10)", () => {
    const contract = registry.getWidgetAssistantContract('email');
    expect(contract).toBeDefined();
    expect(contract!.perItemCards.map(c => c.label)).toEqual(['Reply', 'Reply All', 'Forward', 'Summarize the thread']);
    expect(contract!.interactionPattern).toBe('hybrid');
  });

  it("'document-viewer' still exposes its three respond-in-chat cards (FR-11)", () => {
    const contract = registry.getWidgetAssistantContract('document-viewer');
    expect(contract).toBeDefined();
    expect(contract!.perItemCards.length).toBe(3);
    expect(contract!.perItemCards.every(c => c.landing === 'chat')).toBe(true);
    expect(contract!.interactionPattern).toBe('respond');
  });

  it('overview-only grids still declare the ONE parameterized overview tool + no per-item cards (FR-06/FR-07)', () => {
    for (const type of ['documents-list', 'matters-list', 'workspace']) {
      const contract = registry.getWidgetAssistantContract(type);
      expect(contract).toBeDefined();
      expect(contract!.overviewTools).toEqual(['overview_query']);
      expect(contract!.perItemCards).toEqual([]);
      expect(contract!.interactionPattern).toBe('respond');
    }
  });
});

// ---------------------------------------------------------------------------
// (e) Compile-time enforcement — the field is a REQUIRED type member
// ---------------------------------------------------------------------------

describe('FR-15 compile-time enforcement — assistantContract is a required WidgetMetadata member', () => {
  it('a WidgetMetadata literal missing assistantContract fails to typecheck (compile-time proof)', () => {
    // Never called — its body exists so `tsc`/ts-jest fails the FILE if the
    // @ts-expect-error below stops being an error (i.e. if the field ever
    // becomes optional again by accident). This is the structural (NFR-09)
    // compile-time half of the enforcement.
    function _typeSafetyFixture(): void {
      // @ts-expect-error — assistantContract is a REQUIRED member (task 050).
      const _missing: WidgetMetadata = {
        displayName: 'No Contract',
        category: 'test',
        allowMultiple: false,
        defaultOrder: 1,
      };
      void _missing;
    }
    expect(typeof _typeSafetyFixture).toBe('function');
  });
});
