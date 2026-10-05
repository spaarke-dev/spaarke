/**
 * Test-local mock for `@spaarke/compose-components` (task 092, 2026-10-04).
 *
 * `legalWorkspaceSectionRegistry.test.ts` imports `sectionRegistry.ts`, which
 * imports `composeEditor.registration.ts` — one of ~10 LegalWorkspace section
 * registrations the registry factory aggregates — which imports these 4
 * symbols. Same "minimal stand-in so ts-jest can type-check" pattern as
 * `test/__mocks__/spaarke-ui-components.tsx`.
 */
import * as React from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const ComposeWorkspace: React.FC<any> = () => null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const useComposeLaunch: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const useComposeActionBridge: (...args: any[]) => any = () => ({});
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const useComposeToolbarActivation: (...args: any[]) => any = () => ({});
