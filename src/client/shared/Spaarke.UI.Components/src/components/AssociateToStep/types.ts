/**
 * types.ts
 * Type definitions for the shared AssociateToStep component.
 *
 * @see ADR-012 — Shared Component Library (reusable across all create wizards)
 * @see ADR-021 — Fluent UI v9 design system
 * @see ADR-024 — Polymorphic Resolver Pattern
 */

import type { INavigationService } from '../../types/serviceInterfaces';

export type { INavigationService };

// ---------------------------------------------------------------------------
// EntityTypeOption
// ---------------------------------------------------------------------------

/**
 * Describes a Dataverse entity type that can be selected as an association target.
 *
 * @example
 * ```typescript
 * const entityTypes: EntityTypeOption[] = [
 *   { label: "Matter",  entityType: "sprk_matter",  defaultViewId: "matter-lookup-view-guid" },
 *   { label: "Project", entityType: "sprk_project" },
 * ];
 * ```
 */
export interface EntityTypeOption {
  /** Human-readable display label shown in the record type dropdown. */
  label: string;
  /** Dataverse logical name of the entity (e.g., "sprk_matter"). */
  entityType: string;
  /**
   * Optional GUID of the default view to display in the Dataverse lookup dialog.
   * When omitted the entity's default lookup view is used.
   */
  defaultViewId?: string;
}

// ---------------------------------------------------------------------------
// RegardingTarget — canonical entry for ADR-024 multi-entity resolution
// ---------------------------------------------------------------------------

/**
 * A regarding-target descriptor used by entities that follow the
 * ADR-024 polymorphic resolver pattern (e.g., `sprk_todo`, `sprk_communication`).
 *
 * Extends {@link EntityTypeOption} with the entity-specific lookup attribute name
 * (e.g., `sprk_regardingmatter`). The lookup attribute is informational metadata
 * for callers that need to map the selected target onto the resolver service —
 * the `AssociateToStep` component itself does not use it.
 *
 * The component invokes `PolymorphicResolverService.applyResolverFields` is the
 * caller's responsibility — `AssociateToStep` is a pure UI shell per ADR-024.
 *
 * @example
 * ```typescript
 * const todoTargets: RegardingTarget[] = TODO_REGARDING_TARGETS;
 *
 * <AssociateToStep
 *   entityTypes={todoTargets}
 *   navigationService={navigationService}
 *   value={association}
 *   onChange={(result) => {
 *     // Caller invokes PolymorphicResolverService.applyResolverFields(...)
 *     setAssociation(result);
 *   }}
 * />
 * ```
 */
export interface RegardingTarget extends EntityTypeOption {
  /**
   * Logical name of the entity-specific regarding lookup attribute on the
   * child entity (e.g., `sprk_regardingmatter` for the Matter target on `sprk_todo`).
   * Used by callers to map the user's selection onto the correct lookup field
   * when invoking `PolymorphicResolverService.applyResolverFields`.
   */
  lookupAttribute: string;
}

// ---------------------------------------------------------------------------
// TODO_REGARDING_TARGETS — canonical list of the 11 sprk_todo regarding targets
// ---------------------------------------------------------------------------

/**
 * Canonical list of the eleven entity targets supported for `sprk_todo`
 * regarding associations per spec.md FR-07 / ADR-024 / entity-schema.md.
 *
 * Order matches the schema doc presentation order. The first entry is shown
 * as the default selection in the picker.
 *
 * Note: `Contact` uses the OOB `contact` logical name (not `sprk_contact`).
 * Per design.md row 97 + entity-schema.md note, the lookup attribute is
 * `sprk_regardingcontact` and the target entity is OOB `contact`.
 *
 * @see spec.md FR-07
 * @see src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 */
export const TODO_REGARDING_TARGETS: ReadonlyArray<RegardingTarget> = [
  { label: 'Matter', entityType: 'sprk_matter', lookupAttribute: 'sprk_regardingmatter' },
  { label: 'Project', entityType: 'sprk_project', lookupAttribute: 'sprk_regardingproject' },
  { label: 'Event', entityType: 'sprk_event', lookupAttribute: 'sprk_regardingevent' },
  { label: 'Communication', entityType: 'sprk_communication', lookupAttribute: 'sprk_regardingcommunication' },
  { label: 'Work Assignment', entityType: 'sprk_workassignment', lookupAttribute: 'sprk_regardingworkassignment' },
  { label: 'Invoice', entityType: 'sprk_invoice', lookupAttribute: 'sprk_regardinginvoice' },
  { label: 'Budget', entityType: 'sprk_budget', lookupAttribute: 'sprk_regardingbudget' },
  { label: 'Analysis', entityType: 'sprk_analysis', lookupAttribute: 'sprk_regardinganalysis' },
  { label: 'Organization', entityType: 'sprk_organization', lookupAttribute: 'sprk_regardingorganization' },
  { label: 'Contact', entityType: 'contact', lookupAttribute: 'sprk_regardingcontact' },
  { label: 'Document', entityType: 'sprk_document', lookupAttribute: 'sprk_regardingdocument' },
] as const;

// ---------------------------------------------------------------------------
// EVENT_REGARDING_TARGETS — canonical list of the 9 sprk_event regarding targets
// ---------------------------------------------------------------------------

/**
 * Canonical list of the nine entity targets supported for `sprk_event`
 * regarding associations (visual-host-create-button-r1 task 015).
 *
 * Confirmed against the LIVE Dataverse schema via `describe('tables/sprk_event')`
 * (spaarkedev1, 2026-07-08) — every `lookupAttribute` below is a real column on
 * `sprk_event` today.
 *
 * **Note the typo in `sprk_regardingorganziation`** (transposed "z"/"i" — NOT
 * "organization"): this is the REAL live lookup-attribute name on `sprk_event`.
 * Do not "fix" it — pointing at the corrected spelling would target a
 * nonexistent column and silently fail to bind. The `label` is spelled
 * correctly ("Organization"); only the `lookupAttribute` value carries the
 * typo, matching the live column name exactly.
 *
 * `Account` and `Contact` use the OOB `account`/`contact` logical names (not
 * `sprk_account`/`sprk_contact`), confirmed via the same schema query.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see projects/visual-host-create-button-r1/tasks/015-wire-event-key-deploy-smoke.poml
 */
export const EVENT_REGARDING_TARGETS: ReadonlyArray<RegardingTarget> = [
  { label: 'Matter', entityType: 'sprk_matter', lookupAttribute: 'sprk_regardingmatter' },
  { label: 'Project', entityType: 'sprk_project', lookupAttribute: 'sprk_regardingproject' },
  { label: 'Invoice', entityType: 'sprk_invoice', lookupAttribute: 'sprk_regardinginvoice' },
  { label: 'Account', entityType: 'account', lookupAttribute: 'sprk_regardingaccount' },
  { label: 'Contact', entityType: 'contact', lookupAttribute: 'sprk_regardingcontact' },
  { label: 'Work Assignment', entityType: 'sprk_workassignment', lookupAttribute: 'sprk_regardingworkassignment' },
  { label: 'Analysis', entityType: 'sprk_analysis', lookupAttribute: 'sprk_regardinganalysis' },
  { label: 'Budget', entityType: 'sprk_budget', lookupAttribute: 'sprk_regardingbudget' },
  // NOTE: intentional typo — see doc comment above.
  { label: 'Organization', entityType: 'sprk_organization', lookupAttribute: 'sprk_regardingorganziation' },
] as const;

// ---------------------------------------------------------------------------
// INVOICE_REGARDING_TARGETS — canonical list of the 2 sprk_invoice regarding targets
// ---------------------------------------------------------------------------

/**
 * Canonical list of the two entity targets supported for `sprk_invoice`
 * regarding associations (visual-host-create-button-r1 task 030).
 *
 * Confirmed against the LIVE Dataverse schema per
 * `notes/field-manifests/invoice.md` (Phase 0, spaarkedev1, 2026-07-08):
 * Invoice's entity-specific lookups are **Matter + Project only** — narrower
 * than `EVENT_REGARDING_TARGETS` / `TODO_REGARDING_TARGETS` — per spec FR-09
 * / design §5.6 ("`applyResolverFields` succeeds for Matter and Project
 * parents on Event/Invoice/KPI").
 *
 * Unlike `EVENT_REGARDING_TARGETS` (whose lookup columns carry the
 * `sprk_regarding{entity}` prefix), the manifest confirms Invoice's
 * entity-specific lookup columns are named directly `sprk_matter` /
 * `sprk_project` (no "regarding" prefix) — this is informational metadata
 * only (`RegardingTarget.lookupAttribute` doc comment); `applyResolverFields`
 * discovers the real nav-prop from live metadata at runtime regardless.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see projects/visual-host-create-button-r1/notes/field-manifests/invoice.md
 * @see projects/visual-host-create-button-r1/tasks/030-create-invoice-wizard.poml
 */
export const INVOICE_REGARDING_TARGETS: ReadonlyArray<RegardingTarget> = [
  { label: 'Matter', entityType: 'sprk_matter', lookupAttribute: 'sprk_matter' },
  { label: 'Project', entityType: 'sprk_project', lookupAttribute: 'sprk_project' },
] as const;

// ---------------------------------------------------------------------------
// REPORTCARD_REGARDING_TARGETS — canonical list of the 2 sprk_reportcard regarding targets
// ---------------------------------------------------------------------------

/**
 * Canonical list of the two entity targets supported for `sprk_reportcard`
 * regarding associations (visual-host-create-button-r1 task 040).
 *
 * Confirmed against the LIVE Dataverse schema via `describe('tables/sprk_reportcard')`
 * (spaarkedev1, 2026-07-08) per
 * `projects/visual-host-create-button-r1/notes/field-manifests/reportcard.md`:
 * `sprk_reportcard`'s entity-specific lookups are **Matter + Project only** —
 * `sprk_regardingmatter` / `sprk_regardingproject` — no other
 * `sprk_regarding{entity}` lookups exist on this table.
 *
 * Unlike `INVOICE_REGARDING_TARGETS` (whose lookup columns are named directly
 * `sprk_matter` / `sprk_project`, no "regarding" prefix), Report Card's
 * entity-specific lookups DO carry the `sprk_regarding{entity}` prefix — same
 * convention as `TODO_REGARDING_TARGETS` / `EVENT_REGARDING_TARGETS`. This is
 * informational metadata only (`RegardingTarget.lookupAttribute` doc comment);
 * `applyResolverFields` discovers the real nav-prop from live metadata at
 * runtime regardless.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see projects/visual-host-create-button-r1/notes/field-manifests/reportcard.md
 * @see projects/visual-host-create-button-r1/tasks/040-create-reportcard-wizard.poml
 */
export const REPORTCARD_REGARDING_TARGETS: ReadonlyArray<RegardingTarget> = [
  { label: 'Matter', entityType: 'sprk_matter', lookupAttribute: 'sprk_regardingmatter' },
  { label: 'Project', entityType: 'sprk_project', lookupAttribute: 'sprk_regardingproject' },
] as const;

// ---------------------------------------------------------------------------
// AssociationResult
// ---------------------------------------------------------------------------

/**
 * Represents the record selected by the user via the Dataverse lookup dialog.
 * Returned via the `onChange` callback once the user picks a record.
 */
export interface AssociationResult {
  /** Dataverse logical name of the selected record's entity type. */
  entityType: string;
  /** GUID of the selected record (lowercase, no braces). */
  recordId: string;
  /** Display name of the selected record as returned by the lookup dialog. */
  recordName: string;
}

// ---------------------------------------------------------------------------
// AssociateToStepProps
// ---------------------------------------------------------------------------

/**
 * Props for the shared `AssociateToStep` wizard step component.
 *
 * The component renders:
 *   1. A record-type dropdown populated from `entityTypes`
 *   2. A "Select Record" button that triggers `navigationService.openLookup()`
 *   3. A selected-record display card with a Clear action
 *   4. A Skip option for proceeding without association
 *
 * @example Usage in CreateMatterWizard:
 * ```tsx
 * <AssociateToStep
 *   entityTypes={[
 *     { label: "Project", entityType: "sprk_project" },
 *     { label: "Account", entityType: "account" },
 *   ]}
 *   navigationService={navigationService}
 *   value={associationResult}
 *   onChange={setAssociationResult}
 *   onSkip={handleSkip}
 * />
 * ```
 */
export interface AssociateToStepProps {
  /**
   * Available record types the user can associate with.
   * Rendered as options in the record type dropdown.
   * Must contain at least one entry.
   */
  entityTypes: EntityTypeOption[];

  /**
   * Navigation service used to open the Dataverse lookup side pane.
   * Typically injected by the consuming wizard from a PCF or Code Page adapter.
   */
  navigationService: INavigationService;

  /**
   * Current selection (controlled component).
   * `null` means no association selected; `undefined` also treated as no selection.
   */
  value?: AssociationResult | null;

  /**
   * Called when the association changes:
   * - A new record is selected → receives `AssociationResult`
   * - The user clears a selection → receives `null`
   */
  onChange?: (result: AssociationResult | null) => void;

  /**
   * Called when the user explicitly clicks "Skip" to proceed without linking a record.
   * The consumer is responsible for advancing the wizard step.
   */
  onSkip?: () => void;

  /**
   * When `true`, all interactive controls are disabled.
   * Useful while the wizard is in a loading or submitting state.
   */
  disabled?: boolean;

  /**
   * When `true`, the step renders the pre-supplied `value` as a fixed,
   * read-only association: the record-type dropdown, "Select Record" button,
   * and Clear affordance are all suppressed. Used when the parent record is
   * unambiguous and must not be changed by the user — e.g. a wizard launched
   * from a Visual Host visual, where the host record is always the parent
   * (design §5.5, `lockAssociation`).
   *
   * Distinct from {@link disabled} (a transient greying-out during submit):
   * `locked` is a permanent contract that the association cannot be edited.
   * When `locked` is `true` but no `value` is supplied, the step renders only
   * its header (there is nothing to lock).
   */
  locked?: boolean;
}
