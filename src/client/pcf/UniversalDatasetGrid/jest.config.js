/**
 * Jest configuration for UniversalDatasetGrid PCF control
 *
 * Task 018: Add Unit Tests for Grid Enhancements
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/control'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
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
  },
  transformIgnorePatterns: [
    '/node_modules/(?!(@fluentui)/)'
  ],
  collectCoverageFrom: [
    'control/**/*.{ts,tsx}',
    '!control/**/*.d.ts',
    '!control/**/index.ts',
    '!control/**/index-minimal.ts',
    '!control/**/generated/**'
  ],
  coverageThreshold: {
    global: {
      branches: 80,
      functions: 80,
      lines: 80,
      statements: 80
    }
  }
};
