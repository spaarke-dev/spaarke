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
  { label: 'Matter',          entityType: 'sprk_matter',          lookupAttribute: 'sprk_regardingmatter' },
  { label: 'Project',         entityType: 'sprk_project',         lookupAttribute: 'sprk_regardingproject' },
  { label: 'Event',           entityType: 'sprk_event',           lookupAttribute: 'sprk_regardingevent' },
  { label: 'Communication',   entityType: 'sprk_communication',   lookupAttribute: 'sprk_regardingcommunication' },
  { label: 'Work Assignment', entityType: 'sprk_workassignment',  lookupAttribute: 'sprk_regardingworkassignment' },
  { label: 'Invoice',         entityType: 'sprk_invoice',         lookupAttribute: 'sprk_regardinginvoice' },
  { label: 'Budget',          entityType: 'sprk_budget',          lookupAttribute: 'sprk_regardingbudget' },
  { label: 'Analysis',        entityType: 'sprk_analysis',        lookupAttribute: 'sprk_regardinganalysis' },
  { label: 'Organization',    entityType: 'sprk_organization',    lookupAttribute: 'sprk_regardingorganization' },
  { label: 'Contact',         entityType: 'contact',              lookupAttribute: 'sprk_regardingcontact' },
  { label: 'Document',        entityType: 'sprk_document',        lookupAttribute: 'sprk_regardingdocument' },
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
}
