/**
 * Test-local mock for `@spaarke/ai-widgets` (task 092, 2026-10-04).
 *
 * `legalWorkspaceSectionRegistry.test.ts` imports `sectionRegistry.ts`, which
 * imports `analysisHub.registration.ts` — another LegalWorkspace section the
 * registry factory aggregates — which imports `AnalysisHubWidget`. Same
 * minimal-stand-in pattern as the other `test/__mocks__/spaarke-*` files.
 */
import * as React from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const AnalysisHubWidget: React.FC<any> = () => null;
