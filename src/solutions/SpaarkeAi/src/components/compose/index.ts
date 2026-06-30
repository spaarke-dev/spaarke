/**
 * Spaarke Compose — components barrel
 *
 * Project: spaarkeai-compose-r1
 * Phase:   Phase 4 — Frontend Compose surface
 *
 * Re-exports the Compose-surface React components and their prop types so
 * consumers (notably `App.tsx`, the SpaarkeAi root) can import from a single
 * path: `import { ComposeWorkspace } from '@/components/compose'`.
 *
 * Population order across Phase 4 tasks:
 *   - 042 → `ComposeWorkspace.tsx` (orchestrator)
 *   - 043 → `ComposeToolbar.tsx`   (Fluent v9 toolbar)
 *   - 044 → `ComposeEmptyState.tsx` (Browse + Search affordances)
 *   - 045 → `ComposeEditor.tsx`     (TipTap + DOCX bridge) — in @spaarke/compose-components
 *   - 046 → wire ComposeWorkspace into App.tsx via composeMode URL param (Path A)
 */

export { ComposeEmptyState } from './ComposeEmptyState';
export type { ComposeEmptyStateProps } from './ComposeEmptyState';
export { ComposeWorkspace } from './ComposeWorkspace';
export type { ComposeWorkspaceProps } from './ComposeWorkspace';
export { ComposeToolbar } from './ComposeToolbar';
export type { ComposeToolbarProps, ComposeSummarizeRequestEvent } from './ComposeToolbar';
