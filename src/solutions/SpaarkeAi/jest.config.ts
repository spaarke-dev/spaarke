import type { Config } from 'jest';

/**
 * Jest configuration for SpaarkeAi solution unit tests.
 *
 * Tests are placed alongside components in __tests__ directories.
 * Module mapping resolves @spaarke/* packages to their source trees
 * so tests don't require a dist build of the shared libraries.
 *
 * @see src/client/shared/Spaarke.AI.Widgets/jest.config.ts — reference config
 */
const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}', '**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    // Map workspace packages to source — avoids needing dist builds in CI
    '^@spaarke/ai-widgets$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/index.ts',
    // Subpath imports — match deep-import patterns like
    // `@spaarke/ai-widgets/hooks/useWorkspaceLayouts` used by SpaarkeAi adapters
    // that skip the barrel's side-effect widget registration.
    // (R6 Hotfix Wave B-G9c3 unblocks ConversationPane.r5.test.tsx +
    // ConversationPane.slash-nl-rewire.test.tsx — both blocked on this mapping
    // gap pre-fix.)
    '^@spaarke/ai-widgets/(.*)$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/$1',
    '^@spaarke/ui-components$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/index.ts',
    // Deep-import twin of the exact mapping above (mirrors the ai-widgets
    // pair). Unblocks suites that requireActual('@spaarke/ai-widgets') —
    // its barrel reaches CreateMatterWizardWidget which deep-imports
    // `@spaarke/ui-components/components/CreateMatterWizard`; without this
    // mapping the suite dies at module load (pre-existing failure noted in
    // ai-architecture-redesign-r1 task 023; fixed here for the Click-path
    // ConversationPane tests).
    '^@spaarke/ui-components/(.*)$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/$1',
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/__mocks__/@spaarke/auth.ts',
    // spaarkeai-compose-r1 task 043: `useDocumentActions` hook lives in
    // @spaarke/document-operations (extracted from SemanticSearch in task 031).
    // Map to source so ComposeToolbar tests can mock the hook directly.
    '^@spaarke/document-operations$': '<rootDir>/../../client/shared/Spaarke.DocumentOperations/src/index.ts',
    '^@spaarke/document-operations/(.*)$': '<rootDir>/../../client/shared/Spaarke.DocumentOperations/src/$1',
    // spaarkeai-compose-r1 task 091 (Phase 7): ComposeWorkspace + siblings
    // promoted from `src/solutions/SpaarkeAi/src/components/compose/` to
    // `@spaarke/compose-components` (Calendar Pattern D precedent). Map to
    // source so component tests can import the widgets by package name.
    '^@spaarke/compose-components$': '<rootDir>/../../client/shared/Spaarke.Compose.Components/src/index.ts',
    '^@spaarke/compose-components/(.*)$': '<rootDir>/../../client/shared/Spaarke.Compose.Components/src/$1',
    '^@spaarke/ai-context$': '<rootDir>/../../client/shared/Spaarke.AI.Context/src/index.ts',
    '^@spaarke/ai-outputs$': '<rootDir>/../../client/shared/Spaarke.AI.Outputs/src/index.ts',
    // messaging-communication-app-r2 task 030: @spaarke/communication-components
    // hosts CommunicationsWorkspaceWidget (rich Pattern D communications-list
    // widget). Map to source, mirrors the compose-components pair above.
    '^@spaarke/communication-components$': '<rootDir>/../../client/shared/Spaarke.Communication.Components/src/index.ts',
    '^@spaarke/communication-components/(.*)$': '<rootDir>/../../client/shared/Spaarke.Communication.Components/src/$1',
    // d3-force ships pure ESM — ts-jest's CommonJS transform can't consume it.
    // Map to a tiny CJS stub so transitive imports of useForceSimulation don't
    // crash jsdom tests. (R5 task 038.) The stub returns the chainable
    // simulation surface the hook expects.
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    // `marked` ships pure ESM that ts-jest's CommonJS transform can't consume.
    // Every test transitively importing @spaarke/ui-components/services/
    // renderMarkdown fails with "SyntaxError: Unexpected token 'export'" at
    // marked.esm.js parse time. Map to a pass-through stub so tests don't need
    // a Markdown render. (R6 Hotfix Wave B-G9c3, 2026-06-10.)
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    // `@spaarke/sdap-client` is not resolvable from the SpaarkeAi workspace
    // (it lives on the PCF / Office Add-in module-resolution paths). Every
    // test transitively importing `@spaarke/ui-components/services/index`
    // fails because EntityCreationService.ts has a top-level
    // `import { SdapApiClient } from '@spaarke/sdap-client'`. The 057
    // affordance tests (Send/AddToAssistant/Pin) all touch this chain. Map
    // to a tiny stub. (R6 Wave C-G3 gap-fill, 2026-06-11.)
    '^@spaarke/sdap-client$': '<rootDir>/src/__mocks__/sdap-client.ts',
    // Dedupe React — the workspace-linked shared libraries each have their own
    // node_modules/react copy. Without forcing a single instance, hooks fail
    // with "Cannot read properties of null (reading 'useRef')" because the
    // dispatcher pointer lives in the test-runner's React, but a sub-component
    // imports a SECOND React instance from a nested node_modules. (R5 task 038.)
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    // CSS modules
    '\\.css$': 'identity-obj-proxy',
  },
  transform: {
    '^.+\\.(ts|tsx)$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          // Override module resolution for Jest (CommonJS-compatible)
          module: 'commonjs',
          moduleResolution: 'node',
          // Relax Vite-specific settings that break ts-jest
          allowImportingTsExtensions: false,
          noEmit: false,
        },
      },
    ],
  },
  testRunner: 'jest-circus/runner',
  collectCoverage: false,
  moduleDirectories: ['node_modules', '<rootDir>/node_modules'],
};

export default config;
