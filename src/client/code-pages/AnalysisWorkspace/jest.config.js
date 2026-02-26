module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    'src/hooks/useAnalysisLoader.ts',
    'src/hooks/useAutoSave.ts',
    'src/hooks/useSelectionBroadcast.ts',
    'src/components/AnalysisToolbar.tsx',
    'src/App.tsx',
    '!src/**/*.d.ts',
    '!src/**/index.ts',
    '!src/**/__tests__/**',
  ],
  coverageThreshold: {
    global: {
      statements: 80,
      branches: 75,
      functions: 80,
      lines: 80,
    },
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Map @spaarke/ui-components imports to mocks for isolation
    '^@spaarke/ui-components/services/SprkChatBridge$':
      '<rootDir>/src/__tests__/mocks/MockSprkChatBridge',
    '^@spaarke/ui-components/components/RichTextEditor/hooks/useDocumentStreamConsumer$':
      '<rootDir>/src/__tests__/mocks/MockUseDocumentStreamConsumer',
    '^@spaarke/ui-components/hooks/useDocumentHistory$':
      '<rootDir>/src/__tests__/mocks/MockUseDocumentHistory',
    '^@spaarke/ui-components$':
      '<rootDir>/src/__tests__/mocks/MockUiComponents',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        diagnostics: false,
        tsconfig: {
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          strict: false,
          noImplicitAny: false,
        },
      },
    ],
  },
};
