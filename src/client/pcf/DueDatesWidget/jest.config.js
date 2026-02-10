/**
 * Jest Configuration for DueDatesWidget PCF Control
 *
 * @see https://jestjs.io/docs/configuration
 */
module.exports = {
    preset: 'ts-jest',
    testEnvironment: 'jsdom',
    roots: ['<rootDir>/control'],
    testMatch: [
        '**/__tests__/**/*.test.ts',
        '**/__tests__/**/*.test.tsx'
    ],
    moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
    transform: {
        '^.+\\.(ts|tsx)$': ['ts-jest', {
            tsconfig: '<rootDir>/tsconfig.json'
        }]
    },
    setupFilesAfterEnv: ['<rootDir>/control/__tests__/setupTests.ts'],
    moduleNameMapper: {
        // Handle CSS imports (if any)
        '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
        // Handle module aliases if defined in tsconfig
        '^@/(.*)$': '<rootDir>/control/$1'
    },
    collectCoverageFrom: [
        'control/**/*.{ts,tsx}',
        '!control/**/*.d.ts',
        '!control/generated/**',
        '!control/index.ts',
        '!control/__tests__/**',
        // Exclude files that require extensive PCF runtime mocking
        '!control/components/DueDatesWidgetRoot.tsx',
        '!control/components/WidgetFooter.tsx',
        '!control/components/ErrorBoundary.tsx',
        '!control/providers/ThemeProvider.ts',
        '!control/services/eventFilterService.ts',
        '!control/hooks/useUpcomingEvents.ts',  // Hook testing requires React 18 renderHook
        '!control/utils/index.ts'  // Re-export only
    ],
    coverageThreshold: {
        // Coverage thresholds for tested components and utilities
        // Tested with 100% coverage:
        //   - DateColumn, EventListItem, EventTypeBadge, DaysUntilDueBadge
        //   - daysUntilDue, eventTypeColors utilities
        //   - navigationService (84%)
        // Helper functions from useUpcomingEvents are tested separately
        global: {
            branches: 80,
            functions: 80,
            lines: 80,
            statements: 80
        }
    },
    coverageReporters: ['text', 'lcov', 'html'],
    testPathIgnorePatterns: [
        '/node_modules/',
        '/generated/'
    ],
    globals: {
        'ts-jest': {
            isolatedModules: true
        }
    }
};
