/**
 * SmartToDo barrel export — LegalWorkspace thin shim.
 *
 * R5 FR-01 / task 003 (thin-shim conversion). This folder used to contain the
 * 13-file rich Kanban implementation; that implementation now lives
 * host-agnostic in `@spaarke/smart-todo-components` (task 002). Only the two
 * LW-specific shim components remain here, re-exported for the existing
 * consumer import paths (`./components/SmartToDo/SmartToDo`,
 * `../SmartToDo/SmartToDoDialog`).
 *
 * Zero duplicated component implementation remains in this directory.
 */

export { SmartToDo } from "./SmartToDo";
export type { ISmartToDoProps } from "./SmartToDo";

export { SmartToDoDialog } from "./SmartToDoDialog";
export type { ISmartToDoDialogProps } from "./SmartToDoDialog";
