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
        // Deep shared-lib imports (e.g. `@spaarke/ui-components/dist/services/PolymorphicResolverService`
        // for `cleanGuid`, task 089) resolve to compiled ESM `dist/*.js` files that ts-jest's default
        // transformIgnorePatterns skips (node_modules). Map the deep specifier onto the shared TS SOURCE
        // instead so ts-jest can transform it — mirrors the identical, previously-diagnosed fix in
        // `RegardingResolver/jest.config.js` (unified-access-control-r2 task 051, 2026-09-04).
        "^@spaarke/ui-components/dist/(.*)$": "<rootDir>/../../shared/Spaarke.UI.Components/src/$1",
        // The shared lib carries its OWN node_modules with React 19 (it also targets Code Pages).
        // Left unmapped, a deep-imported shared source file would resolve `react`/`react-dom` against
        // THAT copy — a second React instance alongside this PCF's React 16 — breaking hooks.
        "^react$": "<rootDir>/node_modules/react",
        "^react-dom$": "<rootDir>/node_modules/react-dom",
        "^react/jsx-runtime$": "<rootDir>/node_modules/react/jsx-runtime",
    },
    setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
    transform: {
        "^.+\\.tsx?$": ["ts-jest", {
            tsconfig: "tsconfig.json",
            // `generated/ManifestTypes` (imported type-only as `IInputs`) is pcf-scripts build
            // output and is gitignored, so a fresh checkout has none. TS2307 for it must not make
            // the suites unloadable; every other diagnostic still fails the run (#1392).
            diagnostics: { ignoreCodes: [2307] },
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
