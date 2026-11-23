module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
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
    '^diff$': '<rootDir>/src/__mocks__/diff.js',
    // Code pages under src/solutions/* import this package by its alias and have no node_modules of their own in
    // CI; a test that renders one (recordFamilyPage.test.tsx) resolves the alias to the source, and the shared
    // singletons to this package's copy so there is one React and one Fluent context.
    '^@spaarke/ui-components/(.*)$': '<rootDir>/src/$1',
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components'
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
