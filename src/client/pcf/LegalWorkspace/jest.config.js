/**
 * Jest Configuration for LegalWorkspace PCF Control
 *
 * @see https://jestjs.io/docs/configuration
 */
module.exports = {
    preset: 'ts-jest',
    testEnvironment: 'jsdom',
    roots: ['<rootDir>'],
    testMatch: [
        '**/__tests__/**/*.test.ts',
        '**/__tests__/**/*.test.tsx'
    ],
    moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
    transform: {
        '^.+\\.(ts|tsx)$': ['ts-jest', {
            tsconfig: '<rootDir>/tsconfig.json',
            // Use isolated modules for faster compilation
            isolatedModules: true,
        }]
    },
    setupFilesAfterEnv: ['<rootDir>/__tests__/setupTests.ts'],
    moduleNameMapper: {
        // Handle CSS imports (none expected in this control, but guard anyway)
        '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    },
    // Exclude generated PCF output and node_modules
    testPathIgnorePatterns: [
        '/node_modules/',
        '/out/',
        '/generated/'
    ],
    collectCoverageFrom: [
        '**/*.{ts,tsx}',
        '!**/*.d.ts',
        '!**/index.ts',
        '!**/__tests__/**',
        '!**/out/**',
        '!**/generated/**',
    ],
    coverageReporters: ['text', 'lcov', 'html'],
};
