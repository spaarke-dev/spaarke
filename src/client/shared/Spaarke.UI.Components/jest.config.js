module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    // EntityConfigurationService.ts / CustomCommandFactory.ts / CommandRegistry.ts /
    // Toolbar/CommandToolbar.tsx DELETED here — spaarke-ontology-platform-r1 task 080 / C-19,
    // 2026-10-03 (dead infra from the deleted UniversalDatasetGrid PCF).
    'src/services/CommandExecutor.ts',
    'src/hooks/useKeyboardShortcuts.ts',
    'src/hooks/useForceSimulation.ts',
    'src/utils/themeDetection.ts',
    'src/components/DatasetGrid/GridView.tsx',
    'src/components/SprkChat/SprkChat.tsx',
    'src/components/SprkChat/SprkChatMessage.tsx',
    'src/components/SprkChat/SprkChatInput.tsx',
    'src/components/SprkChat/SprkChatContextSelector.tsx',
    'src/components/SprkChat/SprkChatPredefinedPrompts.tsx',
    'src/components/SprkChat/SprkChatHighlightRefine.tsx',
    'src/components/SprkChat/hooks/useSseStream.ts',
    'src/components/SprkChat/hooks/useChatSession.ts',
    '!src/**/*.d.ts',
    '!src/**/index.ts',
    '!src/**/__tests__/**',
    '!src/__mocks__/**'
  ],
  coverageThreshold: {
    global: {
      statements: 70,
      branches: 65,
      functions: 70,
      lines: 70
    }
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    '^diff$': '<rootDir>/src/__mocks__/diff.js'
  },
  transformIgnorePatterns: [
    'node_modules/(?!(d3-force|d3-dispatch|d3-quadtree|d3-timer|marked)/)'
  ],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true
      }
    }],
    '^.+\\.jsx?$': ['ts-jest', {
      tsconfig: {
        allowJs: true,
        esModuleInterop: true
      }
    }]
  }
};
