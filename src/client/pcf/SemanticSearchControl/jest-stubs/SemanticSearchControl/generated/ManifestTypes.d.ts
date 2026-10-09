/**
 * Jest-only type stub for pcf-scripts' gitignored `generated/ManifestTypes`.
 * Merged into the real source tree via `rootDirs` in tsconfig.jest.json, so a fresh
 * checkout type-checks without running a PCF build. When the real generated file exists
 * it wins (same path in the primary root). Only the names the sources import are declared;
 * a misspelled module path or export still fails type-checking.
 */
export interface IInputs {
  [name: string]: ComponentFramework.PropertyTypes.Property | undefined;
}
export interface IOutputs {
  [name: string]: unknown;
}
