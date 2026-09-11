/**
 * Stub for `./generated/ManifestTypes`.
 *
 * `pcf-scripts build` emits that file into `RegardingResolver/generated/` and it
 * is gitignored, so a clean checkout cannot typecheck `RegardingResolverApp.tsx`
 * or `index.ts` under Jest without first running a full PCF build. Mapping the
 * specifier here (see `jest.config.js` moduleNameMapper) keeps the unit suite
 * runnable standalone.
 *
 * These are TYPE-ONLY shapes mirroring the manifest's input/output properties —
 * the real generated file additionally carries the PCF framework's property
 * wrappers, which the tests do not exercise. Keep in sync with
 * `RegardingResolver/ControlManifest.Input.xml` when a property is added.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

export interface IInputs {
  entity: ComponentFramework.PropertyTypes.StringProperty;
  regardingTargets: ComponentFramework.PropertyTypes.StringProperty;
  regardingRecordType: ComponentFramework.PropertyTypes.LookupProperty;
  regardingRecordNumberField: ComponentFramework.PropertyTypes.StringProperty;
  regardingRecordNameField: ComponentFramework.PropertyTypes.StringProperty;
  title: ComponentFramework.PropertyTypes.StringProperty;
  showVersionFooter: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  readOnly: ComponentFramework.PropertyTypes.TwoOptionsProperty;
}

export interface IOutputs {
  regardingRecordType?: ComponentFramework.LookupValue[];
  regardingRecordNumberField?: string;
  regardingRecordNameField?: string;
}

// A module with only type exports emits nothing at runtime; jest's
// moduleNameMapper requires a resolvable module, so keep a value export.
export const __manifestTypesStub = true;
