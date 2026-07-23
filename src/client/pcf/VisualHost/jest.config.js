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
    // VHVU-060: the pure visuals now live in the sibling @spaarke/visuals
    // package, which ships its OWN node_modules copy of React + @fluentui.
    // Force a single copy of the shared runtime libs (resolved from this PCF's
    // node_modules) so cross-package component imports don't load two Reacts
    // (→ "Invalid hook call") or two @fluentui token contexts under jest.
    '^react$': '<rootDir>/node_modules/react',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    '^scheduler$': '<rootDir>/node_modules/scheduler',
    '^scheduler/(.*)$': '<rootDir>/node_modules/scheduler/$1',
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: 'tsconfig.test.json',
    }],
    // Transform ES modules from @fluentui/react-charting and its dependencies
    '^.+\\.(js|jsx)$': 'babel-jest',
  },
  transformIgnorePatterns: [
    // Allow transformation of @fluentui/react-charting and d3 modules
    'node_modules/(?!(@fluentui/react-charting|d3|d3-.*|internmap|delaunator|robust-predicates)/)',
  ],
  collectCoverageFrom: [
    'control/components/**/*.{ts,tsx}',
    '!control/components/**/*.stories.tsx',
    '!control/components/**/index.ts',
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
