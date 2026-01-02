/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/control'],
  testMatch: ['**/__tests__/**/*.test.tsx', '**/__tests__/**/*.test.ts'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  moduleNameMapper: {
    // Handle module aliases (if any)
    '^@/(.*)$': '<rootDir>/control/$1',
    // Map @spaarke/ui-components to shared library
    '^@spaarke/ui-components$': '<rootDir>/../../shared/Spaarke.UI.Components/src/index.ts',
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: 'tsconfig.test.json',
    }],
  },
  transformIgnorePatterns: [
    'node_modules/(?!(@fluentui)/)',
  ],
  collectCoverageFrom: [
    'control/context/**/*.{ts,tsx}',
    'control/hooks/**/*.{ts,tsx}',
    'control/components/**/*.{ts,tsx}',
    '!control/**/*.stories.tsx',
    '!control/**/index.ts',
  ],
  coverageThreshold: {
    global: {
      branches: 70,
      functions: 70,
      lines: 70,
      statements: 70,
    },
  },
  coverageReporters: ['text', 'lcov', 'html'],
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
