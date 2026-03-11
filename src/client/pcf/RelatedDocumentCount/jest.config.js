/**
 * Jest configuration for RelatedDocumentCount PCF
 *
 * Uses React 16 compatible test libraries (per ADR-022).
 * @see https://jestjs.io/docs/configuration
 */
module.exports = {
    preset: "ts-jest",
    testEnvironment: "jsdom",
    roots: ["<rootDir>/RelatedDocumentCount"],
    testMatch: [
        "**/__tests__/**/*.test.{ts,tsx}",
        "**/*.test.{ts,tsx}",
    ],
    moduleNameMapper: {
        // Handle CSS imports (if any)
        "\\.(css|less|scss|sass)$": "identity-obj-proxy",
    },
    setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
    transform: {
        "^.+\\.tsx?$": ["ts-jest", {
            tsconfig: "tsconfig.test.json",
        }],
    },
    moduleFileExtensions: ["ts", "tsx", "js", "jsx", "json"],
    collectCoverageFrom: [
        "RelatedDocumentCount/**/*.{ts,tsx}",
        "!RelatedDocumentCount/**/*.d.ts",
        "!RelatedDocumentCount/generated/**",
        "!RelatedDocumentCount/index.ts",
        "!RelatedDocumentCount/types/**",
        "!RelatedDocumentCount/services/**",
    ],
    coverageThreshold: {
        global: {
            branches: 80,
            functions: 80,
            lines: 80,
            statements: 80,
        },
    },
    coverageReporters: ["text", "lcov"],
    // Mock platform libraries that are provided at runtime
    globals: {
        Xrm: {},
    },
};
