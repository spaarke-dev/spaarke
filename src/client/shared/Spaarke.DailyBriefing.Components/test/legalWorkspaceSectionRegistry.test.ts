/**
 * Unit tests for `createLegalWorkspaceSectionRegistry` — R2 Option D
 * (2026-06-18, replaces R2 task 002 module-mutation slot pattern).
 *
 * Contract under test:
 *   1. `createLegalWorkspaceSectionRegistry()` with NO options returns a
 *      registry equivalent to the default `SECTION_REGISTRY` const (same
 *      widget IDs, same length, same order). Standalone LegalWorkspace
 *      behavior preserved byte-identically (FR-25 / NFR-10).
 *
 *   2. `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`
 *      threads the loader through to the dailyBriefing entry's factory.
 *      We assert the registry build does not throw and the dailyBriefing
 *      entry has the correct id — verifying the option is at least accepted
 *      by the factory signature (the loader's actual fetch-time invocation
 *      is exercised by the existing useBriefingRender coverage).
 *
 *   3. The legacy band-aid API `setLegalWorkspaceDailyBriefingNotificationLoader`
 *      from R2 task 002 (Wave 8) is REMOVED from the LegalWorkspace barrel.
 *      This locks in the migration so a future commit can't accidentally
 *      re-introduce the module-mutation slot.
 *
 * # Why this test lives here
 *
 * LegalWorkspace has no Jest setup (it's a Vite-built standalone Code Page).
 * Spaarke.DailyBriefing.Components has Jest 30 + ts-jest already configured
 * (R2 task 019 / NFR-05), and the import-side mocks for `@spaarke/ui-components`
 * and `@spaarke/auth` already exist. We import LegalWorkspace sources via
 * relative path and pre-mock LegalWorkspace's heavyweight transitive imports
 * (authInit, telemetry, individual section factories) so this test stays
 * focused on the registry factory contract.
 *
 * See `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md` §7.
 */

// ---------------------------------------------------------------------------
// Pre-import mocks (must come BEFORE the LegalWorkspace imports below).
//
// LegalWorkspace's `sectionRegistry.ts` imports one module per section
// registration. Each of those transitively imports React, services, hooks,
// other shared packages, etc. We mock EVERY section registration to a trivial
// `SectionRegistration` shape so the factory can build without dragging in the
// whole app.
//
// A registration left unmocked loads for real: its whole dependency tree
// (e.g. `@spaarke/communication-components` SOURCE) is then compiled by ts-jest
// under THIS package's tsconfig.test.json, which maps `@spaarke/ui-components`
// to the hand-written mock in `test/__mocks__/`. The first time that tree
// imports a ui-components export the mock lacks, this suite dies with TS2305
// (issue #1906: `nullRecordNumberWhenTargetHasNone`, 2026-11-11). The guard
// test at the bottom fails by name when a new registration is not stubbed here.
//
// `dailyBriefing.registration` is mocked to expose
// `createLegalWorkspaceDailyBriefingRegistration` as a `jest.fn()` so test 2
// can verify the loader is threaded into the factory call.
// ---------------------------------------------------------------------------

// Stub every static section registration. Each must satisfy the minimal
// `SectionRegistration` contract (just an `id` + `category` + `factory`).
// NOTE: `jest.mock` factory bodies are hoisted, so the helper has to be
// inlined inside each factory closure. We use string-literal paths (no
// template literals or constants) to keep Jest's static-hoist analysis happy.

jest.mock('../../../../solutions/LegalWorkspace/src/sections/getStarted.registration', () => ({
  getStartedRegistration: { id: 'get-started', category: 'core', factory: () => ({ id: 'get-started' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/quickSummary.registration', () => ({
  quickSummaryRegistration: { id: 'quick-summary', category: 'core', factory: () => ({ id: 'quick-summary' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/latestUpdates.registration', () => ({
  latestUpdatesRegistration: { id: 'latest-updates', category: 'core', factory: () => ({ id: 'latest-updates' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/todo.registration', () => ({
  todoRegistration: { id: 'todo', category: 'core', factory: () => ({ id: 'todo' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/documents.registration', () => ({
  documentsRegistration: { id: 'documents', category: 'core', factory: () => ({ id: 'documents' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/calendar.registration', () => ({
  calendarRegistration: { id: 'calendar', category: 'core', factory: () => ({ id: 'calendar' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/projects.registration', () => ({
  projectsRegistration: { id: 'projects', category: 'core', factory: () => ({ id: 'projects' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/invoices.registration', () => ({
  invoicesRegistration: { id: 'invoices', category: 'core', factory: () => ({ id: 'invoices' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/workAssignments.registration', () => ({
  workAssignmentsRegistration: {
    id: 'work-assignments',
    category: 'core',
    factory: () => ({ id: 'work-assignments' }),
  },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/matters.registration', () => ({
  mattersRegistration: { id: 'matters', category: 'core', factory: () => ({ id: 'matters' }) },
}));
// Sections added to the registry after this suite was written (#1906). Ids match
// each real registration's `id`, so the worklist / decisionRecords assertions
// below and the registry's metadata-drift guard see the same ids as production.
jest.mock('../../../../solutions/LegalWorkspace/src/sections/communications.registration', () => ({
  communicationsRegistration: { id: 'communications', category: 'data', factory: () => ({ id: 'communications' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/composeEditor.registration', () => ({
  composeEditorRegistration: {
    id: 'compose-editor',
    category: 'productivity',
    factory: () => ({ id: 'compose-editor' }),
  },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/email.registration', () => ({
  emailRegistration: { id: 'email', category: 'data', factory: () => ({ id: 'email' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/reconciliation.registration', () => ({
  reconciliationRegistration: { id: 'reconciliation', category: 'data', factory: () => ({ id: 'reconciliation' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/worklist.registration', () => ({
  worklistRegistration: { id: 'worklist', category: 'data', factory: () => ({ id: 'worklist' }) },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/decisionRecords.registration', () => ({
  decisionRecordsRegistration: {
    id: 'decisionRecords',
    category: 'data',
    factory: () => ({ id: 'decisionRecords' }),
  },
}));
jest.mock('../../../../solutions/LegalWorkspace/src/sections/analysisHub.registration', () => ({
  analysisHubRegistration: { id: 'analysis', category: 'ai', factory: () => ({ id: 'analysis' }) },
}));

// Mock the dailyBriefing registration. `createLegalWorkspaceDailyBriefingRegistration`
// is the seam under test (test 2 asserts the loader is threaded through). We
// stash the jest.fn on `globalThis` so the test body can introspect it after
// the mock is hoisted above the imports.
jest.mock('../../../../solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration', () => {
  const factoryMock = jest.fn((_options?: { loadNotificationContext?: () => Promise<unknown> }) => ({
    id: 'daily-briefing',
    category: 'core',
    factory: () => ({ id: 'daily-briefing' }),
  }));
  // Expose the mock for test assertions.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).__createDailyBriefingMock = factoryMock;
  return {
    createLegalWorkspaceDailyBriefingRegistration: factoryMock,
    dailyBriefingRegistration: {
      id: 'daily-briefing',
      category: 'core',
      factory: () => ({ id: 'daily-briefing' }),
    },
  };
});

// `@spaarke/ui-components` is routed to a local mock via the Jest
// moduleNameMapper (see jest.config.cjs). The mock at
// `test/__mocks__/spaarke-ui-components.tsx` provides `SectionRegistration`,
// `SectionCategory`, `NarrateRequest`, and `SECTION_METADATA_CATALOG`
// stand-ins matching every section id stubbed below — so the dev-mode
// metadata-drift guard inside `runRegistryDevGuards` finds no drift in tests.

// ---------------------------------------------------------------------------
// Now import the LegalWorkspace factory under test.
// ---------------------------------------------------------------------------

import {
  createLegalWorkspaceSectionRegistry,
  SECTION_REGISTRY,
} from '../../../../solutions/LegalWorkspace/src/sectionRegistry';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const getDailyBriefingMock = (): jest.Mock<any, any> => (globalThis as any).__createDailyBriefingMock as jest.Mock;

describe('createLegalWorkspaceSectionRegistry (R2 Option D)', () => {
  beforeEach(() => {
    getDailyBriefingMock().mockClear();
  });

  test('createLegalWorkspaceSectionRegistry() returns same widget IDs as SECTION_REGISTRY const', () => {
    const fromFactory = createLegalWorkspaceSectionRegistry();
    // Same length
    expect(fromFactory.length).toBe(SECTION_REGISTRY.length);
    // Same IDs in same order
    expect(fromFactory.map(r => r.id)).toEqual(SECTION_REGISTRY.map(r => r.id));
  });

  test('createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } }) threads loader', () => {
    const loader = jest.fn().mockResolvedValue(null);
    const mock = getDailyBriefingMock();
    mock.mockClear();

    const registry = createLegalWorkspaceSectionRegistry({
      dailyBriefing: { loadNotificationContext: loader },
    });

    // dailyBriefing entry exists in the registry
    const dailyBriefingEntry = registry.find(r => r.id === 'daily-briefing');
    expect(dailyBriefingEntry).toBeDefined();

    // The factory was called WITH the supplied loader (Option D contract).
    expect(mock).toHaveBeenCalled();
    const lastCallArg = mock.mock.calls.at(-1)?.[0];
    expect(lastCallArg).toBeDefined();
    expect(lastCallArg?.loadNotificationContext).toBe(loader);
  });

  test('registry includes the `worklist` section (ontology task 156), once', () => {
    const ids = createLegalWorkspaceSectionRegistry().map(r => r.id);
    expect(ids.filter(id => id === 'worklist')).toHaveLength(1);
    expect(SECTION_REGISTRY.map(r => r.id)).toContain('worklist');
  });

  test('registry includes the `decisionRecords` section (ontology task 045), once', () => {
    const ids = createLegalWorkspaceSectionRegistry().map(r => r.id);
    expect(ids.filter(id => id === 'decisionRecords')).toHaveLength(1);
    expect(SECTION_REGISTRY.map(r => r.id)).toContain('decisionRecords');
  });

  test('legacy setLegalWorkspaceDailyBriefingNotificationLoader API is removed', () => {
    // Read the LegalWorkspace barrel source text. The R2 task 002 band-aid
    // setter MUST NOT be re-exported. This locks in the Option D migration:
    // a future commit accidentally re-exporting the setter (or re-introducing
    // the module-mutation slot) will fail this assertion.
    //
    // We check source text rather than require()-ing the barrel because the
    // barrel transitively imports the React component tree (LegalWorkspaceApp,
    // FluentProvider, etc.), which would force this test to spin up jsdom
    // for what is fundamentally an API-surface contract check.
    const fs = require('fs');
    const path = require('path');
    const barrelPath = path.resolve(__dirname, '../../../../solutions/LegalWorkspace/src/index.ts');
    const barrelSource: string = fs.readFileSync(barrelPath, 'utf8');

    // The band-aid setter export should be gone (both `export {...}` and
    // re-export forms). The name is allowed to appear in DOCBLOCK COMMENTS
    // documenting the removal — we only fail on actual export/import lines.
    const exportRegex =
      /^[^/\n]*\b(?:export\s*\{[^}]*\b|export\s+(?:const|function|let|var)\s+)setLegalWorkspaceDailyBriefingNotificationLoader\b/m;

    expect(barrelSource).not.toMatch(exportRegex);

    // Also assert no live re-export from the dailyBriefing.registration module
    // (which used to export the setter pre-Option D).
    const dailyBriefingPath = path.resolve(
      __dirname,
      '../../../../solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts'
    );
    const dailyBriefingSource: string = fs.readFileSync(dailyBriefingPath, 'utf8');
    expect(dailyBriefingSource).not.toMatch(exportRegex);

    // And no live var declaration of the module-mutable slot.
    const slotRegex = /^[^/\n]*\b(?:let|const|var)\s+_globalNotificationLoader\b/m;
    expect(dailyBriefingSource).not.toMatch(slotRegex);
  });

  describe('section-stub guard (#1906)', () => {
    // Source-text guard (same technique as the test above). An unstubbed
    // registration compiles its real dependency tree against this package's
    // ui-components MOCK — see the "Pre-import mocks" note at the top. Today
    // that fails late and far from the cause (a TS2305 in another package's
    // source, the night some unrelated PR adds a ui-components export), so it
    // fails here instead, naming the module to stub. Caveat: if the unstubbed
    // module does not compile at all, the suite fails before this test runs —
    // the compile error then names the registration file.
    const registryImports = (registrySource: string): string[] =>
      [...registrySource.matchAll(/^\s*import\s[^;]*?\bfrom\s*["']\.\/(sections\/[^"']+)["']/gm)]
        .map(m => m[1])
        .sort();
    const unstubbed = (registrySource: string, testSource: string): string[] => {
      const mocked = new Set(
        [
          ...testSource.matchAll(/jest\.mock\(\s*'\.\.\/\.\.\/\.\.\/\.\.\/solutions\/LegalWorkspace\/src\/(sections\/[^']+)'/g),
        ].map(m => m[1])
      );
      return registryImports(registrySource).filter(spec => !mocked.has(spec));
    };

    test('every section module sectionRegistry.ts imports is stubbed by a jest.mock in this file', () => {
      const fs = require('fs');
      const path = require('path');
      const registrySource: string = fs.readFileSync(
        path.resolve(__dirname, '../../../../solutions/LegalWorkspace/src/sectionRegistry.ts'),
        'utf8'
      );
      const thisSource: string = fs.readFileSync(__filename, 'utf8');

      // The parse must find the registry's section imports at all, or the
      // assertion below would pass vacuously.
      expect(registryImports(registrySource).length).toBeGreaterThanOrEqual(10);
      expect(unstubbed(registrySource, thisSource)).toEqual([]);
    });

    test('must-fire control: a registry import with no matching jest.mock is reported', () => {
      const registrySource = [
        'import { aRegistration } from "./sections/a.registration";',
        'import { bRegistration } from "./sections/b/b.registration";',
        'import type { SectionRegistration } from "@spaarke/ui-components";',
      ].join('\n');
      const testSource =
        "jest.mock('../../../../solutions/LegalWorkspace/src/sections/a.registration', () => ({}));";
      expect(unstubbed(registrySource, testSource)).toEqual(['sections/b/b.registration']);
    });
  });
});
