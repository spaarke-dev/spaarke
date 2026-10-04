/**
 * @spaarke/events-components
 *
 * Shared React components for Events + Tasks surfaces.
 *
 * Consumers:
 *  - `src/solutions/EventsPage/` (standalone code page `sprk_eventspage`)
 *  - SpaarkeAi Calendar workspace widget (task 115, dispatched next)
 *
 * Architecture:
 *  - Pure data + UI components; no BFF dependency.
 *  - Auth via `Xrm.WebApi` (ADR-028).
 *  - Fluent UI v9 only (ADR-021).
 *  - React 19 (ADR-022).
 */

export * from './components';
export * from './context';
// './hooks' and './services' — DELETED (task 087, C-25, 2026-10-03). Each
// directory held exactly one dead module (`useEventsBulkActions`, the
// Events-local `FetchXmlService`) with zero consumers outside its own barrel.
export type * from './types';
export * from './utils';
export * from './widgets';
