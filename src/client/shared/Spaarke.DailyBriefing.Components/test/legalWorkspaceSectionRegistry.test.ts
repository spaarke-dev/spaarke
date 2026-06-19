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
 *      is exercised by the existing useBriefingNotifications + smoke tests).
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
// LegalWorkspace's `sectionRegistry.ts` imports 11 individual section
// registrations. Each of those transitively imports React, services, hooks,
// etc. We mock every section registration to a trivial `SectionRegistration`
// shape so the factory can build without dragging in the whole app.
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

jest.mock("../../../../solutions/LegalWorkspace/src/sections/getStarted.registration", () => ({
  getStartedRegistration: { id: "get-started", category: "core", factory: () => ({ id: "get-started" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/quickSummary.registration", () => ({
  quickSummaryRegistration: { id: "quick-summary", category: "core", factory: () => ({ id: "quick-summary" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/latestUpdates.registration", () => ({
  latestUpdatesRegistration: { id: "latest-updates", category: "core", factory: () => ({ id: "latest-updates" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/todo.registration", () => ({
  todoRegistration: { id: "todo", category: "core", factory: () => ({ id: "todo" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/documents.registration", () => ({
  documentsRegistration: { id: "documents", category: "core", factory: () => ({ id: "documents" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/calendar.registration", () => ({
  calendarRegistration: { id: "calendar", category: "core", factory: () => ({ id: "calendar" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/projects.registration", () => ({
  projectsRegistration: { id: "projects", category: "core", factory: () => ({ id: "projects" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/invoices.registration", () => ({
  invoicesRegistration: { id: "invoices", category: "core", factory: () => ({ id: "invoices" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/workAssignments.registration", () => ({
  workAssignmentsRegistration: { id: "work-assignments", category: "core", factory: () => ({ id: "work-assignments" }) },
}));
jest.mock("../../../../solutions/LegalWorkspace/src/sections/matters.registration", () => ({
  mattersRegistration: { id: "matters", category: "core", factory: () => ({ id: "matters" }) },
}));

// Mock the dailyBriefing registration. `createLegalWorkspaceDailyBriefingRegistration`
// is the seam under test (test 2 asserts the loader is threaded through). We
// stash the jest.fn on `globalThis` so the test body can introspect it after
// the mock is hoisted above the imports.
jest.mock("../../../../solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration", () => {
  const factoryMock = jest.fn(
    (_options?: { loadNotificationContext?: () => Promise<unknown> }) => ({
      id: "daily-briefing",
      category: "core",
      factory: () => ({ id: "daily-briefing" }),
    }),
  );
  // Expose the mock for test assertions.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).__createDailyBriefingMock = factoryMock;
  return {
    createLegalWorkspaceDailyBriefingRegistration: factoryMock,
    dailyBriefingRegistration: {
      id: "daily-briefing",
      category: "core",
      factory: () => ({ id: "daily-briefing" }),
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
} from "../../../../solutions/LegalWorkspace/src/sectionRegistry";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const getDailyBriefingMock = (): jest.Mock<any, any> =>
  (globalThis as any).__createDailyBriefingMock as jest.Mock;

describe("createLegalWorkspaceSectionRegistry (R2 Option D)", () => {
  beforeEach(() => {
    getDailyBriefingMock().mockClear();
  });

  test("createLegalWorkspaceSectionRegistry() returns same widget IDs as SECTION_REGISTRY const", () => {
    const fromFactory = createLegalWorkspaceSectionRegistry();
    // Same length
    expect(fromFactory.length).toBe(SECTION_REGISTRY.length);
    // Same IDs in same order
    expect(fromFactory.map((r) => r.id)).toEqual(
      SECTION_REGISTRY.map((r) => r.id),
    );
  });

  test("createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } }) threads loader", () => {
    const loader = jest.fn().mockResolvedValue(null);
    const mock = getDailyBriefingMock();
    mock.mockClear();

    const registry = createLegalWorkspaceSectionRegistry({
      dailyBriefing: { loadNotificationContext: loader },
    });

    // dailyBriefing entry exists in the registry
    const dailyBriefingEntry = registry.find((r) => r.id === "daily-briefing");
    expect(dailyBriefingEntry).toBeDefined();

    // The factory was called WITH the supplied loader (Option D contract).
    expect(mock).toHaveBeenCalled();
    const lastCallArg = mock.mock.calls.at(-1)?.[0];
    expect(lastCallArg).toBeDefined();
    expect(lastCallArg?.loadNotificationContext).toBe(loader);
  });

  test("legacy setLegalWorkspaceDailyBriefingNotificationLoader API is removed", () => {
    // Read the LegalWorkspace barrel source text. The R2 task 002 band-aid
    // setter MUST NOT be re-exported. This locks in the Option D migration:
    // a future commit accidentally re-exporting the setter (or re-introducing
    // the module-mutation slot) will fail this assertion.
    //
    // We check source text rather than require()-ing the barrel because the
    // barrel transitively imports the React component tree (LegalWorkspaceApp,
    // FluentProvider, etc.), which would force this test to spin up jsdom
    // for what is fundamentally an API-surface contract check.
    const fs = require("fs");
    const path = require("path");
    const barrelPath = path.resolve(
      __dirname,
      "../../../../solutions/LegalWorkspace/src/index.ts",
    );
    const barrelSource: string = fs.readFileSync(barrelPath, "utf8");

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
      "../../../../solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts",
    );
    const dailyBriefingSource: string = fs.readFileSync(
      dailyBriefingPath,
      "utf8",
    );
    expect(dailyBriefingSource).not.toMatch(exportRegex);

    // And no live var declaration of the module-mutable slot.
    const slotRegex =
      /^[^/\n]*\b(?:let|const|var)\s+_globalNotificationLoader\b/m;
    expect(dailyBriefingSource).not.toMatch(slotRegex);
  });
});
