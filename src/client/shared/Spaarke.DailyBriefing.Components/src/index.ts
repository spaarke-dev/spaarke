/**
 * @spaarke/daily-briefing-components
 *
 * Shared host-agnostic React components for Daily Briefing.
 *
 * Consumers (post-hoist, R2 tasks 011-016):
 *  - `src/solutions/DailyBriefing/` (standalone code page)
 *  - SpaarkeAi Daily Briefing workspace widget (Pattern D dual-use)
 *  - LegalWorkspace embedded-section shim (via `daily-briefing.registration.ts`)
 *
 * Architecture:
 *  - Pure data + UI components; uses BFF `/narrate` for AI narration.
 *  - Auth via `@spaarke/auth` (ADR-028).
 *  - Fluent UI v9 only (ADR-021).
 *  - React 19 (ADR-022).
 *  - Pattern D dual-use shape per Calendar (`@spaarke/events-components`) and
 *    Smart Todo (`@spaarke/smart-todo-components`) precedent (ADR-012).
 *
 * R2 task 010 — initial scaffold (no business code yet; hoist in 011-013).
 */

export * from './components';
export * from './widgets';
export * from './hooks';
export * from './services';
export * from './utils';
export type * from './types';
