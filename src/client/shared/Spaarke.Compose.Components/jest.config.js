// Jest config for @spaarke/compose-components (spaarkeai-compose-r2 task 030 integration).
//
// First test config for this package. Modelled on the sibling Spaarke.UI.Components
// jest.config.js (ts-jest + jsdom + RTL) with two differences:
//   1. testMatch picks up CO-LOCATED `*.test.tsx` (this package keeps tests next to
//      the widget, e.g. src/widgets/ComposeAiToolbar.test.tsx) — not a `__tests__/` dir.
//   2. Coverage is NOT gated (ADR-038: coverage is observation, never a gate).
//
// `@spaarke/*` runtime deps resolve via node_modules → their built `dist/` (produced by
// scripts/Build-AllClientComponents.ps1 -Component SharedLibs). Type-only `@spaarke/*`
// imports are erased by ts-jest and need no runtime resolution.
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
