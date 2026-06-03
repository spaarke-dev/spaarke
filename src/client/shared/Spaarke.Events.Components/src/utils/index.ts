/**
 * Utility barrel for `@spaarke/events-components`.
 *
 * Task 116 — small date helpers (`addMonths`, `startOfMonth`) used by the
 * Calendar widget's external ◀ ▶ navigation. Adding date-fns to the
 * shared lib's peer-deps was rejected: see `dateMath.ts` rationale.
 */
export * from './dateMath';
