/**
 * Jest harness for @spaarke/visuals (VHVU-090).
 *
 * Mirrors the VisualHost PCF harness. `.cjs` extension because this package is
 * `"type": "module"` — a `module.exports` config in a plain `.js` file would be
 * parsed as ESM and fail. No React/@fluentui singleton moduleNameMapper is
 * needed here (unlike the PCF's cross-package harness): this package resolves a
 * single copy of React + @fluentui from its own node_modules.
 *
 * @type {import('jest').Config}
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.tsx', '**/__tests__/**/*.test.ts'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: 'tsconfig.jest.json' }],
    // Transform ES modules from @fluentui/react-charting + d3 (see transformIgnorePatterns).
    '^.+\\.(js|jsx)$': 'babel-jest',
  },
  transformIgnorePatterns: [
    'node_modules/(?!(@fluentui/react-charting|d3|d3-.*|internmap|delaunator|robust-predicates)/)',
  ],
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
