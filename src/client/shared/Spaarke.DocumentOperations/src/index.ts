// @spaarke/document-operations — barrel export
//
// Cross-surface document-operations hooks consumed by SemanticSearch (today)
// and Compose (task 033 / FR-13 of spaarkeai-compose-r1).
//
// TASK 031 (this barrel): `useDocumentActions` hook + types now exported.
// TASK 032: SemanticSearch refactored to consume from this barrel.
// TASK 033: Compose adopts the hook for promoted-document workflows.

export { useDocumentActions } from './hooks/useDocumentActions';
export type { UseDocumentActionsOptions, UseDocumentActionsResult } from './hooks/useDocumentActions';
