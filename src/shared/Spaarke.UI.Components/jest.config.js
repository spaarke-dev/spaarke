module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    'src/services/EntityConfigurationService.ts',
    'src/services/CustomCommandFactory.ts',
    'src/services/CommandRegistry.ts',
    'src/services/CommandExecutor.ts',
    'src/hooks/useVirtualization.ts',
    'src/hooks/useKeyboardShortcuts.ts',
    'src/utils/themeDetection.ts',
    'src/components/Toolbar/CommandToolbar.tsx',
    'src/components/DatasetGrid/GridView.tsx',
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
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy'
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true
      }
    }]
  }
};
