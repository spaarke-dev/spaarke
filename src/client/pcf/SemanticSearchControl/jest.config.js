/**
 * Jest configuration for SemanticSearchControl PCF
 *
 * @see https://jestjs.io/docs/configuration
 */
module.exports = {
    preset: "ts-jest",
    testEnvironment: "jsdom",
    roots: ["<rootDir>/SemanticSearchControl"],
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
            tsconfig: "tsconfig.json",
        }],
    },
    moduleFileExtensions: ["ts", "tsx", "js", "jsx", "json"],
    collectCoverageFrom: [
        "SemanticSearchControl/**/*.{ts,tsx}",
        "!SemanticSearchControl/**/*.d.ts",
        "!SemanticSearchControl/generated/**",
        "!SemanticSearchControl/index.ts",
    ],
    coverageThreshold: {
        global: {
            branches: 50,
            functions: 50,
            lines: 50,
            statements: 50,
        },
    },
    // Mock platform libraries that are provided at runtime
    globals: {
        Xrm: {},
    },
};
