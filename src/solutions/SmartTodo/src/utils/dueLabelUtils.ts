/**
 * dueLabelUtils — due-date label + urgency tier for the Smart To Do List and
 * Dismissed views.
 *
 * Re-exports the canonical implementations (task 081 / C-17 + U5). This file
 * used to hold a lock-step COPY of `computeDueLabel` (3/7/10-day tiers, with
 * its own inline local-midnight day difference) and of the local-midnight
 * `parseDueDate`; both now come from their single owners so the List view,
 * the Kanban cards and every other surface compute the same tier:
 *   - `computeDueLabel` / `DueUrgency` / `IDueLabel` — `@spaarke/smart-todo-components`
 *     (`utils/todoScoring.ts`; tiers overdue / 3d / 7d / 10d / none, labels
 *     "Overdue" / "Today" / "{n}d"), unchanged behaviour.
 *   - `parseDueDate` — `@spaarke/ui-components` (`utils/dateLocal.ts`), unchanged
 *     behaviour (date-only values parse as local midnight).
 * Existing importers keep importing from this module.
 */
export { computeDueLabel } from '@spaarke/smart-todo-components';
export type { DueUrgency, IDueLabel } from '@spaarke/smart-todo-components';
export { parseDueDate } from '@spaarke/ui-components';
