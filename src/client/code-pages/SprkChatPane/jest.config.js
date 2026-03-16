module.exports = {
  preset: 'ts-jest',
  testEnvironment: '<rootDir>/jest-environment-jsdom-configurable-location.js',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    'src/services/contextService.ts',
    '!src/**/*.d.ts',
    '!src/**/index.ts',
    '!src/**/__tests__/**',
  ],
  // Coverage thresholds are set lower because current tests cover Phase 2 features only
  // (resolveContextMapping + detectPageType). Full coverage will increase as more
  // tests are added for other contextService functions (detectContext, session, polling).
  coverageThreshold: {
    global: {
      statements: 40,
      branches: 35,
      functions: 20,
      lines: 40,
    },
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Map @spaarke/ui-components to a mock to avoid pulling in the full library
    '^@spaarke/ui-components$': '<rootDir>/src/__tests__/mocks/MockUiComponents',
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
