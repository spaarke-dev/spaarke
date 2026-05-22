/**
 * workspaceTemplateFilter.ts — shared constant: the 6-template SpaarkeAi
 * subset surfaced by the WorkspaceLayoutWizard in BOTH the create launch
 * (WorkspacePaneMenu) and the edit launch (ManageWorkspacesPane).
 *
 * # Why this file exists (Task 102, 2026-05-22)
 *
 * Round 7 operator feedback: "the Create New Workspace wizard and the Edit
 * Workspace wizard show different layout types; the 'Create New' is the
 * correct set of options." Root cause: `WorkspacePaneMenu` defined a private
 * `SPAARKEAI_TEMPLATE_FILTER` constant and passed it on create; the new
 * `ManageWorkspacesPane` edit launch (task 093) did NOT pass it, so the
 * wizard fell back to surfacing all 9 templates from its `LAYOUT_TEMPLATES`
 * registry.
 *
 * Factoring the constant out (rather than re-defining or importing across
 * sibling component files) gives both surfaces a single source of truth.
 *
 * # FR-25 preservation
 *
 * The 3-column templates remain in the wizard's `LAYOUT_TEMPLATES` registry —
 * `templateFilter` is just a membership SET that narrows what is surfaced at
 * runtime, NOT a code deletion. Standalone `LegalWorkspace` continues to
 * launch the wizard with no filter → all 9 templates remain visible there
 * (FR-25 backwards-compat).
 *
 * Operator note carried forward from task 091: "do not delete the code that
 * allows a three column because we may use this in the future." Compliance
 * is automatic — this constant is a runtime filter, not a registry edit.
 *
 * # Order
 *
 * Matches the FR-14 specification order. The wizard's `TemplateStep` renders
 * templates in canonical `LAYOUT_TEMPLATES` registry order regardless of the
 * filter array order, so this list functions as a membership set.
 */

export const SPAARKEAI_TEMPLATE_FILTER = [
  "2-col-equal",
  "3-row-mixed",
  "hero-2x2",
  "sidebar-main",
  "single-column",
  "single-column-5",
] as const;

export type SpaarkeAiTemplateId = (typeof SPAARKEAI_TEMPLATE_FILTER)[number];
