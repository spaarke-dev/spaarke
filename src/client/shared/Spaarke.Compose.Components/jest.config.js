// Jest config for @spaarke/compose-components (spaarkeai-compose-r2 task 030 integration).
//
// First test config for this package. Modelled on the sibling Spaarke.UI.Components
// jest.config.js (ts-jest + jsdom + RTL) with two differences:
//   1. testMatch picks up CO-LOCATED `*.test.tsx` (this package keeps tests next to
//      the widget, e.g. src/widgets/ComposeAiToolbar.test.tsx) — not a `__tests__/` dir.
//   2. Coverage is NOT gated (ADR-038: coverage is observation, never a gate).
//
// ---------------------------------------------------------------------------
// Sibling `@spaarke/*` resolution — the contract (r8 task 018, 2026-08-20)
// ---------------------------------------------------------------------------
// `@spaarke/*` runtime deps resolve via node_modules → their built `dist/` (produced by
// scripts/Build-AllClientComponents.ps1 -Component SharedLibs). Type-only `@spaarke/*`
// imports are erased by ts-jest and need no runtime resolution.
//
// `@spaarke/ui-components` is consumed as `dist/`, NOT mapped to its `src/` the way
// `@spaarke/ai-widgets` is below. Measured 2026-08-20: mapping it to `src` moves resolution of its
// OWN dependency graph into this package's node_modules, and 10 of its runtime deps are ones this
// package does not (and should not) declare — d3-force, lexical, pdfjs-dist, react-window,
// dompurify, diff, marked, @hello-pangea/dnd, @microsoft/applicationinsights-web, @spaarke/sdap-client.
// The experiment failed with 72 `Cannot find module '@fluentui/react-icons'` errors. The dist route
// keeps the package boundary honest; the cost is that CI must build the SharedLibs first
// (`compose-client-gate` in .github/workflows/sdap-ci.yml does exactly that, in dependency order:
// Spaarke.Auth → Spaarke.SdapClient → Spaarke.UI.Components → Spaarke.DocumentOperations).
//
// NEVER pass `{ virtual: true }` to a `jest.mock()` of an `@spaarke/*` specifier in this package.
// `virtual: true` registers the specifier in jest's RESOLVER, which is shared by every suite a
// worker runs — so one suite's virtual registration changes how a LATER suite resolves the same
// specifier, and the failure is invisible to a single-suite run. Measured 2026-08-20: with the
// SharedLibs dist present, 7 of the 8 suites that failed a full `--runInBand` package run were
// exactly the 7 files carrying the flag, and every one of them passed when run alone. The flag
// existed only as a workaround for the unbuilt dist that this gate now builds. The same rule was
// already documented per-file for `@spaarke/ai-widgets/events` (see ComposeWorkspace.browse /
// .search / .upload / .imports and hooks/usePendingRedline).
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/*.test.ts', '**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Task 111 (2026-07-10): `ComposeEditor.tsx` has a RUNTIME (non-type-only)
    // subpath import of `@spaarke/ai-widgets/events` (`useDispatchPaneEvent`).
    // `@spaarke/ai-widgets` ships no package.json `exports` map, so classic
    // Node resolution looks for `events/index.js` at the PACKAGE ROOT, which
    // only exists under its `dist/` output, not the package root itself — the
    // subpath resolves fine for TYPE-ONLY imports (erased by ts-jest) but
    // fails for a real runtime import the moment a test actually mounts
    // `ComposeEditor` (previously every test mocked it out, so this gap was
    // latent). Mirrors the identical mapping already in
    // `src/solutions/SpaarkeAi/jest.config.ts` — maps straight to `src` so no
    // dist build is required in CI.
    '^@spaarke/ai-widgets$': '<rootDir>/../Spaarke.AI.Widgets/src/index.ts',
    '^@spaarke/ai-widgets/(.*)$': '<rootDir>/../Spaarke.AI.Widgets/src/$1',
    // Dedupe React — mapping ai-widgets to its OWN source tree above means its
    // `import ... from 'react'` resolves through ITS `node_modules/react`, a
    // second copy from this package's. Two React instances = hooks fail with
    // "Cannot read properties of null (reading 'useRef')" (the dispatcher
    // pointer lives in the test-runner's React; a nested component holds a
    // different one). Pin to this package's single copy. Identical fix + same
    // rationale as `src/solutions/SpaarkeAi/jest.config.ts` (R5 task 038).
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
  },
  // @tiptap + the d3-force graph (pulled transitively by the @spaarke/ui-components barrel via
  // useForceSimulation) + marked ship ESM; let ts-jest transform them rather than skip them as
  // node_modules. Mirrors the sibling Spaarke.UI.Components d3 whitelist.
  transformIgnorePatterns: [
    'node_modules/(?!(@tiptap|prosemirror-.*|rope-sequence|w3c-keyname|orderedmap|d3-force|d3-dispatch|d3-quadtree|d3-timer|marked)/)',
  ],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true,
      },
    }],
    '^.+\\.jsx?$': ['ts-jest', {
      tsconfig: {
        allowJs: true,
        esModuleInterop: true,
      },
    }],
  },
};
