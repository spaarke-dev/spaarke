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
